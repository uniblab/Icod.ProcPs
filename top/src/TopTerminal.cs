/*
	top
	Interactively display processes and system activity.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Icod.ProcPs.Top;

using System.Text;
using Icod.DCurses;

/// <summary>Represents terminal geometry required by top.</summary>
internal readonly record struct TopTerminalDimensions(
	int Columns,
	int Rows
);

/// <summary>Identifies terminal events relevant to the top event loop.</summary>
internal enum TopTerminalEventKind {
	Timeout,
	Resize,
	Repaint,
	Interrupt,
	Input,
	Other
}

/// <summary>Identifies terminal-independent keys consumed by top.</summary>
internal enum TopInputKey {
	None,
	Character,
	Enter,
	Escape,
	Backspace,
	Up,
	Down,
	Left,
	Right,
	PageUp,
	PageDown,
	Home,
	End,
	Delete,
	Tab,
	EndOfInput,
	Other
}

/// <summary>Represents one terminal-independent input event consumed by top.</summary>
internal readonly record struct TopInputEvent(
	TopInputKey Key,
	Rune? Character
);

/// <summary>Represents one terminal event consumed by top.</summary>
internal readonly record struct TopTerminalEvent(
	TopTerminalEventKind Kind,
	TopInputEvent? Input = null
);

/// <summary>Identifies semantic styles used by the top renderer.</summary>
internal enum TopLineStyle {
	Default,
	Summary,
	Header,
	Prompt,
	Message,
	Dim,
	HighlightBold,
	HighlightReverse
}

/// <summary>Represents one rendered top line.</summary>
internal readonly record struct TopRenderLine(
	string Text,
	TopLineStyle Style = TopLineStyle.Default
);

/// <summary>Represents a complete top display frame.</summary>
internal sealed class TopRenderFrame {
	internal TopRenderFrame(
		IReadOnlyList<TopRenderLine> lines,
		int columns,
		int rows,
		bool boldEnabled
	) {
		ArgumentNullException.ThrowIfNull( lines );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( columns );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( rows );
		this.Lines = lines;
		this.Columns = columns;
		this.Rows = rows;
		this.BoldEnabled = boldEnabled;
	}

	internal IReadOnlyList<TopRenderLine> Lines { get; }
	internal int Columns { get; }
	internal int Rows { get; }
	internal bool BoldEnabled { get; }
}

/// <summary>Creates the terminal presentation session used by top.</summary>
internal interface ITopTerminalSessionFactory {
	ValueTask<ITopTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Abstracts the DCurses surface consumed by top.</summary>
internal interface ITopTerminalSession : IAsyncDisposable {
	bool IsInteractive { get; }
	CancellationToken TerminationToken { get; }
	TopTerminalDimensions GetDimensions();
	ValueTask<TopTerminalEvent> ReadEventAsync(
		TimeSpan timeout,
		CancellationToken cancellationToken = default
	);
	ValueTask RenderAsync(
		TopRenderFrame frame,
		CancellationToken cancellationToken = default
	);
	ValueTask RepaintAsync(
		CancellationToken cancellationToken = default
	);
	ValueTask AlertAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Opens the system DCurses terminal session.</summary>
internal sealed class SystemTopTerminalSessionFactory : ITopTerminalSessionFactory {
	internal static SystemTopTerminalSessionFactory Instance { get; } = new();

	private SystemTopTerminalSessionFactory() {
	}

	public async ValueTask<ITopTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		CursesSession session = await CursesSession.OpenAsync(
			cancellationToken: cancellationToken
		).ConfigureAwait( false );
		return new DCursesTopTerminalSession( session );
	}
}

/// <summary>Adapts a DCurses session to top's terminal seam.</summary>
internal sealed class DCursesTopTerminalSession : ITopTerminalSession {
	private readonly CursesSession session;
	private static readonly CursesStyle SummaryStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Bold
	);
	private static readonly CursesStyle HeaderStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Reverse
	);
	private static readonly CursesStyle PromptStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Bold | CursesTextAttributes.Reverse
	);
	private static readonly CursesStyle MessageStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Standout
	);
	private static readonly CursesStyle DimStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Dim
	);
	private static readonly CursesStyle HighlightBoldStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Bold
	);
	private static readonly CursesStyle HighlightReverseStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Reverse
	);

	internal DCursesTopTerminalSession( CursesSession session ) {
		ArgumentNullException.ThrowIfNull( session );
		this.session = session;
	}

	public bool IsInteractive => this.session.IsInteractive;
	public CancellationToken TerminationToken => this.session.TerminationToken;

	public TopTerminalDimensions GetDimensions() {
		var result = this.session.GetDimensions();
		if ( !result.IsAvailable ) {
			throw new InvalidOperationException(
				result.Message ?? "The terminal dimensions are unavailable."
			);
		}
		var size = result.GetRequiredValue();
		return new TopTerminalDimensions( size.Columns, size.Rows );
	}

	public async ValueTask<TopTerminalEvent> ReadEventAsync(
		TimeSpan timeout,
		CancellationToken cancellationToken = default
	) {
		if ( TimeSpan.Zero > timeout ) {
			throw new ArgumentOutOfRangeException( nameof( timeout ) );
		}
		cancellationToken.ThrowIfCancellationRequested();

		CursesEvent cursesEvent = await this.session.ReadEventAsync(
			timeout,
			cancellationToken
		).ConfigureAwait( false );
		if ( CursesEventKind.Timeout == cursesEvent.Kind ) {
			return new TopTerminalEvent( TopTerminalEventKind.Timeout );
		}
		if ( CursesEventKind.Input == cursesEvent.Kind ) {
			if ( null == cursesEvent.Input ) {
				return new TopTerminalEvent( TopTerminalEventKind.Other );
			}
			return new TopTerminalEvent(
				TopTerminalEventKind.Input,
				MapInput( cursesEvent.Input )
			);
		}

		CursesLifecycleEvent lifecycle = cursesEvent.Lifecycle
			?? throw new InvalidOperationException(
				"A curses lifecycle event did not include its lifecycle payload."
			);
		return lifecycle.Kind switch {
			CursesLifecycleEventKind.Resize => new TopTerminalEvent(
				TopTerminalEventKind.Resize
			),
			CursesLifecycleEventKind.Resumed => new TopTerminalEvent(
				TopTerminalEventKind.Repaint
			),
			CursesLifecycleEventKind.Interrupt => new TopTerminalEvent(
				TopTerminalEventKind.Interrupt
			),
			CursesLifecycleEventKind.Termination => new TopTerminalEvent(
				TopTerminalEventKind.Interrupt
			),
			_ => new TopTerminalEvent( TopTerminalEventKind.Other )
		};
	}

	public async ValueTask RenderAsync(
		TopRenderFrame frame,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( frame );
		cancellationToken.ThrowIfCancellationRequested();
		_ = this.session.SynchronizeDimensions();

		CursesWindow window = this.session.StandardScreen;
		window.WrapMode = CursesWrapMode.Clip;
		window.ScrollingEnabled = false;
		window.Clear();
		int rows = Math.Min( window.Rows, frame.Lines.Count );
		for ( int row = 0; row < rows; row++ ) {
			TopRenderLine line = frame.Lines[ row ];
			if ( 0 == line.Text.Length ) {
				continue;
			}
			window.Move( row, 0 );
			window.Write( line.Text, StyleFor( line.Style, frame.BoldEnabled ) );
		}
		window.Move( 0, 0 );
		await this.session.RefreshAsync( cancellationToken ).ConfigureAwait( false );
	}

	public async ValueTask RepaintAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		this.session.Invalidate();
		await this.session.RefreshAsync( cancellationToken ).ConfigureAwait( false );
	}

	public async ValueTask AlertAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		_ = await this.session.AlertAsync(
			CursesAlertKind.Audible,
			cancellationToken
		).ConfigureAwait( false );
	}

	public ValueTask DisposeAsync() => this.session.DisposeAsync();

	private static TopInputEvent MapInput( CursesInputEvent input ) {
		ArgumentNullException.ThrowIfNull( input );
		if ( CursesInputEventKind.EndOfInput == input.Kind ) {
			return new TopInputEvent( TopInputKey.EndOfInput, null );
		}
		if ( CursesInputEventKind.Text == input.Kind ) {
			return new TopInputEvent(
				TopInputKey.Character,
				input.Character
			);
		}
		return input.Key switch {
			CursesKey.Character => new TopInputEvent( TopInputKey.Character, input.Character ),
			CursesKey.Space => new TopInputEvent( TopInputKey.Character, new Rune( ' ' ) ),
			CursesKey.Enter => new TopInputEvent( TopInputKey.Enter, null ),
			CursesKey.Escape => new TopInputEvent( TopInputKey.Escape, null ),
			CursesKey.Backspace => new TopInputEvent( TopInputKey.Backspace, null ),
			CursesKey.Up => new TopInputEvent( TopInputKey.Up, null ),
			CursesKey.Down => new TopInputEvent( TopInputKey.Down, null ),
			CursesKey.Left => new TopInputEvent( TopInputKey.Left, null ),
			CursesKey.Right => new TopInputEvent( TopInputKey.Right, null ),
			CursesKey.PageUp => new TopInputEvent( TopInputKey.PageUp, null ),
			CursesKey.PageDown => new TopInputEvent( TopInputKey.PageDown, null ),
			CursesKey.Home => new TopInputEvent( TopInputKey.Home, null ),
			CursesKey.End => new TopInputEvent( TopInputKey.End, null ),
			CursesKey.Delete => new TopInputEvent( TopInputKey.Delete, null ),
			CursesKey.Tab => new TopInputEvent( TopInputKey.Tab, null ),
			_ => new TopInputEvent( TopInputKey.Other, null )
		};
	}

	private static CursesStyle StyleFor(
		TopLineStyle style,
		bool boldEnabled
	) {
		CursesStyle result = style switch {
			TopLineStyle.Summary => SummaryStyle,
			TopLineStyle.Header => HeaderStyle,
			TopLineStyle.Prompt => PromptStyle,
			TopLineStyle.Message => MessageStyle,
			TopLineStyle.Dim => DimStyle,
			TopLineStyle.HighlightBold => HighlightBoldStyle,
			TopLineStyle.HighlightReverse => HighlightReverseStyle,
			_ => CursesStyle.Default
		};
		if ( boldEnabled ) {
			return result;
		}
		return result.WithAttributes(
			result.Attributes & ~CursesTextAttributes.Bold
		);
	}
}

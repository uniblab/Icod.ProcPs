/*
	slabtop
	Interactively display Linux kernel slab-cache information.
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

namespace Icod.ProcPs.SlabTop;

using System.Text;
using Icod.DCurses;

/// <summary>Represents terminal geometry required by slabtop.</summary>
internal readonly record struct SlabTopTerminalDimensions(
	int Columns,
	int Rows
);

/// <summary>Identifies one terminal event relevant to slabtop scheduling.</summary>
internal enum SlabTopTerminalEventKind {
	Timeout,
	Resize,
	Repaint,
	Interrupt,
	Input,
	Other
}

/// <summary>Identifies terminal-independent input consumed by slabtop.</summary>
internal enum SlabTopInputKey {
	None,
	Character,
	EndOfInput,
	Other
}

/// <summary>Represents one terminal-independent input event consumed by slabtop.</summary>
internal readonly record struct SlabTopInputEvent(
	SlabTopInputKey Key,
	Rune? Character
);

/// <summary>Represents one terminal event relevant to slabtop scheduling.</summary>
internal readonly record struct SlabTopTerminalEvent(
	SlabTopTerminalEventKind Kind,
	SlabTopInputEvent? Input = null
);

/// <summary>Creates the terminal presentation session used by slabtop.</summary>
internal interface ISlabTopTerminalSessionFactory {
	ValueTask<ISlabTopTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Abstracts the small DCurses surface consumed by slabtop.</summary>
internal interface ISlabTopTerminalSession : IAsyncDisposable {
	bool IsInteractive { get; }
	CancellationToken TerminationToken { get; }

	SlabTopTerminalDimensions GetDimensions();

	ValueTask<SlabTopTerminalEvent> ReadEventAsync(
		TimeSpan timeout,
		CancellationToken cancellationToken = default
	);

	ValueTask RenderAsync(
		SlabTopRenderFrame frame,
		CancellationToken cancellationToken = default
	);

	ValueTask RepaintAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Opens the system DCurses terminal session.</summary>
internal sealed class SystemSlabTopTerminalSessionFactory
	: ISlabTopTerminalSessionFactory {
	internal static SystemSlabTopTerminalSessionFactory Instance { get; } = new();

	private SystemSlabTopTerminalSessionFactory() {
	}

	public async ValueTask<ISlabTopTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		CursesSession session = await CursesSession.OpenAsync(
			cancellationToken: cancellationToken
		).ConfigureAwait( false );
		return new DCursesSlabTopTerminalSession( session );
	}
}

/// <summary>Adapts a DCurses session to the slabtop-specific terminal seam.</summary>
internal sealed class DCursesSlabTopTerminalSession : ISlabTopTerminalSession {
	private static readonly CursesStyle HeaderStyle = new(
		CursesColor.Default,
		CursesColor.Default,
		CursesTextAttributes.Reverse
	);
	private readonly CursesSession session;

	internal DCursesSlabTopTerminalSession( CursesSession session ) {
		ArgumentNullException.ThrowIfNull( session );
		this.session = session;
	}

	public bool IsInteractive => this.session.IsInteractive;
	public CancellationToken TerminationToken => this.session.TerminationToken;

	public SlabTopTerminalDimensions GetDimensions() {
		var result = this.session.GetDimensions();
		if ( !result.IsAvailable ) {
			throw new InvalidOperationException(
				result.Message ?? "The terminal dimensions are unavailable."
			);
		}
		var size = result.GetRequiredValue();
		return new SlabTopTerminalDimensions( size.Columns, size.Rows );
	}

	public async ValueTask<SlabTopTerminalEvent> ReadEventAsync(
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
			return new SlabTopTerminalEvent( SlabTopTerminalEventKind.Timeout );
		}
		if ( CursesEventKind.Input == cursesEvent.Kind ) {
			if ( null == cursesEvent.Input ) {
				return new SlabTopTerminalEvent(
					SlabTopTerminalEventKind.Other
				);
			}
			return new SlabTopTerminalEvent(
				SlabTopTerminalEventKind.Input,
				MapInput(
					cursesEvent.Input
				)
			);
		}

		CursesLifecycleEvent lifecycle = cursesEvent.Lifecycle
			?? throw new InvalidOperationException(
				"A curses lifecycle event did not include its lifecycle payload."
			);
		return lifecycle.Kind switch {
			CursesLifecycleEventKind.Resize => new SlabTopTerminalEvent(
				SlabTopTerminalEventKind.Resize
			),
			CursesLifecycleEventKind.Resumed => new SlabTopTerminalEvent(
				SlabTopTerminalEventKind.Repaint
			),
			CursesLifecycleEventKind.Interrupt => new SlabTopTerminalEvent(
				SlabTopTerminalEventKind.Interrupt
			),
			CursesLifecycleEventKind.Termination => new SlabTopTerminalEvent(
				SlabTopTerminalEventKind.Interrupt
			),
			_ => new SlabTopTerminalEvent( SlabTopTerminalEventKind.Other )
		};
	}

	public async ValueTask RenderAsync(
		SlabTopRenderFrame frame,
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
			string line = frame.Lines[ row ];
			if ( 0 == line.Length ) {
				continue;
			}
			window.Move( row, 0 );
			window.Write(
				line,
				frame.HeaderRow == row
					? HeaderStyle
					: CursesStyle.Default
			);
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

	public ValueTask DisposeAsync() {
		return this.session.DisposeAsync();
	}

	private static SlabTopInputEvent MapInput(
		CursesInputEvent input
	) {
		ArgumentNullException.ThrowIfNull(
			input
		);
		if ( CursesInputEventKind.EndOfInput == input.Kind ) {
			return new SlabTopInputEvent(
				SlabTopInputKey.EndOfInput,
				null
			);
		}
		if ( CursesInputEventKind.Text == input.Kind ) {
			return new SlabTopInputEvent(
				SlabTopInputKey.Character,
				input.Character
			);
		}
		return input.Key switch {
			CursesKey.Character => new SlabTopInputEvent(
				SlabTopInputKey.Character,
				input.Character
			),
			CursesKey.Space => new SlabTopInputEvent(
				SlabTopInputKey.Character,
				new Rune( ' ' )
			),
			_ => new SlabTopInputEvent(
				SlabTopInputKey.Other,
				null
			)
		};
	}
}

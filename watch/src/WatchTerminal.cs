/*
	watch
	Execute a program periodically and display its output.
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

namespace Icod.ProcPs.Watch;

using Icod.DCurses;

/// <summary>Represents terminal geometry required by the watch presentation model.</summary>
internal readonly record struct WatchTerminalDimensions(
	int Columns,
	int Rows
);

/// <summary>Identifies one terminal event relevant to watch scheduling.</summary>
internal enum WatchTerminalEventKind {
	Timeout,
	Resize,
	Repaint,
	Interrupt,
	Input,
	Other
}

/// <summary>Represents one terminal event relevant to watch scheduling.</summary>
internal readonly record struct WatchTerminalEvent(
	WatchTerminalEventKind Kind
);

/// <summary>Creates the terminal presentation session used by watch.</summary>
internal interface IWatchTerminalSessionFactory {
	ValueTask<IWatchTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Abstracts the small DCurses surface consumed by the watch command.</summary>
internal interface IWatchTerminalSession : IAsyncDisposable {
	bool IsInteractive {
		get;
	}

	CancellationToken TerminationToken {
		get;
	}

	WatchTerminalDimensions GetDimensions();

	ValueTask<WatchTerminalEvent> ReadEventAsync(
		TimeSpan timeout,
		CancellationToken cancellationToken = default
	);

	ValueTask RenderAsync(
		WatchRenderFrame frame,
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
internal sealed class SystemWatchTerminalSessionFactory
	: IWatchTerminalSessionFactory {
	internal static SystemWatchTerminalSessionFactory Instance {
		get;
	} = new();

	private SystemWatchTerminalSessionFactory() {
	}

	public async ValueTask<IWatchTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		CursesSession session = await CursesSession.OpenAsync(
			cancellationToken: cancellationToken
		).ConfigureAwait( false );
		return new DCursesWatchTerminalSession( session );
	}
}

/// <summary>Adapts a DCurses session to the watch-specific presentation seam.</summary>
internal sealed class DCursesWatchTerminalSession
	: IWatchTerminalSession {
	private readonly CursesSession session;

	internal DCursesWatchTerminalSession(
		CursesSession session
	) {
		ArgumentNullException.ThrowIfNull( session );
		this.session = session;
	}

	public bool IsInteractive => this.session.IsInteractive;

	public CancellationToken TerminationToken => this.session.TerminationToken;

	public WatchTerminalDimensions GetDimensions() {
		var result = this.session.GetDimensions();
		if ( !result.IsAvailable ) {
			throw new InvalidOperationException(
				result.Message
					?? "The terminal dimensions are unavailable."
			);
		}

		var size = result.GetRequiredValue();
		return new WatchTerminalDimensions(
			size.Columns,
			size.Rows
		);
	}

	public async ValueTask<WatchTerminalEvent> ReadEventAsync(
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
			return new WatchTerminalEvent( WatchTerminalEventKind.Timeout );
		}
		if ( CursesEventKind.Input == cursesEvent.Kind ) {
			return new WatchTerminalEvent( WatchTerminalEventKind.Input );
		}

		CursesLifecycleEvent lifecycle = cursesEvent.Lifecycle
			?? throw new InvalidOperationException(
				"A curses lifecycle event did not include its lifecycle payload."
			);
		return lifecycle.Kind switch {
			CursesLifecycleEventKind.Resize => new WatchTerminalEvent(
				WatchTerminalEventKind.Resize
			),
			CursesLifecycleEventKind.Resumed => new WatchTerminalEvent(
				WatchTerminalEventKind.Repaint
			),
			CursesLifecycleEventKind.Interrupt => new WatchTerminalEvent(
				WatchTerminalEventKind.Interrupt
			),
			CursesLifecycleEventKind.Termination => new WatchTerminalEvent(
				WatchTerminalEventKind.Interrupt
			),
			_ => new WatchTerminalEvent( WatchTerminalEventKind.Other )
		};
	}

	public async ValueTask RenderAsync(
		WatchRenderFrame frame,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( frame );
		cancellationToken.ThrowIfCancellationRequested();

		_ = this.session.SynchronizeDimensions();
		CursesWindow window = this.session.StandardScreen;
		window.WrapMode = CursesWrapMode.Clip;
		window.ScrollingEnabled = false;
		window.Clear();

		int row = 0;
		foreach ( string headerLine in frame.HeaderLines ) {
			if ( row >= window.Rows ) {
				break;
			}
			window.Move( row, 0 );
			window.Write( headerLine, CursesStyle.Default );
			row++;
		}

		WatchScreen screen = frame.Screen;
		for ( int bodyRow = 0; bodyRow < screen.Height && row + bodyRow < window.Rows; bodyRow++ ) {
			for ( int column = 0; column < screen.Width && column < window.Columns; column++ ) {
				WatchCell cell = screen.GetCell( bodyRow, column );
				if ( cell.IsContinuation ) {
					continue;
				}

				bool highlighted = frame.Highlights is not null
					&& frame.Highlights[ ( bodyRow * screen.Width ) + column ];
				CursesStyle style = highlighted
					? cell.Style.WithAttributes(
						cell.Style.Attributes | CursesTextAttributes.Reverse
					)
					: cell.Style;
				string content = 0 == cell.Content.Length
					? " "
					: cell.Content;
				if ( !highlighted
					&& cell.Style.IsDefault
					&& " " == content ) {
					continue;
				}

				window.Move( row + bodyRow, column );
				window.Write( content, style );
			}
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

	public ValueTask DisposeAsync() {
		return this.session.DisposeAsync();
	}
}

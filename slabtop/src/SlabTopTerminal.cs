namespace Icod.ProcPs.SlabTop;

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

/// <summary>Represents one terminal event relevant to slabtop scheduling.</summary>
internal readonly record struct SlabTopTerminalEvent(
	SlabTopTerminalEventKind Kind
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
			new CursesSessionOptions {
				InputMode = CursesInputMode.CBreak,
				EchoInput = false,
				UseAlternateScreen = true,
				EnableKeypad = true,
				HideCursor = true
			},
			cancellationToken
		).ConfigureAwait( false );
		return new DCursesSlabTopTerminalSession( session );
	}
}

/// <summary>Adapts a DCurses session to the slabtop-specific terminal seam.</summary>
internal sealed class DCursesSlabTopTerminalSession : ISlabTopTerminalSession {
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
			return new SlabTopTerminalEvent( SlabTopTerminalEventKind.Input );
		}

		CursesLifecycleEvent lifecycle = cursesEvent.Lifecycle
			?? throw new InvalidOperationException(
				"A curses lifecycle event did not include its lifecycle payload."
			);
		return lifecycle.Kind switch {
			CursesLifecycleEventKind.Resize => SynchronizeAndReturn(
				this.session,
				SlabTopTerminalEventKind.Resize
			),
			CursesLifecycleEventKind.Resumed => SynchronizeAndReturn(
				this.session,
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
			window.Write( line, CursesStyle.Default );
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

	private static SlabTopTerminalEvent SynchronizeAndReturn(
		CursesSession session,
		SlabTopTerminalEventKind kind
	) {
		ArgumentNullException.ThrowIfNull( session );
		_ = session.SynchronizeDimensions();
		return new SlabTopTerminalEvent( kind );
	}
}

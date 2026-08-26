namespace Icod.ProcPs.HugeTop;

using Icod.DCurses;

/// <summary>Represents terminal geometry required by hugetop.</summary>
internal readonly record struct HugeTopTerminalDimensions(
	int Columns,
	int Rows
);

/// <summary>Identifies one terminal event relevant to hugetop scheduling.</summary>
internal enum HugeTopTerminalEventKind {
	Timeout,
	Resize,
	Repaint,
	Interrupt,
	Input,
	Other
}

/// <summary>Represents one terminal event relevant to hugetop scheduling.</summary>
internal readonly record struct HugeTopTerminalEvent(
	HugeTopTerminalEventKind Kind
);

/// <summary>Creates the terminal presentation session used by hugetop.</summary>
internal interface IHugeTopTerminalSessionFactory {
	ValueTask<IHugeTopTerminalSession> OpenAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Abstracts the small DCurses surface consumed by hugetop.</summary>
internal interface IHugeTopTerminalSession : IAsyncDisposable {
	bool IsInteractive { get; }
	CancellationToken TerminationToken { get; }

	HugeTopTerminalDimensions GetDimensions();

	ValueTask<HugeTopTerminalEvent> ReadEventAsync(
		TimeSpan timeout,
		CancellationToken cancellationToken = default
	);

	ValueTask RenderAsync(
		HugeTopRenderFrame frame,
		CancellationToken cancellationToken = default
	);

	ValueTask RepaintAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Opens the system DCurses terminal session.</summary>
internal sealed class SystemHugeTopTerminalSessionFactory
	: IHugeTopTerminalSessionFactory {
	internal static SystemHugeTopTerminalSessionFactory Instance { get; } = new();

	private SystemHugeTopTerminalSessionFactory() {
	}

	public async ValueTask<IHugeTopTerminalSession> OpenAsync(
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
		return new DCursesHugeTopTerminalSession( session );
	}
}

/// <summary>Adapts a DCurses session to the hugetop-specific terminal seam.</summary>
internal sealed class DCursesHugeTopTerminalSession : IHugeTopTerminalSession {
	private readonly CursesSession session;

	internal DCursesHugeTopTerminalSession( CursesSession session ) {
		ArgumentNullException.ThrowIfNull( session );
		this.session = session;
	}

	public bool IsInteractive => this.session.IsInteractive;
	public CancellationToken TerminationToken => this.session.TerminationToken;

	public HugeTopTerminalDimensions GetDimensions() {
		var result = this.session.GetDimensions();
		if ( !result.IsAvailable ) {
			throw new InvalidOperationException(
				result.Message ?? "The terminal dimensions are unavailable."
			);
		}
		var size = result.GetRequiredValue();
		return new HugeTopTerminalDimensions( size.Columns, size.Rows );
	}

	public async ValueTask<HugeTopTerminalEvent> ReadEventAsync(
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
			return new HugeTopTerminalEvent( HugeTopTerminalEventKind.Timeout );
		}
		if ( CursesEventKind.Input == cursesEvent.Kind ) {
			return new HugeTopTerminalEvent( HugeTopTerminalEventKind.Input );
		}

		CursesLifecycleEvent lifecycle = cursesEvent.Lifecycle
			?? throw new InvalidOperationException(
				"A curses lifecycle event did not include its lifecycle payload."
			);
		return lifecycle.Kind switch {
			CursesLifecycleEventKind.Resize => SynchronizeAndReturn(
				this.session,
				HugeTopTerminalEventKind.Resize
			),
			CursesLifecycleEventKind.Resumed => SynchronizeAndReturn(
				this.session,
				HugeTopTerminalEventKind.Repaint
			),
			CursesLifecycleEventKind.Interrupt => new HugeTopTerminalEvent(
				HugeTopTerminalEventKind.Interrupt
			),
			CursesLifecycleEventKind.Termination => new HugeTopTerminalEvent(
				HugeTopTerminalEventKind.Interrupt
			),
			_ => new HugeTopTerminalEvent( HugeTopTerminalEventKind.Other )
		};
	}

	public async ValueTask RenderAsync(
		HugeTopRenderFrame frame,
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

	public ValueTask DisposeAsync() => this.session.DisposeAsync();

	private static HugeTopTerminalEvent SynchronizeAndReturn(
		CursesSession session,
		HugeTopTerminalEventKind kind
	) {
		ArgumentNullException.ThrowIfNull( session );
		_ = session.SynchronizeDimensions();
		return new HugeTopTerminalEvent( kind );
	}
}

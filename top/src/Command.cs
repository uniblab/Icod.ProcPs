namespace Icod.ProcPs.Top;

using System.Globalization;
using System.Text;
using Icod.Host;
using Icod.Processes;
using Icod.ProcPs.Shared;
using Icod.Timing;

/// <summary>Defines process-control operations consumed by top's interactive commands.</summary>
internal interface ITopProcessControl {
	ProcessOperationResult<ProcessSignal> ParseSignal( string text );
	Task<ProcessOperationResult> SignalAsync(
		ProcProcessSnapshot process,
		ProcessSignal signal,
		CancellationToken cancellationToken
	);
	ProcessOperationResult SetPriority( ProcProcessSnapshot process, int niceValue );
}

/// <summary>Provides top process controls through Icod.Processes.</summary>
internal sealed class SystemTopProcessControl : ITopProcessControl {
	internal static SystemTopProcessControl Instance { get; } = new();
	private readonly IProcessSignalProvider signals;
	private readonly IProcessPrioritySelectorProvider priorities;

	private SystemTopProcessControl()
		: this(
			SystemProcessSignalProvider.Instance,
			SystemProcessPrioritySelectorProvider.Instance
		) {
	}

	internal SystemTopProcessControl(
		IProcessSignalProvider signals,
		IProcessPrioritySelectorProvider priorities
	) {
		ArgumentNullException.ThrowIfNull( signals );
		ArgumentNullException.ThrowIfNull( priorities );
		this.signals = signals;
		this.priorities = priorities;
	}

	public ProcessOperationResult<ProcessSignal> ParseSignal( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		return this.signals.ParseSignal( text );
	}

	public Task<ProcessOperationResult> SignalAsync(
		ProcProcessSnapshot process,
		ProcessSignal signal,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( process );
		ArgumentNullException.ThrowIfNull( signal );
		return this.signals.DeliverAsync(
			ProcessTarget.ForProcess( process.Identity ),
			signal,
			null,
			cancellationToken
		);
	}

	public ProcessOperationResult SetPriority(
		ProcProcessSnapshot process,
		int niceValue
	) {
		ArgumentNullException.ThrowIfNull( process );
		return this.priorities.SetPriority(
			ProcessPriorityTarget.ForProcess( process.Identity ),
			niceValue
		);
	}
}

/// <summary>Implements the procps-ng compatible <c>top</c> command.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int Canceled = 130;
	private const int MinimumColumns = 40;
	private const int MinimumRows = 7;
	private const int DefaultBatchWidth = 512;
	private const int MaximumMonitoredProcessIds = 20;
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.Top",
		typeof( Command ).Assembly
	);
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private const string Usage = """
Usage:
 top [options]

Options:
 -A, --apply-defaults          use built-in defaults (no personal top configuration)
 -b, --batch                   run in non-interactive batch mode
 -c, --cmdline-toggle          start by displaying command lines
 -d, --delay SECONDS           set the refresh delay; fractional seconds are accepted
 -E, --scale-summary-mem SCALE set summary memory scale: k, m, g, t, p, or e
 -e, --scale-task-mem SCALE    set task memory scale: k, m, g, t, p, or e
 -H, --threads-show            show individual lightweight tasks where supported
 -i, --idle-toggle             suppress tasks idle during the most recent interval
 -n, --iterations NUMBER       exit after NUMBER refreshes
 -O, --list-fields             list fields implemented by this top and exit
 -o, --sort-override FIELD     sort by CPU, MEM, PID, TIME+, VIRT, RES, USER, COMMAND, NI, or S
 -p, --pid PIDLIST             monitor only the selected process IDs (maximum 20)
 -s, --secure-mode             disable interactive delay, signal, and renice commands
 -S, --accum-time-toggle       not available: child CPU counters are not yet observed
 -U, --filter-any-user USER    filter by any observed real/effective user ID
 -u, --filter-only-euser USER  filter by effective user ID
 -w, --width [COLUMNS]         batch output width; without COLUMNS use 512
 -1, --single-cpu-toggle       toggle the aggregate CPU presentation label
 -V, --version                 display version information and exit
 -h, --help                    display this help and exit

Interactive keys:
 q quit; P/M/N/T sort; c command line; H threads; i idle tasks; V forest;
 I CPU normalization; E/e memory scale; d/s delay; u/U user filter;
 k signal; r renice; arrows/PgUp/PgDn/Home/End scroll; h/? help.
""";

	/// <summary>Runs <c>top</c> synchronously.</summary>
	public static int Run(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsync( args, stdout, stderr ).GetAwaiter().GetResult();
	}

	/// <summary>Runs <c>top</c> asynchronously with injectable observation providers.</summary>
	public static Task<int> RunAsync(
		IReadOnlyList<string> args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcProcessProvider? processProvider = null,
		IProcSystemMetricsProvider? metricsProvider = null,
		IProcMatchSupplementProvider? supplementProvider = null,
		IProcAccountDisplayResolver? accountResolver = null,
		IProcessorResourceProvider? processorProvider = null,
		IMonotonicClock? clock = null,
		Func<DateTimeOffset>? wallClock = null,
		Func<string, string?>? environmentVariableProvider = null,
		Func<int>? currentProcessIdProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsyncCore(
			args,
			stdout,
			stderr,
			processProvider,
			metricsProvider,
			supplementProvider,
			accountResolver,
			processorProvider,
			clock,
			wallClock,
			environmentVariableProvider,
			currentProcessIdProvider,
			SystemTopTerminalSessionFactory.Instance,
			SystemTopProcessControl.Instance,
			cancellationToken
		);
	}

	internal static async Task<int> RunAsyncCore(
		IReadOnlyList<string> args,
		Stream? stdout,
		Stream? stderr,
		IProcProcessProvider? processProvider,
		IProcSystemMetricsProvider? metricsProvider,
		IProcMatchSupplementProvider? supplementProvider,
		IProcAccountDisplayResolver? accountResolver,
		IProcessorResourceProvider? processorProvider,
		IMonotonicClock? clock,
		Func<DateTimeOffset>? wallClock,
		Func<string, string?>? environmentVariableProvider,
		Func<int>? currentProcessIdProvider,
		ITopTerminalSessionFactory terminalFactory,
		ITopProcessControl processControl,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminalFactory );
		ArgumentNullException.ThrowIfNull( processControl );

		Stream output = stdout ?? Console.OpenStandardOutput();
		Stream errorOutput = stderr ?? Console.OpenStandardError();
		IProcAccountDisplayResolver accounts = accountResolver ?? SystemProcAccountDisplayResolver.Instance;
		Func<string, string?> environment = environmentVariableProvider ?? Environment.GetEnvironmentVariable;
		int currentProcessId = currentProcessIdProvider?.Invoke() ?? Environment.ProcessId;
		ParsedArguments parsed = Parse( args, accounts, environment, currentProcessId );
		if ( parsed.Error is not null ) {
			await WriteLineAsync( errorOutput, $"top: {parsed.Error}", cancellationToken ).ConfigureAwait( false );
			await WriteTextAsync( errorOutput, NormalizeLineEndings( Usage ), cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( parsed.Help ) {
			await WriteTextAsync( output, NormalizeLineEndings( Usage ), cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.Version ) {
			await WriteLineAsync( output, VersionText, cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ListFields ) {
			foreach ( string line in TopRenderer.ListFields() ) {
				await WriteLineAsync( output, line, cancellationToken ).ConfigureAwait( false );
			}
			return Success;
		}

		IProcProcessProvider processes = processProvider ?? SystemProcProcessProvider.Instance;
		IProcSystemMetricsProvider metrics = metricsProvider ?? SystemProcSystemMetricsProvider.Instance;
		IProcMatchSupplementProvider supplements = supplementProvider ?? SystemProcMatchSupplementProvider.Instance;
		IProcessorResourceProvider processors = processorProvider ?? SystemHostResourceProvider.Instance;
		IMonotonicClock monotonicClock = clock ?? SystemMonotonicClock.Instance;
		Func<DateTimeOffset> now = wallClock ?? ( () => DateTimeOffset.Now );
		var sampler = new TopSampler(
			processes,
			metrics,
			supplements,
			accounts,
			processors,
			monotonicClock,
			now
		);

		try {
			return parsed.Batch
				? await RunBatchAsync(
					sampler,
					parsed,
					monotonicClock,
					output,
					cancellationToken
				).ConfigureAwait( false )
				: await RunInteractiveAsync(
					sampler,
					parsed,
					processes,
					accounts,
					terminalFactory,
					processControl,
					monotonicClock,
					errorOutput,
					cancellationToken
				).ConfigureAwait( false );
		} catch ( OperationCanceledException ) {
			return Canceled;
		} catch ( PlatformNotSupportedException exception ) {
			await WriteLineAsync( errorOutput, $"top: {exception.Message}", CancellationToken.None ).ConfigureAwait( false );
			return Failure;
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or FormatException
		) {
			await WriteLineAsync( errorOutput, $"top: {exception.Message}", CancellationToken.None ).ConfigureAwait( false );
			return Failure;
		}
	}

	private static async Task<int> RunBatchAsync(
		TopSampler sampler,
		ParsedArguments parsed,
		IMonotonicClock clock,
		Stream output,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( sampler );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( output );

		int completed = 0;
		while ( !parsed.Iterations.HasValue || completed < parsed.Iterations.Value ) {
			cancellationToken.ThrowIfCancellationRequested();
			long cycleStarted = clock.GetTimestamp();
			TopSample sample = await sampler.CaptureAsync(
				parsed.State.ShowThreads,
				cancellationToken
			).ConfigureAwait( false );
			if ( 0 < completed ) {
				await WriteLineAsync( output, string.Empty, cancellationToken ).ConfigureAwait( false );
			}
			foreach ( string line in TopRenderer.RenderBatch(
				sample,
				parsed.State,
				parsed.BatchWidth
			) ) {
				await WriteLineAsync( output, line, cancellationToken ).ConfigureAwait( false );
			}
			completed++;
			if ( parsed.Iterations.HasValue && completed >= parsed.Iterations.Value ) {
				break;
			}
			TimeSpan elapsed = clock.GetElapsedTime( cycleStarted, clock.GetTimestamp() );
			TimeSpan remaining = parsed.State.Delay > elapsed
				? parsed.State.Delay - elapsed
				: TimeSpan.Zero;
			if ( TimeSpan.Zero < remaining ) {
				await clock.DelayAsync( remaining, cancellationToken ).ConfigureAwait( false );
			}
		}
		return Success;
	}

	private static async Task<int> RunInteractiveAsync(
		TopSampler sampler,
		ParsedArguments parsed,
		IProcProcessProvider processProvider,
		IProcAccountDisplayResolver accountResolver,
		ITopTerminalSessionFactory terminalFactory,
		ITopProcessControl processControl,
		IMonotonicClock clock,
		Stream errorOutput,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( sampler );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( processProvider );
		ArgumentNullException.ThrowIfNull( accountResolver );
		ArgumentNullException.ThrowIfNull( terminalFactory );
		ArgumentNullException.ThrowIfNull( processControl );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( errorOutput );

		ITopTerminalSession? terminal = null;
		try {
			terminal = await terminalFactory.OpenAsync( cancellationToken ).ConfigureAwait( false );
			if ( !terminal.IsInteractive ) {
				await WriteLineAsync(
					errorOutput,
					"top: interactive terminal input and output are required; use -b for batch mode",
					CancellationToken.None
				).ConfigureAwait( false );
				return Failure;
			}
			TopTerminalDimensions dimensions = terminal.GetDimensions();
			if ( !IsUsableDimensions( dimensions ) ) {
				await WriteLineAsync(
					errorOutput,
					$"top: terminal is too small; at least {MinimumColumns} columns by {MinimumRows} rows are required",
					CancellationToken.None
				).ConfigureAwait( false );
				return Failure;
			}

			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				terminal.TerminationToken
			);
			CancellationToken token = linked.Token;
			int completed = 0;
			while ( true ) {
				token.ThrowIfCancellationRequested();
				long cycleStarted = clock.GetTimestamp();
				TopSample sample = await sampler.CaptureAsync(
					parsed.State.ShowThreads,
					token
				).ConfigureAwait( false );
				completed++;
				dimensions = terminal.GetDimensions();
				if ( !IsUsableDimensions( dimensions ) ) {
					parsed.State.Message = "terminal is too small for the top display";
				}
				await RenderInteractiveAsync(
					terminal,
					sample,
					parsed.State,
					dimensions,
					token
				).ConfigureAwait( false );
				if ( parsed.Iterations.HasValue && completed >= parsed.Iterations.Value ) {
					return Success;
				}

				bool resample = false;
				while ( !resample ) {
					TimeSpan elapsed = clock.GetElapsedTime( cycleStarted, clock.GetTimestamp() );
					TimeSpan remaining = parsed.State.Delay > elapsed
						? parsed.State.Delay - elapsed
						: TimeSpan.Zero;
					if ( TimeSpan.Zero >= remaining ) {
						break;
					}

					TopTerminalEvent terminalEvent = await terminal.ReadEventAsync(
						remaining,
						token
					).ConfigureAwait( false );
					if ( TopTerminalEventKind.Timeout == terminalEvent.Kind ) {
						break;
					}
					if ( TopTerminalEventKind.Interrupt == terminalEvent.Kind ) {
						return Canceled;
					}
					if ( TopTerminalEventKind.Repaint == terminalEvent.Kind ) {
						await terminal.RepaintAsync( token ).ConfigureAwait( false );
						continue;
					}
					if ( TopTerminalEventKind.Resize == terminalEvent.Kind ) {
						dimensions = terminal.GetDimensions();
						await RenderInteractiveAsync(
							terminal,
							sample,
							parsed.State,
							dimensions,
							token
						).ConfigureAwait( false );
						continue;
					}
					if ( TopTerminalEventKind.Input != terminalEvent.Kind || !terminalEvent.Input.HasValue ) {
						continue;
					}

					TopCommandAction action = await HandleInputAsync(
						terminalEvent.Input.Value,
						sample,
						parsed.State,
						processProvider,
						accountResolver,
						processControl,
						dimensions,
						token
					).ConfigureAwait( false );
					if ( TopCommandAction.Exit == action ) {
						return Success;
					}
					if ( TopCommandAction.Resample == action ) {
						resample = true;
						continue;
					}
					if ( TopCommandAction.Rerender == action ) {
						await RenderInteractiveAsync(
							terminal,
							sample,
							parsed.State,
							dimensions,
							token
						).ConfigureAwait( false );
					}
				}
			}
		} finally {
			if ( terminal is not null ) {
				await terminal.DisposeAsync().ConfigureAwait( false );
			}
		}
	}

	private static async ValueTask RenderInteractiveAsync(
		ITopTerminalSession terminal,
		TopSample sample,
		TopRuntimeState state,
		TopTerminalDimensions dimensions,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( terminal );
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		if ( !IsUsableDimensions( dimensions ) ) {
			var lines = new List<TopRenderLine> {
				new(
					$"top: terminal too small ({dimensions.Columns}x{dimensions.Rows}); need {MinimumColumns}x{MinimumRows}",
					TopLineStyle.Message
				)
			};
			await terminal.RenderAsync(
				new TopRenderFrame(
					lines,
					Math.Max( 1, dimensions.Columns ),
					Math.Max( 1, dimensions.Rows )
				),
				cancellationToken
			).ConfigureAwait( false );
			return;
		}
		await terminal.RenderAsync(
			TopRenderer.RenderInteractive( sample, state, dimensions ),
			cancellationToken
		).ConfigureAwait( false );
	}

	private static async Task<TopCommandAction> HandleInputAsync(
		TopInputEvent input,
		TopSample sample,
		TopRuntimeState state,
		IProcProcessProvider processProvider,
		IProcAccountDisplayResolver accountResolver,
		ITopProcessControl processControl,
		TopTerminalDimensions dimensions,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		ArgumentNullException.ThrowIfNull( processProvider );
		ArgumentNullException.ThrowIfNull( accountResolver );
		ArgumentNullException.ThrowIfNull( processControl );

		if ( state.Prompt is not null ) {
			return await HandlePromptInputAsync(
				input,
				sample,
				state,
				processProvider,
				accountResolver,
				processControl,
				cancellationToken
			).ConfigureAwait( false );
		}
		if ( state.ShowHelp ) {
			state.ShowHelp = false;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.EndOfInput == input.Key ) {
			return TopCommandAction.Exit;
		}
		if ( TopInputKey.Up == input.Key ) {
			state.VerticalOffset = Math.Max( 0, state.VerticalOffset - 1 );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Down == input.Key ) {
			state.VerticalOffset++;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.PageUp == input.Key ) {
			state.VerticalOffset = Math.Max( 0, state.VerticalOffset - Math.Max( 1, dimensions.Rows - 7 ) );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.PageDown == input.Key ) {
			state.VerticalOffset += Math.Max( 1, dimensions.Rows - 7 );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Home == input.Key ) {
			state.VerticalOffset = 0;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.End == input.Key ) {
			state.VerticalOffset = int.MaxValue;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Left == input.Key ) {
			state.HorizontalOffset = Math.Max( 0, state.HorizontalOffset - 8 );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Right == input.Key ) {
			state.HorizontalOffset += 8;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Enter == input.Key ) {
			return TopCommandAction.Resample;
		}
		if ( TopInputKey.Character != input.Key || !input.Character.HasValue ) {
			return TopCommandAction.None;
		}

		int value = input.Character.Value.Value;
		if ( ' ' == value ) {
			return TopCommandAction.Resample;
		}
		char key = 0x7f >= value ? (char)value : '\0';
		switch ( key ) {
			case 'q':
			case 'Q':
				return TopCommandAction.Exit;
			case 'P':
				state.SortField = TopSortField.Cpu;
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'M':
				state.SortField = TopSortField.Memory;
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'N':
				state.SortField = TopSortField.Pid;
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'T':
				state.SortField = TopSortField.Time;
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'c':
				state.ShowCommandLine = !state.ShowCommandLine;
				return TopCommandAction.Rerender;
			case 'H':
				state.ShowThreads = !state.ShowThreads;
				state.VerticalOffset = 0;
				return TopCommandAction.Resample;
			case 'i':
				state.HideIdle = !state.HideIdle;
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'V':
				state.Forest = !state.Forest;
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'I':
				state.IrixMode = !state.IrixMode;
				state.Message = state.IrixMode
					? "Irix mode On: 100% CPU represents one processor"
					: "Irix mode Off: CPU is normalized to total processor capacity";
				return TopCommandAction.Rerender;
			case 'E':
				state.SummaryScale = TopRenderer.NextScale( state.SummaryScale );
				return TopCommandAction.Rerender;
			case 'e':
				state.TaskScale = TopRenderer.NextScale( state.TaskScale );
				return TopCommandAction.Rerender;
			case '1':
				state.SingleCpuSummary = !state.SingleCpuSummary;
				state.Message = "Per-CPU activity rows are not yet exposed by the shared metrics contract; aggregate CPU remains shown.";
				return TopCommandAction.Rerender;
			case 'd':
			case 's':
				if ( state.SecureMode ) {
					state.Message = "secure mode: changing the delay is disabled";
					return TopCommandAction.Rerender;
				}
				state.Prompt = new TopPromptState(
					TopPromptKind.Delay,
					$"Change delay from {state.Delay.TotalSeconds:0.###} to: "
				);
				return TopCommandAction.Rerender;
			case 'k':
				if ( state.SecureMode ) {
					state.Message = "secure mode: signalling processes is disabled";
					return TopCommandAction.Rerender;
				}
				state.Prompt = new TopPromptState( TopPromptKind.KillProcessId, "PID to signal: " );
				return TopCommandAction.Rerender;
			case 'r':
				if ( state.SecureMode ) {
					state.Message = "secure mode: changing priorities is disabled";
					return TopCommandAction.Rerender;
				}
				state.Prompt = new TopPromptState( TopPromptKind.ReniceProcessId, "PID to renice: " );
				return TopCommandAction.Rerender;
			case 'u':
				state.Prompt = new TopPromptState(
					TopPromptKind.EffectiveUser,
					"Effective user (blank clears): "
				);
				return TopCommandAction.Rerender;
			case 'U':
				state.Prompt = new TopPromptState(
					TopPromptKind.AnyUser,
					"Any observed user (blank clears): "
				);
				return TopCommandAction.Rerender;
			case '=':
				state.ProcessIds.Clear();
				state.UserFilter = null;
				state.VerticalOffset = 0;
				state.HorizontalOffset = 0;
				state.Message = "filters and scrolling reset";
				return TopCommandAction.Rerender;
			case 'h':
			case '?':
				state.ShowHelp = true;
				return TopCommandAction.Rerender;
			default:
				return TopCommandAction.None;
		}
	}

	private static async Task<TopCommandAction> HandlePromptInputAsync(
		TopInputEvent input,
		TopSample sample,
		TopRuntimeState state,
		IProcProcessProvider processProvider,
		IProcAccountDisplayResolver accountResolver,
		ITopProcessControl processControl,
		CancellationToken cancellationToken
	) {
		TopPromptState prompt = state.Prompt
			?? throw new InvalidOperationException( "Prompt input was requested without an active prompt." );
		if ( TopInputKey.Escape == input.Key ) {
			state.Prompt = null;
			state.Message = "command canceled";
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Backspace == input.Key || TopInputKey.Delete == input.Key ) {
			prompt.Buffer = RemoveLastTextElement( prompt.Buffer );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Character == input.Key && input.Character.HasValue ) {
			if ( !Rune.IsControl( input.Character.Value ) ) {
				prompt.Buffer += input.Character.Value.ToString();
			}
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Enter != input.Key ) {
			return TopCommandAction.None;
		}

		string text = prompt.Buffer.Trim();
		switch ( prompt.Kind ) {
			case TopPromptKind.Delay:
				if ( !TryParseDelay( text, out TimeSpan delay ) ) {
					state.Message = "invalid delay; enter a nonnegative finite number of seconds";
					state.Prompt = null;
					return TopCommandAction.Rerender;
				}
				state.Delay = delay;
				state.Prompt = null;
				state.Message = $"delay set to {delay.TotalSeconds:0.###} seconds";
				return TopCommandAction.Resample;

			case TopPromptKind.KillProcessId:
				if ( !TryParsePositiveProcessId( text, out int killPid ) ) {
					state.Prompt = null;
					state.Message = "invalid process identifier";
					return TopCommandAction.Rerender;
				}
				state.Prompt = new TopPromptState(
					TopPromptKind.KillSignal,
					$"Signal to PID {killPid} [TERM]: "
				) {
					ProcessId = killPid
				};
				return TopCommandAction.Rerender;

			case TopPromptKind.KillSignal:
				if ( !prompt.ProcessId.HasValue ) {
					throw new InvalidOperationException( "The signal prompt lost its process identifier." );
				}
				ProcessOperationResult<ProcessSignal> parsedSignal = processControl.ParseSignal(
					0 == text.Length ? "TERM" : text
				);
				if ( !parsedSignal.Succeeded || parsedSignal.Value is null ) {
					state.Prompt = null;
					state.Message = parsedSignal.Message ?? "invalid signal";
					return TopCommandAction.Rerender;
				}
				ProcProcessSnapshot? signalTarget = await ResolveTargetAsync(
					prompt.ProcessId.Value,
					sample,
					processProvider,
					cancellationToken
				).ConfigureAwait( false );
				if ( signalTarget is null ) {
					state.Prompt = null;
					state.Message = $"process {prompt.ProcessId.Value} is no longer available";
					return TopCommandAction.Rerender;
				}
				ProcessOperationResult signalResult = await processControl.SignalAsync(
					signalTarget,
					parsedSignal.Value,
					cancellationToken
				).ConfigureAwait( false );
				state.Prompt = null;
				state.Message = signalResult.Succeeded
					? $"sent {parsedSignal.Value} to PID {signalTarget.ProcessId}"
					: $"signal failed: {signalResult.Message ?? signalResult.Status.ToString()}";
				return signalResult.Succeeded ? TopCommandAction.Resample : TopCommandAction.Rerender;

			case TopPromptKind.ReniceProcessId:
				if ( !TryParsePositiveProcessId( text, out int renicePid ) ) {
					state.Prompt = null;
					state.Message = "invalid process identifier";
					return TopCommandAction.Rerender;
				}
				state.Prompt = new TopPromptState(
					TopPromptKind.ReniceValue,
					$"Nice value for PID {renicePid} (-20..19): "
				) {
					ProcessId = renicePid
				};
				return TopCommandAction.Rerender;

			case TopPromptKind.ReniceValue:
				if ( !prompt.ProcessId.HasValue ) {
					throw new InvalidOperationException( "The renice prompt lost its process identifier." );
				}
				if ( !int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int niceValue )
					|| niceValue is < -20 or > 19 ) {
					state.Prompt = null;
					state.Message = "nice value must be between -20 and 19";
					return TopCommandAction.Rerender;
				}
				ProcProcessSnapshot? priorityTarget = await ResolveTargetAsync(
					prompt.ProcessId.Value,
					sample,
					processProvider,
					cancellationToken
				).ConfigureAwait( false );
				if ( priorityTarget is null ) {
					state.Prompt = null;
					state.Message = $"process {prompt.ProcessId.Value} is no longer available";
					return TopCommandAction.Rerender;
				}
				ProcessOperationResult priorityResult = processControl.SetPriority( priorityTarget, niceValue );
				state.Prompt = null;
				state.Message = priorityResult.Succeeded
					? $"PID {priorityTarget.ProcessId} nice value set to {niceValue}"
					: $"renice failed: {priorityResult.Message ?? priorityResult.Status.ToString()}";
				return priorityResult.Succeeded ? TopCommandAction.Resample : TopCommandAction.Rerender;

			case TopPromptKind.EffectiveUser:
			case TopPromptKind.AnyUser:
				state.Prompt = null;
				if ( 0 == text.Length ) {
					state.UserFilter = null;
					state.VerticalOffset = 0;
					state.Message = "user filter cleared";
					return TopCommandAction.Rerender;
				}
				if ( !TryParseUserFilter(
					text,
					TopPromptKind.AnyUser == prompt.Kind,
					accountResolver,
					out TopUserFilter? userFilter,
					out string? error
				) ) {
					state.Message = error;
					return TopCommandAction.Rerender;
				}
				state.UserFilter = userFilter;
				state.VerticalOffset = 0;
				state.Message = "user filter updated";
				return TopCommandAction.Rerender;
			default:
				throw new ArgumentOutOfRangeException( nameof( prompt.Kind ) );
		}
	}

	private static async Task<ProcProcessSnapshot?> ResolveTargetAsync(
		int processId,
		TopSample sample,
		IProcProcessProvider processProvider,
		CancellationToken cancellationToken
	) {
		TopTaskRow? current = sample.Tasks.FirstOrDefault(
			row => row.Process.ProcessId == processId
		);
		if ( current is not null ) {
			return current.Process;
		}
		ProcObservedValue<ProcProcessSnapshot> observed = await processProvider.GetProcessAsync(
			processId,
			cancellationToken
		).ConfigureAwait( false );
		return observed.HasValue ? observed.Value : null;
	}

	private static ParsedArguments Parse(
		IReadOnlyList<string> args,
		IProcAccountDisplayResolver accountResolver,
		Func<string, string?> environmentVariableProvider,
		int currentProcessId
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( accountResolver );
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( currentProcessId );
		var result = new ParsedArguments();
		string? columns = environmentVariableProvider( "COLUMNS" );
		if ( int.TryParse( columns, NumberStyles.None, CultureInfo.InvariantCulture, out int environmentWidth )
			&& 0 < environmentWidth ) {
			result.BatchWidth = environmentWidth;
		}

		for ( int index = 0; index < args.Count && result.Error is null; index++ ) {
			string argument = args[ index ];
			if ( "--" == argument ) {
				if ( index + 1 < args.Count ) {
					result.Fail( $"unexpected operand '{args[ index + 1 ]}'" );
				}
				break;
			}
			if ( argument is "-h" or "--help" ) {
				result.Help = true;
				continue;
			}
			if ( argument is "-V" or "--version" ) {
				result.Version = true;
				continue;
			}
			if ( argument is "-A" or "--apply-defaults" ) {
				result.ApplyDefaults = true;
				continue;
			}
			if ( argument is "-b" or "--batch" ) {
				result.Batch = true;
				continue;
			}
			if ( argument is "-c" or "--cmdline-toggle" ) {
				result.State.ShowCommandLine = true;
				continue;
			}
			if ( argument is "-H" or "--threads-show" ) {
				result.State.ShowThreads = true;
				continue;
			}
			if ( argument is "-i" or "--idle-toggle" ) {
				result.State.HideIdle = true;
				continue;
			}
			if ( argument is "-O" or "--list-fields" ) {
				result.ListFields = true;
				continue;
			}
			if ( argument is "-s" or "--secure-mode" ) {
				result.State.SecureMode = true;
				continue;
			}
			if ( argument is "-S" or "--accum-time-toggle" ) {
				result.Fail( "-S/--accum-time-toggle is unavailable because the shared process contract does not expose dead-child CPU counters" );
				continue;
			}
			if ( argument is "-1" or "--single-cpu-toggle" ) {
				result.State.SingleCpuSummary = false;
				continue;
			}

			if ( TryOptionValue( args, ref index, argument, "-d", "--delay", out string? delayText ) ) {
				if ( !TryParseDelay( delayText!, out TimeSpan delay ) ) {
					result.Fail( $"invalid delay '{delayText}'" );
				} else {
					result.State.Delay = delay;
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-E", "--scale-summary-mem", out string? summaryScale ) ) {
				if ( !TopRenderer.TryParseScale( summaryScale!, out TopMemoryScale scale ) ) {
					result.Fail( $"invalid summary memory scale '{summaryScale}'" );
				} else {
					result.State.SummaryScale = scale;
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-e", "--scale-task-mem", out string? taskScale ) ) {
				if ( !TopRenderer.TryParseScale( taskScale!, out TopMemoryScale scale ) ) {
					result.Fail( $"invalid task memory scale '{taskScale}'" );
				} else {
					result.State.TaskScale = scale;
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-n", "--iterations", out string? iterationText ) ) {
				if ( !int.TryParse( iterationText, NumberStyles.None, CultureInfo.InvariantCulture, out int iterations )
					|| 0 >= iterations ) {
					result.Fail( $"invalid iteration count '{iterationText}'" );
				} else {
					result.Iterations = iterations;
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-o", "--sort-override", out string? sortText ) ) {
				if ( !TopRenderer.TryParseSortField( sortText!, out TopSortField sortField ) ) {
					result.Fail( $"unknown sort field '{sortText}'" );
				} else {
					result.State.SortField = sortField;
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-p", "--pid", out string? pidText ) ) {
				if ( !AddProcessIds( pidText!, result.State.ProcessIds, currentProcessId, out string? error ) ) {
					result.Fail( error! );
				} else if ( MaximumMonitoredProcessIds < result.State.ProcessIds.Count ) {
					result.Fail( $"no more than {MaximumMonitoredProcessIds} process IDs may be monitored" );
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-u", "--filter-only-euser", out string? effectiveUser ) ) {
				if ( !TryParseUserFilter( effectiveUser!, false, accountResolver, out TopUserFilter? filter, out string? error ) ) {
					result.Fail( error! );
				} else {
					result.State.UserFilter = filter;
				}
				continue;
			}
			if ( TryOptionValue( args, ref index, argument, "-U", "--filter-any-user", out string? anyUser ) ) {
				if ( !TryParseUserFilter( anyUser!, true, accountResolver, out TopUserFilter? filter, out string? error ) ) {
					result.Fail( error! );
				} else {
					result.State.UserFilter = filter;
				}
				continue;
			}
			if ( TryParseWidthOption( args, ref index, argument, out int? width, out bool matched, out string? widthError ) ) {
				if ( widthError is not null ) {
					result.Fail( widthError );
				} else if ( matched ) {
					result.BatchWidth = width ?? DefaultBatchWidth;
				}
				continue;
			}

			result.Fail( $"unknown option '{argument}'" );
		}

		if ( result.Error is null && result.ApplyDefaults && 1 != args.Count ) {
			result.Fail( "-A/--apply-defaults must be the only command-line option" );
		}
		if ( result.Error is null
			&& 0 < result.State.ProcessIds.Count
			&& result.State.UserFilter is not null ) {
			result.Fail( "-p/--pid is mutually exclusive with -u and -U user filtering" );
		}
		return result;
	}

	private static bool TryOptionValue(
		IReadOnlyList<string> args,
		ref int index,
		string argument,
		string shortName,
		string longName,
		out string? value
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( argument );
		ArgumentNullException.ThrowIfNull( shortName );
		ArgumentNullException.ThrowIfNull( longName );
		value = null;
		if ( argument == shortName || argument == longName ) {
			value = index + 1 < args.Count
				? args[ ++index ]
				: string.Empty;
			return true;
		}
		string longPrefix = longName + "=";
		if ( argument.StartsWith( longPrefix, StringComparison.Ordinal ) ) {
			value = argument[ longPrefix.Length.. ];
			return true;
		}
		if ( 2 == shortName.Length
			&& argument.StartsWith( shortName, StringComparison.Ordinal )
			&& argument.Length > shortName.Length ) {
			value = argument[ shortName.Length.. ];
			return true;
		}
		return false;
	}

	private static bool TryParseWidthOption(
		IReadOnlyList<string> args,
		ref int index,
		string argument,
		out int? width,
		out bool matched,
		out string? error
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( argument );
		width = null;
		matched = false;
		error = null;
		string? text = null;
		if ( "-w" == argument || "--width" == argument ) {
			matched = true;
			if ( index + 1 < args.Count
				&& !args[ index + 1 ].StartsWith( "-", StringComparison.Ordinal )
				&& int.TryParse( args[ index + 1 ], NumberStyles.None, CultureInfo.InvariantCulture, out _ ) ) {
				text = args[ ++index ];
			}
		} else if ( argument.StartsWith( "--width=", StringComparison.Ordinal ) ) {
			matched = true;
			text = argument[ "--width=".Length.. ];
		} else if ( argument.StartsWith( "-w", StringComparison.Ordinal ) && 2 < argument.Length ) {
			matched = true;
			text = argument[ 2.. ];
		}
		if ( !matched ) {
			return false;
		}
		if ( text is null ) {
			width = null;
			return true;
		}
		if ( !int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedWidth )
			|| 0 >= parsedWidth ) {
			error = $"invalid output width '{text}'";
			return true;
		}
		width = parsedWidth;
		return true;
	}

	private static bool AddProcessIds(
		string text,
		ISet<int> destination,
		int currentProcessId,
		out string? error
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( destination );
		error = null;
		string[] tokens = text.Split(
			new[] { ',', ' ', '\t' },
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
		);
		if ( 0 == tokens.Length ) {
			error = "a process ID list is required";
			return false;
		}
		foreach ( string token in tokens ) {
			if ( !int.TryParse( token, NumberStyles.None, CultureInfo.InvariantCulture, out int processId )
				|| 0 > processId ) {
				error = $"invalid process identifier '{token}'";
				return false;
			}
			destination.Add( 0 == processId ? currentProcessId : processId );
		}
		return true;
	}

	private static bool TryParseUserFilter(
		string text,
		bool anyUser,
		IProcAccountDisplayResolver accountResolver,
		out TopUserFilter? filter,
		out string? error
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( accountResolver );
		filter = null;
		error = null;
		string normalized = text.Trim();
		bool negate = normalized.StartsWith( '!' );
		if ( negate ) {
			normalized = normalized[ 1.. ];
		}
		if ( 0 == normalized.Length ) {
			error = "a user name or numeric identifier is required";
			return false;
		}
		if ( !accountResolver.TryResolveUser( normalized, out uint userId ) ) {
			error = $"unknown user '{normalized}'";
			return false;
		}
		filter = new TopUserFilter( userId, anyUser, negate );
		return true;
	}

	private static bool TryParseDelay( string text, out TimeSpan delay ) {
		ArgumentNullException.ThrowIfNull( text );
		delay = default;
		if ( !double.TryParse(
			text,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out double seconds
		) || !double.IsFinite( seconds ) || 0.0 > seconds ) {
			return false;
		}
		try {
			delay = TimeSpan.FromSeconds( seconds );
			return true;
		} catch ( OverflowException ) {
			return false;
		}
	}

	private static bool TryParsePositiveProcessId( string text, out int processId ) =>
		int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out processId )
		&& 0 < processId;

	private static string RemoveLastTextElement( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		if ( 0 == text.Length ) {
			return text;
		}
		int[] starts = StringInfo.ParseCombiningCharacters( text );
		return 0 == starts.Length ? string.Empty : text[ ..starts[ ^1 ] ];
	}

	private static bool IsUsableDimensions( TopTerminalDimensions dimensions ) =>
		dimensions.Columns >= MinimumColumns && dimensions.Rows >= MinimumRows;

	private static string NormalizeLineEndings( string value ) {
		ArgumentNullException.ThrowIfNull( value );
		string normalized = value
			.Replace( "\r\n", "\n", StringComparison.Ordinal )
			.Replace( "\r", "\n", StringComparison.Ordinal );
		return "\n" == Environment.NewLine
			? normalized
			: normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}

	private static async Task WriteLineAsync(
		Stream output,
		string text,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( output );
		ArgumentNullException.ThrowIfNull( text );
		await WriteTextAsync(
			output,
			text + Environment.NewLine,
			cancellationToken
		).ConfigureAwait( false );
	}

	private static async Task WriteTextAsync(
		Stream output,
		string text,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( output );
		ArgumentNullException.ThrowIfNull( text );
		byte[] bytes = Utf8.GetBytes( text );
		await output.WriteAsync( bytes.AsMemory(), cancellationToken ).ConfigureAwait( false );
	}

	private enum TopCommandAction {
		None,
		Rerender,
		Resample,
		Exit
	}

	private sealed class ParsedArguments {
		internal bool ApplyDefaults { get; set; }
		internal bool Batch { get; set; }
		internal bool Help { get; set; }
		internal bool Version { get; set; }
		internal bool ListFields { get; set; }
		internal int? Iterations { get; set; }
		internal int BatchWidth { get; set; } = DefaultBatchWidth;
		internal TopRuntimeState State { get; } = new();
		internal string? Error { get; private set; }

		internal void Fail( string error ) {
			ArgumentException.ThrowIfNullOrWhiteSpace( error );
			this.Error ??= error;
		}
	}
}

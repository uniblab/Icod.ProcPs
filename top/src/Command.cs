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
 -A, --apply-defaults          use built-in defaults plus system restrictions
 -b, --batch                   run in non-interactive batch mode
 -c, --cmdline-toggle          reverse the remembered command name/line state
 -d, --delay SECONDS           set the refresh delay; fractional seconds are accepted
 -E, --scale-summary-mem SCALE set summary memory scale: k, m, g, t, p, or e
 -e, --scale-task-mem SCALE    set task memory scale: k, m, g, t, p, or e
 -H, --threads-show            show individual lightweight tasks where supported
 -i, --idle-toggle             reverse the remembered idle-task state
 -n, --iterations NUMBER       exit after NUMBER refreshes
 -O, --list-fields             list fields implemented by this top and exit
 -o, --sort-override FIELD     sort by PID, USER, PR, NI, VIRT, RES, SHR, S, %CPU, %MEM, TIME+, or COMMAND
 -p, --pid PIDLIST             monitor only the selected process IDs (maximum 20)
 -s, --secure-mode             force secure mode, including for privileged users
 -S, --accum-time-toggle       not available: child CPU counters are not yet observed
 -U, --filter-any-user USER    filter by any observed real/effective user ID
 -u, --filter-only-euser USER  filter by effective user ID
 -w, --width [COLUMNS]         batch output width; without COLUMNS use 512
 -1, --single-cpu-toggle       reverse the remembered aggregate CPU state
 -V, --version                 display version information and exit
 -h, --help                    display this help and exit

Interactive keys:
 q quit; 0 zero suppress; n/# max tasks; P/M/N/T sort; R reverse/normal sort;
 A alternate display; a/w next/previous window; g/G choose/rename window;
 -/_ show/hide current/all windows; =/+ reset current/all windows; B bold enable;
 b emphasis mode; J numeric justify; j character justify; f manage fields;
 x sort column; y running rows; z colors; Z map colors; l load/uptime; t CPU summary;
 m memory summary; C scroll coordinates; </> move sort field; c command line;
 H threads; i idle tasks; V forest; F focus parent; v hide/show children; X fixed width;
 Y inspect; I CPU normalization; E/e memory scale; d/s delay; u/U user filter;
 O/o other filter; L locate; & locate next; k signal; r renice; W write config;
 arrows/PgUp/PgDn/Home/End scroll; h/? help.
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
		CancellationToken cancellationToken,
		SystemTopConfigurationStore? configurationStore = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminalFactory );
		ArgumentNullException.ThrowIfNull( processControl );

		Stream output = stdout ?? Console.OpenStandardOutput();
		Stream errorOutput = stderr ?? Console.OpenStandardError();
		IProcAccountDisplayResolver accounts = accountResolver ?? SystemProcAccountDisplayResolver.Instance;
		Func<string, string?> environment = environmentVariableProvider ?? Environment.GetEnvironmentVariable;
		int currentProcessId = currentProcessIdProvider?.Invoke() ?? Environment.ProcessId;
		configurationStore ??= new SystemTopConfigurationStore(
			environment
		);
		TopRuntimeState startupState = new();
		if ( ShouldLoadConfiguration( args ) ) {
			try {
				await configurationStore.LoadAsync(
					startupState,
					ShouldLoadPersonalConfiguration( args ),
					cancellationToken
				).ConfigureAwait( false );
			} catch ( OperationCanceledException ) {
				return Canceled;
			} catch ( Exception exception ) when (
				exception is IOException
					or UnauthorizedAccessException
					or FormatException
			) {
				await WriteLineAsync(
					errorOutput,
					$"top: {exception.Message}",
					CancellationToken.None
				).ConfigureAwait( false );
				return Failure;
			}
		}
		if ( ForcesSecureMode( args ) ) {
			startupState.SecureMode = true;
		}
		ParsedArguments parsed = Parse(
			args,
			accounts,
			environment,
			currentProcessId,
			startupState
		);
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
					configurationStore,
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
		SystemTopConfigurationStore configurationStore,
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
		ArgumentNullException.ThrowIfNull( configurationStore );
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
				if (
					parsed.Iterations.HasValue
					&& parsed.Iterations.Value <= completed
				) {
					return Success;
				}

				bool resample = false;
				while ( !resample ) {
					bool inspectPaused = parsed.State.InspectSession is not null;
					TimeSpan remaining;
					if ( inspectPaused ) {
						remaining = TimeSpan.FromSeconds( 1 );
					} else {
						TimeSpan elapsed = clock.GetElapsedTime(
							cycleStarted,
							clock.GetTimestamp()
						);
						remaining = parsed.State.Delay > elapsed
							? parsed.State.Delay - elapsed
							: TimeSpan.Zero;
						if ( remaining <= TimeSpan.Zero ) {
							break;
						}
					}

					TopTerminalEvent terminalEvent = await terminal.ReadEventAsync(
						remaining,
						token
					).ConfigureAwait( false );
					if ( TopTerminalEventKind.Timeout == terminalEvent.Kind ) {
						if ( inspectPaused ) {
							continue;
						}
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
						configurationStore,
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
					Math.Max( 1, dimensions.Rows ),
					state.BoldEnabled
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
		SystemTopConfigurationStore configurationStore,
		TopTerminalDimensions dimensions,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		ArgumentNullException.ThrowIfNull( processProvider );
		ArgumentNullException.ThrowIfNull( accountResolver );
		ArgumentNullException.ThrowIfNull( processControl );
		ArgumentNullException.ThrowIfNull( configurationStore );

		if ( state.InspectSession is not null ) {
			TopInspectInputResult inspectResult = await state.InspectSession.HandleInputAsync(
				input,
				dimensions,
				cancellationToken
			).ConfigureAwait( false );
			if ( TopInspectInputResult.Close == inspectResult ) {
				state.InspectSession = null;
				state.Message = null;
				return TopCommandAction.Resample;
			}
			return ( TopInspectInputResult.Changed == inspectResult )
				? TopCommandAction.Rerender
				: TopCommandAction.None
			;
		}

		if ( state.ColorManager is not null ) {
			return HandleColorManagerInput(
				input,
				state
			);
		}
		if ( state.ShowFieldManager ) {
			return HandleFieldManagerInput(
				input,
				state,
				dimensions
			);
		}
		if ( state.Prompt is not null ) {
			return await HandlePromptInputAsync(
				input,
				sample,
				state,
				dimensions,
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
		int pageSize = Math.Max(
			1,
			TopRenderer.GetTaskPageSize(
				state,
				dimensions
			)
		);

		if ( TopInputKey.Up == input.Key ) {
			state.VerticalOffset = Math.Max( 0, state.VerticalOffset - 1 );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Down == input.Key ) {
			state.VerticalOffset++;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.PageUp == input.Key ) {
			state.VerticalOffset = Math.Max( 0, state.VerticalOffset - pageSize );
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.PageDown == input.Key ) {
			state.VerticalOffset += pageSize;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Home == input.Key ) {
			state.VerticalOffset = 0;
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.End == input.Key ) {
			state.VerticalOffset = TopRenderer.GetEndOffset(
				sample,
				state,
				dimensions
			);
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
		char key = value <= 0x7f ? (char)value : '\0';
		switch ( key ) {
			case 'q':
			case 'Q':
				return TopCommandAction.Exit;
			case 'P':
				state.SortField = TopFieldId.Cpu;
				state.ExitForestForSort();
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'M':
				state.SortField = TopFieldId.Memory;
				state.ExitForestForSort();
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'N':
				state.SortField = TopFieldId.Pid;
				state.ExitForestForSort();
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case 'T':
				state.SortField = TopFieldId.Time;
				state.ExitForestForSort();
				state.VerticalOffset = 0;
				return TopCommandAction.Rerender;
			case '0':
				state.SuppressZeros = !state.SuppressZeros;
				return TopCommandAction.Rerender;
			case 'R':
				state.SortHighToLow = !state.SortHighToLow;
				state.ExitForestForSort();
				state.VerticalOffset = 0;
				state.Message = state.SortHighToLow
					? "sort direction: high to low"
					: "sort direction: low to high";
				return TopCommandAction.Rerender;
			case 'A':
				state.SynchronizeCurrentWindow();
				state.AlternateDisplayMode = !state.AlternateDisplayMode;
				state.Message = ( state.AlternateDisplayMode )
					? "alternate display mode enabled"
					: $"full-screen display: {state.CurrentWindowLabel}"
				;
				return TopCommandAction.Rerender;
			case '-':
				if ( !state.AlternateDisplayMode ) {
					state.Message = "window visibility is available only in alternate-display mode";
					return TopCommandAction.Rerender;
				}
				state.TaskDisplayVisible = !state.TaskDisplayVisible;
				state.SynchronizeCurrentWindow();
				state.Message = ( state.TaskDisplayVisible )
					? $"{state.CurrentWindowLabel} task display shown"
					: $"{state.CurrentWindowLabel} task display hidden"
				;
				return TopCommandAction.Rerender;
			case '_':
				if ( !state.AlternateDisplayMode ) {
					state.Message = "window visibility is available only in alternate-display mode";
					return TopCommandAction.Rerender;
				}
				state.ToggleAllTaskDisplays();
				state.Message = "all task-window visibility toggled";
				return TopCommandAction.Rerender;
			case 'a':
				ActivateRelativeWindow(
					state,
					1
				);
				return TopCommandAction.Rerender;
			case 'w':
				ActivateRelativeWindow(
					state,
					-1
				);
				return TopCommandAction.Rerender;
			case 'g':
				state.Prompt = new TopPromptState(
					TopPromptKind.Window,
					"Choose window (1-4): "
				);
				return TopCommandAction.Rerender;
			case 'G':
				state.Prompt = new TopPromptState(
					TopPromptKind.WindowName,
					$"Name {state.CurrentWindowLabel} (1-3 UTF-8 bytes): "
				);
				return TopCommandAction.Rerender;
			case 'B':
				state.BoldEnabled = !state.BoldEnabled;
				return TopCommandAction.Rerender;
			case 'b':
				state.HighlightBold = !state.HighlightBold;
				return TopCommandAction.Rerender;
			case 'z':
				state.ColorsEnabled = !state.ColorsEnabled;
				return TopCommandAction.Rerender;
			case 'Z':
				state.ColorManager = new TopColorManagerState(
					state
				);
				return TopCommandAction.Rerender;
			case 'l':
				state.LoadAverageVisible = !state.LoadAverageVisible;
				return TopCommandAction.Rerender;
			case 'C':
				state.ScrollCoordinatesVisible = !state.ScrollCoordinatesVisible;
				return TopCommandAction.Rerender;
			case 't':
				state.CycleCpuSummaryPresentation();
				return TopCommandAction.Rerender;
			case 'm':
				state.CycleMemorySummaryPresentation();
				return TopCommandAction.Rerender;
			case 'J':
				state.NumericLeftJustified = !state.NumericLeftJustified;
				return TopCommandAction.Rerender;
			case 'j':
				state.CharacterRightJustified = !state.CharacterRightJustified;
				return TopCommandAction.Rerender;
			case 'f': {
				state.ShowFieldManager = true;
				state.FieldMoveActive = false;
				state.Message = null;
				int sortFieldIndex = state.FieldOrder.IndexOf(
					state.SortField
				);
				if ( 0 <= sortFieldIndex ) {
					state.FieldCursor = sortFieldIndex;
				} else {
					state.FieldCursor = 0;
				}
				return TopCommandAction.Rerender;
			}
			case '<':
				_ = state.MoveSortField( -1 );
				return TopCommandAction.Rerender;
			case '>':
				_ = state.MoveSortField( 1 );
				return TopCommandAction.Rerender;
			case 'x':
				state.HighlightSortColumn = !state.HighlightSortColumn;
				return TopCommandAction.Rerender;
			case 'y':
				state.HighlightRunning = !state.HighlightRunning;
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
			case 'F':
				if (
					!TopRenderer.ToggleForestFocus(
						sample,
						state
					)
				) {
					return TopCommandAction.None;
				}
				return TopCommandAction.Rerender;
			case 'v':
				if (
					!TopRenderer.ToggleTopmostForestChildren(
						sample,
						state
					)
				) {
					return TopCommandAction.None;
				}
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
			case 'X':
				state.Message = null;
				state.Prompt = new TopPromptState(
					TopPromptKind.FixedWidthExtra,
					$"Extra fixed width (-1 auto, 0 default, max {TopFixedWidth.MaximumExtra}): "
				);
				return TopCommandAction.Rerender;
			case 'Y': {
				state.Message = null;
				if ( 0 == state.InspectEntries.Count ) {
					state.Message = "no Inspect entries are configured";
					return TopCommandAction.Rerender;
				}
				int? defaultProcessId = TopRenderer.GetTopmostProcessId(
					sample,
					state
				);
				string label = ( defaultProcessId.HasValue )
					? $"Inspect PID [{defaultProcessId.Value}]: "
					: "Inspect PID: "
				;
				state.Prompt = new TopPromptState(
					TopPromptKind.InspectProcessId,
					label
				) {
					ProcessId = defaultProcessId
				};
				return TopCommandAction.Rerender;
			}
			case '1':
				state.SingleCpuSummary = !state.SingleCpuSummary;
				state.Message = "Per-CPU activity rows are not yet exposed by the shared metrics contract; aggregate CPU remains shown.";
				return TopCommandAction.Rerender;
			case 'n':
			case '#':
				state.Prompt = new TopPromptState(
					TopPromptKind.MaximumTasks,
					"Maximum tasks (0 for unlimited): "
				);
				return TopCommandAction.Rerender;
			case 'O':
				state.Message = null;
				state.Prompt = new TopPromptState(
					TopPromptKind.OtherFilterCaseSensitive,
					"Other filter (case sensitive): "
				);
				return TopCommandAction.Rerender;
			case 'o':
				state.Message = null;
				state.Prompt = new TopPromptState(
					TopPromptKind.OtherFilterIgnoreCase,
					"Other filter (ignore case): "
				);
				return TopCommandAction.Rerender;
			case 'L':
				state.Message = null;
				state.Prompt = new TopPromptState(
					TopPromptKind.Locate,
					"Locate string: "
				);
				return TopCommandAction.Rerender;
			case '&':
				if ( string.IsNullOrEmpty( state.SearchText ) ) {
					state.Message = "no locate string is active";
					return TopCommandAction.Rerender;
				}
				int nextLocate = TopRenderer.FindTaskOffset(
					sample,
					state,
					state.SearchText,
					state.VerticalOffset + 1,
					dimensions
				);
				if ( 0 <= nextLocate ) {
					state.VerticalOffset = nextLocate;
					state.Message = null;
				} else {
					state.Message = $"no further match for: {state.SearchText}";
				}
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
			case 'W':
				try {
					string path = await configurationStore.SaveAsync(
						state,
						cancellationToken
					).ConfigureAwait( false );
					state.Message = $"configuration written to {path}";
				} catch ( Exception exception ) when (
					exception is IOException
						or UnauthorizedAccessException
				) {
					state.Message = $"configuration write failed: {exception.Message}";
				}
				return TopCommandAction.Rerender;
			case '=':
				state.ProcessIds.Clear();
				ResetCurrentWindowDisplayLimits(
					state
				);
				state.Message = $"display limits reset for {state.CurrentWindowLabel}";
				return TopCommandAction.Rerender;
			case '+':
				state.ProcessIds.Clear();
				ResetAllWindowDisplayLimits(
					state
				);
				state.Message = "display limits reset for all windows";
				return TopCommandAction.Rerender;
			case 'h':
			case '?':
				state.ShowHelp = true;
				return TopCommandAction.Rerender;
			default:
				return TopCommandAction.None;
		}
	}

	private static TopCommandAction HandleColorManagerInput(
		TopInputEvent input,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		TopColorManagerState manager = state.ColorManager
			?? throw new InvalidOperationException(
				"Color mapping input was requested without an active color manager."
			);
		if ( TopInputKey.EndOfInput == input.Key ) {
			manager.Restore(
				state
			);
			state.ColorManager = null;
			return TopCommandAction.Exit;
		}

		TopColorManagerInputResult result = manager.HandleInput(
			input,
			state
		);
		switch ( result ) {
			case TopColorManagerInputResult.Commit:
			case TopColorManagerInputResult.Cancel:
				state.ColorManager = null;
				state.Message = null;
				return TopCommandAction.Rerender;
			case TopColorManagerInputResult.Changed:
				return TopCommandAction.Rerender;
			default:
				return TopCommandAction.None;
		}
	}

	private static TopCommandAction HandleFieldManagerInput(
		TopInputEvent input,
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}
		if ( 0 == state.FieldOrder.Count ) {
			throw new InvalidOperationException(
				"The top field order cannot be empty."
			);
		}

		if ( TopInputKey.EndOfInput == input.Key ) {
			return TopCommandAction.Exit;
		}
		if ( TopInputKey.Escape == input.Key ) {
			state.ShowFieldManager = false;
			state.FieldMoveActive = false;
			return TopCommandAction.Rerender;
		}

		int pageSize = Math.Max(
			1,
			dimensions.Rows - 3
		);
		if ( TopInputKey.Up == input.Key ) {
			MoveFieldCursor(
				state,
				state.FieldCursor - 1
			);
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Down == input.Key ) {
			MoveFieldCursor(
				state,
				state.FieldCursor + 1
			);
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.PageUp == input.Key ) {
			MoveFieldCursor(
				state,
				state.FieldCursor - pageSize
			);
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.PageDown == input.Key ) {
			MoveFieldCursor(
				state,
				state.FieldCursor + pageSize
			);
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Home == input.Key ) {
			MoveFieldCursor(
				state,
				0
			);
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.End == input.Key ) {
			MoveFieldCursor(
				state,
				state.FieldOrder.Count - 1
			);
			return TopCommandAction.Rerender;
		}
		if ( TopInputKey.Right == input.Key ) {
			state.FieldMoveActive = true;
			return TopCommandAction.Rerender;
		}
		if (
			TopInputKey.Left == input.Key
			|| TopInputKey.Enter == input.Key
		) {
			state.FieldMoveActive = false;
			return TopCommandAction.Rerender;
		}
		if (
			TopInputKey.Character != input.Key
			|| !input.Character.HasValue
		) {
			return TopCommandAction.None;
		}

		int value = input.Character.Value.Value;
		char key = '\0';
		if ( value <= 0x7f ) {
			key = (char)value;
		}
		switch ( key ) {
			case 'q':
			case 'Q':
				state.ShowFieldManager = false;
				state.FieldMoveActive = false;
				return TopCommandAction.Rerender;
			case 'a':
				ActivateRelativeWindow(
					state,
					1
				);
				SelectCurrentSortField(
					state
				);
				return TopCommandAction.Rerender;
			case 'w':
				ActivateRelativeWindow(
					state,
					-1
				);
				SelectCurrentSortField(
					state
				);
				return TopCommandAction.Rerender;
			case 'd':
			case ' ': {
				TopFieldId selected = state.FieldOrder[
					state.FieldCursor
				];
				if ( !state.VisibleFields.Remove( selected ) ) {
					state.VisibleFields.Add( selected );
				}
				state.HorizontalOffset = 0;
				return TopCommandAction.Rerender;
			}
			case 's':
				state.SortField = state.FieldOrder[
					state.FieldCursor
				];
				state.ExitForestForSort();
				state.VerticalOffset = 0;
				state.Message = null;
				return TopCommandAction.Rerender;
			default:
				return TopCommandAction.None;
		}
	}

	private static void ResetCurrentWindowDisplayLimits(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		state.TaskDisplayVisible = true;
		state.UserFilter = null;
		state.OtherFilters.Clear();
		state.HideIdle = false;
		state.MaximumTasks = 0;
		state.SearchText = null;
		state.ClearForestRestrictions();
		state.VerticalOffset = 0;
		state.HorizontalOffset = 0;
		state.SynchronizeCurrentWindow();
	}

	private static void ResetAllWindowDisplayLimits(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		int currentWindowIndex = state.CurrentWindowIndex;
		for ( int index = 0; index < TopRuntimeState.WindowCount; index++ ) {
			state.ActivateWindow(
				index
			);
			ResetCurrentWindowDisplayLimits(
				state
			);
		}
		state.ShowAllTaskDisplays();
		state.ActivateWindow(
			currentWindowIndex
		);
	}

	private static void ActivateRelativeWindow(
		TopRuntimeState state,
		int delta
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( delta is not -1 and not 1 ) {
			throw new ArgumentOutOfRangeException(
				nameof( delta )
			);
		}

		int windowIndex = (
			state.CurrentWindowIndex
			+ delta
			+ TopRuntimeState.WindowCount
		) % TopRuntimeState.WindowCount;
		state.ActivateWindow(
			windowIndex
		);
		state.Message = $"current window: {state.CurrentWindowLabel}";
	}

	private static void SelectCurrentSortField(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		int sortFieldIndex = state.FieldOrder.IndexOf(
			state.SortField
		);
		state.FieldCursor = ( 0 <= sortFieldIndex )
			? sortFieldIndex
			: 0
		;
		state.FieldMoveActive = false;
	}

	private static void MoveFieldCursor(
		TopRuntimeState state,
		int targetIndex
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 == state.FieldOrder.Count ) {
			throw new InvalidOperationException(
				"The top field order cannot be empty."
			);
		}

		int currentIndex = Math.Clamp(
			state.FieldCursor,
			0,
			state.FieldOrder.Count - 1
		);
		int destination = Math.Clamp(
			targetIndex,
			0,
			state.FieldOrder.Count - 1
		);
		if (
			state.FieldMoveActive
			&& currentIndex != destination
		) {
			TopFieldId field = state.FieldOrder[
				currentIndex
			];
			state.FieldOrder.RemoveAt(
				currentIndex
			);
			state.FieldOrder.Insert(
				destination,
				field
			);
			state.HorizontalOffset = 0;
		}
		state.FieldCursor = destination;
	}

	private static async Task<TopCommandAction> HandlePromptInputAsync(
		TopInputEvent input,
		TopSample sample,
		TopRuntimeState state,
		TopTerminalDimensions dimensions,
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

			case TopPromptKind.MaximumTasks:
				state.Prompt = null;
				if (
					!int.TryParse(
						text,
						NumberStyles.None,
						CultureInfo.InvariantCulture,
						out int maximumTasks
					)
				) {
					state.Message = "maximum tasks must be zero or a positive integer";
					return TopCommandAction.Rerender;
				}
				state.MaximumTasks = maximumTasks;
				state.VerticalOffset = 0;
				state.Message = 0 == maximumTasks
					? "maximum tasks: unlimited"
					: $"maximum tasks set to {maximumTasks}";
				return TopCommandAction.Rerender;

			case TopPromptKind.FixedWidthExtra:
				state.Prompt = null;
				if (
					!int.TryParse(
						text,
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out int fixedWidthExtra
					)
					|| !TopFixedWidth.IsValid( fixedWidthExtra )
				) {
					state.Message = $"extra fixed width must be between -1 and {TopFixedWidth.MaximumExtra}";
					return TopCommandAction.Rerender;
				}
				TopFixedWidth.Configure(
					state,
					fixedWidthExtra
				);
				if ( -1 == fixedWidthExtra ) {
					state.Message = "extra fixed width: automatic";
				} else if ( 0 == fixedWidthExtra ) {
					state.Message = "extra fixed width: defaults";
				} else {
					state.Message = $"extra fixed width: +{fixedWidthExtra}";
				}
				return TopCommandAction.Rerender;

			case TopPromptKind.InspectProcessId:
				state.Prompt = null;
				int inspectPid;
				if ( 0 == text.Length ) {
					if ( !prompt.ProcessId.HasValue ) {
						state.Message = "Inspect requires a positive process identifier";
						return TopCommandAction.Rerender;
					}
					inspectPid = prompt.ProcessId.Value;
				} else if (
					!int.TryParse(
						text,
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out inspectPid
					)
					|| 1 > inspectPid
				) {
					state.Message = "Inspect requires a positive process identifier";
					return TopCommandAction.Rerender;
				}

				ProcProcessSnapshot? inspectTarget = await ResolveTargetAsync(
					inspectPid,
					sample,
					processProvider,
					cancellationToken
				).ConfigureAwait( false );
				if ( inspectTarget is null ) {
					state.Message = $"process {inspectPid} is no longer available";
					return TopCommandAction.Rerender;
				}
				state.InspectSession = new TopInspectSession(
					inspectTarget.ProcessId,
					state.InspectEntries
				);
				state.Message = null;
				return TopCommandAction.Rerender;

			case TopPromptKind.Window:
				state.Prompt = null;
				if (
					!int.TryParse(
						text,
						NumberStyles.None,
						CultureInfo.InvariantCulture,
						out int selectedWindow
					)
					|| selectedWindow is < 1 or > TopRuntimeState.WindowCount
				) {
					state.Message = $"window must be between 1 and {TopRuntimeState.WindowCount}";
					return TopCommandAction.Rerender;
				}
				state.ActivateWindow(
					selectedWindow - 1
				);
				state.Message = $"current window: {state.CurrentWindowLabel}";
				return TopCommandAction.Rerender;

			case TopPromptKind.WindowName:
				state.Prompt = null;
				string windowName = prompt.Buffer.Trim();
				int windowNameBytes = Utf8.GetByteCount(
					windowName
				);
				if ( windowNameBytes is < 1 or > 3 ) {
					state.Message = "window name must occupy 1 through 3 UTF-8 bytes";
					return TopCommandAction.Rerender;
				}
				state.RenameCurrentWindow(
					windowName
				);
				state.Message = $"window renamed: {state.CurrentWindowLabel}";
				return TopCommandAction.Rerender;

			case TopPromptKind.Locate:
				state.Prompt = null;
				string locateText = prompt.Buffer;
				if ( 0 == locateText.Length ) {
					state.SearchText = null;
					state.Message = "locate disabled";
					return TopCommandAction.Rerender;
				}
				state.SearchText = locateText;
				int locateOffset = TopRenderer.FindTaskOffset(
					sample,
					state,
					locateText,
					state.VerticalOffset,
					dimensions
				);
				if ( 0 <= locateOffset ) {
					state.VerticalOffset = locateOffset;
					state.Message = null;
				} else {
					state.Message = $"locate string not found: {locateText}";
				}
				return TopCommandAction.Rerender;

			case TopPromptKind.OtherFilterCaseSensitive:
			case TopPromptKind.OtherFilterIgnoreCase:
				state.Prompt = null;
				bool caseSensitive = TopPromptKind.OtherFilterCaseSensitive == prompt.Kind;
				if (
					!TopOtherFilterParser.TryParse(
						prompt.Buffer,
						caseSensitive,
						state,
						out TopOtherFilter? otherFilter,
						out string? filterError
					)
				) {
					state.Message = filterError ?? "invalid other filter";
					return TopCommandAction.Rerender;
				}
				state.OtherFilters.Insert(
					0,
					otherFilter!
				);
				state.VerticalOffset = 0;
				state.Message = $"filter added: {otherFilter!.RawText}";
				return TopCommandAction.Rerender;

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

	private static bool ShouldLoadConfiguration(
		IReadOnlyList<string> args
	) {
		ArgumentNullException.ThrowIfNull( args );

		foreach ( string argument in args ) {
			if (
				argument is "-h" or "--help"
					or "-V" or "--version"
					or "-O" or "--list-fields"
			) {
				return false;
			}
		}
		return true;
	}

	private static bool ShouldLoadPersonalConfiguration(
		IReadOnlyList<string> args
	) {
		ArgumentNullException.ThrowIfNull( args );

		foreach ( string argument in args ) {
			if ( argument is "-A" or "--apply-defaults" ) {
				return false;
			}
		}
		return true;
	}

	private static bool ForcesSecureMode(
		IReadOnlyList<string> args
	) {
		ArgumentNullException.ThrowIfNull( args );

		foreach ( string argument in args ) {
			if ( argument is "-s" or "--secure-mode" ) {
				return true;
			}
		}
		return false;
	}

	private static ParsedArguments Parse(
		IReadOnlyList<string> args,
		IProcAccountDisplayResolver accountResolver,
		Func<string, string?> environmentVariableProvider,
		int currentProcessId,
		TopRuntimeState startupState
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( accountResolver );
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( currentProcessId );
		ArgumentNullException.ThrowIfNull( startupState );
		var result = new ParsedArguments( startupState );
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
				result.State.ShowCommandLine = !result.State.ShowCommandLine;
				continue;
			}
			if ( argument is "-H" or "--threads-show" ) {
				result.State.ShowThreads = true;
				continue;
			}
			if ( argument is "-i" or "--idle-toggle" ) {
				result.State.HideIdle = !result.State.HideIdle;
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
				result.State.SingleCpuSummary = !result.State.SingleCpuSummary;
				continue;
			}

			if ( TryOptionValue( args, ref index, argument, "-d", "--delay", out string? delayText ) ) {
				if ( result.State.SecureMode ) {
					result.Fail( "-d/--delay is unavailable in secure mode" );
				} else if ( !TryParseDelay( delayText!, out TimeSpan delay ) ) {
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
				if (
					!TopRenderer.TryParseSortOverride(
						sortText!,
						out TopFieldId sortField,
						out bool? sortHighToLow
					)
				) {
					result.Fail( $"unknown sort field '{sortText}'" );
				} else {
					result.State.SortField = sortField;
					result.State.ExitForestForSort();
					if ( sortHighToLow.HasValue ) {
						result.State.SortHighToLow = sortHighToLow.Value;
					}
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
		internal ParsedArguments( TopRuntimeState state ) {
			ArgumentNullException.ThrowIfNull( state );
			this.State = state;
		}

		internal bool ApplyDefaults { get; set; }
		internal bool Batch { get; set; }
		internal bool Help { get; set; }
		internal bool Version { get; set; }
		internal bool ListFields { get; set; }
		internal int? Iterations { get; set; }
		internal int BatchWidth { get; set; } = DefaultBatchWidth;
		internal TopRuntimeState State { get; }
		internal string? Error { get; private set; }

		internal void Fail( string error ) {
			ArgumentException.ThrowIfNullOrWhiteSpace( error );
			this.Error ??= error;
		}
	}
}

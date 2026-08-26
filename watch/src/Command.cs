// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Watch;

using System.Globalization;
using System.Text;
using Icod.DCurses;
using Icod.Processes;
using Icod.Timing;

/// <summary>Implements the procps-ng compatible <c>watch</c> command.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int ExecutionFailure = 2;
	private const int Canceled = 130;
	private const double MinimumIntervalSeconds = 0.1;
	private const double MaximumIntervalSeconds = 31.0 * 24.0 * 60.0 * 60.0;
	private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds( 2 );
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.Watch",
		typeof( Command ).Assembly
	);

	/// <summary>Runs the <c>watch</c> command synchronously.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="stdout">Optional standard-output stream.</param>
	/// <param name="stderr">Optional standard-error stream.</param>
	/// <returns>The process exit status.</returns>
	public static int Run(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsync( args, stdout, stderr ).GetAwaiter().GetResult();
	}

	/// <summary>Runs the <c>watch</c> command.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="stdout">Optional stream used for help and version output.</param>
	/// <param name="stderr">Optional stream used for diagnostics.</param>
	/// <param name="processExecutor">Optional child-process executor.</param>
	/// <param name="clock">Optional monotonic clock.</param>
	/// <param name="environmentVariableProvider">Optional environment lookup.</param>
	/// <param name="wallClock">Optional wall-clock provider for the title.</param>
	/// <param name="hostName">Optional host-name provider for the title.</param>
	/// <param name="cancellationToken">Cancellation for the command.</param>
	/// <returns>The process exit status.</returns>
	public static Task<int> RunAsync(
		IReadOnlyList<string> args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcessExecutor? processExecutor = null,
		IMonotonicClock? clock = null,
		Func<string, string?>? environmentVariableProvider = null,
		Func<DateTimeOffset>? wallClock = null,
		Func<string>? hostName = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsyncCore(
			args,
			stdout,
			stderr,
			processExecutor,
			SystemWatchTerminalSessionFactory.Instance,
			clock,
			environmentVariableProvider,
			wallClock,
			hostName,
			cancellationToken
		);
	}

	internal static async Task<int> RunAsyncCore(
		IReadOnlyList<string> args,
		Stream? stdout,
		Stream? stderr,
		IProcessExecutor? processExecutor,
		IWatchTerminalSessionFactory terminalFactory,
		IMonotonicClock? clock,
		Func<string, string?>? environmentVariableProvider,
		Func<DateTimeOffset>? wallClock,
		Func<string>? hostName,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminalFactory );

		Stream output = stdout ?? Console.OpenStandardOutput();
		Stream errorOutput = stderr ?? Console.OpenStandardError();
		IProcessExecutor executor = processExecutor ?? SystemProcessExecutor.Instance;
		IMonotonicClock monotonicClock = clock ?? SystemMonotonicClock.Instance;
		Func<string, string?> environment = environmentVariableProvider
			?? Environment.GetEnvironmentVariable;
		Func<DateTimeOffset> currentTime = wallClock ?? GetCurrentTime;
		Func<string> currentHostName = hostName ?? GetHostName;

		ParsedArguments parsed = Parse( args, environment );
		if ( parsed.Help ) {
			await WriteUsageAsync( output, cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.Version ) {
			await WriteTextAsync(
				output,
				$"{VersionText}{Environment.NewLine}",
				cancellationToken
			).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.Error is not null ) {
			await WriteFailureAsync( errorOutput, parsed.Error ).ConfigureAwait( false );
			await WriteUsageAsync( errorOutput, cancellationToken ).ConfigureAwait( false );
			return Failure;
		}

		IWatchTerminalSession? terminal = null;
		try {
			terminal = await terminalFactory.OpenAsync( cancellationToken ).ConfigureAwait( false );
			if ( !terminal.IsInteractive ) {
				await WriteFailureAsync(
					errorOutput,
					"interactive terminal input and output are required"
				).ConfigureAwait( false );
				return Failure;
			}

			WatchTerminalDimensions dimensions = GetDimensions( terminal, environment );
			if ( !IsUsableDimensions( dimensions, parsed.NoTitle ) ) {
				await WriteFailureAsync(
					errorOutput,
					"terminal is too small for the requested display"
				).ConfigureAwait( false );
				return ExecutionFailure;
			}

			using CancellationTokenSource linkedCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken,
					terminal.TerminationToken
				);
			CancellationToken refreshToken = linkedCancellation.Token;
			long startedTimestamp = monotonicClock.GetTimestamp();
			long lastCompletionTimestamp = startedTimestamp;
			long iteration = 0;
			long unchangedCycles = 1;
			WatchScreen? previousScreen = null;
			bool[]? permanentDifferences = null;
			string? previousRawOutput = null;
			int previousStatus = Success;
			TimeSpan previousElapsed = TimeSpan.Zero;

			while ( true ) {
				refreshToken.ThrowIfCancellationRequested();

				if ( 0 < iteration ) {
					while ( true ) {
						TimeSpan delay = GetRemainingDelay(
							parsed,
							monotonicClock,
							startedTimestamp,
							lastCompletionTimestamp,
							iteration
						);
						if ( TimeSpan.Zero >= delay ) {
							break;
						}

						WatchTerminalEvent terminalEvent = await terminal.ReadEventAsync(
							delay,
							refreshToken
						).ConfigureAwait( false );
						if ( WatchTerminalEventKind.Timeout == terminalEvent.Kind ) {
							break;
						}
						if ( WatchTerminalEventKind.Interrupt == terminalEvent.Kind ) {
							return Canceled;
						}
						if ( WatchTerminalEventKind.Repaint == terminalEvent.Kind ) {
							await terminal.RepaintAsync( refreshToken ).ConfigureAwait( false );
							continue;
						}
						if ( WatchTerminalEventKind.Resize != terminalEvent.Kind ) {
							continue;
						}

						dimensions = GetDimensions( terminal, environment );
						if ( !IsUsableDimensions( dimensions, parsed.NoTitle ) ) {
							continue;
						}
						previousScreen = null;
						permanentDifferences = null;
						unchangedCycles = 1;

						if ( parsed.NoRerun && previousRawOutput is not null ) {
							WatchScreen redrawScreen = WatchScreen.Create(
								previousRawOutput,
								dimensions,
								parsed.NoTitle,
								parsed.NoWrap,
								parsed.Color
							);
							WatchRenderFrame redrawFrame = BuildFrame(
								redrawScreen,
								parsed,
								previousStatus,
								previousElapsed,
								currentHostName(),
								currentTime(),
								null
							);
							await terminal.RenderAsync(
								redrawFrame,
								refreshToken
							).ConfigureAwait( false );
							previousScreen = redrawScreen;
							continue;
						}

						break;
					}
				}

				dimensions = GetDimensions( terminal, environment );
				if ( !IsUsableDimensions( dimensions, parsed.NoTitle ) ) {
					WatchTerminalEvent resizeWait = await terminal.ReadEventAsync(
						TimeSpan.FromMilliseconds( 250 ),
						refreshToken
					).ConfigureAwait( false );
					if ( WatchTerminalEventKind.Interrupt == resizeWait.Kind ) {
						return Canceled;
					}
					continue;
				}
				if ( previousScreen is not null
					&& (
						previousScreen.Width != dimensions.Columns
						|| previousScreen.Height != GetBodyHeight( dimensions, parsed.NoTitle )
					) ) {
					previousScreen = null;
					permanentDifferences = null;
					unchangedCycles = 1;
				}

				using MergedCaptureStream capture = new();
				ProcessRunOptions processOptions = BuildProcessOptions( parsed, capture );
				ProcessResult processResult;
				try {
					processResult = await executor.RunAsync(
						processOptions,
						refreshToken
					).ConfigureAwait( false );
				} catch ( OperationCanceledException ) {
					throw;
				} catch ( Exception exception ) when (
					exception is ArgumentException
						or IOException
						or InvalidOperationException
						or NotSupportedException
						or UnauthorizedAccessException
				) {
					await WriteFailureAsync( errorOutput, exception.Message ).ConfigureAwait( false );
					return ExecutionFailure;
				}

				if ( refreshToken.IsCancellationRequested ) {
					return Canceled;
				}
				if ( processResult.WasCanceled ) {
					return Canceled;
				}

				int status = processResult.Termination.ToPortableExitCode();
				string childOutput = capture.GetText();
				if ( 0 == childOutput.Length ) {
					childOutput = string.Concat(
						processResult.StandardOutput ?? string.Empty,
						processResult.StandardError ?? string.Empty
					);
				}
				if ( ProcessTerminationKind.LaunchFailed == processResult.Termination.Kind
					&& string.IsNullOrEmpty( childOutput )
					&& !string.IsNullOrWhiteSpace( processResult.Termination.Message ) ) {
					childOutput = string.Concat(
						"watch: ",
						processResult.Termination.Message,
						Environment.NewLine
					);
				}

				dimensions = GetDimensions( terminal, environment );
				if ( !IsUsableDimensions( dimensions, parsed.NoTitle ) ) {
					previousRawOutput = childOutput;
					previousStatus = status;
					previousElapsed = processResult.Elapsed;
					lastCompletionTimestamp = monotonicClock.GetTimestamp();
					iteration++;
					continue;
				}
				if ( previousScreen is not null
					&& (
						previousScreen.Width != dimensions.Columns
						|| previousScreen.Height != GetBodyHeight( dimensions, parsed.NoTitle )
					) ) {
					previousScreen = null;
					permanentDifferences = null;
					unchangedCycles = 1;
				}

				WatchScreen screen = WatchScreen.Create(
					childOutput,
					dimensions,
					parsed.NoTitle,
					parsed.NoWrap,
					parsed.Color
				);
				bool changed = previousScreen is not null
					&& !screen.VisibleEquals( previousScreen );
				bool[]? highlights = parsed.Differences && previousScreen is not null
					? screen.GetDifferences( previousScreen )
					: null;
				if ( parsed.PermanentDifferences && highlights is not null ) {
					if ( permanentDifferences is null
						|| permanentDifferences.Length != highlights.Length ) {
						permanentDifferences = new bool[ highlights.Length ];
					}
					for ( int index = 0; index < highlights.Length; index++ ) {
						permanentDifferences[ index ] |= highlights[ index ];
					}
					highlights = permanentDifferences;
				}

				WatchRenderFrame frame = BuildFrame(
					screen,
					parsed,
					status,
					processResult.Elapsed,
					currentHostName(),
					currentTime(),
					highlights
				);
				if ( parsed.Beep && 0 != status ) {
					await terminal.AlertAsync( refreshToken ).ConfigureAwait( false );
				}
				await terminal.RenderAsync( frame, refreshToken ).ConfigureAwait( false );

				previousRawOutput = childOutput;
				previousStatus = status;
				previousElapsed = processResult.Elapsed;
				lastCompletionTimestamp = monotonicClock.GetTimestamp();

				if ( parsed.ErrorExit && 0 != status ) {
					return status;
				}
				if ( previousScreen is not null ) {
					if ( parsed.ChangeExit && changed ) {
						return Success;
					}
					if ( parsed.EqualExitCycles.HasValue ) {
						if ( changed ) {
							unchangedCycles = 1;
						} else {
							if ( unchangedCycles >= parsed.EqualExitCycles.Value ) {
								return Success;
							}
							unchangedCycles++;
						}
					}
				}

				previousScreen = screen;
				iteration++;
			}
		} catch ( OperationCanceledException ) {
			return Canceled;
		} catch ( Exception exception ) when (
			exception is ArgumentException
				or IOException
				or InvalidOperationException
				or NotSupportedException
				or UnauthorizedAccessException
		) {
			await WriteFailureAsync( errorOutput, exception.Message ).ConfigureAwait( false );
			return Failure;
		} finally {
			if ( terminal is not null ) {
				try {
					await terminal.DisposeAsync().ConfigureAwait( false );
				} catch ( Exception exception ) when (
					exception is IOException
						or InvalidOperationException
						or NotSupportedException
						or ObjectDisposedException
				) {
				}
			}
		}
	}

	private static ProcessRunOptions BuildProcessOptions(
		ParsedArguments parsed,
		Stream capture
	) {
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( capture );

		ProcessRunOptions options;
		if ( parsed.Exec ) {
			options = new ProcessRunOptions( parsed.Command[ 0 ] );
			for ( int index = 1; index < parsed.Command.Count; index++ ) {
				options.Arguments.Add( parsed.Command[ index ] );
			}
		} else if ( OperatingSystem.IsWindows() ) {
			options = new ProcessRunOptions( "cmd.exe" );
			options.Arguments.Add( "/D" );
			options.Arguments.Add( "/S" );
			options.Arguments.Add( "/C" );
			options.Arguments.Add( string.Join( " ", parsed.Command ) );
		} else {
			options = new ProcessRunOptions( "/bin/sh" );
			options.Arguments.Add( "-c" );
			options.Arguments.Add( string.Join( " ", parsed.Command ) );
		}

		options.ResolveExecutable = true;
		options.ReturnLaunchFailureResult = true;
		options.StandardOutput = capture;
		options.StandardError = capture;
		return options;
	}

	private static TimeSpan GetRemainingDelay(
		ParsedArguments parsed,
		IMonotonicClock clock,
		long startedTimestamp,
		long lastCompletionTimestamp,
		long iteration
	) {
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentOutOfRangeException.ThrowIfNegative( iteration );

		long now = clock.GetTimestamp();
		if ( parsed.Precise ) {
			double dueTicks = parsed.Interval.Ticks * (double)iteration;
			TimeSpan due = dueTicks >= TimeSpan.MaxValue.Ticks
				? TimeSpan.MaxValue
				: TimeSpan.FromTicks( checked( (long)dueTicks ) );
			TimeSpan elapsed = clock.GetElapsedTime( startedTimestamp, now );
			return due > elapsed
				? due - elapsed
				: TimeSpan.Zero;
		}

		TimeSpan sinceCompletion = clock.GetElapsedTime(
			lastCompletionTimestamp,
			now
		);
		return parsed.Interval > sinceCompletion
			? parsed.Interval - sinceCompletion
			: TimeSpan.Zero;
	}

	private static WatchTerminalDimensions GetDimensions(
		IWatchTerminalSession terminal,
		Func<string, string?> environmentVariableProvider
	) {
		ArgumentNullException.ThrowIfNull( terminal );
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );

		WatchTerminalDimensions dimensions = terminal.GetDimensions();
		int columns = ParsePositiveDimension( environmentVariableProvider( "COLUMNS" ) )
			?? dimensions.Columns;
		int rows = ParsePositiveDimension( environmentVariableProvider( "LINES" ) )
			?? dimensions.Rows;
		return new WatchTerminalDimensions( columns, rows );
	}

	private static int? ParsePositiveDimension(
		string? text
	) {
		if ( text is null ) {
			return null;
		}
		if ( int.TryParse(
			text,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out int value
		) && 0 < value ) {
			return value;
		}
		return null;
	}

	private static int GetBodyHeight(
		WatchTerminalDimensions dimensions,
		bool noTitle
	) {
		return dimensions.Rows - ( noTitle ? 0 : 2 );
	}

	private static bool IsUsableDimensions(
		WatchTerminalDimensions dimensions,
		bool noTitle
	) {
		int minimumRows = noTitle ? 1 : 3;
		return 2 <= dimensions.Columns
			&& minimumRows <= dimensions.Rows
			&& int.MaxValue >= (long)dimensions.Columns * dimensions.Rows;
	}

	private static WatchRenderFrame BuildFrame(
		WatchScreen screen,
		ParsedArguments parsed,
		int status,
		TimeSpan elapsed,
		string hostName,
		DateTimeOffset now,
		bool[]? highlights
	) {
		ArgumentNullException.ThrowIfNull( screen );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( hostName );

		List<string> headerLines = [];
		if ( !parsed.NoTitle ) {
			string intervalText = parsed.Interval.TotalSeconds.ToString(
				"0.###",
				CultureInfo.InvariantCulture
			);
			string commandText = string.Join( " ", parsed.Command );
			string left = $"Every {intervalText}s: {commandText}";
			string right = $"{hostName}: {now:HH:mm:ss}";
			headerLines.Add(
				ComposeHeaderRow( left, right, screen.Width )
			);
			headerLines.Add(
				$"Elapsed: {elapsed.TotalSeconds.ToString( "0.###", CultureInfo.InvariantCulture )}s  Exit: {status}"
			);
		}

		return new WatchRenderFrame(
			headerLines,
			screen,
			highlights
		);
	}

	private static string ComposeHeaderRow(
		string left,
		string right,
		int width
	) {
		ArgumentNullException.ThrowIfNull( left );
		ArgumentNullException.ThrowIfNull( right );
		if ( 1 > width ) {
			throw new ArgumentOutOfRangeException( nameof( width ) );
		}

		string clippedLeft = WatchTextLayout.ClipToWidth( left, width );
		int leftWidth = WatchTextLayout.GetWidth( clippedLeft );
		int rightWidth = WatchTextLayout.GetWidth( right );
		if ( leftWidth >= width ) {
			return clippedLeft;
		}
		if ( rightWidth + 1 >= width ) {
			return clippedLeft;
		}

		int availableLeft = width - rightWidth - 1;
		clippedLeft = WatchTextLayout.ClipToWidth( left, availableLeft );
		leftWidth = WatchTextLayout.GetWidth( clippedLeft );
		return string.Concat(
			clippedLeft,
			new string( ' ', width - leftWidth - rightWidth ),
			right
		);
	}

	private static ParsedArguments Parse(
		IReadOnlyList<string> args,
		Func<string, string?> environmentVariableProvider
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );

		TimeSpan interval = DefaultInterval;
		string? environmentInterval = environmentVariableProvider( "WATCH_INTERVAL" );
		if ( environmentInterval is not null
			&& !TryParseInterval( environmentInterval, out interval ) ) {
			return ParsedArguments.Failed(
				"Could not parse interval from WATCH_INTERVAL"
			);
		}

		bool beep = false;
		bool color = false;
		bool differences = false;
		bool permanentDifferences = false;
		bool errorExit = false;
		bool follow = false;
		bool changeExit = false;
		long? equalExitCycles = null;
		bool precise = false;
		bool noRerun = false;
		bool noTitle = false;
		bool noWrap = false;
		bool exec = false;
		string? shotsDirectory = null;
		int index = 0;

		for ( ; index < args.Count; index++ ) {
			string argument = args[ index ];
			if ( "--" == argument ) {
				index++;
				break;
			}
			if ( !argument.StartsWith( '-' ) || "-" == argument ) {
				break;
			}
			if ( "--help" == argument || "-h" == argument ) {
				return ParsedArguments.ForHelp();
			}
			if ( "--version" == argument || "-v" == argument ) {
				return ParsedArguments.ForVersion();
			}
			if ( "--beep" == argument || "-b" == argument ) {
				beep = true;
				continue;
			}
			if ( "--color" == argument || "-c" == argument ) {
				color = true;
				continue;
			}
			if ( "--no-color" == argument || "-C" == argument ) {
				color = false;
				continue;
			}
			if ( "--differences" == argument || "-d" == argument ) {
				differences = true;
				continue;
			}
			if ( argument.StartsWith( "--differences=", StringComparison.Ordinal ) ) {
				string mode = argument[ "--differences=".Length.. ];
				if ( !string.Equals(
					mode,
					"permanent",
					StringComparison.OrdinalIgnoreCase
				) ) {
					return ParsedArguments.Failed( "invalid differences mode" );
				}
				differences = true;
				permanentDifferences = true;
				continue;
			}
			if ( argument.StartsWith( "-d", StringComparison.Ordinal )
				&& 2 < argument.Length ) {
				string mode = argument[ 2.. ].TrimStart( '=' );
				if ( !string.Equals(
					mode,
					"permanent",
					StringComparison.OrdinalIgnoreCase
				) ) {
					return ParsedArguments.Failed( "invalid differences mode" );
				}
				differences = true;
				permanentDifferences = true;
				continue;
			}
			if ( "--errexit" == argument || "-e" == argument ) {
				errorExit = true;
				continue;
			}
			if ( "--follow" == argument || "-f" == argument ) {
				follow = true;
				continue;
			}
			if ( "--chgexit" == argument || "-g" == argument ) {
				changeExit = true;
				continue;
			}
			if ( TryRequiredOptionValue(
				args,
				ref index,
				argument,
				"-q",
				"--equexit",
				out string equalText,
				out string? equalError
			) ) {
				if ( equalError is not null ) {
					return ParsedArguments.Failed( equalError );
				}
				if ( !long.TryParse(
					equalText,
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out long cycles
				) ) {
					return ParsedArguments.Failed( "failed to parse argument" );
				}
				equalExitCycles = Math.Max( 1L, cycles );
				continue;
			}
			if ( TryRequiredOptionValue(
				args,
				ref index,
				argument,
				"-n",
				"--interval",
				out string intervalText,
				out string? intervalError
			) ) {
				if ( intervalError is not null ) {
					return ParsedArguments.Failed( intervalError );
				}
				if ( !TryParseInterval( intervalText, out interval ) ) {
					return ParsedArguments.Failed( "failed to parse argument" );
				}
				continue;
			}
			if ( "--precise" == argument || "-p" == argument ) {
				precise = true;
				continue;
			}
			if ( "--no-rerun" == argument || "-r" == argument ) {
				noRerun = true;
				continue;
			}
			if ( TryRequiredOptionValue(
				args,
				ref index,
				argument,
				"-s",
				"--shotsdir",
				out string shotsText,
				out string? shotsError
			) ) {
				if ( shotsError is not null ) {
					return ParsedArguments.Failed( shotsError );
				}
				shotsDirectory = shotsText;
				continue;
			}
			if ( "--no-title" == argument || "-t" == argument ) {
				noTitle = true;
				continue;
			}
			if ( "--no-wrap" == argument || "-w" == argument ) {
				noWrap = true;
				continue;
			}
			if ( "--exec" == argument || "-x" == argument ) {
				exec = true;
				continue;
			}
			return ParsedArguments.Failed( $"unrecognized option '{argument}'" );
		}

		if ( index >= args.Count ) {
			return ParsedArguments.Failed( "missing command" );
		}
		if ( follow && ( differences || changeExit || equalExitCycles.HasValue ) ) {
			return ParsedArguments.Failed(
				"follow option conflicts with change and exit options"
			);
		}

		List<string> command = new( args.Count - index );
		for ( ; index < args.Count; index++ ) {
			command.Add( args[ index ] );
		}
		return new ParsedArguments(
			interval,
			beep,
			color,
			differences,
			permanentDifferences,
			errorExit,
			follow,
			changeExit,
			equalExitCycles,
			precise,
			noRerun,
			shotsDirectory,
			noTitle,
			noWrap,
			exec,
			command,
			Help: false,
			Version: false,
			Error: null
		);
	}

	private static bool TryParseInterval(
		string text,
		out TimeSpan interval
	) {
		ArgumentNullException.ThrowIfNull( text );
		string normalized = text.Replace( ',', '.' );
		if ( !double.TryParse(
			normalized,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out double seconds
		) || !double.IsFinite( seconds ) ) {
			interval = default;
			return false;
		}

		seconds = Math.Clamp(
			seconds,
			MinimumIntervalSeconds,
			MaximumIntervalSeconds
		);
		interval = TimeSpan.FromSeconds( seconds );
		return true;
	}

	private static bool TryRequiredOptionValue(
		IReadOnlyList<string> args,
		ref int index,
		string argument,
		string shortName,
		string longName,
		out string value,
		out string? error
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( argument );
		ArgumentNullException.ThrowIfNull( shortName );
		ArgumentNullException.ThrowIfNull( longName );

		value = string.Empty;
		error = null;
		if ( argument == shortName || argument == longName ) {
			if ( index + 1 >= args.Count ) {
				error = $"option '{argument}' requires an argument";
				return true;
			}
			value = args[ ++index ];
			return true;
		}
		if ( argument.StartsWith( shortName, StringComparison.Ordinal )
			&& shortName.Length < argument.Length ) {
			value = argument[ shortName.Length.. ].TrimStart( '=' );
			return true;
		}

		string prefix = $"{longName}=";
		if ( argument.StartsWith( prefix, StringComparison.Ordinal ) ) {
			value = argument[ prefix.Length.. ];
			return true;
		}
		return false;
	}

	private static Task WriteUsageAsync(
		Stream output,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( output );
		return WriteTextAsync( output, HelpText(), cancellationToken );
	}

	private static string HelpText() => string.Join(
		Environment.NewLine,
		"Usage:",
		" watch [options] command",
		string.Empty,
		"Options:",
		" -b, --beep                  beep if command has a non-zero exit",
		" -c, --color                 interpret ANSI color and style sequences",
		" -C, --no-color              do not interpret ANSI color/style sequences",
		" -d, --differences[=permanent]",
		"                              highlight changes between updates",
		" -e, --errexit               exit if command has a non-zero exit",
		" -f, --follow                follow output without change/exit comparisons",
		" -g, --chgexit               exit when visible command output changes",
		" -q, --equexit <cycles>      exit after visible output is unchanged for cycles",
		" -n, --interval <secs>       seconds between updates",
		" -p, --precise               include command running time in the interval",
		" -r, --no-rerun              do not rerun command because of a resize",
		" -s, --shotsdir <dir>        reserve screenshot directory compatibility",
		" -t, --no-title              turn off the header",
		" -w, --no-wrap               truncate long lines instead of wrapping",
		" -x, --exec                  execute command directly instead of through a shell",
		" -h, --help                  display this help and exit",
		" -v, --version               output version information and exit",
		string.Empty
	);

	private static DateTimeOffset GetCurrentTime() => DateTimeOffset.Now;

	private static string GetHostName() => Environment.MachineName;

	private static async Task WriteTextAsync(
		Stream stream,
		string text,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( stream );
		ArgumentNullException.ThrowIfNull( text );
		byte[] bytes = Encoding.UTF8.GetBytes( text );
		await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	private static async Task WriteFailureAsync(
		Stream stderr,
		string message
	) {
		ArgumentNullException.ThrowIfNull( stderr );
		ArgumentNullException.ThrowIfNull( message );
		try {
			await WriteTextAsync(
				stderr,
				$"watch: {message}{Environment.NewLine}",
				CancellationToken.None
			).ConfigureAwait( false );
		} catch ( IOException ) {
		} catch ( ObjectDisposedException ) {
		}
	}

	private sealed record ParsedArguments(
		TimeSpan Interval,
		bool Beep,
		bool Color,
		bool Differences,
		bool PermanentDifferences,
		bool ErrorExit,
		bool Follow,
		bool ChangeExit,
		long? EqualExitCycles,
		bool Precise,
		bool NoRerun,
		string? ShotsDirectory,
		bool NoTitle,
		bool NoWrap,
		bool Exec,
		IReadOnlyList<string> Command,
		bool Help,
		bool Version,
		string? Error
	) {
		internal static ParsedArguments ForHelp() {
			return Empty( help: true, version: false, error: null );
		}

		internal static ParsedArguments ForVersion() {
			return Empty( help: false, version: true, error: null );
		}

		internal static ParsedArguments Failed(
			string error
		) {
			ArgumentException.ThrowIfNullOrWhiteSpace( error );
			return Empty( help: false, version: false, error: error );
		}

		private static ParsedArguments Empty(
			bool help,
			bool version,
			string? error
		) {
			return new ParsedArguments(
				DefaultInterval,
				false,
				false,
				false,
				false,
				false,
				false,
				false,
				null,
				false,
				false,
				null,
				false,
				false,
				false,
				Array.Empty<string>(),
				help,
				version,
				error
			);
		}
	}
}

/// <summary>Represents one semantic watch screen cell.</summary>
internal readonly record struct WatchCell(
	string Content,
	CursesStyle Style,
	int DisplayWidth,
	bool IsContinuation
) {
	internal static WatchCell Blank => new(
		string.Empty,
		CursesStyle.Default,
		1,
		false
	);

	internal static WatchCell Continuation(
		CursesStyle style
	) {
		return new WatchCell(
			string.Empty,
			style,
			0,
			true
		);
	}
}

/// <summary>Represents one visible watch body independent of terminal I/O.</summary>
internal sealed class WatchScreen {
	private readonly WatchCell[] cells;

	private WatchScreen(
		int width,
		int height,
		WatchCell[] cells
	) {
		if ( 1 > width ) {
			throw new ArgumentOutOfRangeException( nameof( width ) );
		}
		if ( 1 > height ) {
			throw new ArgumentOutOfRangeException( nameof( height ) );
		}
		ArgumentNullException.ThrowIfNull( cells );
		if ( cells.Length != checked( width * height ) ) {
			throw new ArgumentException(
				"Cell count does not match the requested screen geometry.",
				nameof( cells )
			);
		}
		this.Width = width;
		this.Height = height;
		this.cells = cells;
	}

	internal int Width {
		get;
	}

	internal int Height {
		get;
	}

	internal static WatchScreen Create(
		string output,
		WatchTerminalDimensions dimensions,
		bool noTitle,
		bool noWrap,
		bool preserveColor
	) {
		ArgumentNullException.ThrowIfNull( output );
		int bodyHeight = dimensions.Rows - ( noTitle ? 0 : 2 );
		if ( 1 > dimensions.Columns || 1 > bodyHeight ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		WatchCell[] cells = new WatchCell[ checked( dimensions.Columns * bodyHeight ) ];
		Array.Fill( cells, WatchCell.Blank );
		int row = 0;
		int column = 0;
		CursesStyle style = CursesStyle.Default;
		bool skipUntilNewline = false;
		int index = 0;

		while ( index < output.Length && row < bodyHeight ) {
			if ( '\u001b' == output[ index ]
				&& index + 1 < output.Length
				&& '[' == output[ index + 1 ] ) {
				int end = FindCsiEnd( output, index + 2 );
				if ( 0 <= end ) {
					if ( preserveColor && 'm' == output[ end ] ) {
						style = ApplySgr(
							style,
							output.AsSpan( index + 2, end - index - 2 )
						);
					}
					index = end + 1;
					continue;
				}
			}

			char character = output[ index ];
			if ( '\n' == character ) {
				row++;
				column = 0;
				skipUntilNewline = false;
				index++;
				continue;
			}
			if ( '\r' == character ) {
				column = 0;
				skipUntilNewline = false;
				index++;
				continue;
			}
			if ( skipUntilNewline ) {
				index++;
				continue;
			}
			if ( '\t' == character ) {
				int spaces = 8 - ( column % 8 );
				for ( int count = 0; count < spaces && row < bodyHeight; count++ ) {
					WriteElement(
						cells,
						dimensions.Columns,
						bodyHeight,
						ref row,
						ref column,
						" ",
						1,
						style,
						noWrap,
						ref skipUntilNewline
					);
				}
				index++;
				continue;
			}
			if ( char.IsControl( character ) ) {
				index++;
				continue;
			}

			string textElement = StringInfo.GetNextTextElement( output, index );
			int elementLength = textElement.Length;
			int displayWidth = UnicodeCursesTextWidthProvider.Instance.GetWidth( textElement );
			if ( 0 == displayWidth ) {
				AppendZeroWidthElement(
					cells,
					dimensions.Columns,
					row,
					column,
					textElement
				);
			} else {
				WriteElement(
					cells,
					dimensions.Columns,
					bodyHeight,
					ref row,
					ref column,
					textElement,
					displayWidth,
					style,
					noWrap,
					ref skipUntilNewline
				);
			}
			index += elementLength;
		}

		return new WatchScreen(
			dimensions.Columns,
			bodyHeight,
			cells
		);
	}

	internal WatchCell GetCell(
		int row,
		int column
	) {
		if ( 0 > row || row >= this.Height ) {
			throw new ArgumentOutOfRangeException( nameof( row ) );
		}
		if ( 0 > column || column >= this.Width ) {
			throw new ArgumentOutOfRangeException( nameof( column ) );
		}
		return this.cells[ ( row * this.Width ) + column ];
	}

	internal bool VisibleEquals(
		WatchScreen other
	) {
		ArgumentNullException.ThrowIfNull( other );
		if ( this.Width != other.Width || this.Height != other.Height ) {
			return false;
		}
		for ( int index = 0; index < this.cells.Length; index++ ) {
			WatchCell left = this.cells[ index ];
			WatchCell right = other.cells[ index ];
			if ( left.IsContinuation != right.IsContinuation
				|| left.DisplayWidth != right.DisplayWidth
				|| !string.Equals(
					left.Content,
					right.Content,
					StringComparison.Ordinal
				) ) {
				return false;
			}
		}
		return true;
	}

	internal bool[] GetDifferences(
		WatchScreen other
	) {
		ArgumentNullException.ThrowIfNull( other );
		if ( this.Width != other.Width || this.Height != other.Height ) {
			throw new ArgumentException(
				"Screen geometries must match for difference calculation.",
				nameof( other )
			);
		}

		bool[] result = new bool[ this.cells.Length ];
		for ( int index = 0; index < result.Length; index++ ) {
			WatchCell current = this.cells[ index ];
			WatchCell prior = other.cells[ index ];
			bool changed = current.IsContinuation != prior.IsContinuation
				|| current.DisplayWidth != prior.DisplayWidth
				|| !string.Equals(
					current.Content,
					prior.Content,
					StringComparison.Ordinal
				);
			if ( !changed ) {
				continue;
			}
			result[ index ] = true;
			MarkWideNeighbor( result, index, current );
			MarkWideNeighbor( result, index, prior );
		}
		return result;
	}

	private static void MarkWideNeighbor(
		bool[] differences,
		int index,
		WatchCell cell
	) {
		ArgumentNullException.ThrowIfNull( differences );
		if ( 2 == cell.DisplayWidth && index + 1 < differences.Length ) {
			differences[ index + 1 ] = true;
		}
	}

	private static int FindCsiEnd(
		string text,
		int start
	) {
		ArgumentNullException.ThrowIfNull( text );
		for ( int index = start; index < text.Length; index++ ) {
			char candidate = text[ index ];
			if ( '@' <= candidate && '~' >= candidate ) {
				return index;
			}
			if ( candidate > 0x7F ) {
				return -1;
			}
		}
		return -1;
	}

	private static void AppendZeroWidthElement(
		WatchCell[] cells,
		int width,
		int row,
		int column,
		string textElement
	) {
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( textElement );
		if ( 0 >= column ) {
			return;
		}

		int target = ( row * width ) + column - 1;
		while ( target >= row * width && cells[ target ].IsContinuation ) {
			target--;
		}
		if ( target < row * width || 0 == cells[ target ].Content.Length ) {
			return;
		}

		WatchCell prior = cells[ target ];
		cells[ target ] = prior with {
			Content = string.Concat( prior.Content, textElement )
		};
	}

	private static void WriteElement(
		WatchCell[] cells,
		int width,
		int height,
		ref int row,
		ref int column,
		string content,
		int displayWidth,
		CursesStyle style,
		bool noWrap,
		ref bool skipUntilNewline
	) {
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentException.ThrowIfNullOrEmpty( content );
		if ( displayWidth is < 1 or > 2 ) {
			throw new ArgumentOutOfRangeException( nameof( displayWidth ) );
		}
		if ( row >= height ) {
			return;
		}

		if ( column >= width || column + displayWidth > width ) {
			if ( noWrap ) {
				skipUntilNewline = true;
				return;
			}
			row++;
			column = 0;
			if ( row >= height ) {
				return;
			}
		}

		int cellIndex = ( row * width ) + column;
		cells[ cellIndex ] = new WatchCell(
			content,
			style,
			displayWidth,
			false
		);
		if ( 2 == displayWidth ) {
			cells[ cellIndex + 1 ] = WatchCell.Continuation( style );
		}
		column += displayWidth;
	}

	private static CursesStyle ApplySgr(
		CursesStyle style,
		ReadOnlySpan<char> parameterText
	) {
		List<int> parameters = ParseSgrParameters( parameterText );
		for ( int index = 0; index < parameters.Count; index++ ) {
			int code = parameters[ index ];
			if ( 0 == code ) {
				style = CursesStyle.Default;
				continue;
			}

			CursesTextAttributes attributes = style.Attributes;
			switch ( code ) {
				case 1:
					style = style.WithAttributes( attributes | CursesTextAttributes.Bold );
					continue;
				case 2:
					style = style.WithAttributes( attributes | CursesTextAttributes.Dim );
					continue;
				case 4:
					style = style.WithAttributes( attributes | CursesTextAttributes.Underline );
					continue;
				case 7:
					style = style.WithAttributes( attributes | CursesTextAttributes.Reverse );
					continue;
				case 22:
					style = style.WithAttributes(
						attributes & ~( CursesTextAttributes.Bold | CursesTextAttributes.Dim )
					);
					continue;
				case 24:
					style = style.WithAttributes( attributes & ~CursesTextAttributes.Underline );
					continue;
				case 27:
					style = style.WithAttributes( attributes & ~CursesTextAttributes.Reverse );
					continue;
				case 39:
					style = style.WithForeground( CursesColor.Default );
					continue;
				case 49:
					style = style.WithBackground( CursesColor.Default );
					continue;
			}

			if ( code is >= 30 and <= 37 ) {
				style = style.WithForeground( CursesColor.Indexed( code - 30 ) );
				continue;
			}
			if ( code is >= 90 and <= 97 ) {
				style = style.WithForeground( CursesColor.Indexed( code - 90 + 8 ) );
				continue;
			}
			if ( code is >= 40 and <= 47 ) {
				style = style.WithBackground( CursesColor.Indexed( code - 40 ) );
				continue;
			}
			if ( code is >= 100 and <= 107 ) {
				style = style.WithBackground( CursesColor.Indexed( code - 100 + 8 ) );
				continue;
			}
			if ( code is not 38 and not 48 ) {
				continue;
			}

			bool foreground = 38 == code;
			if ( index + 2 < parameters.Count && 5 == parameters[ index + 1 ] ) {
				int colorIndex = parameters[ index + 2 ];
				if ( 0 <= colorIndex ) {
					style = foreground
						? style.WithForeground( CursesColor.Indexed( colorIndex ) )
						: style.WithBackground( CursesColor.Indexed( colorIndex ) );
				}
				index += 2;
				continue;
			}
			if ( index + 4 < parameters.Count && 2 == parameters[ index + 1 ] ) {
				int red = parameters[ index + 2 ];
				int green = parameters[ index + 3 ];
				int blue = parameters[ index + 4 ];
				if ( red is >= 0 and <= 255
					&& green is >= 0 and <= 255
					&& blue is >= 0 and <= 255 ) {
					CursesColor color = CursesColor.Rgb(
						(byte)red,
						(byte)green,
						(byte)blue
					);
					style = foreground
						? style.WithForeground( color )
						: style.WithBackground( color );
				}
				index += 4;
			}
		}
		return style;
	}

	private static List<int> ParseSgrParameters(
		ReadOnlySpan<char> text
	) {
		List<int> result = [];
		if ( text.IsEmpty ) {
			result.Add( 0 );
			return result;
		}

		int start = 0;
		for ( int index = 0; index <= text.Length; index++ ) {
			if ( index < text.Length && ';' != text[ index ] ) {
				continue;
			}
			ReadOnlySpan<char> item = text[ start..index ];
			result.Add(
				item.IsEmpty
					? 0
					: int.TryParse(
						item,
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out int value
					)
						? value
						: -1
			);
			start = index + 1;
		}
		return result;
	}
}

/// <summary>Represents a semantic frame ready for a watch terminal adapter.</summary>
internal sealed class WatchRenderFrame {
	internal WatchRenderFrame(
		IReadOnlyList<string> headerLines,
		WatchScreen screen,
		bool[]? highlights
	) {
		ArgumentNullException.ThrowIfNull( headerLines );
		ArgumentNullException.ThrowIfNull( screen );
		if ( highlights is not null
			&& highlights.Length != checked( screen.Width * screen.Height ) ) {
			throw new ArgumentException(
				"Highlight count does not match the visible screen geometry.",
				nameof( highlights )
			);
		}

		this.HeaderLines = headerLines.ToArray();
		this.Screen = screen;
		this.Highlights = highlights is null ? null : highlights.ToArray();
	}

	internal IReadOnlyList<string> HeaderLines {
		get;
	}

	internal WatchScreen Screen {
		get;
	}

	internal bool[]? Highlights {
		get;
	}
}

/// <summary>Unicode display-width helpers shared by watch headers and tests.</summary>
internal static class WatchTextLayout {
	internal static int GetWidth(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );
		int width = 0;
		TextElementEnumerator elements = StringInfo.GetTextElementEnumerator( text );
		while ( elements.MoveNext() ) {
			width += UnicodeCursesTextWidthProvider.Instance.GetWidth(
				(string)elements.Current
			);
		}
		return width;
	}

	internal static string ClipToWidth(
		string text,
		int maximumWidth
	) {
		ArgumentNullException.ThrowIfNull( text );
		if ( 0 > maximumWidth ) {
			throw new ArgumentOutOfRangeException( nameof( maximumWidth ) );
		}

		StringBuilder builder = new();
		int width = 0;
		TextElementEnumerator elements = StringInfo.GetTextElementEnumerator( text );
		while ( elements.MoveNext() ) {
			string element = (string)elements.Current;
			int elementWidth = UnicodeCursesTextWidthProvider.Instance.GetWidth( element );
			if ( width + elementWidth > maximumWidth ) {
				break;
			}
			builder.Append( element );
			width += elementWidth;
		}
		return builder.ToString();
	}
}

/// <summary>Captures standard output and error into one arrival-ordered UTF-8 byte stream.</summary>
internal sealed class MergedCaptureStream : Stream {
	private readonly MemoryStream buffer = new();
	private readonly SemaphoreSlim gate = new( 1, 1 );
	private int disposed;

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => 0 == Volatile.Read( ref this.disposed );

	public override long Length {
		get {
			this.ThrowIfDisposed();
			lock ( this.buffer ) {
				return this.buffer.Length;
			}
		}
	}

	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	internal string GetText() {
		this.ThrowIfDisposed();
		lock ( this.buffer ) {
			return Encoding.UTF8.GetString( this.buffer.ToArray() );
		}
	}

	public override void Flush() {
		this.ThrowIfDisposed();
	}

	public override Task FlushAsync(
		CancellationToken cancellationToken
	) {
		this.ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		return Task.CompletedTask;
	}

	public override int Read(
		byte[] buffer,
		int offset,
		int count
	) {
		throw new NotSupportedException();
	}

	public override long Seek(
		long offset,
		SeekOrigin origin
	) {
		throw new NotSupportedException();
	}

	public override void SetLength(
		long value
	) {
		throw new NotSupportedException();
	}

	public override void Write(
		byte[] buffer,
		int offset,
		int count
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		this.ThrowIfDisposed();
		lock ( this.buffer ) {
			this.buffer.Write( buffer, offset, count );
		}
	}

	public override void Write(
		ReadOnlySpan<byte> buffer
	) {
		this.ThrowIfDisposed();
		lock ( this.buffer ) {
			this.buffer.Write( buffer );
		}
	}

	public override async ValueTask WriteAsync(
		ReadOnlyMemory<byte> buffer,
		CancellationToken cancellationToken = default
	) {
		this.ThrowIfDisposed();
		await this.gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
		try {
			this.buffer.Write( buffer.Span );
		} finally {
			this.gate.Release();
		}
	}

	protected override void Dispose(
		bool disposing
	) {
		if ( 0 != Interlocked.Exchange( ref this.disposed, 1 ) ) {
			return;
		}
		if ( disposing ) {
			this.gate.Dispose();
			this.buffer.Dispose();
		}
		base.Dispose( disposing );
	}

	private void ThrowIfDisposed() {
		ObjectDisposedException.ThrowIf(
			0 != Volatile.Read( ref this.disposed ),
			this
		);
	}
}

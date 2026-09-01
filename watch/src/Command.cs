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
	private const int CommandLaunchFailure = 127;
	private const int Canceled = 130;
	private const double MinimumIntervalSeconds = 0.1;
	private const double MaximumIntervalSeconds = 31.0 * 24.0 * 60.0 * 60.0;
	private const string ErrorExitPrompt =
		"command exit with a non-zero status, press a key to exit";
	private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds( 2 );
	private static readonly TimeSpan MaximumInputWait =
		TimeSpan.FromSeconds( MaximumIntervalSeconds );
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
			WatchScreen? followScreen = null;
			WatchRenderFrame? currentFrame = null;
			bool[]? permanentDifferences = null;
			string? previousRawOutput = null;
			int previousStatus = Success;
			TimeSpan previousElapsed = TimeSpan.Zero;

			while ( true ) {
				refreshToken.ThrowIfCancellationRequested();

				if ( 0 < iteration ) {
					bool screenshotTaken = false;
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
						if ( WatchTerminalEventKind.Input == terminalEvent.Kind ) {
							if ( !terminalEvent.Input.HasValue ) {
								continue;
							}
							WatchInputEvent input = terminalEvent.Input.Value;
							if ( WatchInputKey.EndOfInput == input.Key ) {
								return Success;
							}
							if (
								WatchInputKey.Character != input.Key
								|| !input.Character.HasValue
								|| char.MaxValue < input.Character.Value.Value
							) {
								continue;
							}
							char command = (char)input.Character.Value.Value;
							if ( 'q' == command ) {
								return Success;
							}
							if ( ' ' == command ) {
								break;
							}
							if (
								's' == command
								&& !screenshotTaken
								&& currentFrame is not null
							) {
								_ = await WatchScreenshot.WriteAsync(
									currentFrame,
									parsed.ShotsDirectory,
									currentTime(),
									refreshToken
								).ConfigureAwait( false );
								screenshotTaken = true;
							}
							continue;
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

						if ( parsed.Follow && followScreen is not null ) {
							followScreen = followScreen.ResizeForFollow(
								dimensions,
								parsed.NoTitle
							);
							WatchRenderFrame followRedrawFrame = BuildFrame(
								followScreen,
								parsed,
								previousStatus,
								previousElapsed,
								currentHostName(),
								currentTime(),
								null
							);
							await terminal.RenderAsync(
								followRedrawFrame,
								refreshToken
							).ConfigureAwait( false );
							currentFrame = followRedrawFrame;
							if ( parsed.NoRerun ) {
								continue;
							}
							break;
						}

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
							currentFrame = redrawFrame;
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
				ProcessRunOptions processOptions = BuildProcessOptions(
					parsed,
					capture,
					dimensions
				);
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

				int status = ProcessTerminationKind.LaunchFailed == processResult.Termination.Kind
					? CommandLaunchFailure
					: processResult.Termination.ToPortableExitCode();
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

				WatchScreen screen;
				int outputAlerts;
				if ( parsed.Follow ) {
					followScreen = WatchScreen.AppendFollow(
						followScreen,
						childOutput,
						dimensions,
						parsed.NoTitle,
						parsed.NoWrap,
						parsed.Color,
						out outputAlerts
					);
					screen = followScreen;
				} else {
					screen = WatchScreen.Create(
						childOutput,
						dimensions,
						parsed.NoTitle,
						parsed.NoWrap,
						parsed.Color,
						out outputAlerts
					);
				}
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
				for ( int alert = 0; alert < outputAlerts; alert++ ) {
					await terminal.AlertAsync(
						refreshToken
					).ConfigureAwait( false );
				}
				if ( parsed.Beep && 0 != status ) {
					await terminal.AlertAsync( refreshToken ).ConfigureAwait( false );
				}
				await terminal.RenderAsync( frame, refreshToken ).ConfigureAwait( false );
				currentFrame = frame;

				previousRawOutput = childOutput;
				previousStatus = status;
				previousElapsed = processResult.Elapsed;
				lastCompletionTimestamp = monotonicClock.GetTimestamp();

				if ( parsed.ErrorExit && 0 != status ) {
					return await WaitForErrorExitAcknowledgementAsync(
						terminal,
						status,
						refreshToken
					).ConfigureAwait( false );
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

	private static async Task<int> WaitForErrorExitAcknowledgementAsync(
		IWatchTerminalSession terminal,
		int status,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( terminal );

		while ( true ) {
			WatchTerminalEvent pending = await terminal.ReadEventAsync(
				TimeSpan.Zero,
				cancellationToken
			).ConfigureAwait( false );
			if ( WatchTerminalEventKind.Timeout == pending.Kind ) {
				break;
			}
			if ( WatchTerminalEventKind.Interrupt == pending.Kind ) {
				return Canceled;
			}
			if (
				WatchTerminalEventKind.Resize == pending.Kind
				|| WatchTerminalEventKind.Repaint == pending.Kind
			) {
				await terminal.RepaintAsync(
					cancellationToken
				).ConfigureAwait( false );
			}
		}

		await terminal.ShowStatusAsync(
			ErrorExitPrompt,
			cancellationToken
		).ConfigureAwait( false );
		while ( true ) {
			WatchTerminalEvent terminalEvent = await terminal.ReadEventAsync(
				MaximumInputWait,
				cancellationToken
			).ConfigureAwait( false );
			if ( WatchTerminalEventKind.Interrupt == terminalEvent.Kind ) {
				return Canceled;
			}
			if ( WatchTerminalEventKind.Input == terminalEvent.Kind ) {
				return status;
			}
			if (
				WatchTerminalEventKind.Resize == terminalEvent.Kind
				|| WatchTerminalEventKind.Repaint == terminalEvent.Kind
			) {
				await terminal.RepaintAsync(
					cancellationToken
				).ConfigureAwait( false );
				await terminal.ShowStatusAsync(
					ErrorExitPrompt,
					cancellationToken
				).ConfigureAwait( false );
			}
		}
	}

	private static ProcessRunOptions BuildProcessOptions(
		ParsedArguments parsed,
		Stream capture,
		WatchTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( capture );
		if ( 1 > dimensions.Columns || 1 > dimensions.Rows ) {
			throw new ArgumentOutOfRangeException(
				nameof( dimensions )
			);
		}

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
		options.EnvironmentVariables[ "COLUMNS" ] = dimensions.Columns.ToString(
			CultureInfo.InvariantCulture
		);
		options.EnvironmentVariables[ "LINES" ] = dimensions.Rows.ToString(
			CultureInfo.InvariantCulture
		);
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
				"0.0",
				CultureInfo.CurrentCulture
			);
			string commandText = string.Join( " ", parsed.Command );
			string leftPrefix = $"Every {intervalText}s: ";
			string right = string.Concat(
				hostName,
				": ",
				now.ToString(
					"G",
					CultureInfo.CurrentCulture
				)
			);
			headerLines.Add(
				ComposeHeaderRow(
					leftPrefix,
					commandText,
					right,
					screen.Width
				)
			);
			headerLines.Add(
				ComposeLowHeader( elapsed, status, screen.Width )
			);
		}

		return new WatchRenderFrame(
			headerLines,
			screen,
			highlights
		);
	}

	private static string ComposeHeaderRow(
		string leftPrefix,
		string command,
		string right,
		int width
	) {
		ArgumentNullException.ThrowIfNull( leftPrefix );
		ArgumentNullException.ThrowIfNull( command );
		ArgumentNullException.ThrowIfNull( right );
		if ( 1 > width ) {
			throw new ArgumentOutOfRangeException( nameof( width ) );
		}

		int rightWidth = WatchTextLayout.GetWidth( right );
		if ( width < rightWidth ) {
			return string.Empty;
		}

		int prefixWidth = WatchTextLayout.GetWidth( leftPrefix );
		int availableForCommand = width - prefixWidth - rightWidth;
		string left = string.Empty;
		if ( 0 <= availableForCommand ) {
			int commandWidth = WatchTextLayout.GetWidth( command );
			if ( commandWidth < availableForCommand ) {
				left = string.Concat(
					leftPrefix,
					command
				);
			} else {
				const string Ellipsis = "…";
				int ellipsisWidth = WatchTextLayout.GetWidth( Ellipsis );
				if ( ellipsisWidth < availableForCommand ) {
					int commandLimit = Math.Max(
						0,
						availableForCommand - ellipsisWidth - 1
					);
					left = string.Concat(
						leftPrefix,
						WatchTextLayout.ClipToWidth(
							command,
							commandLimit
						),
						Ellipsis
					);
				} else {
					left = leftPrefix;
				}
			}
		}

		int leftWidth = WatchTextLayout.GetWidth( left );
		return string.Concat(
			left,
			new string( ' ', width - leftWidth - rightWidth ),
			right
		);
	}

	private static string ComposeLowHeader(
		TimeSpan elapsed,
		int status,
		int width
	) {
		if ( 1 > width ) {
			throw new ArgumentOutOfRangeException( nameof( width ) );
		}

		string text;
		if ( TimeSpan.FromDays( 1 ) < elapsed ) {
			text = $"in >1 day ({status})";
		} else if ( TimeSpan.FromMilliseconds( 1 ) > elapsed ) {
			text = $"in <0.001s ({status})";
		} else {
			text = string.Concat(
				"in ",
				elapsed.TotalSeconds.ToString(
					"0.000",
					CultureInfo.CurrentCulture
				),
				"s (",
				status.ToString( CultureInfo.InvariantCulture ),
				")"
			);
		}

		int textWidth = WatchTextLayout.GetWidth( text );
		if ( width < textWidth ) {
			return string.Empty;
		}
		return string.Concat(
			new string(
				' ',
				width - textWidth
			),
			text
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
				differences = true;
				permanentDifferences = true;
				continue;
			}
			if ( argument.StartsWith( "-d", StringComparison.Ordinal )
				&& 2 < argument.Length ) {
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
		" -e, --errexit               freeze non-zero output until a fresh key press",
		" -f, --follow                retain, append, and scroll command output",
		" -g, --chgexit               exit when visible command output changes",
		" -q, --equexit <cycles>      exit after visible output is unchanged for cycles",
		" -n, --interval <secs>       seconds between updates",
		" -p, --precise               include command running time in the interval",
		" -r, --no-rerun              do not rerun command because of a resize",
		" -s, --shotsdir <dir>        directory to store screenshots",
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
		WatchCell[] cells,
		int cursorRow = 0,
		int cursorColumn = 0
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
		if ( 0 > cursorRow || height <= cursorRow ) {
			throw new ArgumentOutOfRangeException( nameof( cursorRow ) );
		}
		if ( 0 > cursorColumn || width < cursorColumn ) {
			throw new ArgumentOutOfRangeException( nameof( cursorColumn ) );
		}
		this.Width = width;
		this.Height = height;
		this.cells = cells;
		this.CursorRow = cursorRow;
		this.CursorColumn = cursorColumn;
	}

	private int CursorRow { get; }
	private int CursorColumn { get; }

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
		return Create(
			output,
			dimensions,
			noTitle,
			noWrap,
			preserveColor,
			out _
		);
	}

	internal static WatchScreen Create(
		string output,
		WatchTerminalDimensions dimensions,
		bool noTitle,
		bool noWrap,
		bool preserveColor,
		out int alertCount
	) {
		ArgumentNullException.ThrowIfNull( output );
		alertCount = 0;
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
			char character = output[ index ];
			if ( '\n' == character ) {
				if ( skipUntilNewline ) {
					style = CursesStyle.Default;
				}
				row++;
				column = 0;
				skipUntilNewline = false;
				index++;
				continue;
			}
			if ( skipUntilNewline ) {
				index++;
				continue;
			}
			if ( preserveColor && '\u001b' == character ) {
				index = ConsumeAnsiEscape(
					output,
					index,
					ref style
				);
				continue;
			}
			if ( '\r' == character ) {
				column = 0;
				index++;
				continue;
			}
			if ( '\a' == character ) {
				alertCount++;
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

	internal static WatchScreen AppendFollow(
		WatchScreen? previous,
		string output,
		WatchTerminalDimensions dimensions,
		bool noTitle,
		bool noWrap,
		bool preserveColor
	) {
		return AppendFollow(
			previous,
			output,
			dimensions,
			noTitle,
			noWrap,
			preserveColor,
			out _
		);
	}

	internal static WatchScreen AppendFollow(
		WatchScreen? previous,
		string output,
		WatchTerminalDimensions dimensions,
		bool noTitle,
		bool noWrap,
		bool preserveColor,
		out int alertCount
	) {
		ArgumentNullException.ThrowIfNull( output );
		alertCount = 0;
		int bodyHeight = dimensions.Rows - ( noTitle ? 0 : 2 );
		if ( 1 > dimensions.Columns || 1 > bodyHeight ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		WatchScreen? basis = previous;
		if (
			basis is not null
			&& (
				basis.Width != dimensions.Columns
				|| basis.Height != bodyHeight
			)
		) {
			basis = basis.ResizeForFollow(
				dimensions,
				noTitle
			);
		}

		WatchCell[] cells = new WatchCell[
			checked( dimensions.Columns * bodyHeight )
		];
		Array.Fill( cells, WatchCell.Blank );
		int row = 0;
		int column = 0;
		if ( basis is not null ) {
			Array.Copy(
				basis.cells,
				cells,
				cells.Length
			);
			row = basis.CursorRow;
			column = basis.CursorColumn;
		}

		CursesStyle style = CursesStyle.Default;
		bool skipUntilNewline = false;
		int index = 0;
		while ( index < output.Length ) {
			char character = output[ index ];
			if ( '\n' == character ) {
				if ( skipUntilNewline ) {
					style = CursesStyle.Default;
				}
				ClearFollowLineFrom(
					cells,
					dimensions.Columns,
					row,
					column
				);
				AdvanceFollowRow(
					cells,
					dimensions.Columns,
					bodyHeight,
					ref row,
					ref column
				);
				skipUntilNewline = false;
				index++;
				continue;
			}
			if ( skipUntilNewline ) {
				index++;
				continue;
			}
			if ( preserveColor && '\u001b' == character ) {
				index = ConsumeAnsiEscape(
					output,
					index,
					ref style
				);
				continue;
			}
			if ( '\r' == character ) {
				column = 0;
				index++;
				continue;
			}
			if ( '\a' == character ) {
				alertCount++;
				index++;
				continue;
			}
			if ( '\t' == character ) {
				int spaces = 8 - ( column % 8 );
				for ( int count = 0; count < spaces; count++ ) {
					WriteFollowElement(
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
					if ( skipUntilNewline ) {
						break;
					}
				}
				index++;
				continue;
			}
			if ( char.IsControl( character ) ) {
				index++;
				continue;
			}

			string textElement = StringInfo.GetNextTextElement(
				output,
				index
			);
			int elementLength = textElement.Length;
			int displayWidth = UnicodeCursesTextWidthProvider.Instance.GetWidth(
				textElement
			);
			if ( 0 == displayWidth ) {
				AppendZeroWidthElement(
					cells,
					dimensions.Columns,
					row,
					column,
					textElement
				);
			} else {
				WriteFollowElement(
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
			cells,
			row,
			column
		);
	}

	internal WatchScreen ResizeForFollow(
		WatchTerminalDimensions dimensions,
		bool noTitle
	) {
		int bodyHeight = dimensions.Rows - ( noTitle ? 0 : 2 );
		if ( 1 > dimensions.Columns || 1 > bodyHeight ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}
		if (
			this.Width == dimensions.Columns
			&& this.Height == bodyHeight
		) {
			return this;
		}

		WatchCell[] resized = new WatchCell[
			checked( dimensions.Columns * bodyHeight )
		];
		Array.Fill( resized, WatchCell.Blank );
		int rowsToCopy = Math.Min(
			this.Height,
			bodyHeight
		);
		int sourceFirstRow = Math.Max(
			0,
			this.Height - bodyHeight
		);
		int columnsToCopy = Math.Min(
			this.Width,
			dimensions.Columns
		);
		for ( int rowOffset = 0; rowOffset < rowsToCopy; rowOffset++ ) {
			int sourceRow = sourceFirstRow + rowOffset;
			for ( int column = 0; column < columnsToCopy; column++ ) {
				WatchCell cell = this.GetCell(
					sourceRow,
					column
				);
				if ( cell.IsContinuation ) {
					continue;
				}
				if (
					2 == cell.DisplayWidth
					&& dimensions.Columns <= column + 1
				) {
					continue;
				}
				int target = ( rowOffset * dimensions.Columns ) + column;
				resized[ target ] = cell;
				if ( 2 == cell.DisplayWidth ) {
					resized[ target + 1 ] = WatchCell.Continuation(
						cell.Style
					);
				}
			}
		}

		int cursorRow = this.CursorRow - sourceFirstRow;
		if ( 0 > cursorRow ) {
			cursorRow = 0;
		}
		cursorRow = Math.Min(
			cursorRow,
			bodyHeight - 1
		);
		int cursorColumn = Math.Min(
			this.CursorColumn,
			dimensions.Columns
		);
		return new WatchScreen(
			dimensions.Columns,
			bodyHeight,
			resized,
			cursorRow,
			cursorColumn
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

	private static int ConsumeAnsiEscape(
		string text,
		int escapeIndex,
		ref CursesStyle style
	) {
		ArgumentNullException.ThrowIfNull( text );
		if (
			0 > escapeIndex
			|| text.Length <= escapeIndex
			|| '\u001b' != text[ escapeIndex ]
		) {
			throw new ArgumentOutOfRangeException( nameof( escapeIndex ) );
		}

		int index = escapeIndex + 1;
		if ( text.Length <= index ) {
			return index;
		}

		char candidate = text[ index ];
		if ( '(' == candidate ) {
			index++;
			if ( text.Length <= index ) {
				return index;
			}
			index++;
			if ( text.Length <= index ) {
				return index;
			}
			candidate = text[ index ];
		}
		if ( '[' != candidate ) {
			return index;
		}

		int parameterStart = index + 1;
		index = parameterStart;
		const int MaximumAnsiBufferLength = 100;
		for (
			int count = 0;
			index < text.Length && count < MaximumAnsiBufferLength;
			count++, index++
		) {
			candidate = text[ index ];
			if ( 'm' == candidate ) {
				style = ApplySgr(
					style,
					text.AsSpan(
						parameterStart,
						index - parameterStart
					)
				);
				return index + 1;
			}
			if (
				( '0' > candidate || '9' < candidate )
				&& ';' != candidate
			) {
				return index + 1;
			}
		}
		return index;
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

	private static void WriteFollowElement(
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

		if ( column >= width || column + displayWidth > width ) {
			if ( noWrap ) {
				skipUntilNewline = true;
				return;
			}
			AdvanceFollowRow(
				cells,
				width,
				height,
				ref row,
				ref column
			);
		}

		ClearFollowCellForWrite(
			cells,
			width,
			row,
			column
		);
		if ( 2 == displayWidth ) {
			ClearFollowCellForWrite(
				cells,
				width,
				row,
				column + 1
			);
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

	private static void AdvanceFollowRow(
		WatchCell[] cells,
		int width,
		int height,
		ref int row,
		ref int column
	) {
		ArgumentNullException.ThrowIfNull( cells );
		column = 0;
		if ( row + 1 < height ) {
			row++;
			return;
		}

		Array.Copy(
			cells,
			width,
			cells,
			0,
			cells.Length - width
		);
		Array.Fill(
			cells,
			WatchCell.Blank,
			cells.Length - width,
			width
		);
		row = height - 1;
	}

	private static void ClearFollowLineFrom(
		WatchCell[] cells,
		int width,
		int row,
		int column
	) {
		ArgumentNullException.ThrowIfNull( cells );
		if ( width <= column ) {
			return;
		}
		if (
			0 < column
			&& cells[ ( row * width ) + column ].IsContinuation
		) {
			cells[ ( row * width ) + column - 1 ] = WatchCell.Blank;
		}
		Array.Fill(
			cells,
			WatchCell.Blank,
			( row * width ) + column,
			width - column
		);
	}

	private static void ClearFollowCellForWrite(
		WatchCell[] cells,
		int width,
		int row,
		int column
	) {
		ArgumentNullException.ThrowIfNull( cells );
		int index = ( row * width ) + column;
		WatchCell current = cells[ index ];
		if ( current.IsContinuation && 0 < column ) {
			cells[ index - 1 ] = WatchCell.Blank;
		}
		if (
			2 == current.DisplayWidth
			&& column + 1 < width
		) {
			cells[ index + 1 ] = WatchCell.Blank;
		}
		cells[ index ] = WatchCell.Blank;
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
				case 3:
				case 5:
				case 23:
				case 25:
					// DCurses 0.1 has no semantic italic or blink attributes.
					continue;
				case 4:
					style = style.WithAttributes( attributes | CursesTextAttributes.Underline );
					continue;
				case 7:
					style = style.WithAttributes( attributes | CursesTextAttributes.Reverse );
					continue;
				case 21:
					style = style.WithAttributes(
						attributes & ~CursesTextAttributes.Bold
					);
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
				return style;
			}

			bool foreground = 38 == code;
			if ( index + 2 < parameters.Count && 5 == parameters[ index + 1 ] ) {
				int colorIndex = parameters[ index + 2 ];
				if ( colorIndex is >= 0 and <= 255 ) {
					style = foreground
						? style.WithForeground( CursesColor.Indexed( colorIndex ) )
						: style.WithBackground( CursesColor.Indexed( colorIndex ) );
				}
				index += 2;
				continue;
			}
			return style;
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

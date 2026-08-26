// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.HugeTop;

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;
using Icod.Timing;

/// <summary>Implements the procps-ng compatible <c>hugetop</c> command.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int Canceled = 130;
	private const int MinimumColumns = 20;
	private const int MinimumRows = 5;
	private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds( 3 );
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.HugeTop",
		typeof( Command ).Assembly
	);

	/// <summary>Runs <c>hugetop</c> synchronously.</summary>
	public static int Run(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsync( args, stdout, stderr ).GetAwaiter().GetResult();
	}

	/// <summary>Runs <c>hugetop</c> asynchronously.</summary>
	public static Task<int> RunAsync(
		IReadOnlyList<string> args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcHugePageProvider? hugePageProvider = null,
		IMonotonicClock? clock = null,
		Func<DateTimeOffset>? wallClockProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsyncCore(
			args,
			stdout,
			stderr,
			hugePageProvider,
			SystemHugeTopTerminalSessionFactory.Instance,
			clock,
			wallClockProvider,
			cancellationToken
		);
	}

	internal static async Task<int> RunAsyncCore(
		IReadOnlyList<string> args,
		Stream? stdout,
		Stream? stderr,
		IProcHugePageProvider? hugePageProvider,
		IHugeTopTerminalSessionFactory terminalFactory,
		IMonotonicClock? clock,
		Func<DateTimeOffset>? wallClockProvider,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminalFactory );

		Stream output = stdout ?? Console.OpenStandardOutput();
		Stream errorOutput = stderr ?? Console.OpenStandardError();
		IProcHugePageProvider provider = hugePageProvider ?? SystemProcHugePageProvider.Instance;
		IMonotonicClock monotonicClock = clock ?? SystemMonotonicClock.Instance;
		Func<DateTimeOffset> wallClock = wallClockProvider ?? GetCurrentTime;
		ParsedArguments parsed = Parse( args );

		if ( parsed.Error is not null ) {
			await WriteTextAsync(
				errorOutput,
				$"hugetop: {parsed.Error}{Environment.NewLine}",
				cancellationToken
			).ConfigureAwait( false );
			await WriteUsageAsync( errorOutput, cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
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
		if ( parsed.Once ) {
			return await RunOnceAsync(
				provider,
				parsed,
				wallClock,
				output,
				errorOutput,
				cancellationToken
			).ConfigureAwait( false );
		}

		return await RunInteractiveAsync(
			provider,
			terminalFactory,
			monotonicClock,
			parsed,
			wallClock,
			errorOutput,
			cancellationToken
		).ConfigureAwait( false );
	}

	private static async Task<int> RunOnceAsync(
		IProcHugePageProvider provider,
		ParsedArguments parsed,
		Func<DateTimeOffset> wallClock,
		Stream output,
		Stream errorOutput,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( provider );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( wallClock );
		ArgumentNullException.ThrowIfNull( output );
		ArgumentNullException.ThrowIfNull( errorOutput );

		ProcObservedValue<ProcHugePageSnapshot> observed = await provider.GetSnapshotAsync(
			cancellationToken
		).ConfigureAwait( false );
		if ( !observed.HasValue ) {
			await WriteUnavailableAsync(
				errorOutput,
				observed,
				cancellationToken
			).ConfigureAwait( false );
			return Failure;
		}

		string text = HugeTopRenderer.Render(
			observed.Value,
			parsed.Numa,
			parsed.Human,
			wallClock()
		);
		await WriteTextAsync( output, text, cancellationToken ).ConfigureAwait( false );
		return Success;
	}

	private static async Task<int> RunInteractiveAsync(
		IProcHugePageProvider provider,
		IHugeTopTerminalSessionFactory terminalFactory,
		IMonotonicClock clock,
		ParsedArguments parsed,
		Func<DateTimeOffset> wallClock,
		Stream errorOutput,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( provider );
		ArgumentNullException.ThrowIfNull( terminalFactory );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( wallClock );
		ArgumentNullException.ThrowIfNull( errorOutput );

		IHugeTopTerminalSession? terminal = null;
		try {
			terminal = await terminalFactory.OpenAsync( cancellationToken ).ConfigureAwait( false );
			if ( !terminal.IsInteractive ) {
				await WriteFailureAsync(
					errorOutput,
					"interactive terminal input and output are required; use --once for batch output"
				).ConfigureAwait( false );
				return Failure;
			}

			HugeTopTerminalDimensions dimensions = terminal.GetDimensions();
			if ( !IsUsableDimensions( dimensions ) ) {
				await WriteFailureAsync(
					errorOutput,
					$"terminal is too small for the hugetop display; at least {MinimumColumns} columns by {MinimumRows} rows are required"
				).ConfigureAwait( false );
				return Failure;
			}

			using CancellationTokenSource linkedCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken,
					terminal.TerminationToken
				);
			CancellationToken refreshToken = linkedCancellation.Token;
			while ( true ) {
				refreshToken.ThrowIfCancellationRequested();
				long cycleStarted = clock.GetTimestamp();
				ProcObservedValue<ProcHugePageSnapshot> observed = await provider.GetSnapshotAsync(
					refreshToken
				).ConfigureAwait( false );
				if ( !observed.HasValue ) {
					await WriteUnavailableAsync(
						errorOutput,
						observed,
						refreshToken
					).ConfigureAwait( false );
					return Failure;
				}

				ProcHugePageSnapshot currentSnapshot = observed.Value;
				DateTimeOffset currentObservedAt = wallClock();
				dimensions = terminal.GetDimensions();
				if ( !IsUsableDimensions( dimensions ) ) {
					await WriteFailureAsync(
						errorOutput,
						$"terminal is too small for the hugetop display; at least {MinimumColumns} columns by {MinimumRows} rows are required"
					).ConfigureAwait( false );
					return Failure;
				}

				await terminal.RenderAsync(
					HugeTopRenderer.RenderFrame(
						currentSnapshot,
						parsed.Numa,
						parsed.Human,
						currentObservedAt,
						dimensions
					),
					refreshToken
				).ConfigureAwait( false );

				while ( true ) {
					TimeSpan elapsed = clock.GetElapsedTime(
						cycleStarted,
						clock.GetTimestamp()
					);
					TimeSpan remaining = (parsed.Delay > elapsed)
						? parsed.Delay - elapsed
						: TimeSpan.Zero
					;
					if ( TimeSpan.Zero >= remaining ) {
						break;
					}

					HugeTopTerminalEvent terminalEvent = await terminal.ReadEventAsync(
						remaining,
						refreshToken
					).ConfigureAwait( false );
					if ( HugeTopTerminalEventKind.Timeout == terminalEvent.Kind ) {
						break;
					}
					if ( HugeTopTerminalEventKind.Interrupt == terminalEvent.Kind ) {
						return Canceled;
					}
					if ( HugeTopTerminalEventKind.Repaint == terminalEvent.Kind ) {
						await terminal.RepaintAsync( refreshToken ).ConfigureAwait( false );
						continue;
					}
					if ( HugeTopTerminalEventKind.Resize != terminalEvent.Kind ) {
						continue;
					}

					dimensions = terminal.GetDimensions();
					if ( !IsUsableDimensions( dimensions ) ) {
						await WriteFailureAsync(
							errorOutput,
							$"terminal is too small for the hugetop display; at least {MinimumColumns} columns by {MinimumRows} rows are required"
						).ConfigureAwait( false );
						return Failure;
					}
					await terminal.RenderAsync(
						HugeTopRenderer.RenderFrame(
							currentSnapshot,
							parsed.Numa,
							parsed.Human,
							currentObservedAt,
							dimensions
						),
						refreshToken
					).ConfigureAwait( false );
				}
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

	private static ParsedArguments Parse( IReadOnlyList<string> args ) {
		ArgumentNullException.ThrowIfNull( args );
		TimeSpan delay = DefaultDelay;
		bool numa = false;
		bool once = false;
		bool human = false;

		for ( int index = 0; index < args.Count; index++ ) {
			string argument = args[ index ];
			if ( "-h" == argument || "--help" == argument ) {
				return ParsedArguments.ForHelp();
			}
			if ( "-V" == argument || "--version" == argument ) {
				return ParsedArguments.ForVersion();
			}
			if ( "-n" == argument || "--numa" == argument ) {
				numa = true;
				continue;
			}
			if ( "-o" == argument || "--once" == argument ) {
				once = true;
				continue;
			}
			if ( "-H" == argument || "--human" == argument ) {
				human = true;
				continue;
			}
			if ( TryOptionValue(
				args,
				ref index,
				argument,
				"-d",
				"--delay",
				out string delayText,
				out string? delayError
			) ) {
				if ( delayError is not null ) {
					return ParsedArguments.Failed( delayError );
				}
				if ( !long.TryParse(
					delayText,
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out long seconds
				) ) {
					return ParsedArguments.Failed( "illegal delay" );
				}
				if ( 1L > seconds ) {
					return ParsedArguments.Failed( "delay must be positive integer" );
				}
				if ( uint.MaxValue < seconds ) {
					return ParsedArguments.Failed( "too large delay value" );
				}
				delay = TimeSpan.FromSeconds( seconds );
				continue;
			}
			if ( "--" == argument ) {
				if ( index + 1 < args.Count ) {
					return ParsedArguments.Failed(
						$"unexpected operand '{args[ index + 1 ]}'"
					);
				}
				break;
			}
			if ( argument.StartsWith( "-", StringComparison.Ordinal ) ) {
				return ParsedArguments.Failed( $"unrecognized option '{argument}'" );
			}
			return ParsedArguments.Failed( $"unexpected operand '{argument}'" );
		}

		return new ParsedArguments(
			delay,
			numa,
			once,
			human,
			Help: false,
			Version: false,
			Error: null
		);
	}

	private static bool TryOptionValue(
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
			value = argument[ shortName.Length.. ];
			return true;
		}
		string prefix = $"{longName}=";
		if ( argument.StartsWith( prefix, StringComparison.Ordinal ) ) {
			value = argument[ prefix.Length.. ];
			return true;
		}
		return false;
	}

	private static bool IsUsableDimensions( HugeTopTerminalDimensions dimensions ) =>
		MinimumColumns <= dimensions.Columns
		&& MinimumRows <= dimensions.Rows
		&& int.MaxValue >= (long)dimensions.Columns * dimensions.Rows;

	private static DateTimeOffset GetCurrentTime() => DateTimeOffset.Now;

	private static async Task WriteUnavailableAsync<T>(
		Stream stderr,
		ProcObservedValue<T> observed,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( stderr );
		ArgumentNullException.ThrowIfNull( observed );
		string? diagnostic = observed.Diagnostic;
		if ( string.IsNullOrWhiteSpace( diagnostic ) ) {
			diagnostic = "huge-page information is unavailable on this host";
		}
		await WriteTextAsync(
			stderr,
			$"hugetop: {diagnostic}{Environment.NewLine}",
			cancellationToken
		).ConfigureAwait( false );
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
		" hugetop [options]",
		string.Empty,
		"Options:",
		" -d, --delay <secs>  delay updates",
		" -n, --numa          display per-NUMA-node huge-page information",
		" -o, --once          only display once, then exit",
		" -H, --human         display human-readable output",
		" -h, --help          display this help and exit",
		" -V, --version       output version information and exit",
		string.Empty
	);

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

	private static async Task WriteFailureAsync( Stream stderr, string message ) {
		ArgumentNullException.ThrowIfNull( stderr );
		ArgumentNullException.ThrowIfNull( message );
		try {
			await WriteTextAsync(
				stderr,
				$"hugetop: {message}{Environment.NewLine}",
				CancellationToken.None
			).ConfigureAwait( false );
		} catch ( IOException ) {
		} catch ( ObjectDisposedException ) {
		}
	}

	private sealed record ParsedArguments(
		TimeSpan Delay,
		bool Numa,
		bool Once,
		bool Human,
		bool Help,
		bool Version,
		string? Error
	) {
		internal static ParsedArguments ForHelp() => new(
			DefaultDelay,
			false,
			false,
			false,
			true,
			false,
			null
		);

		internal static ParsedArguments ForVersion() => new(
			DefaultDelay,
			false,
			false,
			false,
			false,
			true,
			null
		);

		internal static ParsedArguments Failed( string error ) {
			ArgumentException.ThrowIfNullOrWhiteSpace( error );
			return new ParsedArguments(
				DefaultDelay,
				false,
				false,
				false,
				false,
				false,
				error
			);
		}
	}
}

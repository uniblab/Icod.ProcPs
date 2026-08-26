namespace Icod.ProcPs.Tload;

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements the procps-ng compatible <c>tload</c> command.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int Canceled = 130;
	private const double DefaultScale = 0d;
	private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds( 5d );
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.Tload",
		typeof( Command ).Assembly
	);

	/// <summary>Runs <c>tload</c> synchronously.</summary>
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

	/// <summary>Runs <c>tload</c> asynchronously with injectable ProcPs observation and sampling.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="stdout">Optional standard-output stream.</param>
	/// <param name="stderr">Optional standard-error stream.</param>
	/// <param name="metricsProvider">Optional ProcPs system-metrics provider.</param>
	/// <param name="sampler">Optional monotonic ProcPs sampler.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is the procps-compatible exit status.</returns>
	public static Task<int> RunAsync(
		IReadOnlyList<string> args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcSystemMetricsProvider? metricsProvider = null,
		ProcSampler? sampler = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsyncCore(
			args,
			stdout,
			stderr,
			metricsProvider,
			sampler,
			SystemTloadTerminalSessionFactory.Instance,
			cancellationToken
		);
	}

	internal static async Task<int> RunAsyncCore(
		IReadOnlyList<string> args,
		Stream? stdout,
		Stream? stderr,
		IProcSystemMetricsProvider? metricsProvider,
		ProcSampler? sampler,
		ITloadTerminalSessionFactory terminalFactory,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminalFactory );

		Stream output = stdout ?? Console.OpenStandardOutput();
		Stream errorOutput = stderr ?? Console.OpenStandardError();
		IProcSystemMetricsProvider metrics = metricsProvider
			?? SystemProcSystemMetricsProvider.Instance;
		ProcSampler refreshSampler = sampler ?? ProcSampler.CreateSystem();
		ParsedArguments parsed = Parse( args );

		if ( parsed.Error is not null ) {
			await WriteTextAsync(
				errorOutput,
				$"tload: {parsed.Error}{Environment.NewLine}",
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

		ITloadTerminalSession? terminal = null;
		try {
			terminal = await terminalFactory.OpenAsync(
				parsed.TerminalPath,
				output,
				cancellationToken
			).ConfigureAwait( false );

			TloadTerminalDimensions dimensions = terminal.GetDimensions();
			if ( !IsUsableDimensions( dimensions ) ) {
				await WriteFailureAsync(
					errorOutput,
					"screen too small or too large"
				).ConfigureAwait( false );
				return Failure;
			}

			var graph = new TloadGraphState( parsed.Scale );
			await foreach ( ProcTimedSample<ProcObservedValue<ProcLoadAverages>> sample in
				refreshSampler.RefreshAsync(
					CaptureLoadAveragesAsync,
					parsed.Delay,
					fireImmediately: true,
					cancellationToken: cancellationToken
				).ConfigureAwait( false ) ) {
				TloadTerminalDimensions currentDimensions = terminal.GetDimensions();
				if ( !IsUsableDimensions( currentDimensions ) ) {
					await WriteFailureAsync(
						errorOutput,
						"screen too small or too large"
					).ConfigureAwait( false );
					return Failure;
				}
				if ( currentDimensions != dimensions ) {
					dimensions = currentDimensions;
					graph.Reset();
				}

				ProcObservedValue<ProcLoadAverages> observed = sample.Value;
				if ( !observed.HasValue ) {
					string diagnostic = string.IsNullOrWhiteSpace( observed.Diagnostic )
						? "load average is unavailable on this host"
						: observed.Diagnostic!
					;
					await WriteFailureAsync(
						errorOutput,
						diagnostic
					).ConfigureAwait( false );
					return Failure;
				}
				if ( !IsValidLoad( observed.Value ) ) {
					await WriteFailureAsync(
						errorOutput,
						"load-average provider returned an invalid value"
					).ConfigureAwait( false );
					return Failure;
				}

				string frame = graph.Render(
					observed.Value,
					dimensions
				);
				await terminal.WriteFrameAsync(
					frame,
					cancellationToken
				).ConfigureAwait( false );
			}

			return Success;
		} catch ( OperationCanceledException ) {
			return Canceled;
		} catch ( Exception exception ) when (
			exception is ArgumentException
				or IOException
				or InvalidOperationException
				or NotSupportedException
				or UnauthorizedAccessException
		) {
			await WriteFailureAsync(
				errorOutput,
				exception.Message
			).ConfigureAwait( false );
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

		async Task<ProcObservedValue<ProcLoadAverages>> CaptureLoadAveragesAsync(
			CancellationToken token
		) {
			ProcSystemSnapshot snapshot = await metrics.GetSnapshotAsync(
				token
			).ConfigureAwait( false );
			return snapshot.LoadAverages;
		}
	}

	private static bool IsUsableDimensions( TloadTerminalDimensions dimensions ) {
		return 2 <= dimensions.Columns
			&& 2 <= dimensions.Rows
			&& int.MaxValue >= (long)dimensions.Columns * dimensions.Rows;
	}

	private static bool IsValidLoad( ProcLoadAverages load ) {
		ArgumentNullException.ThrowIfNull( load );
		return double.IsFinite( load.OneMinute )
			&& double.IsFinite( load.FiveMinutes )
			&& double.IsFinite( load.FifteenMinutes )
			&& 0d <= load.OneMinute
			&& 0d <= load.FiveMinutes
			&& 0d <= load.FifteenMinutes;
	}

	private static ParsedArguments Parse( IReadOnlyList<string> args ) {
		ArgumentNullException.ThrowIfNull( args );
		double scale = DefaultScale;
		TimeSpan delay = DefaultDelay;
		string? terminalPath = null;

		for ( int index = 0; index < args.Count; index++ ) {
			string argument = args[ index ];
			if ( "--" == argument ) {
				index++;
				for ( ; index < args.Count; index++ ) {
					if ( terminalPath is not null ) {
						return ParsedArguments.Failed( "too many terminal operands" );
					}
					terminalPath = args[ index ];
				}
				break;
			}
			if ( "-h" == argument || "--help" == argument ) {
				return ParsedArguments.ForHelp();
			}
			if ( "-V" == argument || "--version" == argument ) {
				return ParsedArguments.ForVersion();
			}
			if ( TryOptionValue(
				args,
				ref index,
				argument,
				"-s",
				"--scale",
				out string scaleText,
				out string? scaleError
			) ) {
				if ( scaleError is not null ) {
					return ParsedArguments.Failed( scaleError );
				}
				if ( !double.TryParse(
					scaleText,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out scale
				) || !double.IsFinite( scale ) ) {
					return ParsedArguments.Failed( "failed to parse scale argument" );
				}
				if ( 0d > scale ) {
					return ParsedArguments.Failed( "scale cannot be negative" );
				}
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
					return ParsedArguments.Failed( "failed to parse delay argument" );
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
			if ( argument.StartsWith( '-' ) && "-" != argument ) {
				return ParsedArguments.Failed( $"unrecognized option '{argument}'" );
			}
			if ( terminalPath is not null ) {
				return ParsedArguments.Failed( "too many terminal operands" );
			}
			terminalPath = argument;
		}

		return new ParsedArguments(
			scale,
			delay,
			terminalPath,
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

	private static Task WriteUsageAsync(
		Stream output,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( output );
		return WriteTextAsync(
			output,
			HelpText(),
			cancellationToken
		);
	}

	private static string HelpText() => string.Join(
		Environment.NewLine,
		"Usage:",
		" tload [options] [tty]",
		string.Empty,
		"Options:",
		" -d, --delay <secs>  update delay in seconds",
		" -s, --scale <num>   vertical scale",
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
		await stream.WriteAsync(
			bytes,
			cancellationToken
		).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	private static async Task WriteFailureAsync(
		Stream stderr,
		string message
	) {
		ArgumentNullException.ThrowIfNull( stderr );
		ArgumentException.ThrowIfNullOrWhiteSpace( message );

		try {
			await WriteTextAsync(
				stderr,
				$"tload: {message}{Environment.NewLine}",
				CancellationToken.None
			).ConfigureAwait( false );
		} catch ( IOException ) {
		} catch ( ObjectDisposedException ) {
		}
	}

	private sealed record ParsedArguments(
		double Scale,
		TimeSpan Delay,
		string? TerminalPath,
		bool Help,
		bool Version,
		string? Error
	) {
		public static ParsedArguments ForHelp() => new(
			DefaultScale,
			DefaultDelay,
			null,
			true,
			false,
			null
		);

		public static ParsedArguments ForVersion() => new(
			DefaultScale,
			DefaultDelay,
			null,
			false,
			true,
			null
		);

		public static ParsedArguments Failed( string error ) {
			ArgumentException.ThrowIfNullOrWhiteSpace( error );
			return new ParsedArguments(
				DefaultScale,
				DefaultDelay,
				null,
				false,
				false,
				error
			);
		}
	}
}

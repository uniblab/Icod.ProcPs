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

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;
using Icod.Timing;

/// <summary>Implements the procps-ng compatible <c>slabtop</c> command.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int Canceled = 130;
	private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds( 3 );
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.SlabTop",
		typeof( Command ).Assembly
	);

	/// <summary>Runs <c>slabtop</c> synchronously.</summary>
	public static int Run(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsync( args, stdout, stderr ).GetAwaiter().GetResult();
	}

	/// <summary>Runs <c>slabtop</c> asynchronously.</summary>
	public static Task<int> RunAsync(
		IReadOnlyList<string> args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcSlabProvider? slabProvider = null,
		IMonotonicClock? clock = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return RunAsyncCore(
			args,
			stdout,
			stderr,
			slabProvider,
			SystemSlabTopTerminalSessionFactory.Instance,
			clock,
			cancellationToken
		);
	}

	internal static async Task<int> RunAsyncCore(
		IReadOnlyList<string> args,
		Stream? stdout,
		Stream? stderr,
		IProcSlabProvider? slabProvider,
		ISlabTopTerminalSessionFactory terminalFactory,
		IMonotonicClock? clock,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminalFactory );

		Stream output = stdout ?? Console.OpenStandardOutput();
		Stream errorOutput = stderr ?? Console.OpenStandardError();
		IProcSlabProvider provider = slabProvider ?? SystemProcSlabProvider.Instance;
		IMonotonicClock monotonicClock = clock ?? SystemMonotonicClock.Instance;
		ParsedArguments parsed = Parse( args );

		if ( parsed.Error is not null ) {
			await WriteTextAsync(
				errorOutput,
				$"slabtop: {parsed.Error}{Environment.NewLine}",
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
			errorOutput,
			cancellationToken
		).ConfigureAwait( false );
	}

	private static async Task<int> RunOnceAsync(
		IProcSlabProvider provider,
		ParsedArguments parsed,
		Stream output,
		Stream errorOutput,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( provider );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( output );
		ArgumentNullException.ThrowIfNull( errorOutput );

		ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> observed =
			await provider.GetSlabsAsync( cancellationToken ).ConfigureAwait( false );
		if ( !observed.HasValue ) {
			await WriteUnavailableAsync(
				errorOutput,
				observed,
				cancellationToken
			).ConfigureAwait( false );
			return Failure;
		}

		string text = SlabTopRenderer.Render(
			observed.Value,
			parsed.Sort,
			parsed.Human
		);
		await WriteTextAsync( output, text, cancellationToken ).ConfigureAwait( false );
		return Success;
	}

	private static async Task<int> RunInteractiveAsync(
		IProcSlabProvider provider,
		ISlabTopTerminalSessionFactory terminalFactory,
		IMonotonicClock clock,
		ParsedArguments parsed,
		Stream errorOutput,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( provider );
		ArgumentNullException.ThrowIfNull( terminalFactory );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( parsed );
		ArgumentNullException.ThrowIfNull( errorOutput );

		ISlabTopTerminalSession? terminal = null;
		try {
			terminal = await terminalFactory.OpenAsync( cancellationToken ).ConfigureAwait( false );
			if ( !terminal.IsInteractive ) {
				await WriteFailureAsync(
					errorOutput,
					"interactive terminal input and output are required; use --once for batch output"
				).ConfigureAwait( false );
				return Failure;
			}

			SlabTopTerminalDimensions dimensions = terminal.GetDimensions();
			if ( !IsUsableDimensions( dimensions ) ) {
				await WriteFailureAsync(
					errorOutput,
					"terminal is too small for the slabtop display"
				).ConfigureAwait( false );
				return Failure;
			}

			using CancellationTokenSource linkedCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken,
					terminal.TerminationToken
				);
			CancellationToken refreshToken = linkedCancellation.Token;
			SlabSortCriterion sort = parsed.Sort;
			while ( true ) {
				refreshToken.ThrowIfCancellationRequested();
				long cycleStarted = clock.GetTimestamp();
				ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> observed =
					await provider.GetSlabsAsync( refreshToken ).ConfigureAwait( false );
				if ( !observed.HasValue ) {
					await WriteUnavailableAsync(
						errorOutput,
						observed,
						refreshToken
					).ConfigureAwait( false );
					return Failure;
				}

				IReadOnlyList<ProcSlabCacheEntry> currentEntries = observed.Value;
				dimensions = terminal.GetDimensions();
				if ( !IsUsableDimensions( dimensions ) ) {
					await WriteFailureAsync(
						errorOutput,
						"terminal is too small for the slabtop display"
					).ConfigureAwait( false );
					return Failure;
				}

				SlabTopRenderFrame frame = SlabTopRenderer.RenderFrame(
					currentEntries,
					sort,
					parsed.Human,
					dimensions
				);
				await terminal.RenderAsync( frame, refreshToken ).ConfigureAwait( false );

				while ( true ) {
					TimeSpan elapsed = clock.GetElapsedTime(
						cycleStarted,
						clock.GetTimestamp()
					);
					TimeSpan remaining = parsed.Delay > elapsed
						? parsed.Delay - elapsed
						: TimeSpan.Zero;
					if ( TimeSpan.Zero >= remaining ) {
						break;
					}

					SlabTopTerminalEvent terminalEvent = await terminal.ReadEventAsync(
						remaining,
						refreshToken
					).ConfigureAwait( false );
					if ( SlabTopTerminalEventKind.Timeout == terminalEvent.Kind ) {
						break;
					}
					if ( SlabTopTerminalEventKind.Interrupt == terminalEvent.Kind ) {
						return Canceled;
					}
					if ( SlabTopTerminalEventKind.Input == terminalEvent.Kind ) {
						if ( !terminalEvent.Input.HasValue ) {
							continue;
						}
						SlabTopInputEvent input = terminalEvent.Input.Value;
						if ( SlabTopInputKey.EndOfInput == input.Key ) {
							return Success;
						}
						if (
							SlabTopInputKey.Character != input.Key
							|| !input.Character.HasValue
						) {
							continue;
						}
						int value = input.Character.Value.Value;
						if ( char.MaxValue < value ) {
							continue;
						}
						char command = (char)value;
						if ( 'q' == char.ToLowerInvariant( command ) ) {
							return Success;
						}
						if ( ' ' == command ) {
							break;
						}
						if (
							TryParseSort(
								command,
								out SlabSortCriterion requestedSort
							)
						) {
							sort = requestedSort;
							break;
						}
						continue;
					}
					if ( SlabTopTerminalEventKind.Repaint == terminalEvent.Kind ) {
						await terminal.RepaintAsync( refreshToken ).ConfigureAwait( false );
						continue;
					}
					if ( SlabTopTerminalEventKind.Resize != terminalEvent.Kind ) {
						continue;
					}

					dimensions = terminal.GetDimensions();
					if ( !IsUsableDimensions( dimensions ) ) {
						await WriteFailureAsync(
							errorOutput,
							"terminal is too small for the slabtop display"
						).ConfigureAwait( false );
						return Failure;
					}

					SlabTopRenderFrame resizedFrame = SlabTopRenderer.RenderFrame(
						currentEntries,
						sort,
						parsed.Human,
						dimensions
					);
					await terminal.RenderAsync(
						resizedFrame,
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
		bool delaySpecified = false;
		bool once = false;
		bool human = false;
		SlabSortCriterion sort = SlabSortCriterion.Objects;

		for ( int index = 0; index < args.Count; index++ ) {
			string argument = args[ index ];
			if ( "-h" == argument || "--help" == argument ) {
				return ParsedArguments.ForHelp();
			}
			if ( "-V" == argument || "--version" == argument ) {
				return ParsedArguments.ForVersion();
			}
			if ( "--human" == argument ) {
				human = true;
				continue;
			}
			if ( "-o" == argument || "--once" == argument ) {
				if ( delaySpecified ) {
					return ParsedArguments.Failed( "Cannot combine -d and -o options" );
				}
				once = true;
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
				if ( once ) {
					return ParsedArguments.Failed( "Cannot combine -d and -o options" );
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
				delaySpecified = true;
				continue;
			}
			if ( TryOptionValue(
				args,
				ref index,
				argument,
				"-s",
				"--sort",
				out string sortText,
				out string? sortError
			) ) {
				if ( sortError is not null ) {
					return ParsedArguments.Failed( sortError );
				}
				if ( string.IsNullOrEmpty( sortText ) ) {
					return ParsedArguments.Failed( "sort criterion cannot be empty" );
				}
				sort = ParseSort( sortText[ 0 ] );
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
			if ( argument.StartsWith( '-' ) ) {
				return ParsedArguments.Failed( $"unrecognized option '{argument}'" );
			}
			return ParsedArguments.Failed( $"unexpected operand '{argument}'" );
		}

		return new ParsedArguments(
			delay,
			once,
			human,
			sort,
			Help: false,
			Version: false,
			Error: null
		);
	}

	private static SlabSortCriterion ParseSort( char criterion ) {
		_ = TryParseSort(
			criterion,
			out SlabSortCriterion sort
		);
		return sort;
	}

	private static bool TryParseSort(
		char criterion,
		out SlabSortCriterion sort
	) {
		switch ( char.ToLowerInvariant( criterion ) ) {
			case 'a':
				sort = SlabSortCriterion.ActiveObjects;
				return true;
			case 'b':
				sort = SlabSortCriterion.ObjectsPerSlab;
				return true;
			case 'c':
				sort = SlabSortCriterion.CacheSize;
				return true;
			case 'l':
				sort = SlabSortCriterion.Slabs;
				return true;
			case 'v':
				sort = SlabSortCriterion.ActiveSlabs;
				return true;
			case 'n':
				sort = SlabSortCriterion.Name;
				return true;
			case 'o':
				sort = SlabSortCriterion.Objects;
				return true;
			case 'p':
				sort = SlabSortCriterion.PagesPerSlab;
				return true;
			case 's':
				sort = SlabSortCriterion.ObjectSize;
				return true;
			case 'u':
				sort = SlabSortCriterion.Utilization;
				return true;
			default:
				sort = SlabSortCriterion.Objects;
				return false;
		}
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

	private static bool IsUsableDimensions( SlabTopTerminalDimensions dimensions ) {
		return 40 <= dimensions.Columns
			&& 9 <= dimensions.Rows
			&& int.MaxValue >= (long)dimensions.Columns * dimensions.Rows;
	}

	private static async Task WriteUnavailableAsync<T>(
		Stream stderr,
		ProcObservedValue<T> observed,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( stderr );
		ArgumentNullException.ThrowIfNull( observed );
		string diagnostic = string.IsNullOrWhiteSpace( observed.Diagnostic )
			? "slab allocator information is unavailable on this host"
			: observed.Diagnostic;
		await WriteTextAsync(
			stderr,
			$"slabtop: {diagnostic}{Environment.NewLine}",
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

	private static string HelpText() {
		return string.Join(
			Environment.NewLine,
			"Usage:",
			" slabtop [options]",
			string.Empty,
			"Options:",
			" -d, --delay <secs>  delay updates",
			" -s, --sort <char>   specify sort criteria",
			" -o, --once          only display once, then exit",
			"     --human         display human-readable output",
			" -h, --help          display this help and exit",
			" -V, --version       output version information and exit",
			string.Empty,
			"Valid sort criteria: a b c l v n o p s u",
			string.Empty
		);
	}

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
				$"slabtop: {message}{Environment.NewLine}",
				CancellationToken.None
			).ConfigureAwait( false );
		} catch ( IOException ) {
		} catch ( ObjectDisposedException ) {
		}
	}

	private sealed record ParsedArguments(
		TimeSpan Delay,
		bool Once,
		bool Human,
		SlabSortCriterion Sort,
		bool Help,
		bool Version,
		string? Error
	) {
		internal static ParsedArguments ForHelp() {
			return new ParsedArguments(
				DefaultDelay,
				false,
				false,
				SlabSortCriterion.Objects,
				true,
				false,
				null
			);
		}

		internal static ParsedArguments ForVersion() {
			return new ParsedArguments(
				DefaultDelay,
				false,
				false,
				SlabSortCriterion.Objects,
				false,
				true,
				null
			);
		}

		internal static ParsedArguments Failed( string error ) {
			ArgumentException.ThrowIfNullOrWhiteSpace( error );
			return new ParsedArguments(
				DefaultDelay,
				false,
				false,
				SlabSortCriterion.Objects,
				false,
				false,
				error
			);
		}
	}
}

/// <summary>Identifies the procps-ng slabtop sort criterion.</summary>
internal enum SlabSortCriterion {
	ActiveObjects,
	ObjectsPerSlab,
	CacheSize,
	Slabs,
	ActiveSlabs,
	Name,
	Objects,
	PagesPerSlab,
	ObjectSize,
	Utilization
}

/// <summary>Represents a bounded semantic slabtop frame.</summary>
internal sealed class SlabTopRenderFrame {
	internal SlabTopRenderFrame(
		int columns,
		int rows,
		IReadOnlyList<string> lines,
		int? headerRow = null
	) {
		if ( 1 > columns ) {
			throw new ArgumentOutOfRangeException( nameof( columns ) );
		}
		if ( 1 > rows ) {
			throw new ArgumentOutOfRangeException( nameof( rows ) );
		}
		ArgumentNullException.ThrowIfNull( lines );
		if ( rows != lines.Count ) {
			throw new ArgumentException(
				"Frame line count does not match its row count.",
				nameof( lines )
			);
		}
		if (
			headerRow.HasValue
			&& ( 0 > headerRow.Value || rows <= headerRow.Value )
		) {
			throw new ArgumentOutOfRangeException( nameof( headerRow ) );
		}
		this.Columns = columns;
		this.Rows = rows;
		this.Lines = lines.ToArray();
		this.HeaderRow = headerRow;
	}

	internal int Columns { get; }
	internal int Rows { get; }
	internal IReadOnlyList<string> Lines { get; }
	internal int? HeaderRow { get; }
}

/// <summary>Renders procps-ng-style slabtop reports independently from terminal I/O.</summary>
internal static class SlabTopRenderer {
	private const int HeaderRow = 6;

	internal static string Render(
		IReadOnlyList<ProcSlabCacheEntry> entries,
		SlabSortCriterion sort,
		bool human
	) {
		ArgumentNullException.ThrowIfNull( entries );
		IReadOnlyList<string> lines = BuildLines( entries, sort, human );
		return string.Concat(
			string.Join( Environment.NewLine, lines ),
			Environment.NewLine
		);
	}

	internal static SlabTopRenderFrame RenderFrame(
		IReadOnlyList<ProcSlabCacheEntry> entries,
		SlabSortCriterion sort,
		bool human,
		SlabTopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( entries );
		if ( 1 > dimensions.Columns || 1 > dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		IReadOnlyList<string> source = BuildLines( entries, sort, human );
		string[] lines = new string[ dimensions.Rows ];
		int visibleRows = Math.Max(
			0,
			dimensions.Rows - 1
		);
		for ( int index = 0; index < dimensions.Rows; index++ ) {
			lines[ index ] = index < visibleRows && index < source.Count
				? source[ index ]
				: string.Empty;
		}
		return new SlabTopRenderFrame(
			dimensions.Columns,
			dimensions.Rows,
			lines,
			HeaderRow < dimensions.Rows
				? HeaderRow
				: null
		);
	}

	private static IReadOnlyList<string> BuildLines(
		IReadOnlyList<ProcSlabCacheEntry> entries,
		SlabSortCriterion sort,
		bool human
	) {
		ProcSlabCacheEntry[] ordered = SortEntries( entries, sort ).ToArray();
		ulong totalObjects = Sum( entries, static entry => entry.TotalObjects );
		ulong activeObjects = Sum( entries, static entry => entry.ActiveObjects );
		ulong totalSlabs = Sum( entries, static entry => entry.TotalSlabs );
		ulong activeSlabs = Sum( entries, static entry => entry.ActiveSlabs );
		ulong activeCaches = (ulong)entries.Count( static entry => 0UL < entry.ActiveObjects );
		ulong totalCaches = (ulong)entries.Count;
		ulong activeSize = Sum( entries, static entry => ActiveCacheSize( entry ) );
		ulong totalSize = Sum( entries, static entry => CacheSize( entry ) );
		ulong minObjectSize = 0UL;
		ulong maxObjectSize = 0UL;
		double averageObjectSize = 0;
		if ( 0 < entries.Count ) {
			minObjectSize = entries.Min( static entry => entry.ObjectSizeBytes );
			maxObjectSize = entries.Max( static entry => entry.ObjectSizeBytes );
			averageObjectSize = entries.Average(
				static entry => (double)entry.ObjectSizeBytes
			);
		}

		List<string> lines = [
			$" Active / Total Objects (% used)       : {activeObjects} / {totalObjects} ({Percent( activeObjects, totalObjects ):0.0}%)",
			$" Active / Total Slabs (% used)         : {activeSlabs} / {totalSlabs} ({Percent( activeSlabs, totalSlabs ):0.0}%)",
			$" Active / Total Caches (% used)        : {activeCaches} / {totalCaches} ({Percent( activeCaches, totalCaches ):0.0}%)",
			$" Active / Total Size (% used)          : {FormatBytes( activeSize, human )} / {FormatBytes( totalSize, human )} ({Percent( activeSize, totalSize ):0.0}%)",
			$" Minimum / Average / Maximum Object    : {FormatObjectSize( minObjectSize )} / {FormatObjectSize( (ulong)Math.Round( averageObjectSize ) )} / {FormatObjectSize( maxObjectSize )}",
			string.Empty,
			"    OBJS   ACTIVE  USE OBJ SIZE  SLABS OBJ/SLAB CACHE SIZE NAME"
		];
		foreach ( ProcSlabCacheEntry entry in ordered ) {
			lines.Add(
				string.Concat(
					$"{entry.TotalObjects,8} {entry.ActiveObjects,8} {Percent( entry.ActiveObjects, entry.TotalObjects ),4:0}% ",
					$"{FormatObjectSize( entry.ObjectSizeBytes ),8} {entry.TotalSlabs,6} {entry.ObjectsPerSlab,8} ",
					$"{FormatBytes( CacheSize( entry ), human ),10} {entry.Name}"
				)
			);
		}
		return lines;
	}

	private static IEnumerable<ProcSlabCacheEntry> SortEntries(
		IReadOnlyList<ProcSlabCacheEntry> entries,
		SlabSortCriterion sort
	) {
		ArgumentNullException.ThrowIfNull( entries );
		return sort switch {
			SlabSortCriterion.ActiveObjects => entries.OrderByDescending(
				static entry => entry.ActiveObjects
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.ObjectsPerSlab => entries.OrderByDescending(
				static entry => entry.ObjectsPerSlab
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.CacheSize => entries.OrderByDescending(
				static entry => CacheSize( entry )
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.Slabs => entries.OrderByDescending(
				static entry => entry.TotalSlabs
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.ActiveSlabs => entries.OrderByDescending(
				static entry => entry.ActiveSlabs
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.Name => entries.OrderBy(
				static entry => entry.Name,
				StringComparer.Ordinal
			),
			SlabSortCriterion.PagesPerSlab => entries.OrderByDescending(
				static entry => entry.PagesPerSlab
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.ObjectSize => entries.OrderByDescending(
				static entry => entry.ObjectSizeBytes
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			SlabSortCriterion.Utilization => entries.OrderByDescending(
				static entry => Percent( entry.ActiveObjects, entry.TotalObjects )
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal ),
			_ => entries.OrderByDescending(
				static entry => entry.TotalObjects
			).ThenBy( static entry => entry.Name, StringComparer.Ordinal )
		};
	}

	private static ulong CacheSize( ProcSlabCacheEntry entry ) {
		ArgumentNullException.ThrowIfNull( entry );
		return SlabBytes( entry.TotalSlabs, entry.PagesPerSlab );
	}

	private static ulong ActiveCacheSize( ProcSlabCacheEntry entry ) {
		ArgumentNullException.ThrowIfNull( entry );
		return SlabBytes( entry.ActiveSlabs, entry.PagesPerSlab );
	}

	private static ulong SlabBytes( ulong slabs, ulong pagesPerSlab ) {
		return SaturatingMultiply(
			SaturatingMultiply( slabs, pagesPerSlab ),
			(ulong)Math.Max( 1, Environment.SystemPageSize )
		);
	}

	private static ulong Sum(
		IEnumerable<ProcSlabCacheEntry> entries,
		Func<ProcSlabCacheEntry, ulong> selector
	) {
		ArgumentNullException.ThrowIfNull( entries );
		ArgumentNullException.ThrowIfNull( selector );
		ulong total = 0UL;
		foreach ( ProcSlabCacheEntry entry in entries ) {
			total = SaturatingAdd( total, selector( entry ) );
		}
		return total;
	}

	private static double Percent( ulong active, ulong total ) {
		if ( 0UL == total ) {
			return 0;
		}
		return 100d * active / total;
	}

	private static string FormatObjectSize(
		ulong bytes
	) {
		return $"{bytes / 1024d:0.00}K";
	}

	private static string FormatBytes( ulong bytes, bool human ) {
		if ( !human ) {
			return $"{bytes / 1024d:0.00}K";
		}

		string[] suffixes = [ "B", "Ki", "Mi", "Gi", "Ti", "Pi", "Ei" ];
		double value = bytes;
		int suffixIndex = 0;
		while ( 1024d <= value && suffixIndex + 1 < suffixes.Length ) {
			value /= 1024d;
			suffixIndex++;
		}
		if ( 10d <= value || 0 == suffixIndex ) {
			return $"{value:0}{suffixes[ suffixIndex ]}";
		}
		return $"{value:0.0}{suffixes[ suffixIndex ]}";
	}

	private static ulong SaturatingAdd( ulong left, ulong right ) {
		if ( ulong.MaxValue - left < right ) {
			return ulong.MaxValue;
		}
		return left + right;
	}

	private static ulong SaturatingMultiply( ulong left, ulong right ) {
		if ( 0UL != right && ulong.MaxValue / right < left ) {
			return ulong.MaxValue;
		}
		return left * right;
	}
}

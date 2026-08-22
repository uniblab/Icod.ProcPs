namespace Icod.ProcPs.Shared;

using System.Globalization;
using System.Text;
using Icod.CommandFramework.Processes;

/// <summary>Describes one numeric Linux <c>/proc/PID/smaps</c> detail field.</summary>
public sealed class ProcMemoryMapMetric {
	/// <summary>Gets the kernel field name without the trailing colon.</summary>
	public string Name { get; }
	/// <summary>Gets the unsigned numeric value as reported by the kernel.</summary>
	public ulong Value { get; }
	/// <summary>Gets the optional unit token, such as <c>kB</c>.</summary>
	public string? Unit { get; }
	/// <summary>Initializes a memory-map detail field.</summary>
	public ProcMemoryMapMetric( string name, ulong value, string? unit = null ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		this.Name = name;
		this.Value = value;
		this.Unit = unit;
	}
}

/// <summary>Combines one memory-map range with the optional detail fields from Linux <c>smaps</c>.</summary>
public sealed class ProcMemoryMapRegion {
	/// <summary>Gets the basic range, permission, offset, device, inode, and mapping-name data.</summary>
	public ProcMemoryMapEntry Map { get; }
	/// <summary>Gets numeric <c>smaps</c> fields in kernel presentation order.</summary>
	public IReadOnlyList<ProcMemoryMapMetric> Metrics { get; }
	/// <summary>Gets the optional Linux <c>VmFlags</c> value without the field name.</summary>
	public string? VmFlags { get; }
	/// <summary>Initializes a memory-map region.</summary>
	public ProcMemoryMapRegion( ProcMemoryMapEntry map, IEnumerable<ProcMemoryMapMetric>? metrics = null, string? vmFlags = null ) {
		ArgumentNullException.ThrowIfNull( map );
		this.Map = map;
		this.Metrics = null == metrics ? Array.Empty<ProcMemoryMapMetric>() : metrics.ToArray();
		this.VmFlags = vmFlags;
	}
	/// <summary>Gets a numeric detail field by exact kernel field name.</summary>
	/// <param name="name">The case-sensitive kernel field name.</param>
	/// <returns>The field value when present; otherwise <see langword="null"/>.</returns>
	public ulong? GetMetric( string name ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		foreach ( var metric in this.Metrics ) if ( string.Equals( metric.Name, name, StringComparison.Ordinal ) ) return metric.Value;
		return null;
	}
}

/// <summary>Contains one reuse-protected process memory-map observation.</summary>
public sealed class ProcMemoryMapSet {
	/// <summary>Gets the observed regions in ascending address order.</summary>
	public IReadOnlyList<ProcMemoryMapRegion> Regions { get; }
	/// <summary>Gets whether the observation contains <c>smaps</c>-style details.</summary>
	public bool IsDetailed { get; }
	/// <summary>Initializes a process memory-map set.</summary>
	public ProcMemoryMapSet( IEnumerable<ProcMemoryMapRegion> regions, bool isDetailed ) {
		ArgumentNullException.ThrowIfNull( regions );
		this.Regions = regions.OrderBy( static region => region.Map.StartAddress ).ToArray();
		this.IsDetailed = isDetailed;
	}
}

/// <summary>Observes reuse-protected process memory maps for ProcPs consumers.</summary>
public interface IProcMemoryMapProvider {
	/// <summary>Observes memory mappings for the supplied process snapshot.</summary>
	/// <param name="process">The reuse-protected process snapshot.</param>
	/// <param name="detailed">Whether Linux <c>smaps</c> details are required.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The observed map set or an explicit availability result.</returns>
	Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync( ProcProcessSnapshot process, bool detailed = false, CancellationToken cancellationToken = default );
}

/// <summary>Parses Linux procfs memory-map formats into shared ProcPs models.</summary>
public static class ProcMemoryMapParsers {
	/// <summary>Parses Linux <c>/proc/PID/maps</c> text.</summary>
	/// <param name="text">Complete maps text.</param>
	/// <returns>Parsed map regions without detailed metrics.</returns>
	public static ProcMemoryMapSet ParseMaps( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var regions = new List<ProcMemoryMapRegion>();
		using var reader = new StringReader( text );
		while ( reader.ReadLine() is { } line ) {
			if ( string.IsNullOrWhiteSpace( line ) ) continue;
			regions.Add( new ProcMemoryMapRegion( LinuxProcParsers.ParseMemoryMapLine( line ) ) );
		}
		return new ProcMemoryMapSet( regions, false );
	}

	/// <summary>Parses Linux <c>/proc/PID/smaps</c> text while preserving numeric detail-field order.</summary>
	/// <param name="text">Complete smaps text.</param>
	/// <returns>Parsed detailed map regions.</returns>
	public static ProcMemoryMapSet ParseSmaps( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var regions = new List<ProcMemoryMapRegion>();
		ProcMemoryMapEntry? currentMap = null;
		var metrics = new List<ProcMemoryMapMetric>();
		string? vmFlags = null;
		using var reader = new StringReader( text );
		while ( reader.ReadLine() is { } line ) {
			if ( string.IsNullOrWhiteSpace( line ) ) continue;
			if ( IsMapHeader( line ) ) {
				if ( null != currentMap ) regions.Add( new ProcMemoryMapRegion( currentMap, metrics, vmFlags ) );
				currentMap = LinuxProcParsers.ParseMemoryMapLine( line );
				metrics = new List<ProcMemoryMapMetric>();
				vmFlags = null;
				continue;
			}
			if ( null == currentMap ) throw new FormatException( "A /proc/PID/smaps detail appeared before the first mapping." );
			var separator = line.IndexOf( ':' );
			if ( 0 >= separator ) continue;
			var name = line[ ..separator ].Trim();
			var valueText = line[ ( separator + 1 ).. ].Trim();
			if ( string.Equals( name, "VmFlags", StringComparison.Ordinal ) ) {
				vmFlags = valueText;
				continue;
			}
			var parts = valueText.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			if ( 2 > parts.Length || !string.Equals( parts[ 1 ], "kB", StringComparison.Ordinal ) || !ulong.TryParse( parts[ 0 ], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ) continue;
			metrics.Add( new ProcMemoryMapMetric( name, value, parts[ 1 ] ) );
		}
		if ( null != currentMap ) regions.Add( new ProcMemoryMapRegion( currentMap, metrics, vmFlags ) );
		return new ProcMemoryMapSet( regions, true );
	}

	private static bool IsMapHeader( string line ) {
		if ( 0 == line.Length || !char.IsAsciiHexDigit( line[ 0 ] ) ) return false;
		var space = line.IndexOf( ' ' );
		var dash = line.IndexOf( '-' );
		return 0 < dash && dash < space;
	}
}

/// <summary>Dispatches process memory-map observations to the strongest platform provider.</summary>
public sealed class SystemProcMemoryMapProvider : IProcMemoryMapProvider {
	private readonly IProcMemoryMapProvider provider;
	/// <summary>Gets the shared system memory-map provider.</summary>
	public static SystemProcMemoryMapProvider Instance { get; } = new();
	/// <summary>Initializes the provider over the system process inspector.</summary>
	public SystemProcMemoryMapProvider() : this( SystemProcessInspector.Instance ) { }
	/// <summary>Initializes the provider over an injectable process inspector and procfs root.</summary>
	/// <param name="inspector">Reuse-aware process inspector.</param>
	/// <param name="procRoot">Linux procfs root used by production or fixtures.</param>
	public SystemProcMemoryMapProvider( IProcessInspector inspector, string procRoot = "/proc" ) {
		ArgumentNullException.ThrowIfNull( inspector );
		ArgumentException.ThrowIfNullOrWhiteSpace( procRoot );
		this.provider = OperatingSystem.IsLinux()
			? new LinuxProcMemoryMapProvider( inspector, procRoot )
			: UnsupportedProcMemoryMapProvider.Instance;
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync( ProcProcessSnapshot process, bool detailed = false, CancellationToken cancellationToken = default )
		=> this.provider.ObserveAsync( process, detailed, cancellationToken );

	private sealed class UnsupportedProcMemoryMapProvider : IProcMemoryMapProvider {
		public static UnsupportedProcMemoryMapProvider Instance { get; } = new();
		public Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync( ProcProcessSnapshot process, bool detailed = false, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( process );
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( ProcObservedValue<ProcMemoryMapSet>.Missing(
				ProcObservationAvailability.Unsupported,
				"Linux /proc/PID/maps and /proc/PID/smaps semantics are not available from the current non-Linux ProcPs provider."
			) );
		}
	}
}

/// <summary>Reads exact Linux <c>/proc/PID/maps</c> and <c>/proc/PID/smaps</c> observations.</summary>
public sealed class LinuxProcMemoryMapProvider : IProcMemoryMapProvider {
	private readonly IProcessInspector inspector;
	private readonly string procRoot;
	private static readonly Encoding ProcfsTextEncoding = Encoding.GetEncoding( Encoding.UTF8.CodePage, new EncoderReplacementFallback( "?" ), new DecoderReplacementFallback( "?" ) );
	/// <summary>Initializes a Linux procfs memory-map provider.</summary>
	/// <param name="inspector">Reuse-aware process inspector.</param>
	/// <param name="procRoot">Procfs root used by production or fixtures.</param>
	public LinuxProcMemoryMapProvider( IProcessInspector inspector, string procRoot = "/proc" ) {
		ArgumentNullException.ThrowIfNull( inspector );
		ArgumentException.ThrowIfNullOrWhiteSpace( procRoot );
		this.inspector = inspector;
		this.procRoot = procRoot;
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync( ProcProcessSnapshot process, bool detailed = false, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( process );
		cancellationToken.ThrowIfCancellationRequested();
		var before = this.inspector.ObserveIdentity( process.ProcessId );
		if ( !before.Succeeded ) return MissingFromOperation( before.Status, before.Message );
		if ( null != process.Identity.ReuseToken && !process.Identity.Equals( before.Value ) ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Reused, $"Process identifier {process.ProcessId} was reused before memory-map observation." );
		}
		try {
			var filename = detailed ? "smaps" : "maps";
			var path = System.IO.Path.Combine( this.procRoot, process.ProcessId.ToString( CultureInfo.InvariantCulture ), filename );
			var bytes = await File.ReadAllBytesAsync( path, cancellationToken ).ConfigureAwait( false );
			var text = ProcfsTextEncoding.GetString( bytes );
			var maps = detailed ? ProcMemoryMapParsers.ParseSmaps( text ) : ProcMemoryMapParsers.ParseMaps( text );
			var after = this.inspector.ObserveIdentity( process.ProcessId );
			if ( !after.Succeeded ) return MissingFromOperation( after.Status, after.Message );
			if ( null != process.Identity.ReuseToken && !process.Identity.Equals( after.Value ) ) {
				return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Reused, $"Process identifier {process.ProcessId} was reused during memory-map observation." );
			}
			return ProcObservedValue<ProcMemoryMapSet>.Available( maps, ProcObservationSource.LinuxProcfs, Icod.CommandFramework.Host.ObservationFidelity.Exact );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( DirectoryNotFoundException exception ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( FileNotFoundException exception ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( IOException exception ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( FormatException exception ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		} catch ( OverflowException exception ) {
			return ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}

	private static ProcObservedValue<ProcMemoryMapSet> MissingFromOperation( ProcessOperationStatus status, string? message ) => status switch {
		ProcessOperationStatus.AccessDenied => ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.AccessDenied, message ),
		ProcessOperationStatus.Vanished => ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Vanished, message ),
		ProcessOperationStatus.Reused => ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Reused, message ),
		ProcessOperationStatus.Unsupported => ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Unsupported, message ),
		_ => ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Unavailable, message )
	};
}

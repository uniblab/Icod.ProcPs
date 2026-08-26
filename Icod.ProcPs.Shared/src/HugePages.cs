namespace Icod.ProcPs.Shared;

using System.Globalization;

/// <summary>Describes one configured huge-page pool for a Linux NUMA node.</summary>
public sealed class ProcHugePagePool {
	/// <summary>Gets the huge-page size in bytes.</summary>
	public ulong PageSizeBytes { get; }

	/// <summary>Gets the configured huge-page count.</summary>
	public ulong TotalPages { get; }

	/// <summary>Gets the currently free huge-page count.</summary>
	public ulong FreePages { get; }

	/// <summary>Initializes one huge-page pool observation.</summary>
	public ProcHugePagePool(
		ulong pageSizeBytes,
		ulong totalPages,
		ulong freePages
	) {
		if ( 0UL == pageSizeBytes ) {
			throw new ArgumentOutOfRangeException( nameof( pageSizeBytes ) );
		}
		if ( freePages > totalPages ) {
			throw new ArgumentOutOfRangeException( nameof( freePages ) );
		}

		this.PageSizeBytes = pageSizeBytes;
		this.TotalPages = totalPages;
		this.FreePages = freePages;
	}
}

/// <summary>Describes the huge-page pools observed for one Linux NUMA node.</summary>
public sealed class ProcHugePageNode {
	/// <summary>Gets the zero-based NUMA node identifier.</summary>
	public int NodeId { get; }

	/// <summary>Gets the node's huge-page pools ordered by page size.</summary>
	public IReadOnlyList<ProcHugePagePool> Pools { get; }

	/// <summary>Initializes a NUMA-node huge-page observation.</summary>
	public ProcHugePageNode(
		int nodeId,
		IEnumerable<ProcHugePagePool> pools
	) {
		ArgumentOutOfRangeException.ThrowIfNegative( nodeId );
		ArgumentNullException.ThrowIfNull( pools );

		this.NodeId = nodeId;
		this.Pools = pools
			.OrderBy( static pool => pool.PageSizeBytes )
			.ToArray();
	}
}

/// <summary>Describes huge-page memory attributed to one process.</summary>
public sealed class ProcHugePageProcess {
	/// <summary>Gets the process identifier.</summary>
	public int ProcessId { get; }

	/// <summary>Gets the observed short command name.</summary>
	public string CommandName { get; }

	/// <summary>Gets bytes mapped through shared hugetlb mappings.</summary>
	public ulong SharedBytes { get; }

	/// <summary>Gets bytes mapped through private hugetlb mappings.</summary>
	public ulong PrivateBytes { get; }

	/// <summary>Initializes one process huge-page observation.</summary>
	public ProcHugePageProcess(
		int processId,
		string commandName,
		ulong sharedBytes,
		ulong privateBytes
	) {
		if ( 0 >= processId ) {
			throw new ArgumentOutOfRangeException( nameof( processId ) );
		}
		ArgumentException.ThrowIfNullOrWhiteSpace( commandName );

		this.ProcessId = processId;
		this.CommandName = commandName;
		this.SharedBytes = sharedBytes;
		this.PrivateBytes = privateBytes;
	}
}

/// <summary>Contains one coherent huge-page system and process observation.</summary>
public sealed class ProcHugePageSnapshot {
	/// <summary>Gets NUMA-node huge-page pool observations.</summary>
	public IReadOnlyList<ProcHugePageNode> Nodes { get; }

	/// <summary>Gets processes with nonzero hugetlb usage.</summary>
	public IReadOnlyList<ProcHugePageProcess> Processes { get; }

	/// <summary>Initializes a huge-page snapshot.</summary>
	public ProcHugePageSnapshot(
		IEnumerable<ProcHugePageNode> nodes,
		IEnumerable<ProcHugePageProcess> processes
	) {
		ArgumentNullException.ThrowIfNull( nodes );
		ArgumentNullException.ThrowIfNull( processes );

		this.Nodes = nodes
			.OrderBy( static node => node.NodeId )
			.ToArray();
		this.Processes = processes
			.OrderByDescending(
				static process => SaturatingAdd(
					process.SharedBytes,
					process.PrivateBytes
				)
			)
			.ThenBy( static process => process.ProcessId )
			.ToArray();
	}

	private static ulong SaturatingAdd( ulong left, ulong right ) {
		if ( ulong.MaxValue - left < right ) {
			return ulong.MaxValue;
		}
		return left + right;
	}
}

/// <summary>Observes procps-ng compatible huge-page system and process information.</summary>
public interface IProcHugePageProvider {
	/// <summary>Captures huge-page pools and process hugetlb usage.</summary>
	Task<ProcObservedValue<ProcHugePageSnapshot>> GetSnapshotAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Selects the exact Linux huge-page provider or a controlled unsupported provider.</summary>
public sealed class SystemProcHugePageProvider : IProcHugePageProvider {
	private readonly IProcHugePageProvider provider;

	/// <summary>Gets the shared system huge-page provider.</summary>
	public static SystemProcHugePageProvider Instance { get; } = new();

	/// <summary>Initializes the system huge-page provider.</summary>
	public SystemProcHugePageProvider() {
		this.provider = (OperatingSystem.IsLinux())
			? new LinuxProcHugePageProvider(
				SystemProcProcessProvider.Instance,
				SystemProcMemoryMapProvider.Instance
			)
			: UnsupportedProcHugePageProvider.Instance
		;
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcHugePageSnapshot>> GetSnapshotAsync(
		CancellationToken cancellationToken = default
	) => this.provider.GetSnapshotAsync( cancellationToken );

	private sealed class UnsupportedProcHugePageProvider : IProcHugePageProvider {
		internal static UnsupportedProcHugePageProvider Instance { get; } = new();

		public Task<ProcObservedValue<ProcHugePageSnapshot>> GetSnapshotAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(
				ProcObservedValue<ProcHugePageSnapshot>.Missing(
					ProcObservationAvailability.Unsupported,
					"hugetop requires Linux sysfs huge-page pools and /proc/PID/smaps hugetlb accounting."
				)
			);
		}
	}
}

/// <summary>Reads exact Linux huge-page pools and per-process hugetlb accounting.</summary>
public sealed class LinuxProcHugePageProvider : IProcHugePageProvider {
	private const ulong Kibibyte = 1024UL;
	private readonly IProcProcessProvider processProvider;
	private readonly IProcMemoryMapProvider memoryMapProvider;
	private readonly string sysNodeRoot;

	/// <summary>Initializes a Linux huge-page provider.</summary>
	public LinuxProcHugePageProvider(
		IProcProcessProvider processProvider,
		IProcMemoryMapProvider memoryMapProvider,
		string sysNodeRoot = "/sys/devices/system/node"
	) {
		ArgumentNullException.ThrowIfNull( processProvider );
		ArgumentNullException.ThrowIfNull( memoryMapProvider );
		ArgumentException.ThrowIfNullOrWhiteSpace( sysNodeRoot );

		this.processProvider = processProvider;
		this.memoryMapProvider = memoryMapProvider;
		this.sysNodeRoot = sysNodeRoot;
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<ProcHugePageSnapshot>> GetSnapshotAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		try {
			IReadOnlyList<ProcHugePageNode> nodes = await this.ReadNodesAsync(
				cancellationToken
			).ConfigureAwait( false );
			if ( 0 == nodes.Count ) {
				return ProcObservedValue<ProcHugePageSnapshot>.Missing(
					ProcObservationAvailability.Unavailable,
					"No Linux sysfs huge-page pools were found."
				);
			}

			IReadOnlyList<ProcHugePageProcess> processes =
				await this.ReadProcessesAsync( cancellationToken ).ConfigureAwait( false );
			return ProcObservedValue<ProcHugePageSnapshot>.Available(
				new ProcHugePageSnapshot( nodes, processes ),
				ProcObservationSource.LinuxSysfs,
				ObservationFidelity.Exact
			);
		} catch ( UnauthorizedAccessException exception ) {
			return Missing( ProcObservationAvailability.AccessDenied, exception );
		} catch ( DirectoryNotFoundException exception ) {
			return Missing( ProcObservationAvailability.Unavailable, exception );
		} catch ( FileNotFoundException exception ) {
			return Missing( ProcObservationAvailability.Unavailable, exception );
		} catch ( IOException exception ) {
			return Missing( ProcObservationAvailability.Unavailable, exception );
		} catch ( FormatException exception ) {
			return Missing( ProcObservationAvailability.Malformed, exception );
		} catch ( OverflowException exception ) {
			return Missing( ProcObservationAvailability.Malformed, exception );
		}
	}

	private async Task<IReadOnlyList<ProcHugePageNode>> ReadNodesAsync(
		CancellationToken cancellationToken
	) {
		var nodes = new List<ProcHugePageNode>();
		foreach ( string nodeDirectory in Directory.EnumerateDirectories(
			this.sysNodeRoot,
			"node*",
			SearchOption.TopDirectoryOnly
		) ) {
			cancellationToken.ThrowIfCancellationRequested();
			string? nodeName = System.IO.Path.GetFileName( nodeDirectory );
			if ( !TryParseNodeId( nodeName, out int nodeId ) ) {
				continue;
			}

			string hugePagesDirectory = System.IO.Path.Combine(
				nodeDirectory,
				"hugepages"
			);
			if ( !Directory.Exists( hugePagesDirectory ) ) {
				continue;
			}

			var pools = new List<ProcHugePagePool>();
			foreach ( string poolDirectory in Directory.EnumerateDirectories(
				hugePagesDirectory,
				"hugepages-*kB",
				SearchOption.TopDirectoryOnly
			) ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( !TryParsePageSize(
					System.IO.Path.GetFileName( poolDirectory ),
					out ulong pageSizeBytes
				) ) {
					continue;
				}

				ulong totalPages = await ReadUnsignedAsync(
					System.IO.Path.Combine( poolDirectory, "nr_hugepages" ),
					cancellationToken
				).ConfigureAwait( false );
				ulong freePages = await ReadUnsignedAsync(
					System.IO.Path.Combine( poolDirectory, "free_hugepages" ),
					cancellationToken
				).ConfigureAwait( false );
				if ( freePages > totalPages ) {
					throw new FormatException(
						$"Huge-page pool '{poolDirectory}' reports more free pages than configured pages."
					);
				}
				pools.Add(
					new ProcHugePagePool(
						pageSizeBytes,
						totalPages,
						freePages
					)
				);
			}

			if ( 0 < pools.Count ) {
				nodes.Add( new ProcHugePageNode( nodeId, pools ) );
			}
		}

		return nodes
			.OrderBy( static node => node.NodeId )
			.ToArray();
	}

	private async Task<IReadOnlyList<ProcHugePageProcess>> ReadProcessesAsync(
		CancellationToken cancellationToken
	) {
		ProcProcessCollection collection = await this.processProvider.GetProcessesAsync(
			cancellationToken
		).ConfigureAwait( false );
		var result = new List<ProcHugePageProcess>();
		foreach ( ProcProcessSnapshot process in collection.Processes ) {
			cancellationToken.ThrowIfCancellationRequested();
			ProcObservedValue<ProcMemoryMapSet> maps = await this.memoryMapProvider.ObserveAsync(
				process,
				detailed: true,
				cancellationToken: cancellationToken
			).ConfigureAwait( false );
			if ( !maps.HasValue ) {
				continue;
			}

			ulong sharedKilobytes = 0UL;
			ulong privateKilobytes = 0UL;
			foreach ( ProcMemoryMapRegion region in maps.Value.Regions ) {
				sharedKilobytes = SaturatingAdd(
					sharedKilobytes,
					region.GetMetric( "Shared_Hugetlb" ) ?? 0UL
				);
				privateKilobytes = SaturatingAdd(
					privateKilobytes,
					region.GetMetric( "Private_Hugetlb" ) ?? 0UL
				);
			}
			if ( 0UL == sharedKilobytes && 0UL == privateKilobytes ) {
				continue;
			}

			string commandName = "?";
			if ( process.CommandName.HasValue
				&& !string.IsNullOrWhiteSpace( process.CommandName.Value ) ) {
				commandName = process.CommandName.Value;
			}
			result.Add(
				new ProcHugePageProcess(
					process.ProcessId,
					commandName,
					SaturatingMultiply( sharedKilobytes, Kibibyte ),
					SaturatingMultiply( privateKilobytes, Kibibyte )
				)
			);
		}

		return result
			.OrderByDescending(
				static process => SaturatingAdd(
					process.SharedBytes,
					process.PrivateBytes
				)
			)
			.ThenBy( static process => process.ProcessId )
			.ToArray();
	}

	private static bool TryParseNodeId( string? name, out int nodeId ) {
		nodeId = 0;
		if ( string.IsNullOrWhiteSpace( name )
			|| !name.StartsWith( "node", StringComparison.Ordinal ) ) {
			return false;
		}
		return int.TryParse(
			name[ 4.. ],
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out nodeId
		) && 0 <= nodeId;
	}

	private static bool TryParsePageSize(
		string? name,
		out ulong pageSizeBytes
	) {
		pageSizeBytes = 0UL;
		const string prefix = "hugepages-";
		const string suffix = "kB";
		if ( string.IsNullOrWhiteSpace( name )
			|| !name.StartsWith( prefix, StringComparison.Ordinal )
			|| !name.EndsWith( suffix, StringComparison.Ordinal ) ) {
			return false;
		}

		string sizeText = name[ prefix.Length..^suffix.Length ];
		if ( !ulong.TryParse(
			sizeText,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out ulong kibibytes
		) || 0UL == kibibytes ) {
			return false;
		}
		pageSizeBytes = checked( kibibytes * Kibibyte );
		return true;
	}

	private static async Task<ulong> ReadUnsignedAsync(
		string path,
		CancellationToken cancellationToken
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		string text = await File.ReadAllTextAsync(
			path,
			cancellationToken
		).ConfigureAwait( false );
		return ulong.Parse(
			text.Trim(),
			NumberStyles.None,
			CultureInfo.InvariantCulture
		);
	}

	private static ProcObservedValue<ProcHugePageSnapshot> Missing(
		ProcObservationAvailability availability,
		Exception exception
	) {
		ArgumentNullException.ThrowIfNull( exception );
		return ProcObservedValue<ProcHugePageSnapshot>.Missing(
			availability,
			exception.Message
		);
	}

	private static ulong SaturatingAdd( ulong left, ulong right ) {
		if ( ulong.MaxValue - left < right ) {
			return ulong.MaxValue;
		}
		return left + right;
	}

	private static ulong SaturatingMultiply( ulong value, ulong multiplier ) {
		if ( 0UL != multiplier && ulong.MaxValue / multiplier < value ) {
			return ulong.MaxValue;
		}
		return value * multiplier;
	}
}

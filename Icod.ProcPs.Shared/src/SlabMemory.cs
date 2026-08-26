namespace Icod.ProcPs.Shared;

using System.Globalization;

/// <summary>Describes one Linux slab-cache row with slab-instance counts required by slabtop.</summary>
public sealed class ProcSlabCacheEntry {
	/// <summary>Gets the slab cache name.</summary>
	public string Name { get; }

	/// <summary>Gets active object count.</summary>
	public ulong ActiveObjects { get; }

	/// <summary>Gets total object count.</summary>
	public ulong TotalObjects { get; }

	/// <summary>Gets object size in bytes.</summary>
	public ulong ObjectSizeBytes { get; }

	/// <summary>Gets objects per slab.</summary>
	public ulong ObjectsPerSlab { get; }

	/// <summary>Gets pages per slab.</summary>
	public ulong PagesPerSlab { get; }

	/// <summary>Gets active slab count.</summary>
	public ulong ActiveSlabs { get; }

	/// <summary>Gets total slab count.</summary>
	public ulong TotalSlabs { get; }

	/// <summary>Initializes a complete slab-cache observation.</summary>
	public ProcSlabCacheEntry(
		string name,
		ulong activeObjects,
		ulong totalObjects,
		ulong objectSizeBytes,
		ulong objectsPerSlab,
		ulong pagesPerSlab,
		ulong activeSlabs,
		ulong totalSlabs
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( activeObjects > totalObjects ) {
			throw new ArgumentOutOfRangeException( nameof( activeObjects ) );
		}
		if ( activeSlabs > totalSlabs ) {
			throw new ArgumentOutOfRangeException( nameof( activeSlabs ) );
		}
		if ( 0UL == objectSizeBytes ) {
			throw new ArgumentOutOfRangeException( nameof( objectSizeBytes ) );
		}
		if ( 0UL == objectsPerSlab ) {
			throw new ArgumentOutOfRangeException( nameof( objectsPerSlab ) );
		}
		if ( 0UL == pagesPerSlab ) {
			throw new ArgumentOutOfRangeException( nameof( pagesPerSlab ) );
		}

		this.Name = name;
		this.ActiveObjects = activeObjects;
		this.TotalObjects = totalObjects;
		this.ObjectSizeBytes = objectSizeBytes;
		this.ObjectsPerSlab = objectsPerSlab;
		this.PagesPerSlab = pagesPerSlab;
		this.ActiveSlabs = activeSlabs;
		this.TotalSlabs = totalSlabs;
	}
}

/// <summary>Observes the exact Linux slab allocator cache table required by slabtop.</summary>
public interface IProcSlabProvider {
	/// <summary>Captures the current slab-cache table.</summary>
	Task<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>> GetSlabsAsync(
		CancellationToken cancellationToken = default
	);
}

/// <summary>Selects the Linux slabinfo provider or a controlled unsupported provider.</summary>
public sealed class SystemProcSlabProvider : IProcSlabProvider {
	private readonly IProcSlabProvider inner;

	/// <summary>Gets the shared system slab provider.</summary>
	public static SystemProcSlabProvider Instance { get; } = new();

	/// <summary>Initializes the system slab provider.</summary>
	public SystemProcSlabProvider() {
		if ( OperatingSystem.IsLinux() ) {
			this.inner = new LinuxProcSlabProvider();
		} else {
			this.inner = UnsupportedProcSlabProvider.Instance;
		}
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>> GetSlabsAsync(
		CancellationToken cancellationToken = default
	) {
		return this.inner.GetSlabsAsync( cancellationToken );
	}

	private sealed class UnsupportedProcSlabProvider : IProcSlabProvider {
		internal static UnsupportedProcSlabProvider Instance { get; } = new();

		public Task<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>> GetSlabsAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(
				ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
					ProcObservationAvailability.Unsupported,
					"slabtop requires the Linux /proc/slabinfo allocator interface."
				)
			);
		}
	}
}

/// <summary>Reads exact slab-cache accounting from Linux procfs.</summary>
public sealed class LinuxProcSlabProvider : IProcSlabProvider {
	private readonly string procRoot;

	/// <summary>Initializes a Linux slab provider.</summary>
	public LinuxProcSlabProvider( string procRoot = "/proc" ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( procRoot );
		this.procRoot = procRoot;
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>> GetSlabsAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		try {
			string text = await File.ReadAllTextAsync(
				System.IO.Path.Combine( this.procRoot, "slabinfo" ),
				cancellationToken
			).ConfigureAwait( false );
			IReadOnlyList<ProcSlabCacheEntry> entries =
				ProcKernelMemoryParsers.ParseSlabInfo( text );
			return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Available(
				entries,
				ProcObservationSource.LinuxProcfs,
				ObservationFidelity.Exact
			);
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.AccessDenied,
				exception.Message
			);
		} catch ( DirectoryNotFoundException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.Unavailable,
				exception.Message
			);
		} catch ( FileNotFoundException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.Unavailable,
				exception.Message
			);
		} catch ( IOException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.Unavailable,
				exception.Message
			);
		} catch ( FormatException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.Malformed,
				exception.Message
			);
		}
	}
}

/// <summary>Parses Linux kernel-memory text formats used by ProcPs full-screen tools.</summary>
public static class ProcKernelMemoryParsers {
	/// <summary>Parses Linux <c>/proc/slabinfo</c>, including exact slabdata counts.</summary>
	public static IReadOnlyList<ProcSlabCacheEntry> ParseSlabInfo( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		List<ProcSlabCacheEntry> entries = [];

		foreach ( string line in text.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) ) {
			if ( line.StartsWith( "slabinfo", StringComparison.Ordinal )
				|| line.StartsWith( "#", StringComparison.Ordinal ) ) {
				continue;
			}

			string[] fields = line.Split(
				(char[]?)null,
				StringSplitOptions.RemoveEmptyEntries
			);
			if ( 6 > fields.Length ) {
				throw new FormatException(
					$"The slabinfo row '{line}' does not contain the required core fields."
				);
			}
			if ( !TryReadCoreFields(
				fields,
				out ulong activeObjects,
				out ulong totalObjects,
				out ulong objectSize,
				out ulong objectsPerSlab,
				out ulong pagesPerSlab
			) ) {
				throw new FormatException(
					$"The slabinfo row for '{fields[ 0 ]}' contains invalid numeric fields."
				);
			}
			if ( activeObjects > totalObjects ) {
				throw new FormatException(
					$"The slabinfo row for '{fields[ 0 ]}' reports more active objects than total objects."
				);
			}
			if ( 0UL == objectSize
				|| 0UL == objectsPerSlab
				|| 0UL == pagesPerSlab ) {
				throw new FormatException(
					$"The slabinfo row for '{fields[ 0 ]}' reports a zero size or slab geometry."
				);
			}

			int slabDataIndex = Array.FindIndex(
				fields,
				static field => string.Equals( field, "slabdata", StringComparison.Ordinal )
			);
			if ( 0 > slabDataIndex
				|| slabDataIndex + 2 >= fields.Length
				|| !ulong.TryParse(
					fields[ slabDataIndex + 1 ],
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out ulong activeSlabs
				)
				|| !ulong.TryParse(
					fields[ slabDataIndex + 2 ],
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out ulong totalSlabs
				) ) {
				throw new FormatException(
					$"The slabinfo row for '{fields[ 0 ]}' does not contain valid slabdata counts."
				);
			}
			if ( activeSlabs > totalSlabs ) {
				throw new FormatException(
					$"The slabinfo row for '{fields[ 0 ]}' reports more active slabs than total slabs."
				);
			}

			entries.Add(
				new ProcSlabCacheEntry(
					fields[ 0 ],
					activeObjects,
					totalObjects,
					objectSize,
					objectsPerSlab,
					pagesPerSlab,
					activeSlabs,
					totalSlabs
				)
			);
		}

		return entries;
	}

	private static bool TryReadCoreFields(
		string[] fields,
		out ulong activeObjects,
		out ulong totalObjects,
		out ulong objectSize,
		out ulong objectsPerSlab,
		out ulong pagesPerSlab
	) {
		ArgumentNullException.ThrowIfNull( fields );
		activeObjects = 0UL;
		totalObjects = 0UL;
		objectSize = 0UL;
		objectsPerSlab = 0UL;
		pagesPerSlab = 0UL;
		return ulong.TryParse(
			fields[ 1 ], NumberStyles.None, CultureInfo.InvariantCulture, out activeObjects
		) && ulong.TryParse(
			fields[ 2 ], NumberStyles.None, CultureInfo.InvariantCulture, out totalObjects
		) && ulong.TryParse(
			fields[ 3 ], NumberStyles.None, CultureInfo.InvariantCulture, out objectSize
		) && ulong.TryParse(
			fields[ 4 ], NumberStyles.None, CultureInfo.InvariantCulture, out objectsPerSlab
		) && ulong.TryParse(
			fields[ 5 ], NumberStyles.None, CultureInfo.InvariantCulture, out pagesPerSlab
		);
	}
}

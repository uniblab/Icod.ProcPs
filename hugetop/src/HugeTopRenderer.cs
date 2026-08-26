namespace Icod.ProcPs.HugeTop;

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Contains one bounded hugetop screen frame.</summary>
internal sealed class HugeTopRenderFrame {
	internal HugeTopRenderFrame(
		IReadOnlyList<string> lines,
		int columns,
		int rows
	) {
		ArgumentNullException.ThrowIfNull( lines );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( columns );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( rows );
		this.Lines = lines;
		this.Columns = columns;
		this.Rows = rows;
	}

	internal IReadOnlyList<string> Lines { get; }
	internal int Columns { get; }
	internal int Rows { get; }
}

/// <summary>Renders procps-ng-style hugetop reports independently from terminal I/O.</summary>
internal static class HugeTopRenderer {
	private const ulong Kibibyte = 1024UL;

	/// <summary>Renders an unbounded one-shot report.</summary>
	internal static string Render(
		ProcHugePageSnapshot snapshot,
		bool numa,
		bool human,
		DateTimeOffset now
	) {
		ArgumentNullException.ThrowIfNull( snapshot );
		IReadOnlyList<string> lines = BuildLines( snapshot, numa, human, now );
		return string.Concat(
			string.Join( Environment.NewLine, lines ),
			Environment.NewLine
		);
	}

	/// <summary>Renders a bounded full-screen frame.</summary>
	internal static HugeTopRenderFrame RenderFrame(
		ProcHugePageSnapshot snapshot,
		bool numa,
		bool human,
		DateTimeOffset now,
		HugeTopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( 1 > dimensions.Columns || 1 > dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		IReadOnlyList<string> source = BuildLines( snapshot, numa, human, now );
		var lines = new string[ dimensions.Rows ];
		for ( int index = 0; index < dimensions.Rows; index++ ) {
			string line = (index < source.Count) ? source[ index ] : string.Empty;
			lines[ index ] = LimitRunes( line, dimensions.Columns );
		}
		return new HugeTopRenderFrame(
			lines,
			dimensions.Columns,
			dimensions.Rows
		);
	}

	private static IReadOnlyList<string> BuildLines(
		ProcHugePageSnapshot snapshot,
		bool numa,
		bool human,
		DateTimeOffset now
	) {
		var lines = new List<string> {
			$"hugetop - {now.ToLocalTime():HH:mm:ss}"
		};
		if ( numa ) {
			foreach ( ProcHugePageNode node in snapshot.Nodes ) {
				lines.Add( FormatNode( $"node{node.NodeId}", node.Pools ) );
			}
		} else {
			lines.Add( FormatNode( "node(s)", AggregatePools( snapshot.Nodes ) ) );
		}
		lines.Add( string.Empty );
		lines.Add( "     PID     SHARED    PRIVATE COMMAND" );
		foreach ( ProcHugePageProcess process in snapshot.Processes ) {
			string shared = FormatMemory( process.SharedBytes, human );
			string privateBytes = FormatMemory( process.PrivateBytes, human );
			lines.Add(
				string.Format(
					CultureInfo.InvariantCulture,
					"{0,8} {1,10} {2,10} {3}",
					process.ProcessId,
					shared,
					privateBytes,
					process.CommandName
				)
			);
		}
		return lines;
	}

	private static IReadOnlyList<ProcHugePagePool> AggregatePools(
		IReadOnlyList<ProcHugePageNode> nodes
	) {
		ArgumentNullException.ThrowIfNull( nodes );
		var totals = new SortedDictionary<ulong, (ulong Total, ulong Free)>();
		foreach ( ProcHugePageNode node in nodes ) {
			foreach ( ProcHugePagePool pool in node.Pools ) {
				if ( !totals.TryGetValue(
					pool.PageSizeBytes,
					out (ulong Total, ulong Free) current
				) ) {
					current = ( 0UL, 0UL );
				}
				totals[ pool.PageSizeBytes ] = (
					SaturatingAdd( current.Total, pool.TotalPages ),
					SaturatingAdd( current.Free, pool.FreePages )
				);
			}
		}
		return totals
			.Select(
				static pair => new ProcHugePagePool(
					pair.Key,
					pair.Value.Total,
					Math.Min( pair.Value.Total, pair.Value.Free )
				)
			)
			.ToArray();
	}

	private static string FormatNode(
		string label,
		IReadOnlyList<ProcHugePagePool> pools
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( label );
		ArgumentNullException.ThrowIfNull( pools );
		IEnumerable<string> values = pools.Select(
			pool => string.Format(
				CultureInfo.InvariantCulture,
				"{0} - {1}/{2}",
				FormatMemory( pool.PageSizeBytes, human: true ),
				pool.FreePages,
				pool.TotalPages
			)
		);
		return $"{label}: {string.Join( ", ", values )}";
	}

	private static string FormatMemory( ulong bytes, bool human ) {
		if ( !human ) {
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0}K",
				bytes / Kibibyte
			);
		}

		string[] suffixes = [ "B", "Ki", "Mi", "Gi", "Ti", "Pi", "Ei" ];
		double value = bytes;
		int suffixIndex = 0;
		while ( 1024d <= value && suffixIndex + 1 < suffixes.Length ) {
			value /= 1024d;
			suffixIndex++;
		}
		string number = (10d <= value || 0 == suffixIndex)
			? value.ToString( "0", CultureInfo.InvariantCulture )
			: value.ToString( "0.0", CultureInfo.InvariantCulture )
		;
		return number + suffixes[ suffixIndex ];
	}

	private static string LimitRunes( string text, int maximumRunes ) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegative( maximumRunes );
		if ( 0 == maximumRunes || 0 == text.Length ) {
			return string.Empty;
		}
		var builder = new StringBuilder();
		int count = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			if ( count >= maximumRunes ) {
				break;
			}
			builder.Append( rune );
			count++;
		}
		return builder.ToString();
	}

	private static ulong SaturatingAdd( ulong left, ulong right ) {
		if ( ulong.MaxValue - left < right ) {
			return ulong.MaxValue;
		}
		return left + right;
	}
}

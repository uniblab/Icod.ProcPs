// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Free;

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements procps-ng 4.0.6 <c>free</c>.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private const string VersionText = "free from procps-ng 4.0.6";
	private const string HelpText = """

Usage:
 free [options]

Options:
 -b, --bytes         show output in bytes
     --kilo          show output in kilobytes
     --mega          show output in megabytes
     --giga          show output in gigabytes
     --tera          show output in terabytes
     --peta          show output in petabytes
 -k, --kibi          show output in kibibytes
 -m, --mebi          show output in mebibytes
 -g, --gibi          show output in gibibytes
     --tebi          show output in tebibytes
     --pebi          show output in pebibytes
 -h, --human         show human-readable output
     --si            use powers of 1000 not 1024
 -l, --lohi          show detailed low and high memory statistics
 -L, --line          show output on a single line
 -t, --total         show total for RAM + swap
 -v, --committed     show committed memory and commit limit
 -s N, --seconds N   repeat printing every N seconds
 -c N, --count N     repeat printing N times, then exit
 -w, --wide          wide output

     --help     display this help and exit
 -V, --version  output version information and exit

For more details see free(1).
""" + "\n";
	/// <summary>Runs <c>free</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, output, error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}
	/// <summary>Runs procps-ng <c>free</c> asynchronously.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcSystemMetricsProvider? metricsProvider = null,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var parsed = ParseArguments( args );
		if ( null != parsed.Error ) {
			if ( 0 < parsed.Error.Length ) await WriteDiagnosticAsync( stderr, parsed.Error, cancellationToken ).ConfigureAwait( false );
			if ( parsed.ShowUsageOnError ) await WriteErrorAsync( stderr, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false );
			return 1;
		}
		if ( parsed.ShowHelp ) { await WriteAsync( stdout, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false ); return 0; }
		if ( parsed.ShowVersion ) { await WriteLineAsync( stdout, VersionText, cancellationToken ).ConfigureAwait( false ); return 0; }
		var metrics = metricsProvider ?? SystemProcSystemMetricsProvider.Instance;
		Func<TimeSpan, CancellationToken, Task> delay = delayAsync ?? new Func<TimeSpan, CancellationToken, Task>( DefaultDelayAsync );
		var remaining = parsed.RepeatCount;
		try {
			while ( true ) {
				var memory = await metrics.GetMemoryAsync( cancellationToken ).ConfigureAwait( false );
				if ( !memory.HasValue ) return await ReportMemoryFailureAsync( stderr, memory, cancellationToken ).ConfigureAwait( false );
				await WriteAsync( stdout, Render( memory.Value, parsed ), cancellationToken ).ConfigureAwait( false );
				if ( remaining.HasValue ) {
					remaining = remaining.Value - 1;
					if ( 1 > remaining.Value ) break;
				} else if ( !parsed.Repeat ) break;
				if ( !parsed.Line ) await WriteLineAsync( stdout, string.Empty, cancellationToken ).ConfigureAwait( false );
				await delay( parsed.RepeatInterval, cancellationToken ).ConfigureAwait( false );
			}
			return 0;
		} catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested ) { return 130; }
	}
	private static Task DefaultDelayAsync( TimeSpan duration, CancellationToken cancellationToken ) => Task.Delay( duration, cancellationToken );
	private static async Task<int> ReportMemoryFailureAsync( Stream? stderr, ProcObservedValue<ProcMemoryInfo> observation, CancellationToken cancellationToken ) {
		string text;
		if ( ProcObservationAvailability.Unsupported == observation.Availability ) text = "free: memory information with procps-ng semantics is not available on this platform";
		else if ( ProcObservationAvailability.Unavailable == observation.Availability && ( observation.Diagnostic?.Contains( "meminfo", StringComparison.OrdinalIgnoreCase ) ?? false ) ) text = "free: Memory information file /proc/meminfo does not exist";
		else text = "free: Unable to create meminfo structure";
		if ( ProcObservationAvailability.Unsupported == observation.Availability && !string.IsNullOrWhiteSpace( observation.Diagnostic ) ) text = string.Concat( text, ": ", observation.Diagnostic );
		await WriteDiagnosticAsync( stderr, text, cancellationToken ).ConfigureAwait( false );
		return 1;
	}
	/// <summary>Renders one procps-ng <c>free</c> sample.</summary>
	public static string Render( ProcMemoryInfo memory, bool wide = false, bool lohi = false, bool total = false, bool committed = false, bool line = false, int exponent = 0, bool si = false, bool human = false ) => Render( memory, new FreeArguments( exponent, si, human, lohi, line, total, committed, wide, false, null, TimeSpan.FromSeconds( 1 ), false, false, null, false ) );
	private static string Render( ProcMemoryInfo memory, FreeArguments options ) {
		var values = Derive( memory );
		string F( ulong value ) => FormatSize( value, options.Exponent, options.Si, options.Human );
		var builder = new StringBuilder();
		if ( options.Line ) {
			builder.Append( "SwapUse " ).Append( F( values.SwapUsed ).PadLeft( 11 ) ).Append( ' ' );
			builder.Append( "CachUse " ).Append( F( SaturatingAdd( values.Buffers, values.Cache ) ).PadLeft( 11 ) ).Append( ' ' );
			builder.Append( " MemUse " ).Append( F( values.Used ).PadLeft( 11 ) ).Append( ' ' );
			builder.Append( "MemFree " ).Append( F( values.Free ).PadLeft( 11 ) ).Append( ' ' ).Append( Environment.NewLine );
			return builder.ToString();
		}
		builder.Append( options.Wide
			? "               total        used        free      shared     buffers       cache   available"
			: "               total        used        free      shared  buff/cache   available" ).Append( Environment.NewLine );
		if ( options.Wide ) AppendRow( builder, "Mem:", F( values.Total ), F( values.Used ), F( values.Free ), F( values.Shared ), F( values.Buffers ), F( values.Cache ), F( values.Available ) );
		else AppendRow( builder, "Mem:", F( values.Total ), F( values.Used ), F( values.Free ), F( values.Shared ), F( SaturatingAdd( values.Buffers, values.Cache ) ), F( values.Available ) );
		if ( options.LoHi ) {
			AppendRow( builder, "Low:", F( values.LowTotal ), F( values.LowUsed ), F( values.LowFree ) );
			AppendRow( builder, "High:", F( values.HighTotal ), F( values.HighUsed ), F( values.HighFree ) );
		}
		AppendRow( builder, "Swap:", F( values.SwapTotal ), F( values.SwapUsed ), F( values.SwapFree ) );
		if ( options.Total ) AppendRow( builder, "Total:", F( SaturatingAdd( values.Total, values.SwapTotal ) ), F( SaturatingAdd( values.Used, values.SwapUsed ) ), F( SaturatingAdd( values.Free, values.SwapFree ) ) );
		if ( options.Committed ) AppendRow( builder, "Comm:", F( values.CommitLimit ), F( values.Committed ), F( unchecked( values.CommitLimit - values.Committed ) ) );
		return builder.ToString();
	}
	private static void AppendRow( StringBuilder builder, string label, params string[] fields ) {
		builder.Append( label.PadRight( 9 ) );
		for ( var index = 0; index < fields.Length; index++ ) { if ( 0 < index ) builder.Append( ' ' ); builder.Append( fields[ index ].PadLeft( 11 ) ); }
		builder.Append( Environment.NewLine );
	}
	/// <summary>Scales a byte count using procps-ng <c>free</c> integer and human-readable rules.</summary>
	public static string FormatSize( ulong bytes, int exponent = 0, bool si = false, bool human = false ) {
		var numberBase = si ? 1000d : 1024d;
		if ( !human ) {
			if ( 1 == exponent ) return bytes.ToString( CultureInfo.InvariantCulture );
			if ( 0 == exponent ) return ( bytes / ( si ? 1000UL : 1024UL ) ).ToString( CultureInfo.InvariantCulture );
			var divisor = Math.Pow( numberBase, exponent - 1 );
			return Math.Truncate( bytes / divisor ).ToString( "0", CultureInfo.InvariantCulture );
		}
		const string units = "BKMGTP";
		var raw = string.Concat( bytes.ToString( CultureInfo.InvariantCulture ), "B" );
		if ( 4 >= raw.Length ) return raw;
		var last = raw;
		for ( var index = 1; index < units.Length; index++ ) {
			var scaled = (float)( bytes / Math.Pow( numberBase, index ) );
			if ( si ) {
				var decimalValue = string.Concat( scaled.ToString( "F1", CultureInfo.InvariantCulture ), units[ index ] );
				if ( 4 >= decimalValue.Length ) return decimalValue;
				last = string.Concat( Math.Truncate( scaled ).ToString( "0", CultureInfo.InvariantCulture ), units[ index ] );
				if ( 4 >= last.Length ) return last;
			} else {
				var decimalValue = string.Concat( scaled.ToString( "F1", CultureInfo.InvariantCulture ), units[ index ], "i" );
				if ( 5 >= decimalValue.Length ) return decimalValue;
				last = string.Concat( Math.Truncate( scaled ).ToString( "0", CultureInfo.InvariantCulture ), units[ index ], "i" );
				if ( 5 >= last.Length ) return last;
			}
		}
		return last;
	}
	private static MemoryValues Derive( ProcMemoryInfo memory ) {
		ulong Read( string key ) => memory.Fields.TryGetValue( key, out var value ) ? value : 0UL;
		var total = memory.TotalBytes ?? Read( "MemTotal" );
		var free = memory.FreeBytes ?? memory.AvailableBytes ?? Read( "MemFree" );
		var available = memory.AvailableBytes ?? Read( "MemAvailable" );
		if ( 0 == available || available > total ) available = free;
		var used = total >= available ? total - available : total >= free ? total - free : 0UL;
		var buffers = memory.BuffersBytes ?? Read( "Buffers" );
		var cache = memory.CacheBytes ?? SaturatingAdd( Read( "Cached" ), Read( "SReclaimable" ) );
		var swapTotal = memory.SwapTotalBytes ?? Read( "SwapTotal" );
		var swapFree = memory.SwapFreeBytes ?? Read( "SwapFree" );
		var swapUsed = swapTotal >= swapFree ? swapTotal - swapFree : 0UL;
		var lowTotal = memory.LowTotalBytes ?? Read( "LowTotal" );
		var lowFree = memory.LowFreeBytes ?? Read( "LowFree" );
		if ( 0 == lowTotal ) { lowTotal = total; lowFree = free; }
		var highTotal = memory.HighTotalBytes ?? Read( "HighTotal" );
		var highFree = memory.HighFreeBytes ?? Read( "HighFree" );
		return new MemoryValues(
			total, used, free, memory.SharedBytes ?? Read( "Shmem" ), buffers, cache, available,
			lowTotal, lowTotal >= lowFree ? lowTotal - lowFree : 0UL, lowFree,
			highTotal, highTotal >= highFree ? highTotal - highFree : 0UL, highFree,
			swapTotal, swapUsed, swapFree, memory.CommitLimitBytes ?? Read( "CommitLimit" ), memory.CommittedBytes ?? Read( "Committed_AS" )
		);
	}
	private static ulong SaturatingAdd( ulong left, ulong right ) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
	private static FreeArguments ParseArguments( string[] args ) {
		var exponent = 0; var si = false; var human = false; var lohi = false; var line = false; var total = false; var committed = false; var wide = false; var repeat = false; int? count = null; var interval = TimeSpan.FromSeconds( 1 ); var unitSet = false; var operandSeen = false; var endOfOptions = false;
		for ( var index = 0; index < args.Length; index++ ) {
			var token = args[ index ];
			if ( endOfOptions ) { operandSeen = true; continue; }
			if ( "--" == token ) { endOfOptions = true; continue; }
			if ( !token.StartsWith( '-' ) || "-" == token ) { operandSeen = true; continue; }
			if ( token.StartsWith( "--", StringComparison.Ordinal ) ) {
				var equal = token.IndexOf( '=' ); var name = 0 > equal ? token[ 2.. ] : token[ 2..equal ]; var value = 0 > equal ? null : token[ ( equal + 1 ).. ];
				var resolution = ResolveLongOption( name, token );
				if ( null != resolution.Error ) return Fail( resolution.Error, true );
				var option = resolution.Option!;
				if ( option is "seconds" or "count" ) {
					if ( null == value ) { if ( index + 1 >= args.Length ) return Fail( $"free: option '--{option}' requires an argument", true ); value = args[ ++index ]; }
					var failure = ApplyValueOption( option, value ); if ( null != failure ) return failure; continue;
				}
				if ( null != value ) return Fail( $"free: option '--{option}' doesn't allow an argument", true );
				var immediate = ApplyFlagOption( option ); if ( null != immediate ) return immediate; continue;
			}
			for ( var position = 1; position < token.Length; position++ ) {
				var option = token[ position ];
				if ( option is 's' or 'c' ) {
					string value; if ( position + 1 < token.Length ) value = token[ ( position + 1 ).. ]; else { if ( index + 1 >= args.Length ) return Fail( $"free: option requires an argument -- '{option}'", true ); value = args[ ++index ]; }
					var failure = ApplyValueOption( 's' == option ? "seconds" : "count", value ); if ( null != failure ) return failure; break;
				}
				var name = option switch { 'b' => "bytes", 'k' => "kibi", 'm' => "mebi", 'g' => "gibi", 'h' => "human", 'l' => "lohi", 'L' => "line", 't' => "total", 'v' => "committed", 'w' => "wide", 'V' => "version", _ => null };
				if ( null == name ) return Fail( $"free: invalid option -- '{option}'", true );
				var immediate = ApplyFlagOption( name ); if ( null != immediate ) return immediate;
			}
		}
		return operandSeen ? Fail( string.Empty, true ) : Success();
		FreeArguments? ApplyValueOption( string option, string value ) {
			if ( "seconds" == option ) {
				if ( !TryParseSeconds( value, out var seconds ) ) return Fail( $"free: seconds argument failed: '{value}'", false );
				if ( 0d >= seconds ) return Fail( $"free: seconds argument `{value}' is not positive number", false );
				var microseconds = (float)( seconds * 1_000_000d );
				if ( 1f > microseconds || !float.IsFinite( microseconds ) || microseconds > long.MaxValue / 10f ) return Fail( $"free: seconds argument `{value}' is not positive number", false );
				interval = TimeSpan.FromTicks( checked( (long)microseconds * 10L ) ); repeat = true; return null;
			}
			if ( !int.TryParse( value, NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedCount ) || 1 > parsedCount ) return Fail( $"free: failed to parse count argument: '{value}'", false );
			count = parsedCount; repeat = true; return null;
		}
		FreeArguments? ApplyFlagOption( string option ) {
			if ( option is "bytes" or "kibi" or "mebi" or "gibi" or "tebi" or "pebi" or "kilo" or "mega" or "giga" or "tera" or "peta" ) {
				if ( unitSet ) return Fail( "free: Multiple unit options don't make sense.", false ); unitSet = true;
				exponent = option switch { "bytes" => 1, "kibi" or "kilo" => 2, "mebi" or "mega" => 3, "gibi" or "giga" => 4, "tebi" or "tera" => 5, _ => 6 };
				if ( option is "kilo" or "mega" or "giga" or "tera" or "peta" ) si = true; return null;
			}
			switch ( option ) { case "human": human = true; break; case "si": si = true; break; case "lohi": lohi = true; break; case "line": line = true; break; case "total": total = true; break; case "committed": committed = true; break; case "wide": wide = true; break; case "help": return new( exponent, si, human, lohi, line, total, committed, wide, repeat, count, interval, true, false, null, false ); case "version": return new( exponent, si, human, lohi, line, total, committed, wide, repeat, count, interval, false, true, null, false ); }
			return null;
		}
		FreeArguments Success() => new( exponent, si, human, lohi, line, total, committed, wide, repeat, count, interval, false, false, null, false );
		FreeArguments Fail( string error, bool usage ) => new( exponent, si, human, lohi, line, total, committed, wide, repeat, count, interval, false, false, error, usage );
	}
	private static LongOptionResolution ResolveLongOption( string name, string token ) {
		string[] options = [ "bytes", "kilo", "mega", "giga", "tera", "peta", "kibi", "mebi", "gibi", "tebi", "pebi", "human", "si", "lohi", "line", "total", "committed", "seconds", "count", "wide", "help", "version" ];
		var exact = options.FirstOrDefault( option => string.Equals( option, name, StringComparison.Ordinal ) );
		if ( null != exact ) return new( exact, null );
		var matches = options.Where( option => option.StartsWith( name, StringComparison.Ordinal ) ).ToArray();
		if ( 1 == matches.Length ) return new( matches[ 0 ], null );
		if ( 1 < matches.Length ) return new( null, $"free: option '{token}' is ambiguous; possibilities: {string.Join( " ", matches.Select( option => $"'--{option}'" ) )}" );
		return new( null, $"free: unrecognized option '{token}'" );
	}
	private static bool TryParseSeconds( string text, out double value ) {
		value = 0d;
		if ( string.IsNullOrEmpty( text ) ) return false;
		var index = 0;
		while ( index < text.Length && char.IsWhiteSpace( text[ index ] ) ) index++;
		var negative = false;
		if ( index < text.Length && ( '-' == text[ index ] || '+' == text[ index ] ) ) { negative = '-' == text[ index ]; index++; }
		if ( index == text.Length ) { value = negative ? -0d : 0d; return true; }
		var integerStart = index;
		while ( index < text.Length && char.IsAsciiDigit( text[ index ] ) ) index++;
		var integerEnd = index;
		if ( integerEnd > integerStart && !double.TryParse( text[ integerStart..integerEnd ], NumberStyles.None, CultureInfo.InvariantCulture, out value ) ) return false;
		if ( index == text.Length ) { if ( integerEnd == integerStart ) return false; value = negative ? -value : value; return true; }
		if ( '.' != text[ index ] && ',' != text[ index ] ) return false;
		index++;
		var fraction = 0d; var multiplier = 0.1d;
		while ( index < text.Length && char.IsAsciiDigit( text[ index ] ) ) { fraction += ( text[ index ] - '0' ) * multiplier; multiplier /= 10d; index++; }
		if ( index != text.Length ) return false;
		if ( integerEnd == integerStart && 0d == fraction ) { value = 0d; return true; }
		value += fraction; value = negative ? -value : value; return true;
	}
	private static async Task WriteAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Out.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( text ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static async Task WriteErrorAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Error.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( text ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static Task WriteLineAsync( Stream? stream, string text, CancellationToken cancellationToken ) => WriteAsync( stream, string.Concat( text, Environment.NewLine ), cancellationToken );
	private static async Task WriteDiagnosticAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Error.WriteLineAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( string.Concat( text, Environment.NewLine ) ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static string NormalizeLineEndings( string value ) {
		var normalized = value
			.Replace( "\r\n", "\n", StringComparison.Ordinal )
			.Replace( "\r", "\n", StringComparison.Ordinal );
		return "\n" == Environment.NewLine
			? normalized
			: normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}
	private sealed record FreeArguments( int Exponent, bool Si, bool Human, bool LoHi, bool Line, bool Total, bool Committed, bool Wide, bool Repeat, int? RepeatCount, TimeSpan RepeatInterval, bool ShowHelp, bool ShowVersion, string? Error, bool ShowUsageOnError );
	private sealed record LongOptionResolution( string? Option, string? Error );
	private sealed record MemoryValues( ulong Total, ulong Used, ulong Free, ulong Shared, ulong Buffers, ulong Cache, ulong Available, ulong LowTotal, ulong LowUsed, ulong LowFree, ulong HighTotal, ulong HighUsed, ulong HighFree, ulong SwapTotal, ulong SwapUsed, ulong SwapFree, ulong CommitLimit, ulong Committed );
}

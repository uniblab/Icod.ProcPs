// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Vmstat;

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements procps-ng 4.0.6 <c>vmstat</c>.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private const string VersionText = "vmstat from procps-ng 4.0.6";
	private const string HelpText = """

Usage:
 vmstat [options] [delay [count]]

Options:
 -a, --active           active/inactive memory
 -f, --forks            number of forks since boot
 -m, --slabs            slabinfo
 -n, --one-header       do not redisplay header
 -s, --stats            event counter statistics
 -d, --disk             disk statistics
 -D, --disk-sum         summarize disk statistics
 -p, --partition <dev>  partition specific statistics
 -S, --unit <char>      define display unit
 -w, --wide             wide output
 -t, --timestamp        show timestamp
 -y, --no-first         skips first line of output
 -h, --help             display this help and exit
 -V, --version          output version information and exit

For more details see vmstat(8).
""" + "\n";

	/// <summary>Runs <c>vmstat</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, output, error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}

	/// <summary>Runs procps-ng <c>vmstat</c> asynchronously with injectable observations and timing.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcVmstatProvider? provider = null,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		Func<DateTimeOffset>? nowProvider = null,
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

		var metrics = provider ?? SystemProcVmstatProvider.Instance;
		var delay = delayAsync ?? DefaultDelayAsync;
		var now = nowProvider ?? GetLocalNow;
		try {
			if ( parsed.ImmediateForks ) return await RenderForksAsync( metrics, stdout, stderr, cancellationToken ).ConfigureAwait( false );
			return parsed.Mode switch {
				VmstatMode.Statistics => await RenderStatisticsAsync( metrics, parsed, stdout, stderr, cancellationToken ).ConfigureAwait( false ),
				VmstatMode.DiskSummary => await RenderDiskSummaryAsync( metrics, stdout, stderr, cancellationToken ).ConfigureAwait( false ),
				VmstatMode.Partition => await RenderPartitionLoopAsync( metrics, parsed, stdout, stderr, delay, now, cancellationToken ).ConfigureAwait( false ),
				VmstatMode.Disk => await RenderDiskLoopAsync( metrics, parsed, stdout, stderr, delay, now, cancellationToken ).ConfigureAwait( false ),
				VmstatMode.Slab => await RenderSlabLoopAsync( metrics, parsed, stdout, stderr, delay, now, cancellationToken ).ConfigureAwait( false ),
				_ => await RenderDefaultLoopAsync( metrics, parsed, stdout, stderr, delay, now, cancellationToken ).ConfigureAwait( false )
			};
		} catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested ) { return 130; }
	}

	private static async Task<int> RenderDefaultLoopAsync( IProcVmstatProvider provider, VmstatArguments options, Stream? stdout, Stream? stderr, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, CancellationToken cancellationToken ) {
		var first = await provider.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		if ( !CanRenderDefault( first ) ) {
			await WriteDiagnosticAsync( stderr, "vmstat: memory and CPU statistics are not available on this platform", cancellationToken ).ConfigureAwait( false );
			return 1;
		}
		if ( IsPartialDefault( first ) ) await WriteDiagnosticAsync( stderr, "vmstat: some fields are unavailable on this platform; unavailable fields are shown as '-'", cancellationToken ).ConfigureAwait( false );

		var interval = options.Delay ?? ( options.NoFirst ? TimeSpan.FromSeconds( 1 ) : TimeSpan.Zero );
		var requestedRows = options.Count;
		var infinite = options.Delay.HasValue && !requestedRows.HasValue;
		var rowsRemaining = requestedRows ?? ( options.NoFirst ? 1L : 1L );
		var headerPrinted = false;
		var rowsSinceHeader = 0;
		long linuxIdleDebt = 0;
		ProcVmstatSnapshot? previous = null;
		var current = first;

		if ( !options.NoFirst ) {
			if ( !headerPrinted || ( !options.OneHeader && 21 <= rowsSinceHeader ) ) { await WriteAsync( stdout, RenderDefaultHeader( options ), cancellationToken ).ConfigureAwait( false ); headerPrinted = true; rowsSinceHeader = 0; }
			await WriteAsync( stdout, RenderDefaultRow( null, current, null, options, now(), ref linuxIdleDebt ), cancellationToken ).ConfigureAwait( false );
			rowsSinceHeader++;
			rowsRemaining--;
			previous = current;
			if ( !infinite && 0 >= rowsRemaining ) return 0;
		} else {
			await WriteAsync( stdout, RenderDefaultHeader( options ), cancellationToken ).ConfigureAwait( false );
			headerPrinted = true;
			previous = current;
		}

		while ( infinite || 0 < rowsRemaining ) {
			await delay( interval, cancellationToken ).ConfigureAwait( false );
			current = await provider.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
			if ( !headerPrinted || ( !options.OneHeader && 21 <= rowsSinceHeader ) ) { await WriteAsync( stdout, RenderDefaultHeader( options ), cancellationToken ).ConfigureAwait( false ); headerPrinted = true; rowsSinceHeader = 0; }
			await WriteAsync( stdout, RenderDefaultRow( previous, current, interval, options, now(), ref linuxIdleDebt ), cancellationToken ).ConfigureAwait( false );
			rowsSinceHeader++;
			previous = current;
			if ( !infinite ) rowsRemaining--;
		}
		return 0;
	}

	/// <summary>Renders the procps-ng default vmstat header for the selected layout.</summary>
	public static string RenderDefaultHeader( bool active = false, bool wide = false, bool timestamp = false ) => RenderDefaultHeader( new VmstatArguments( VmstatMode.Default, active, false, wide, timestamp, false, DataUnit.Kibibytes, null, null, null, false, false, false, null, false ) );
	private static string RenderDefaultHeader( VmstatArguments options ) {
		var builder = new StringBuilder();
		builder.Append( options.Wide
			? "--procs-- -----------------------memory---------------------- ---swap-- -----io---- -system-- ----------cpu----------"
			: "procs -----------memory---------- ---swap-- -----io---- -system-- -------cpu-------" );
		if ( options.Timestamp ) builder.Append( " -----timestamp-----" );
		builder.Append( Environment.NewLine );
		builder.Append( options.Active
			? ( options.Wide ? "   r    b         swpd         free        inact       active   si   so    bi    bo   in   cs  us  sy  id  wa  st  gu" : " r  b   swpd   free  inact active   si   so    bi    bo   in   cs us sy id wa st gu" )
			: ( options.Wide ? "   r    b         swpd         free         buff        cache   si   so    bi    bo   in   cs  us  sy  id  wa  st  gu" : " r  b   swpd   free   buff  cache   si   so    bi    bo   in   cs us sy id wa st gu" ) );
		if ( options.Timestamp ) builder.Append( "           timestamp" );
		builder.Append( Environment.NewLine );
		return builder.ToString();
	}

	private static string RenderDefaultRow( ProcVmstatSnapshot? before, ProcVmstatSnapshot current, TimeSpan? elapsed, VmstatArguments options, DateTimeOffset timestamp, ref long linuxIdleDebt ) {
		var memory = current.System.Memory.HasValue ? current.System.Memory.Value : null;
		var counters = current.SystemCounters.HasValue ? current.SystemCounters.Value : null;
		var uptime = current.System.Uptime.HasValue ? current.System.Uptime.Value.Uptime : TimeSpan.Zero;
		var rates = CalculateRates( before, current, elapsed, uptime, options.Unit );
		var cpu = CalculateCpu( before, current, ref linuxIdleDebt );
		string N( ulong? value, int width ) => value.HasValue ? value.Value.ToString( CultureInfo.InvariantCulture ).PadLeft( width ) : "-".PadLeft( width );
		string P( int? value, int width ) => value.HasValue ? value.Value.ToString( CultureInfo.InvariantCulture ).PadLeft( width ) : "-".PadLeft( width );
		ulong? MemoryValue( ulong? bytes ) => bytes.HasValue ? ConvertBytes( bytes.Value, options.Unit ) : null;
		ulong? FieldBytes( string linux, string darwin ) {
			if ( null == memory ) return null;
			if ( memory.Fields.TryGetValue( linux, out var linuxValue ) ) return linuxValue;
			return memory.Fields.TryGetValue( darwin, out var darwinValue ) ? darwinValue : null;
		}
		ulong? swapUsed = ( null == memory ) || !memory.SwapTotalBytes.HasValue || !memory.SwapFreeBytes.HasValue
			? null
			: memory.SwapFreeBytes.Value <= memory.SwapTotalBytes.Value
				? memory.SwapTotalBytes.Value - memory.SwapFreeBytes.Value
				: 0UL
		;
		var col5 = options.Active ? FieldBytes( "Inactive", "DarwinInactive" ) : memory?.BuffersBytes;
		var col6 = options.Active ? FieldBytes( "Active", "DarwinActive" ) : memory?.CacheBytes;
		var rw = options.Wide ? 4 : 2;
		var mw = options.Wide ? 12 : 6;
		var pw = options.Wide ? 3 : 2;
		var builder = new StringBuilder();
		builder.Append( N( counters?.RunningProcesses, rw ) ).Append( ' ' )
			.Append( N( counters?.BlockedProcesses, rw ) ).Append( ' ' )
			.Append( N( MemoryValue( swapUsed ), mw ) ).Append( ' ' )
			.Append( N( MemoryValue( memory?.FreeBytes ), mw ) ).Append( ' ' )
			.Append( N( MemoryValue( col5 ), mw ) ).Append( ' ' )
			.Append( N( MemoryValue( col6 ), mw ) ).Append( ' ' )
			.Append( N( rates.SwapIn, 4 ) ).Append( ' ' ).Append( N( rates.SwapOut, 4 ) ).Append( ' ' )
			.Append( N( rates.BlockIn, 5 ) ).Append( ' ' ).Append( N( rates.BlockOut, 5 ) ).Append( ' ' )
			.Append( N( rates.Interrupts, 4 ) ).Append( ' ' ).Append( N( rates.ContextSwitches, 4 ) ).Append( ' ' )
			.Append( P( cpu.User, pw ) ).Append( ' ' ).Append( P( cpu.System, pw ) ).Append( ' ' ).Append( P( cpu.Idle, pw ) ).Append( ' ' )
			.Append( P( cpu.Wait, pw ) ).Append( ' ' ).Append( P( cpu.Steal, pw ) ).Append( ' ' ).Append( P( cpu.Guest, pw ) );
		if ( options.Timestamp ) builder.Append( ' ' ).Append( timestamp.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture ) );
		builder.Append( Environment.NewLine );
		return builder.ToString();
	}

	private static RateValues CalculateRates( ProcVmstatSnapshot? before, ProcVmstatSnapshot current, TimeSpan? elapsed, TimeSpan uptime, DataUnit unit ) {
		if ( null == before ) {
			var uptimeSeconds = Math.Max( 1d, uptime.TotalSeconds );
			var contextDivisor = GetInitialCpuDivisor( current ) ?? uptimeSeconds;
			return new RateValues(
				RateFromTotalSwap( current.Paging, true, uptimeSeconds, unit ), RateFromTotalSwap( current.Paging, false, uptimeSeconds, unit ),
				RateFromTotalPageIo( current.Paging, true, uptimeSeconds ), RateFromTotalPageIo( current.Paging, false, uptimeSeconds ),
				RateFromTotalSystem( current.SystemCounters, true, uptimeSeconds ), RateFromTotalSystem( current.SystemCounters, false, contextDivisor )
			);
		}
		var duration = elapsed.GetValueOrDefault( TimeSpan.FromSeconds( 1 ) );
		return new RateValues(
			RateSwapDelta( before.Paging, current.Paging, true, duration, unit ), RateSwapDelta( before.Paging, current.Paging, false, duration, unit ),
			RatePageIoDelta( before.Paging, current.Paging, true, duration ), RatePageIoDelta( before.Paging, current.Paging, false, duration ),
			RateSystemDelta( before.SystemCounters, current.SystemCounters, true, duration ), RateSystemDelta( before.SystemCounters, current.SystemCounters, false, duration )
		);
	}

	private static ulong? RateFromTotalSwap( ProcObservedValue<ProcVmstatPagingCounters> observation, bool input, double seconds, DataUnit unit ) {
		if ( !observation.HasValue ) return null;
		var pages = input ? observation.Value.SwapInPages : observation.Value.SwapOutPages;
		var bytes = SaturatingMultiply( pages, observation.Value.PageSizeBytes );
		return TruncateRate( ConvertBytes( bytes, unit ) / seconds );
	}
	private static ulong? RateFromTotalPageIo( ProcObservedValue<ProcVmstatPagingCounters> observation, bool input, double seconds ) => observation.HasValue ? TruncateRate( ( input ? observation.Value.PageInKibibytes : observation.Value.PageOutKibibytes ) / seconds ) : null;
	private static ulong? RateFromTotalSystem( ProcObservedValue<ProcVmstatSystemCounters> observation, bool interrupts, double divisor ) => observation.HasValue ? TruncateRate( ( interrupts ? observation.Value.Interrupts : observation.Value.ContextSwitches ) / divisor ) : null;
	private static ulong? RateSwapDelta( ProcObservedValue<ProcVmstatPagingCounters> before, ProcObservedValue<ProcVmstatPagingCounters> after, bool input, TimeSpan elapsed, DataUnit unit ) {
		if ( !before.HasValue || !after.HasValue ) return null;
		var first = input ? before.Value.SwapInPages : before.Value.SwapOutPages;
		var second = input ? after.Value.SwapInPages : after.Value.SwapOutPages;
		var delta = ProcCounterMath.Delta( first, second );
		var bytes = SaturatingMultiply( delta, after.Value.PageSizeBytes );
		return RoundIntervalRate( ConvertBytes( bytes, unit ), elapsed );
	}
	private static ulong? RatePageIoDelta( ProcObservedValue<ProcVmstatPagingCounters> before, ProcObservedValue<ProcVmstatPagingCounters> after, bool input, TimeSpan elapsed ) {
		if ( !before.HasValue || !after.HasValue ) return null;
		var first = input ? before.Value.PageInKibibytes : before.Value.PageOutKibibytes;
		var second = input ? after.Value.PageInKibibytes : after.Value.PageOutKibibytes;
		return RoundIntervalRate( ProcCounterMath.Delta( first, second ), elapsed );
	}
	private static ulong? RateSystemDelta( ProcObservedValue<ProcVmstatSystemCounters> before, ProcObservedValue<ProcVmstatSystemCounters> after, bool interrupts, TimeSpan elapsed ) {
		if ( !before.HasValue || !after.HasValue ) return null;
		var first = interrupts ? before.Value.Interrupts : before.Value.ContextSwitches;
		var second = interrupts ? after.Value.Interrupts : after.Value.ContextSwitches;
		return RoundIntervalRate( ProcCounterMath.Delta( first, second ), elapsed );
	}
	private static double? GetInitialCpuDivisor( ProcVmstatSnapshot snapshot ) {
		if ( !snapshot.System.Cpu.HasValue ) return null;
		var cpu = snapshot.System.Cpu.Value;
		var divisor = unchecked( cpu.User + cpu.Nice + cpu.System + cpu.Irq + cpu.SoftIrq + cpu.Idle + cpu.IoWait + cpu.Steal );
		return 0UL == divisor ? 1d : divisor;
	}
	private static ulong RoundIntervalRate( ulong value, TimeSpan elapsed ) {
		var seconds = Math.Max( 1UL, checked( (ulong)Math.Round( elapsed.TotalSeconds, MidpointRounding.AwayFromZero ) ) );
		return SaturatingAdd( value, seconds / 2UL ) / seconds;
	}
	private static ulong TruncateRate( double value ) => 0d >= value ? 0UL : value >= ulong.MaxValue ? ulong.MaxValue : checked( (ulong)value );

	private static CpuValues CalculateCpu( ProcVmstatSnapshot? before, ProcVmstatSnapshot current, ref long linuxIdleDebt ) {
		if ( current.System.Cpu.HasValue ) return CalculateLinuxCpu( before?.System.Cpu, current.System.Cpu, ref linuxIdleDebt );
		if ( current.System.CpuActivity.HasValue ) return CalculateNeutralCpu( before?.System.CpuActivity, current.System.CpuActivity );
		return new CpuValues( null, null, null, null, null, null );
	}
	private static CpuValues CalculateLinuxCpu( ProcObservedValue<ProcCpuTimes>? before, ProcObservedValue<ProcCpuTimes> current, ref long idleDebt ) {
		var now = current.Value;
		ulong Delta( Func<ProcCpuTimes, ulong> selector ) {
			if ( null == before || !before.HasValue ) return selector( now );
			var previous = selector( before.Value );
			var next = selector( now );
			return next >= previous ? next - previous : 0UL;
		}
		var userTotal = Delta( value => unchecked( value.User + value.Nice ) );
		var guest = Delta( value => unchecked( value.Guest + value.GuestNice ) );
		var user = userTotal >= guest ? userTotal - guest : 0UL;
		var system = Delta( value => unchecked( value.System + value.Irq + value.SoftIrq ) );
		ulong idle;
		if ( null == before || !before.HasValue ) {
			idle = now.Idle;
		} else {
			var previousIdle = before.Value.Idle;
			long signedIdle;
			if ( now.Idle >= previousIdle ) {
				var delta = now.Idle - previousIdle;
				signedIdle = delta > (ulong)long.MaxValue ? long.MaxValue : checked( (long)delta );
			} else {
				var delta = previousIdle - now.Idle;
				signedIdle = delta > (ulong)long.MaxValue ? long.MinValue : -checked( (long)delta );
			}
			if ( 0 != idleDebt ) {
				if ( 0 < idleDebt && signedIdle > long.MaxValue - idleDebt ) signedIdle = long.MaxValue;
				else if ( 0 > idleDebt && signedIdle < long.MinValue - idleDebt ) signedIdle = long.MinValue;
				else signedIdle += idleDebt;
				idleDebt = 0;
			}
			if ( 0 > signedIdle ) {
				idleDebt = signedIdle;
				idle = 0UL;
			} else {
				idle = checked( (ulong)signedIdle );
			}
		}
		var wait = Delta( value => value.IoWait );
		var steal = Delta( value => value.Steal );
		var divisor = SaturatingAdd( SaturatingAdd( SaturatingAdd( userTotal, system ), idle ), SaturatingAdd( wait, steal ) );
		if ( 0 == divisor ) { divisor = 1; idle = 1; }
		return new CpuValues( Percent( user, divisor ), Percent( system, divisor ), Percent( idle, divisor ), Percent( wait, divisor ), Percent( steal, divisor ), Percent( guest, divisor ) );
	}
	private static CpuValues CalculateNeutralCpu( ProcObservedValue<ProcCpuActivity>? before, ProcObservedValue<ProcCpuActivity> current ) {
		var now = current.Value;
		ulong DeltaRequired( Func<ProcCpuActivity, ulong> selector ) => null != before && before.HasValue ? ProcCounterMath.Delta( selector( before.Value ), selector( now ), now.CounterBitWidth ) : selector( now );
		ulong? DeltaOptional( Func<ProcCpuActivity, ulong?> selector ) {
			var currentValue = selector( now ); if ( !currentValue.HasValue ) return null;
			if ( null == before || !before.HasValue ) return currentValue.Value;
			var previous = selector( before.Value ); return previous.HasValue ? ProcCounterMath.Delta( previous.Value, currentValue.Value, now.CounterBitWidth ) : null;
		}
		var nice = DeltaOptional( value => value.Nice ) ?? 0UL;
		var other = DeltaOptional( value => value.Other ) ?? 0UL;
		var user = unchecked( DeltaRequired( value => value.User ) + nice );
		var system = unchecked( DeltaRequired( value => value.System ) + other );
		var idle = DeltaRequired( value => value.Idle );
		var wait = DeltaOptional( value => value.Wait );
		var divisor = unchecked( user + system + idle + ( wait ?? 0UL ) );
		if ( 0 == divisor ) { divisor = 1; idle = 1; }
		return new CpuValues( Percent( user, divisor ), Percent( system, divisor ), Percent( idle, divisor ), wait.HasValue ? Percent( wait.Value, divisor ) : null, null, null );
	}
	private static int Percent( ulong value, ulong total ) => checked( (int)( ( (UInt128)100UL * value + total / 2UL ) / total ) );

	private static async Task<int> RenderForksAsync( IProcVmstatProvider provider, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		if ( 0 == ( provider.Capabilities & ProcVmstatCapabilities.Forks ) ) return await UnsupportedModeAsync( stderr, "fork statistics", cancellationToken ).ConfigureAwait( false );
		var snapshot = await provider.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		if ( !snapshot.SystemCounters.HasValue ) return await ObservationFailureAsync( stderr, "system statistics", snapshot.SystemCounters.Availability, snapshot.SystemCounters.Diagnostic, cancellationToken ).ConfigureAwait( false );
		await WriteLineAsync( stdout, string.Concat( snapshot.SystemCounters.Value.Forks.ToString( CultureInfo.InvariantCulture ).PadLeft( 13 ), " forks" ), cancellationToken ).ConfigureAwait( false );
		return 0;
	}

	private static async Task<int> RenderStatisticsAsync( IProcVmstatProvider provider, VmstatArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		if ( 0 == ( provider.Capabilities & ProcVmstatCapabilities.Statistics ) ) return await UnsupportedModeAsync( stderr, "statistics summary", cancellationToken ).ConfigureAwait( false );
		var snapshot = await provider.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		if ( !snapshot.System.Memory.HasValue || !snapshot.System.Cpu.HasValue || !snapshot.SystemCounters.HasValue || !snapshot.System.VirtualMemory.HasValue ) return await UnsupportedModeAsync( stderr, "complete statistics summary", cancellationToken ).ConfigureAwait( false );
		var memory = snapshot.System.Memory.Value;
		var cpu = snapshot.System.Cpu.Value;
		var counters = snapshot.SystemCounters.Value;
		var vm = snapshot.System.VirtualMemory.Value;
		var unitLabel = UnitLabel( options.Unit );
		ulong U( ulong? bytes ) => bytes.HasValue ? ConvertBytes( bytes.Value, options.Unit ) : 0UL;
		ulong V( string key ) => vm.TryGetValue( key, out var value ) ? value : 0UL;
		var total = memory.TotalBytes ?? 0UL;
		var free = memory.FreeBytes ?? 0UL;
		var available = memory.AvailableBytes ?? free;
		if ( available > total ) available = free;
		var used = total >= available ? total - available : total >= free ? total - free : 0UL;
		var swapTotal = memory.SwapTotalBytes ?? 0UL;
		var swapFree = memory.SwapFreeBytes ?? 0UL;
		var swapUsed = swapTotal >= swapFree ? swapTotal - swapFree : 0UL;
		var builder = new StringBuilder();
		AppendStatistic( builder, U( total ), string.Concat( unitLabel, " total memory" ) );
		AppendStatistic( builder, U( used ), string.Concat( unitLabel, " used memory" ) );
		AppendStatistic( builder, U( ReadMemoryField( memory, "Active" ) ), string.Concat( unitLabel, " active memory" ) );
		AppendStatistic( builder, U( ReadMemoryField( memory, "Inactive" ) ), string.Concat( unitLabel, " inactive memory" ) );
		AppendStatistic( builder, U( free ), string.Concat( unitLabel, " free memory" ) );
		AppendStatistic( builder, U( memory.BuffersBytes ), string.Concat( unitLabel, " buffer memory" ) );
		AppendStatistic( builder, U( memory.CacheBytes ), string.Concat( unitLabel, " swap cache" ) );
		AppendStatistic( builder, U( swapTotal ), string.Concat( unitLabel, " total swap" ) );
		AppendStatistic( builder, U( swapUsed ), string.Concat( unitLabel, " used swap" ) );
		AppendStatistic( builder, U( swapFree ), string.Concat( unitLabel, " free swap" ) );
		AppendStatistic( builder, cpu.User, "non-nice user cpu ticks" );
		AppendStatistic( builder, cpu.Nice, "nice user cpu ticks" );
		AppendStatistic( builder, cpu.System, "system cpu ticks" );
		AppendStatistic( builder, cpu.Idle, "idle cpu ticks" );
		AppendStatistic( builder, cpu.IoWait, "IO-wait cpu ticks" );
		AppendStatistic( builder, cpu.Irq, "IRQ cpu ticks" );
		AppendStatistic( builder, cpu.SoftIrq, "softirq cpu ticks" );
		AppendStatistic( builder, cpu.Steal, "stolen cpu ticks" );
		AppendStatistic( builder, cpu.Guest, "non-nice guest cpu ticks" );
		AppendStatistic( builder, cpu.GuestNice, "nice guest cpu ticks" );
		AppendStatistic( builder, V( "pgpgin" ), "K paged in" );
		AppendStatistic( builder, V( "pgpgout" ), "K paged out" );
		AppendStatistic( builder, V( "pswpin" ), "pages swapped in" );
		AppendStatistic( builder, V( "pswpout" ), "pages swapped out" );
		AppendStatistic( builder, V( "pgalloc_dma" ), "pages alloc in dma" );
		AppendStatistic( builder, V( "pgalloc_dma32" ), "pages alloc in dma32" );
		AppendStatistic( builder, V( "pgalloc_high" ), "pages alloc in high" );
		AppendStatistic( builder, V( "pgalloc_movable" ), "pages alloc in movable" );
		AppendStatistic( builder, V( "pgalloc_normal" ), "pages alloc in normal" );
		AppendStatistic( builder, V( "pgfree" ), "pages free" );
		AppendStatistic( builder, counters.Interrupts, "interrupts" );
		AppendStatistic( builder, counters.ContextSwitches, "CPU context switches" );
		AppendStatistic( builder, counters.BootTimeUnixSeconds, "boot time" );
		AppendStatistic( builder, counters.Forks, "forks" );
		await WriteAsync( stdout, builder.ToString(), cancellationToken ).ConfigureAwait( false );
		return 0;
	}
	private static void AppendStatistic( StringBuilder builder, ulong value, string label ) => builder.Append( value.ToString( CultureInfo.InvariantCulture ).PadLeft( 13 ) ).Append( ' ' ).Append( label ).Append( Environment.NewLine );

	private static async Task<int> RenderDiskSummaryAsync( IProcVmstatProvider provider, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		if ( 0 == ( provider.Capabilities & ProcVmstatCapabilities.Disk ) ) return await UnsupportedModeAsync( stderr, "disk statistics", cancellationToken ).ConfigureAwait( false );
		var snapshot = await provider.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		if ( !snapshot.Disks.HasValue ) return await ObservationFailureAsync( stderr, "disk statistics", snapshot.Disks.Availability, snapshot.Disks.Diagnostic, cancellationToken ).ConfigureAwait( false );
		var rows = snapshot.Disks.Value;
		var disks = rows.Where( row => !row.IsPartition ).ToArray();
		ulong Sum( Func<ProcDiskStatEntry, ulong> selector ) { ulong result = 0; foreach ( var row in disks ) result = SaturatingAdd( result, selector( row ) ); return result; }
		var builder = new StringBuilder();
		AppendStatistic( builder, checked( (ulong)disks.Length ), "disks" );
		AppendStatistic( builder, checked( (ulong)rows.Count( row => row.IsPartition ) ), "partitions" );
		AppendStatistic( builder, Sum( row => row.ReadsCompleted ), "total reads" );
		AppendStatistic( builder, Sum( row => row.ReadsMerged ), "merged reads" );
		AppendStatistic( builder, Sum( row => row.SectorsRead ), "read sectors" );
		AppendStatistic( builder, Sum( row => row.ReadMilliseconds ), "milli reading" );
		AppendStatistic( builder, Sum( row => row.WritesCompleted ), "writes" );
		AppendStatistic( builder, Sum( row => row.WritesMerged ), "merged writes" );
		AppendStatistic( builder, Sum( row => row.SectorsWritten ), "written sectors" );
		AppendStatistic( builder, Sum( row => row.WriteMilliseconds ), "milli writing" );
		AppendStatistic( builder, Sum( row => row.IoInProgress / 1000UL ), "inprogress IO" );
		AppendStatistic( builder, Sum( row => row.IoMilliseconds / 1000UL ), "milli spent IO" );
		AppendStatistic( builder, Sum( row => row.WeightedIoMilliseconds / 1000UL ), "milli weighted IO" );
		await WriteAsync( stdout, builder.ToString(), cancellationToken ).ConfigureAwait( false );
		return 0;
	}

	private static async Task<int> RenderDiskLoopAsync( IProcVmstatProvider provider, VmstatArguments options, Stream? stdout, Stream? stderr, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, CancellationToken cancellationToken ) {
		if ( 0 == ( provider.Capabilities & ProcVmstatCapabilities.Disk ) ) return await UnsupportedModeAsync( stderr, "disk statistics", cancellationToken ).ConfigureAwait( false );
		return await RenderRepeatedAsync( provider, options, stdout, stderr, delay, now, cancellationToken, true, ( snapshot, timestamp, includeHeader ) => RenderDisks( snapshot, options.Wide, options.Timestamp, timestamp, includeHeader ) ).ConfigureAwait( false );
	}
	private static string? RenderDisks( ProcVmstatSnapshot snapshot, bool wide, bool timestamp, DateTimeOffset now, bool includeHeader ) {
		if ( !snapshot.Disks.HasValue ) return null;
		var rows = snapshot.Disks.Value.Where( row => !row.IsPartition ).ToArray();
		var totalWidth = wide ? 9 : 6;
		var sectorWidth = wide ? 11 : 7;
		var timeWidth = wide ? 11 : 7;
		var currentWidth = wide ? 7 : 6;
		var secondsWidth = wide ? 7 : 6;
		var builder = new StringBuilder();
		if ( includeHeader ) {
			builder.Append( wide
				? "disk- -------------------reads------------------- -------------------writes------------------ ------IO-------"
				: "disk- ------------reads------------ ------------writes----------- -----IO------" );
			if ( timestamp ) builder.Append( " -----timestamp-----" );
			builder.Append( Environment.NewLine );
			builder.Append( " ".PadLeft( 5 ) ).Append( ' ' )
				.Append( "total".PadLeft( totalWidth ) ).Append( ' ' ).Append( "merged".PadLeft( totalWidth ) ).Append( ' ' )
				.Append( "sectors".PadLeft( sectorWidth ) ).Append( ' ' ).Append( "ms".PadLeft( timeWidth ) ).Append( ' ' )
				.Append( "total".PadLeft( totalWidth ) ).Append( ' ' ).Append( "merged".PadLeft( totalWidth ) ).Append( ' ' )
				.Append( "sectors".PadLeft( sectorWidth ) ).Append( ' ' ).Append( "ms".PadLeft( timeWidth ) ).Append( ' ' )
				.Append( "cur".PadLeft( currentWidth ) ).Append( ' ' ).Append( "sec".PadLeft( secondsWidth ) );
			if ( timestamp ) builder.Append( "           timestamp" );
			builder.Append( Environment.NewLine );
		}
		foreach ( var row in rows ) {
			builder.Append( row.Name.PadRight( 5 ) ).Append( ' ' )
				.Append( row.ReadsCompleted.ToString( CultureInfo.InvariantCulture ).PadLeft( totalWidth ) ).Append( ' ' )
				.Append( row.ReadsMerged.ToString( CultureInfo.InvariantCulture ).PadLeft( totalWidth ) ).Append( ' ' )
				.Append( row.SectorsRead.ToString( CultureInfo.InvariantCulture ).PadLeft( sectorWidth ) ).Append( ' ' )
				.Append( row.ReadMilliseconds.ToString( CultureInfo.InvariantCulture ).PadLeft( timeWidth ) ).Append( ' ' )
				.Append( row.WritesCompleted.ToString( CultureInfo.InvariantCulture ).PadLeft( totalWidth ) ).Append( ' ' )
				.Append( row.WritesMerged.ToString( CultureInfo.InvariantCulture ).PadLeft( totalWidth ) ).Append( ' ' )
				.Append( row.SectorsWritten.ToString( CultureInfo.InvariantCulture ).PadLeft( sectorWidth ) ).Append( ' ' )
				.Append( row.WriteMilliseconds.ToString( CultureInfo.InvariantCulture ).PadLeft( timeWidth ) ).Append( ' ' )
				.Append( ( row.IoInProgress / 1000UL ).ToString( CultureInfo.InvariantCulture ).PadLeft( currentWidth ) ).Append( ' ' )
				.Append( ( row.IoMilliseconds / 1000UL ).ToString( CultureInfo.InvariantCulture ).PadLeft( secondsWidth ) );
			if ( timestamp ) builder.Append( ' ' ).Append( now.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture ) );
			builder.Append( Environment.NewLine );
		}
		return builder.ToString();
	}

	private static async Task<int> RenderPartitionLoopAsync( IProcVmstatProvider provider, VmstatArguments options, Stream? stdout, Stream? stderr, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, CancellationToken cancellationToken ) {
		if ( 0 == ( provider.Capabilities & ProcVmstatCapabilities.Partition ) ) return await UnsupportedModeAsync( stderr, "partition statistics", cancellationToken ).ConfigureAwait( false );
		return await RenderRepeatedAsync( provider, options, stdout, stderr, delay, now, cancellationToken, false, ( snapshot, _, includeHeader ) => RenderPartition( snapshot, options.Partition!, includeHeader ) ).ConfigureAwait( false );
	}
	private static string? RenderPartition( ProcVmstatSnapshot snapshot, string partition, bool includeHeader ) {
		if ( !snapshot.Disks.HasValue ) return null;
		var name = partition.StartsWith( "/dev/", StringComparison.Ordinal ) ? partition[ 5.. ] : partition;
		var row = snapshot.Disks.Value.FirstOrDefault( item => string.Equals( item.Name, name, StringComparison.Ordinal ) );
		if ( null == row ) return string.Empty;
		var builder = new StringBuilder();
		if ( includeHeader ) builder.Append( row.Name.PadRight( 10 ) ).Append( ' ' ).Append( "reads".PadLeft( 10 ) ).Append( "read sectors".PadLeft( 17 ) ).Append( "writes".PadLeft( 12 ) ).Append( "requested writes".PadLeft( 18 ) ).Append( Environment.NewLine );
		builder.Append( row.ReadsCompleted.ToString( CultureInfo.InvariantCulture ).PadLeft( 21 ) ).Append( "  " )
			.Append( row.SectorsRead.ToString( CultureInfo.InvariantCulture ).PadLeft( 16 ) ).Append( "  " )
			.Append( row.WritesCompleted.ToString( CultureInfo.InvariantCulture ).PadLeft( 10 ) ).Append( "  " )
			.Append( row.SectorsWritten.ToString( CultureInfo.InvariantCulture ).PadLeft( 16 ) ).Append( Environment.NewLine );
		return builder.ToString();
	}

	private static async Task<int> RenderSlabLoopAsync( IProcVmstatProvider provider, VmstatArguments options, Stream? stdout, Stream? stderr, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, CancellationToken cancellationToken ) {
		if ( 0 == ( provider.Capabilities & ProcVmstatCapabilities.Slab ) ) return await UnsupportedModeAsync( stderr, "slab statistics", cancellationToken ).ConfigureAwait( false );
		return await RenderRepeatedAsync( provider, options, stdout, stderr, delay, now, cancellationToken, true, ( snapshot, _, includeHeader ) => RenderSlabs( snapshot, includeHeader ) ).ConfigureAwait( false );
	}
	private static string? RenderSlabs( ProcVmstatSnapshot snapshot, bool includeHeader ) {
		if ( !snapshot.System.Slab.HasValue ) return null;
		var builder = new StringBuilder();
		if ( includeHeader ) builder.Append( "Cache                       Num  Total   Size  Pages" ).Append( Environment.NewLine );
		foreach ( var row in snapshot.System.Slab.Value.OrderBy( row => row.Name, StringComparer.Ordinal ) ) {
			builder.Append( row.Name.PadRight( 24 ) ).Append( ' ' ).Append( row.ActiveObjects.ToString( CultureInfo.InvariantCulture ).PadLeft( 6 ) ).Append( ' ' ).Append( row.TotalObjects.ToString( CultureInfo.InvariantCulture ).PadLeft( 6 ) ).Append( ' ' ).Append( row.ObjectSizeBytes.ToString( CultureInfo.InvariantCulture ).PadLeft( 6 ) ).Append( ' ' ).Append( row.ObjectsPerSlab.ToString( CultureInfo.InvariantCulture ).PadLeft( 6 ) ).Append( Environment.NewLine );
		}
		return builder.ToString();
	}

	private static async Task<int> RenderRepeatedAsync( IProcVmstatProvider provider, VmstatArguments options, Stream? stdout, Stream? stderr, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, CancellationToken cancellationToken, bool repeatHeaders, Func<ProcVmstatSnapshot, DateTimeOffset, bool, string?> renderer ) {
		var infinite = options.Delay.HasValue && !options.Count.HasValue;
		var remaining = options.Count ?? 1L;
		var interval = options.Delay ?? TimeSpan.Zero;
		var firstIteration = true;
		while ( infinite || 0 < remaining ) {
			var snapshot = await provider.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
			var includeHeader = firstIteration || ( repeatHeaders && !options.OneHeader );
			var text = renderer( snapshot, now(), includeHeader );
			if ( null == text ) return await UnsupportedModeAsync( stderr, "requested statistics", cancellationToken ).ConfigureAwait( false );
			if ( 0 == text.Length && VmstatMode.Partition == options.Mode ) { await WriteDiagnosticAsync( stderr, string.Concat( "vmstat: Disk/Partition ", options.Partition, " not found" ), cancellationToken ).ConfigureAwait( false ); return 1; }
			await WriteAsync( stdout, text, cancellationToken ).ConfigureAwait( false );
			firstIteration = false;
			if ( !infinite ) { remaining--; if ( 0 >= remaining ) break; }
			await delay( interval, cancellationToken ).ConfigureAwait( false );
		}
		return 0;
	}

	private static bool CanRenderDefault( ProcVmstatSnapshot snapshot ) => snapshot.System.Memory.HasValue && ( snapshot.System.Cpu.HasValue || snapshot.System.CpuActivity.HasValue );
	private static bool IsPartialDefault( ProcVmstatSnapshot snapshot ) => !snapshot.SystemCounters.HasValue || !snapshot.Paging.HasValue || !snapshot.System.Cpu.HasValue;
	private static async Task<int> UnsupportedModeAsync( Stream? stderr, string name, CancellationToken cancellationToken ) { await WriteDiagnosticAsync( stderr, string.Concat( "vmstat: ", name, " with procps-ng semantics is not available on this platform" ), cancellationToken ).ConfigureAwait( false ); return 1; }
	private static async Task<int> ObservationFailureAsync( Stream? stderr, string name, ProcObservationAvailability availability, string? diagnostic, CancellationToken cancellationToken ) { var text = string.Concat( "vmstat: unable to read ", name ); if ( ProcObservationAvailability.Unsupported == availability ) text = string.Concat( "vmstat: ", name, " is not supported on this platform" ); if ( !string.IsNullOrWhiteSpace( diagnostic ) ) text = string.Concat( text, ": ", diagnostic ); await WriteDiagnosticAsync( stderr, text, cancellationToken ).ConfigureAwait( false ); return 1; }

	/// <summary>Converts a byte count to a procps-ng vmstat display unit.</summary>
	public static ulong ConvertBytes( ulong bytes, char unit ) => ConvertBytes( bytes, ParseUnit( unit ) );
	private static ulong ConvertBytes( ulong bytes, DataUnit unit ) => bytes / UnitDivisor( unit );
	private static ulong UnitDivisor( DataUnit unit ) => unit switch { DataUnit.Bytes => 1UL, DataUnit.Kilobytes => 1000UL, DataUnit.Kibibytes => 1024UL, DataUnit.Megabytes => 1_000_000UL, DataUnit.Mebibytes => 1_048_576UL, _ => 1024UL };
	private static string UnitLabel( DataUnit unit ) => unit switch { DataUnit.Bytes => "B", DataUnit.Kilobytes => "k", DataUnit.Kibibytes => "K", DataUnit.Megabytes => "m", DataUnit.Mebibytes => "M", _ => "K" };
	private static DataUnit ParseUnit( char unit ) => unit switch { 'b' or 'B' => DataUnit.Bytes, 'k' => DataUnit.Kilobytes, 'K' => DataUnit.Kibibytes, 'm' => DataUnit.Megabytes, 'M' => DataUnit.Mebibytes, _ => throw new ArgumentOutOfRangeException( nameof( unit ) ) };
	private static ulong? ReadMemoryField( ProcMemoryInfo memory, string name ) => memory.Fields.TryGetValue( name, out var value ) ? value : null;
	private static ulong SaturatingAdd( ulong left, ulong right ) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
	private static ulong SaturatingMultiply( ulong left, ulong right ) => 0UL == left || 0UL == right ? 0UL : ulong.MaxValue / left < right ? ulong.MaxValue : left * right;
	private static DateTimeOffset GetLocalNow() => DateTimeOffset.Now;
	private static Task DefaultDelayAsync( TimeSpan duration, CancellationToken cancellationToken ) => Task.Delay( duration, cancellationToken );

	private static VmstatArguments ParseArguments( string[] args ) {
		var mode = VmstatMode.Default; var active = false; var oneHeader = false; var wide = false; var timestamp = false; var noFirst = false; var unit = DataUnit.Kibibytes; string? partition = null; var operands = new List<string>(); var endOptions = false;
		for ( var index = 0; index < args.Length; index++ ) {
			var token = args[ index ];
			if ( endOptions ) { operands.Add( token ); continue; }
			if ( "--" == token ) { endOptions = true; continue; }
			if ( !token.StartsWith( '-' ) || "-" == token ) { operands.Add( token ); continue; }
			if ( token.StartsWith( "--", StringComparison.Ordinal ) ) {
				var equal = token.IndexOf( '=' ); var name = 0 > equal ? token[ 2.. ] : token[ 2..equal ]; var value = 0 > equal ? null : token[ ( equal + 1 ).. ];
				var resolution = ResolveLongOption( name, token ); if ( null != resolution.Error ) return Fail( resolution.Error, true ); var option = resolution.Option!;
				if ( option is "partition" or "unit" ) { if ( null == value ) { if ( index + 1 >= args.Length ) return Fail( $"vmstat: option '--{option}' requires an argument", true ); value = args[ ++index ]; } var failure = ApplyValue( option, value ); if ( null != failure ) return failure; continue; }
				if ( null != value ) return Fail( $"vmstat: option '--{option}' doesn't allow an argument", true ); var immediate = ApplyFlag( option ); if ( null != immediate ) return immediate; continue;
			}
			for ( var position = 1; position < token.Length; position++ ) {
				var option = token[ position ];
				if ( option is 'p' or 'S' ) { string value; if ( position + 1 < token.Length ) value = token[ ( position + 1 ).. ]; else { if ( index + 1 >= args.Length ) return Fail( $"vmstat: option requires an argument -- '{option}'", true ); value = args[ ++index ]; } var failure = ApplyValue( 'p' == option ? "partition" : "unit", value ); if ( null != failure ) return failure; break; }
				var name = option switch { 'a' => "active", 'f' => "forks", 'm' => "slabs", 'n' => "one-header", 's' => "stats", 'd' => "disk", 'D' => "disk-sum", 'w' => "wide", 't' => "timestamp", 'h' => "help", 'V' => "version", 'y' => "no-first", _ => null };
				if ( null == name ) return Fail( $"vmstat: invalid option -- '{option}'", true ); var immediate = ApplyFlag( name ); if ( null != immediate ) return immediate;
			}
		}
		if ( 2 < operands.Count ) return Fail( string.Empty, true );
		TimeSpan? delay = null; long? count = null;
		if ( 0 < operands.Count ) { if ( !ulong.TryParse( operands[ 0 ], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds ) ) return Fail( $"vmstat: failed to parse argument: '{operands[ 0 ]}'", false ); if ( 1UL > seconds ) return Fail( "vmstat: delay must be positive integer", false ); if ( uint.MaxValue < seconds ) return Fail( "vmstat: too large delay value", false ); delay = TimeSpan.FromSeconds( seconds ); }
		if ( 1 < operands.Count ) { if ( !long.TryParse( operands[ 1 ], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedCount ) ) return Fail( $"vmstat: failed to parse argument: '{operands[ 1 ]}'", false ); count = Math.Max( 0L, parsedCount ); }
		return new VmstatArguments( mode, active, oneHeader, wide, timestamp, noFirst, unit, partition, delay, count, false, false, false, null, false );

		VmstatArguments? ApplyFlag( string option ) {
			switch ( option ) {
				case "active": active = true; break;
				case "forks": return new VmstatArguments( mode, active, oneHeader, wide, timestamp, noFirst, unit, partition, null, null, true, false, false, null, false );
				case "slabs": if ( !SetMode( VmstatMode.Slab ) ) return Fail( string.Empty, true ); break;
				case "one-header": oneHeader = true; break;
				case "stats": if ( !SetMode( VmstatMode.Statistics ) ) return Fail( string.Empty, true ); break;
				case "disk": if ( !SetMode( VmstatMode.Disk ) ) return Fail( string.Empty, true ); break;
				case "disk-sum": if ( !SetMode( VmstatMode.DiskSummary ) ) return Fail( string.Empty, true ); break;
				case "wide": wide = true; break;
				case "timestamp": timestamp = true; break;
				case "no-first": noFirst = true; break;
				case "help": return new VmstatArguments( mode, active, oneHeader, wide, timestamp, noFirst, unit, partition, null, null, false, true, false, null, false );
				case "version": return new VmstatArguments( mode, active, oneHeader, wide, timestamp, noFirst, unit, partition, null, null, false, false, true, null, false );
			}
			return null;
		}
		VmstatArguments? ApplyValue( string option, string value ) {
			if ( "partition" == option ) { if ( !SetMode( VmstatMode.Partition ) ) return Fail( string.Empty, true ); partition = value.StartsWith( "/dev/", StringComparison.Ordinal ) ? value[ 5.. ] : value; return null; }
			if ( 0 == value.Length || value[ 0 ] is not ( 'b' or 'B' or 'k' or 'K' or 'm' or 'M' ) ) return Fail( "vmstat: -S requires k, K, m or M (default is KiB)", false ); unit = ParseUnit( value[ 0 ] ); return null;
		}
		bool SetMode( VmstatMode requested ) { if ( VmstatMode.Default != mode && requested != mode ) return false; mode = requested; return true; }
		VmstatArguments Fail( string error, bool usage ) => new( mode, active, oneHeader, wide, timestamp, noFirst, unit, partition, null, null, false, false, false, error, usage );
	}
	private static LongOptionResolution ResolveLongOption( string name, string token ) {
		string[] options = [ "active", "forks", "slabs", "one-header", "stats", "disk", "disk-sum", "partition", "unit", "wide", "timestamp", "help", "version", "no-first" ];
		var exact = options.FirstOrDefault( option => string.Equals( option, name, StringComparison.Ordinal ) ); if ( null != exact ) return new( exact, null );
		var matches = options.Where( option => option.StartsWith( name, StringComparison.Ordinal ) ).ToArray(); if ( 1 == matches.Length ) return new( matches[ 0 ], null ); if ( 1 < matches.Length ) return new( null, $"vmstat: option '{token}' is ambiguous; possibilities: {string.Join( " ", matches.Select( option => $"'--{option}'" ) )}" ); return new( null, $"vmstat: unrecognized option '{token}'" );
	}

	private static async Task WriteAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Out.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( text ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static async Task WriteErrorAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Error.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( text ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static Task WriteLineAsync( Stream? stream, string text, CancellationToken cancellationToken ) => WriteAsync( stream, string.Concat( text, Environment.NewLine ), cancellationToken );
	private static async Task WriteDiagnosticAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Error.WriteLineAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( string.Concat( text, Environment.NewLine ) ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static string NormalizeLineEndings( string value ) { var normalized = value.Replace( "\r\n", "\n", StringComparison.Ordinal ).Replace( "\r", "\n", StringComparison.Ordinal ); return "\n" == Environment.NewLine ? normalized : normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal ); }

	private enum VmstatMode { Default, Statistics, Disk, DiskSummary, Partition, Slab }
	private enum DataUnit { Bytes, Kilobytes, Kibibytes, Megabytes, Mebibytes }
	private sealed record VmstatArguments( VmstatMode Mode, bool Active, bool OneHeader, bool Wide, bool Timestamp, bool NoFirst, DataUnit Unit, string? Partition, TimeSpan? Delay, long? Count, bool ImmediateForks, bool ShowHelp, bool ShowVersion, string? Error, bool ShowUsageOnError );
	private sealed record LongOptionResolution( string? Option, string? Error );
	private sealed record RateValues( ulong? SwapIn, ulong? SwapOut, ulong? BlockIn, ulong? BlockOut, ulong? Interrupts, ulong? ContextSwitches );
	private sealed record CpuValues( int? User, int? System, int? Idle, int? Wait, int? Steal, int? Guest );
}

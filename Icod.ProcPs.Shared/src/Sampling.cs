namespace Icod.ProcPs.Shared;

using System.Runtime.CompilerServices;
using Icod.CommandFramework.Time;

/// <summary>Provides counter-delta calculations with explicit unsigned wraparound semantics.</summary>
public static class ProcCounterMath {
	/// <summary>Calculates the delta between two unsigned counters with wraparound at the requested bit width.</summary>
	public static ulong Delta( ulong before, ulong after, int bitWidth = 64 ) {
		if ( bitWidth is < 1 or > 64 ) throw new ArgumentOutOfRangeException( nameof( bitWidth ) );
		var maximum = 64 == bitWidth ? ulong.MaxValue : ( 1UL << bitWidth ) - 1UL;
		if ( before > maximum || after > maximum ) throw new ArgumentOutOfRangeException( nameof( before ), "Counter value exceeds the selected width." );
		return after >= before ? after - before : checked( maximum - before + after + 1UL );
	}

	/// <summary>Calculates an unsigned counter rate per second for a positive elapsed interval.</summary>
	public static double RatePerSecond( ulong delta, TimeSpan elapsed ) {
		if ( TimeSpan.Zero >= elapsed ) throw new ArgumentOutOfRangeException( nameof( elapsed ) );
		return delta / elapsed.TotalSeconds;
	}
}

/// <summary>Contains one value captured at a monotonic timestamp.</summary>
/// <typeparam name="T">The sampled value type.</typeparam>
public sealed class ProcTimedSample<T> {
	/// <summary>Gets the monotonic timestamp.</summary>
	public long Timestamp { get; }
	/// <summary>Gets the sampled value.</summary>
	public T Value { get; }
	/// <summary>Gets the scheduler tick that caused the sample when sampling periodically.</summary>
	public PeriodicTick? Tick { get; }
	/// <summary>Initializes a timed sample.</summary>
	public ProcTimedSample( long timestamp, T value, PeriodicTick? tick = null ) {
		this.Timestamp = timestamp;
		this.Value = value;
		this.Tick = tick;
	}
}

/// <summary>Contains a before/after sample pair and its monotonic elapsed time.</summary>
/// <typeparam name="T">The sampled value type.</typeparam>
public sealed class ProcSamplingWindow<T> {
	/// <summary>Gets the first sample.</summary>
	public ProcTimedSample<T> Before { get; }
	/// <summary>Gets the second sample.</summary>
	public ProcTimedSample<T> After { get; }
	/// <summary>Gets monotonic elapsed time between samples.</summary>
	public TimeSpan Elapsed { get; }
	/// <summary>Initializes a sampling window.</summary>
	public ProcSamplingWindow( ProcTimedSample<T> before, ProcTimedSample<T> after, TimeSpan elapsed ) {
		ArgumentNullException.ThrowIfNull( before );
		ArgumentNullException.ThrowIfNull( after );
		if ( TimeSpan.Zero > elapsed ) throw new ArgumentOutOfRangeException( nameof( elapsed ) );
		this.Before = before;
		this.After = after;
		this.Elapsed = elapsed;
	}
}

/// <summary>Samples ProcPs providers over the shared monotonic clock and fixed-rate scheduler.</summary>
public sealed class ProcSampler {
	private readonly IMonotonicClock _clock;
	private readonly IPeriodicScheduler _scheduler;
	/// <summary>Initializes a sampler over injectable cross-suite time contracts.</summary>
	public ProcSampler( IMonotonicClock clock, IPeriodicScheduler scheduler ) {
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( scheduler );
		this._clock = clock;
		this._scheduler = scheduler;
	}
	/// <summary>Creates a sampler over the shared system monotonic clock and scheduler.</summary>
	public static ProcSampler CreateSystem() => new( SystemMonotonicClock.Instance, MonotonicPeriodicScheduler.Instance );
	/// <summary>Captures a deterministic two-sample window separated by a monotonic delay.</summary>
	public async Task<ProcSamplingWindow<T>> SampleWindowAsync<T>( Func<CancellationToken, Task<T>> capture, TimeSpan interval, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( capture );
		if ( TimeSpan.Zero < interval ) {
			var beforeTimestamp = this._clock.GetTimestamp();
			var before = new ProcTimedSample<T>( beforeTimestamp, await capture( cancellationToken ).ConfigureAwait( false ) );
			await this._clock.DelayAsync( interval, cancellationToken ).ConfigureAwait( false );
			var afterTimestamp = this._clock.GetTimestamp();
			var after = new ProcTimedSample<T>( afterTimestamp, await capture( cancellationToken ).ConfigureAwait( false ) );
			return new ProcSamplingWindow<T>( before, after, this._clock.GetElapsedTime( beforeTimestamp, afterTimestamp ) );
		}
		throw new ArgumentOutOfRangeException( nameof( interval ) );
	}
	/// <summary>Refreshes a provider at a fixed monotonic cadence.</summary>
	public async IAsyncEnumerable<ProcTimedSample<T>> RefreshAsync<T>(
		Func<CancellationToken, Task<T>> capture,
		TimeSpan interval,
		bool fireImmediately = true,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( capture );
		await foreach ( var tick in this._scheduler.ScheduleAsync( interval, fireImmediately, cancellationToken ).ConfigureAwait( false ) ) {
			var value = await capture( cancellationToken ).ConfigureAwait( false );
			yield return new ProcTimedSample<T>( this._clock.GetTimestamp(), value, tick );
		}
	}
}

/// <summary>Calculates reusable CPU percentages from two aggregate CPU snapshots.</summary>
public static class ProcCpuMath {
	/// <summary>Calculates the busy CPU percentage for a sampling window.</summary>
	public static double BusyPercent( ProcCpuTimes before, ProcCpuTimes after, int counterBitWidth = 64 ) {
		ArgumentNullException.ThrowIfNull( before );
		ArgumentNullException.ThrowIfNull( after );
		var total = ProcCounterMath.Delta( before.Total, after.Total, counterBitWidth );
		if ( 0 == total ) return 0d;
		var idleBefore = unchecked( before.Idle + before.IoWait );
		var idleAfter = unchecked( after.Idle + after.IoWait );
		var idle = ProcCounterMath.Delta( idleBefore, idleAfter, counterBitWidth );
		return 100d * ( total - Math.Min( total, idle ) ) / total;
	}
}

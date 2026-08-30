/*
	top
	Interactively display processes and system activity.
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

namespace Icod.ProcPs.Top;

using System.Globalization;
using Icod.Host;
using Icod.Processes;
using Icod.ProcPs.Shared;
using Icod.Timing;

/// <summary>Identifies the task field used to order top rows.</summary>
internal enum TopSortField {
	Cpu,
	Memory,
	Pid,
	Time,
	VirtualMemory,
	ResidentMemory,
	User,
	Command,
	Nice,
	State
}

/// <summary>Identifies a binary memory scale used by top.</summary>
internal enum TopMemoryScale {
	Kibibytes,
	Mebibytes,
	Gibibytes,
	Tebibytes,
	Pebibytes,
	Exbibytes
}

/// <summary>Describes a command-line or interactive user restriction.</summary>
internal sealed class TopUserFilter {
	internal TopUserFilter( uint userId, bool anyUser, bool negate ) {
		this.UserId = userId;
		this.AnyUser = anyUser;
		this.Negate = negate;
	}

	internal uint UserId { get; }
	internal bool AnyUser { get; }
	internal bool Negate { get; }
}

/// <summary>Contains runtime presentation state shared between refreshes.</summary>
internal sealed class TopRuntimeState {
	internal TimeSpan Delay { get; set; } = TimeSpan.FromSeconds( 3 );
	internal TopSortField SortField { get; set; } = TopSortField.Cpu;
	internal bool SortHighToLow { get; set; } = true;
	internal TopMemoryScale SummaryScale { get; set; } = TopMemoryScale.Mebibytes;
	internal TopMemoryScale TaskScale { get; set; } = TopMemoryScale.Kibibytes;
	internal bool ShowCommandLine { get; set; }
	internal bool ShowThreads { get; set; }
	internal bool HideIdle { get; set; }
	internal bool Forest { get; set; }
	internal bool IrixMode { get; set; } = true;
	internal bool SecureMode { get; set; }
	internal bool SingleCpuSummary { get; set; } = true;
	internal int VerticalOffset { get; set; }
	internal int HorizontalOffset { get; set; }
	internal string? Message { get; set; }
	internal bool ShowHelp { get; set; }
	internal TopPromptState? Prompt { get; set; }
	internal HashSet<int> ProcessIds { get; } = [];
	internal TopUserFilter? UserFilter { get; set; }
}

/// <summary>Identifies one interactive top prompt.</summary>
internal enum TopPromptKind {
	Delay,
	KillProcessId,
	KillSignal,
	ReniceProcessId,
	ReniceValue,
	EffectiveUser,
	AnyUser
}

/// <summary>Contains one line-editing prompt and its partial input.</summary>
internal sealed class TopPromptState {
	internal TopPromptState( TopPromptKind kind, string label ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( label );
		this.Kind = kind;
		this.Label = label;
	}

	internal TopPromptKind Kind { get; set; }
	internal string Label { get; set; }
	internal string Buffer { get; set; } = string.Empty;
	internal int? ProcessId { get; set; }
}

/// <summary>Contains one rendered task row and its source process observation.</summary>
internal sealed class TopTaskRow {
	internal TopTaskRow(
		ProcProcessSnapshot process,
		int threadGroupId,
		string user,
		double cpuPercentIrix,
		double memoryPercent,
		double? cpuSeconds
	) {
		ArgumentNullException.ThrowIfNull( process );
		ArgumentNullException.ThrowIfNull( user );
		this.Process = process;
		this.ThreadGroupId = threadGroupId;
		this.User = user;
		this.CpuPercentIrix = Math.Max( 0.0, cpuPercentIrix );
		this.MemoryPercent = Math.Max( 0.0, memoryPercent );
		this.CpuSeconds = cpuSeconds;
	}

	internal ProcProcessSnapshot Process { get; }
	internal int ThreadGroupId { get; }
	internal string User { get; }
	internal double CpuPercentIrix { get; }
	internal double MemoryPercent { get; }
	internal double? CpuSeconds { get; }
	internal int ForestDepth { get; set; }
}

/// <summary>Contains aggregate CPU percentages for one top refresh.</summary>
internal sealed record TopCpuSummary(
	bool LinuxDetailed,
	double User,
	double System,
	double Nice,
	double Idle,
	double Wait,
	double Irq,
	double SoftIrq,
	double Steal,
	double Other
);

/// <summary>Contains one complete observation consumed by top rendering.</summary>
internal sealed class TopSample {
	internal TopSample(
		ProcSystemSnapshot system,
		IReadOnlyList<TopTaskRow> tasks,
		TopCpuSummary cpuSummary,
		int processorCount,
		DateTimeOffset observedAt
	) {
		ArgumentNullException.ThrowIfNull( system );
		ArgumentNullException.ThrowIfNull( tasks );
		ArgumentNullException.ThrowIfNull( cpuSummary );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( processorCount );
		this.System = system;
		this.Tasks = tasks;
		this.CpuSummary = cpuSummary;
		this.ProcessorCount = processorCount;
		this.ObservedAt = observedAt;
	}

	internal ProcSystemSnapshot System { get; }
	internal IReadOnlyList<TopTaskRow> Tasks { get; }
	internal TopCpuSummary CpuSummary { get; }
	internal int ProcessorCount { get; }
	internal DateTimeOffset ObservedAt { get; }
}

/// <summary>Captures and interval-normalizes the process and system observations consumed by top.</summary>
internal sealed class TopSampler {
	private readonly IProcProcessProvider processProvider;
	private readonly IProcSystemMetricsProvider metricsProvider;
	private readonly IProcMatchSupplementProvider supplementProvider;
	private readonly IProcAccountDisplayResolver accountResolver;
	private readonly IProcessorResourceProvider processorProvider;
	private readonly IMonotonicClock clock;
	private readonly Func<DateTimeOffset> wallClock;
	private Dictionary<ProcessIdentity, ulong> previousProcessCpu = [];
	private ProcCpuTimes? previousLinuxCpu;
	private ProcCpuActivity? previousCpuActivity;
	private long? previousTimestamp;
	private int processorCount = 1;
	private bool processorCountObserved;

	internal TopSampler(
		IProcProcessProvider processProvider,
		IProcSystemMetricsProvider metricsProvider,
		IProcMatchSupplementProvider supplementProvider,
		IProcAccountDisplayResolver accountResolver,
		IProcessorResourceProvider processorProvider,
		IMonotonicClock clock,
		Func<DateTimeOffset> wallClock
	) {
		ArgumentNullException.ThrowIfNull( processProvider );
		ArgumentNullException.ThrowIfNull( metricsProvider );
		ArgumentNullException.ThrowIfNull( supplementProvider );
		ArgumentNullException.ThrowIfNull( accountResolver );
		ArgumentNullException.ThrowIfNull( processorProvider );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( wallClock );
		this.processProvider = processProvider;
		this.metricsProvider = metricsProvider;
		this.supplementProvider = supplementProvider;
		this.accountResolver = accountResolver;
		this.processorProvider = processorProvider;
		this.clock = clock;
		this.wallClock = wallClock;
	}

	internal async Task<TopSample> CaptureAsync(
		bool showThreads,
		CancellationToken cancellationToken
	) {
		if ( !this.processorCountObserved ) {
			this.processorCount = await ObserveProcessorCountAsync( cancellationToken ).ConfigureAwait( false );
			this.processorCountObserved = true;
		}

		long timestamp = this.clock.GetTimestamp();
		ProcSystemSnapshot system = await this.metricsProvider.GetSnapshotAsync(
			cancellationToken
		).ConfigureAwait( false );
		ProcProcessCollection collection = await this.processProvider.GetProcessesAsync(
			cancellationToken
		).ConfigureAwait( false );

		IReadOnlyList<(ProcProcessSnapshot Process, int ThreadGroupId)> processes;
		if ( showThreads && OperatingSystem.IsLinux() ) {
			IReadOnlyList<ProcMatchCandidate> candidates = await this.supplementProvider
				.GetCandidatesAsync(
					collection.Processes,
					true,
					cancellationToken
				).ConfigureAwait( false );
			processes = candidates
				.Select( candidate => ( candidate.Process, candidate.Supplement.ThreadGroupId ) )
				.ToArray();
		} else {
			processes = collection.Processes
				.Select( process => ( process, process.ProcessId ) )
				.ToArray();
		}

		TimeSpan elapsed = this.previousTimestamp.HasValue
			? this.clock.GetElapsedTime( this.previousTimestamp.Value, timestamp )
			: TimeSpan.Zero;
		TopCpuSummary cpuSummary = BuildCpuSummary( system );
		ulong systemDelta = BuildSystemCpuDelta( system );
		var currentProcessCpu = new Dictionary<ProcessIdentity, ulong>();
		var rows = new List<TopTaskRow>( processes.Count );
		foreach ( var item in processes ) {
			cancellationToken.ThrowIfCancellationRequested();
			ProcProcessSnapshot process = item.Process;
			ulong currentCpu = TotalProcessCpu( process );
			currentProcessCpu[ process.Identity ] = currentCpu;
			ulong processDelta = this.previousProcessCpu.TryGetValue(
				process.Identity,
				out ulong previousCpu
			) ? CounterDelta( previousCpu, currentCpu, 64 ) : 0UL;
			double cpuPercent = CalculateProcessCpuPercent(
				process,
				processDelta,
				systemDelta,
				elapsed,
				this.processorCount
			);
			double memoryPercent = CalculateMemoryPercent( process, system );
			string user = FormatUser( process );
			double? cpuSeconds = CalculateCpuSeconds( process, system, this.processorCount );
			rows.Add( new TopTaskRow(
				process,
				item.ThreadGroupId,
				user,
				cpuPercent,
				memoryPercent,
				cpuSeconds
			) );
		}

		this.previousProcessCpu = currentProcessCpu;
		this.previousTimestamp = timestamp;
		this.previousLinuxCpu = system.Cpu.HasValue ? system.Cpu.Value : null;
		this.previousCpuActivity = system.CpuActivity.HasValue ? system.CpuActivity.Value : null;
		return new TopSample(
			system,
			rows,
			cpuSummary,
			this.processorCount,
			this.wallClock()
		);
	}

	private async Task<int> ObserveProcessorCountAsync( CancellationToken cancellationToken ) {
		try {
			ProcessorResourceSnapshot resources = await this.processorProvider
				.GetProcessorResourcesAsync( cancellationToken )
				.ConfigureAwait( false );
			if ( resources.ProcessAvailableProcessorCount.IsAvailable ) {
				return Math.Max(
					1,
					resources.ProcessAvailableProcessorCount.GetRequiredValue()
				);
			}
		} catch ( InvalidOperationException ) {
		}
		return Math.Max( 1, Environment.ProcessorCount );
	}

	private TopCpuSummary BuildCpuSummary( ProcSystemSnapshot system ) {
		if ( system.Cpu.HasValue ) {
			ProcCpuTimes current = system.Cpu.Value;
			ProcCpuTimes? previous = this.previousLinuxCpu;
			ulong user = DeltaOrCurrent( previous?.User, current.User );
			ulong nice = DeltaOrCurrent( previous?.Nice, current.Nice );
			ulong kernel = DeltaOrCurrent( previous?.System, current.System );
			ulong idle = DeltaOrCurrent( previous?.Idle, current.Idle );
			ulong wait = DeltaOrCurrent( previous?.IoWait, current.IoWait );
			ulong irq = DeltaOrCurrent( previous?.Irq, current.Irq );
			ulong soft = DeltaOrCurrent( previous?.SoftIrq, current.SoftIrq );
			ulong steal = DeltaOrCurrent( previous?.Steal, current.Steal );
			double total = Math.Max(
				1.0,
				(double)user + nice + kernel + idle + wait + irq + soft + steal
			);
			return new TopCpuSummary(
				true,
				Percent( user, total ),
				Percent( kernel, total ),
				Percent( nice, total ),
				Percent( idle, total ),
				Percent( wait, total ),
				Percent( irq, total ),
				Percent( soft, total ),
				Percent( steal, total ),
				0.0
			);
		}
		if ( system.CpuActivity.HasValue ) {
			ProcCpuActivity current = system.CpuActivity.Value;
			ProcCpuActivity? previous = this.previousCpuActivity;
			ulong user = DeltaOrCurrent( previous?.User, current.User, current.CounterBitWidth );
			ulong kernel = DeltaOrCurrent( previous?.System, current.System, current.CounterBitWidth );
			ulong idle = DeltaOrCurrent( previous?.Idle, current.Idle, current.CounterBitWidth );
			ulong nice = DeltaOrCurrent( previous?.Nice, current.Nice, current.CounterBitWidth );
			ulong wait = DeltaOrCurrent( previous?.Wait, current.Wait, current.CounterBitWidth );
			ulong other = DeltaOrCurrent( previous?.Other, current.Other, current.CounterBitWidth );
			double total = Math.Max(
				1.0,
				(double)user + kernel + idle + nice + wait + other
			);
			return new TopCpuSummary(
				false,
				Percent( user, total ),
				Percent( kernel, total ),
				Percent( nice, total ),
				Percent( idle, total ),
				Percent( wait, total ),
				0.0,
				0.0,
				0.0,
				Percent( other, total )
			);
		}
		return new TopCpuSummary(
			false,
			0.0,
			0.0,
			0.0,
			0.0,
			0.0,
			0.0,
			0.0,
			0.0,
			0.0
		);
	}

	private ulong BuildSystemCpuDelta( ProcSystemSnapshot system ) {
		if ( system.Cpu.HasValue ) {
			return this.previousLinuxCpu is null
				? 0UL
				: CounterDelta( this.previousLinuxCpu.Total, system.Cpu.Value.Total, 64 );
		}
		if ( system.CpuActivity.HasValue ) {
			return this.previousCpuActivity is null
				? 0UL
				: CounterDelta(
					this.previousCpuActivity.Total,
					system.CpuActivity.Value.Total,
					system.CpuActivity.Value.CounterBitWidth
				);
		}
		return 0UL;
	}

	private string FormatUser( ProcProcessSnapshot process ) {
		if ( !process.EffectiveUserId.HasValue ) {
			return "?";
		}
		uint id = process.EffectiveUserId.Value;
		return this.accountResolver.TryGetUserName( id, out string name )
			? name
			: id.ToString( CultureInfo.InvariantCulture );
	}

	private static double CalculateMemoryPercent(
		ProcProcessSnapshot process,
		ProcSystemSnapshot system
	) {
		if ( !process.ResidentMemoryBytes.HasValue
			|| !system.Memory.HasValue
			|| !system.Memory.Value.TotalBytes.HasValue
			|| 0UL == system.Memory.Value.TotalBytes.Value ) {
			return 0.0;
		}
		return 100.0
			* process.ResidentMemoryBytes.Value
			/ system.Memory.Value.TotalBytes.Value;
	}

	private static double CalculateProcessCpuPercent(
		ProcProcessSnapshot process,
		ulong processDelta,
		ulong systemDelta,
		TimeSpan elapsed,
		int processorCount
	) {
		if ( 0UL == processDelta || TimeSpan.Zero >= elapsed ) {
			return 0.0;
		}
		if ( process.UserCpuTicks.Source == ProcObservationSource.DotNetProcessApi ) {
			double seconds = processDelta / (double)TimeSpan.TicksPerSecond;
			return 100.0 * seconds / elapsed.TotalSeconds;
		}
		if ( process.UserCpuTicks.Source == ProcObservationSource.DarwinLibProc ) {
			double seconds = processDelta / 1_000_000_000.0;
			return 100.0 * seconds / elapsed.TotalSeconds;
		}
		if ( 0UL < systemDelta ) {
			return 100.0 * processDelta * Math.Max( 1, processorCount ) / systemDelta;
		}
		return 0.0;
	}

	private static double? CalculateCpuSeconds(
		ProcProcessSnapshot process,
		ProcSystemSnapshot system,
		int processorCount
	) {
		if ( !process.UserCpuTicks.HasValue || !process.SystemCpuTicks.HasValue ) {
			return null;
		}
		ulong total = SaturatingAdd(
			process.UserCpuTicks.Value,
			process.SystemCpuTicks.Value
		);
		if ( process.UserCpuTicks.Source == ProcObservationSource.DotNetProcessApi ) {
			return total / (double)TimeSpan.TicksPerSecond;
		}
		if ( process.UserCpuTicks.Source == ProcObservationSource.DarwinLibProc ) {
			return total / 1_000_000_000.0;
		}
		if ( process.UserCpuTicks.Source == ProcObservationSource.LinuxProcfs
			&& system.Cpu.HasValue
			&& system.Uptime.HasValue
			&& 0.0 < system.Uptime.Value.Uptime.TotalSeconds ) {
			double hertz = system.Cpu.Value.Total
				/ system.Uptime.Value.Uptime.TotalSeconds
				/ Math.Max( 1, processorCount );
			if ( 0.0 < hertz ) {
				return total / Math.Max( 1.0, Math.Round( hertz ) );
			}
		}
		return null;
	}

	private static ulong TotalProcessCpu( ProcProcessSnapshot process ) {
		if ( !process.UserCpuTicks.HasValue || !process.SystemCpuTicks.HasValue ) {
			return 0UL;
		}
		return SaturatingAdd(
			process.UserCpuTicks.Value,
			process.SystemCpuTicks.Value
		);
	}

	private static ulong DeltaOrCurrent(
		ulong? previous,
		ulong current,
		int width = 64
	) => previous.HasValue
		? CounterDelta( previous.Value, current, width )
		: current;

	private static ulong DeltaOrCurrent(
		ulong? previous,
		ulong? current,
		int width
	) {
		if ( !current.HasValue ) {
			return 0UL;
		}
		return previous.HasValue
			? CounterDelta( previous.Value, current.Value, width )
			: current.Value;
	}

	private static ulong CounterDelta( ulong previous, ulong current, int width ) {
		if ( current >= previous ) {
			return current - previous;
		}
		if ( 64 == width ) {
			return unchecked( ulong.MaxValue - previous + current + 1UL );
		}
		ulong modulus = 1UL << width;
		return modulus - previous + current;
	}

	private static double Percent( ulong value, double total ) => 100.0 * value / total;

	private static ulong SaturatingAdd( ulong left, ulong right ) =>
		ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

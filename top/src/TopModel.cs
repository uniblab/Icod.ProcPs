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

/// <summary>Identifies a binary memory scale used by top.</summary>
internal enum TopMemoryScale {
	Kibibytes,
	Mebibytes,
	Gibibytes,
	Tebibytes,
	Pebibytes,
	Exbibytes
}

/// <summary>Identifies the native procps summary graph selector retained for a summary area.</summary>
internal enum TopSummaryGraphMode {
	Detailed,
	Bar,
	Block
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

/// <summary>Contains the separately configurable state of one top field group/window.</summary>
internal sealed class TopWindowState {
	internal TopWindowState( string name ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		this.Name = name;
	}

	internal string Name { get; private set; }
	internal bool TaskDisplayVisible { get; set; } = true;
	internal TopFieldId SortField { get; set; } = TopFieldId.Cpu;
	internal bool SortHighToLow { get; set; } = true;
	internal bool HighlightBold { get; set; } = true;
	internal bool HighlightRunning { get; set; } = true;
	internal bool HighlightSortColumn { get; set; }
	internal bool ColorsEnabled { get; set; } = true;
	internal TopColorPalette Colors { get; set; } = TopColorPalette.ForWindow( 0 );
	internal bool NumericLeftJustified { get; set; }
	internal bool CharacterRightJustified { get; set; }
	internal int MaximumTasks { get; set; }
	internal string? SearchText { get; set; }
	internal bool ShowCommandLine { get; set; }
	internal bool HideIdle { get; set; }
	internal bool Forest { get; set; }
	internal bool LoadAverageVisible { get; set; } = true;
	internal bool ScrollCoordinatesVisible { get; set; }
	internal bool SingleCpuSummary { get; set; } = true;
	internal bool CpuSummaryVisible { get; set; } = true;
	internal TopSummaryGraphMode CpuSummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	internal bool MemorySummaryVisible { get; set; } = true;
	internal TopSummaryGraphMode MemorySummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	internal int VerticalOffset { get; set; }
	internal int HorizontalOffset { get; set; }
	internal TopUserFilter? UserFilter { get; set; }
	internal List<TopOtherFilter> OtherFilters { get; } = [];
	internal List<TopFieldId> FieldOrder { get; } = TopFieldCatalog.CreateDefaultOrder();
	internal HashSet<TopFieldId> VisibleFields { get; } = TopFieldCatalog.CreateDefaultVisible();

	internal TopWindowState Clone() {
		var result = new TopWindowState(
			this.Name
		);
		result.CopyFrom(
			this
		);
		return result;
	}

	internal void Rename(
		string name
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		this.Name = name;
	}

	internal void CaptureFrom(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		this.TaskDisplayVisible = state.TaskDisplayVisible;
		this.SortField = state.SortField;
		this.SortHighToLow = state.SortHighToLow;
		this.HighlightBold = state.HighlightBold;
		this.HighlightRunning = state.HighlightRunning;
		this.ColorsEnabled = state.ColorsEnabled;
		this.Colors = state.Colors;
		this.HighlightSortColumn = state.HighlightSortColumn;
		this.NumericLeftJustified = state.NumericLeftJustified;
		this.CharacterRightJustified = state.CharacterRightJustified;
		this.MaximumTasks = state.MaximumTasks;
		this.SearchText = state.SearchText;
		this.ShowCommandLine = state.ShowCommandLine;
		this.HideIdle = state.HideIdle;
		this.Forest = state.Forest;
		this.LoadAverageVisible = state.LoadAverageVisible;
		this.ScrollCoordinatesVisible = state.ScrollCoordinatesVisible;
		this.SingleCpuSummary = state.SingleCpuSummary;
		this.CpuSummaryVisible = state.CpuSummaryVisible;
		this.CpuSummaryGraphMode = state.CpuSummaryGraphMode;
		this.MemorySummaryVisible = state.MemorySummaryVisible;
		this.MemorySummaryGraphMode = state.MemorySummaryGraphMode;
		this.VerticalOffset = state.VerticalOffset;
		this.HorizontalOffset = state.HorizontalOffset;
		this.UserFilter = state.UserFilter;

		this.OtherFilters.Clear();
		this.OtherFilters.AddRange(
			state.OtherFilters
		);
		this.FieldOrder.Clear();
		this.FieldOrder.AddRange(
			state.FieldOrder
		);
		this.VisibleFields.Clear();
		this.VisibleFields.UnionWith(
			state.VisibleFields
		);
	}

	internal void ApplyTo(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		state.TaskDisplayVisible = this.TaskDisplayVisible;
		state.SortField = this.SortField;
		state.SortHighToLow = this.SortHighToLow;
		state.HighlightBold = this.HighlightBold;
		state.HighlightRunning = this.HighlightRunning;
		state.ColorsEnabled = this.ColorsEnabled;
		state.Colors = this.Colors;
		state.HighlightSortColumn = this.HighlightSortColumn;
		state.NumericLeftJustified = this.NumericLeftJustified;
		state.CharacterRightJustified = this.CharacterRightJustified;
		state.MaximumTasks = this.MaximumTasks;
		state.SearchText = this.SearchText;
		state.ShowCommandLine = this.ShowCommandLine;
		state.HideIdle = this.HideIdle;
		state.Forest = this.Forest;
		state.LoadAverageVisible = this.LoadAverageVisible;
		state.ScrollCoordinatesVisible = this.ScrollCoordinatesVisible;
		state.SingleCpuSummary = this.SingleCpuSummary;
		state.CpuSummaryVisible = this.CpuSummaryVisible;
		state.CpuSummaryGraphMode = this.CpuSummaryGraphMode;
		state.MemorySummaryVisible = this.MemorySummaryVisible;
		state.MemorySummaryGraphMode = this.MemorySummaryGraphMode;
		state.VerticalOffset = this.VerticalOffset;
		state.HorizontalOffset = this.HorizontalOffset;
		state.UserFilter = this.UserFilter;

		state.OtherFilters.Clear();
		state.OtherFilters.AddRange(
			this.OtherFilters
		);
		state.FieldOrder.Clear();
		state.FieldOrder.AddRange(
			this.FieldOrder
		);
		state.VisibleFields.Clear();
		state.VisibleFields.UnionWith(
			this.VisibleFields
		);
	}

	private void CopyFrom(
		TopWindowState source
	) {
		ArgumentNullException.ThrowIfNull( source );

		this.TaskDisplayVisible = source.TaskDisplayVisible;
		this.SortField = source.SortField;
		this.SortHighToLow = source.SortHighToLow;
		this.HighlightBold = source.HighlightBold;
		this.HighlightRunning = source.HighlightRunning;
		this.ColorsEnabled = source.ColorsEnabled;
		this.Colors = source.Colors;
		this.HighlightSortColumn = source.HighlightSortColumn;
		this.NumericLeftJustified = source.NumericLeftJustified;
		this.CharacterRightJustified = source.CharacterRightJustified;
		this.MaximumTasks = source.MaximumTasks;
		this.SearchText = source.SearchText;
		this.ShowCommandLine = source.ShowCommandLine;
		this.HideIdle = source.HideIdle;
		this.Forest = source.Forest;
		this.LoadAverageVisible = source.LoadAverageVisible;
		this.ScrollCoordinatesVisible = source.ScrollCoordinatesVisible;
		this.SingleCpuSummary = source.SingleCpuSummary;
		this.CpuSummaryVisible = source.CpuSummaryVisible;
		this.CpuSummaryGraphMode = source.CpuSummaryGraphMode;
		this.MemorySummaryVisible = source.MemorySummaryVisible;
		this.MemorySummaryGraphMode = source.MemorySummaryGraphMode;
		this.VerticalOffset = source.VerticalOffset;
		this.HorizontalOffset = source.HorizontalOffset;
		this.UserFilter = source.UserFilter;

		this.OtherFilters.Clear();
		this.OtherFilters.AddRange(
			source.OtherFilters
		);
		this.FieldOrder.Clear();
		this.FieldOrder.AddRange(
			source.FieldOrder
		);
		this.VisibleFields.Clear();
		this.VisibleFields.UnionWith(
			source.VisibleFields
		);
	}
}

/// <summary>Contains runtime presentation state shared between refreshes.</summary>
internal sealed class TopRuntimeState {
	internal const int WindowCount = 4;
	private static readonly string[] WindowNames = [
		"Def",
		"Job",
		"Mem",
		"Usr"
	];
	private TopWindowState[] windows = CreateDefaultWindows();

	internal TimeSpan Delay { get; set; } = TimeSpan.FromSeconds( 3 );
	internal TopFieldId SortField { get; set; } = TopFieldId.Cpu;
	internal bool SortHighToLow { get; set; } = true;
	internal bool BoldEnabled { get; set; } = true;
	internal bool HighlightBold { get; set; } = true;
	internal bool HighlightRunning { get; set; } = true;
	internal bool ColorsEnabled { get; set; } = true;
	internal TopColorPalette Colors { get; set; } = TopColorPalette.ForWindow( 0 );
	internal bool HighlightSortColumn { get; set; }
	internal bool NumericLeftJustified { get; set; }
	internal bool CharacterRightJustified { get; set; }
	internal bool SuppressZeros { get; set; }
	internal int MaximumTasks { get; set; }
	internal string? SearchText { get; set; }
	internal TopMemoryScale SummaryScale { get; set; } = TopMemoryScale.Mebibytes;
	internal TopMemoryScale TaskScale { get; set; } = TopMemoryScale.Kibibytes;
	internal bool ShowCommandLine { get; set; }
	internal bool ShowThreads { get; set; }
	internal bool HideIdle { get; set; }
	internal bool Forest { get; set; }
	internal bool IrixMode { get; set; } = true;
	internal bool SecureMode { get; set; }
	internal bool LoadAverageVisible { get; set; } = true;
	internal bool ScrollCoordinatesVisible { get; set; }
	internal bool SingleCpuSummary { get; set; } = true;
	internal bool CpuSummaryVisible { get; set; } = true;
	internal TopSummaryGraphMode CpuSummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	internal bool MemorySummaryVisible { get; set; } = true;
	internal TopSummaryGraphMode MemorySummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	internal bool AlternateDisplayMode { get; set; }
	internal bool TaskDisplayVisible { get; set; } = true;
	internal int CurrentWindowIndex { get; private set; }
	internal int VerticalOffset { get; set; }
	internal int HorizontalOffset { get; set; }
	internal string? Message { get; set; }
	internal bool ShowHelp { get; set; }
	internal TopColorManagerState? ColorManager { get; set; }
	internal bool ShowFieldManager { get; set; }
	internal int FieldCursor { get; set; }
	internal bool FieldMoveActive { get; set; }
	internal TopPromptState? Prompt { get; set; }
	internal HashSet<int> ProcessIds { get; } = [];
	internal TopUserFilter? UserFilter { get; set; }
	internal List<TopOtherFilter> OtherFilters { get; } = [];
	internal List<TopFieldId> FieldOrder { get; } = TopFieldCatalog.CreateDefaultOrder();
	internal HashSet<TopFieldId> VisibleFields { get; } = TopFieldCatalog.CreateDefaultVisible();
	internal IReadOnlyList<TopWindowState> Windows => this.windows;
	internal string CurrentWindowLabel => $"{this.CurrentWindowIndex + 1}:{this.windows[ this.CurrentWindowIndex ].Name}";

	internal static string GetWindowName(
		int index
	) {
		if ( index is < 0 or >= WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( index )
			);
		}
		return WindowNames[ index ];
	}

	internal void SynchronizeCurrentWindow() {
		this.windows[
			this.CurrentWindowIndex
		].CaptureFrom(
			this
		);
	}

	internal void RenameCurrentWindow(
		string name
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		this.windows[
			this.CurrentWindowIndex
		].Rename(
			name
		);
	}

	internal bool MoveSortField(
		int direction
	) {
		if ( direction is not -1 and not 1 ) {
			throw new ArgumentOutOfRangeException(
				nameof( direction )
			);
		}
		if ( !this.VisibleFields.Contains( this.SortField ) ) {
			return false;
		}

		int sourceIndex = this.FieldOrder.IndexOf(
			this.SortField
		);
		if ( 0 > sourceIndex ) {
			return false;
		}
		int targetIndex = sourceIndex + direction;
		while (
			targetIndex is >= 0 and < this.FieldOrder.Count
			&& !this.VisibleFields.Contains(
				this.FieldOrder[ targetIndex ]
			)
		) {
			targetIndex += direction;
		}
		if ( targetIndex is < 0 || targetIndex >= this.FieldOrder.Count ) {
			return false;
		}

		TopFieldId target = this.FieldOrder[ targetIndex ];
		this.FieldOrder[ targetIndex ] = this.SortField;
		this.FieldOrder[ sourceIndex ] = target;
		this.HorizontalOffset = 0;
		this.SynchronizeCurrentWindow();
		return true;
	}

	internal void ToggleAllTaskDisplays() {
		this.SynchronizeCurrentWindow();
		foreach ( TopWindowState window in this.windows ) {
			window.TaskDisplayVisible = !window.TaskDisplayVisible;
		}
		this.windows[
			this.CurrentWindowIndex
		].ApplyTo(
			this
		);
	}

	internal void ShowAllTaskDisplays() {
		this.SynchronizeCurrentWindow();
		foreach ( TopWindowState window in this.windows ) {
			window.TaskDisplayVisible = true;
		}
		this.windows[
			this.CurrentWindowIndex
		].ApplyTo(
			this
		);
	}

	internal void ActivateWindow(
		int index
	) {
		if ( index is < 0 or >= WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( index )
			);
		}

		this.SynchronizeCurrentWindow();
		this.CurrentWindowIndex = index;
		this.windows[
			index
		].ApplyTo(
			this
		);
		this.FieldMoveActive = false;
	}

	internal void RestoreWindows(
		IReadOnlyList<TopWindowState> restoredWindows,
		int currentWindowIndex
	) {
		ArgumentNullException.ThrowIfNull( restoredWindows );
		if ( WindowCount != restoredWindows.Count ) {
			throw new ArgumentException(
				$"Exactly {WindowCount} top windows are required.",
				nameof( restoredWindows )
			);
		}
		if ( currentWindowIndex is < 0 or >= WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( currentWindowIndex )
			);
		}

		var replacement = new TopWindowState[
			WindowCount
		];
		for ( int index = 0; index < WindowCount; index++ ) {
			replacement[ index ] = restoredWindows[
				index
			].Clone();
		}
		this.windows = replacement;
		this.CurrentWindowIndex = currentWindowIndex;
		this.windows[
			currentWindowIndex
		].ApplyTo(
			this
		);
		this.FieldMoveActive = false;
	}

	internal void CycleCpuSummaryPresentation() {
		(bool visible, TopSummaryGraphMode mode) = NextSummaryPresentation(
			this.CpuSummaryVisible,
			this.CpuSummaryGraphMode
		);
		this.CpuSummaryVisible = visible;
		this.CpuSummaryGraphMode = mode;
	}

	internal void CycleMemorySummaryPresentation() {
		(bool visible, TopSummaryGraphMode mode) = NextSummaryPresentation(
			this.MemorySummaryVisible,
			this.MemorySummaryGraphMode
		);
		this.MemorySummaryVisible = visible;
		this.MemorySummaryGraphMode = mode;
	}

	private static (bool Visible, TopSummaryGraphMode Mode) NextSummaryPresentation(
		bool visible,
		TopSummaryGraphMode mode
	) {
		if ( !Enum.IsDefined( typeof( TopSummaryGraphMode ), mode ) ) {
			throw new InvalidOperationException(
				$"The top summary graph mode '{mode}' is not recognized."
			);
		}
		if ( !visible ) {
			return (
				true,
				mode
			);
		}

		return mode switch {
			TopSummaryGraphMode.Detailed => (
				true,
				TopSummaryGraphMode.Bar
			),
			TopSummaryGraphMode.Bar => (
				true,
				TopSummaryGraphMode.Block
			),
			TopSummaryGraphMode.Block => (
				false,
				TopSummaryGraphMode.Detailed
			),
			_ => throw new InvalidOperationException(
				$"The top summary graph mode '{mode}' is not recognized."
			)
		};
	}

	private static TopWindowState[] CreateDefaultWindows() {
		var result = new TopWindowState[
			WindowCount
		];
		for ( int index = 0; index < WindowCount; index++ ) {
			result[ index ] = new TopWindowState(
				WindowNames[ index ]
			) {
				Colors = TopColorPalette.ForWindow( index )
			};
		}
		return result;
	}
}

/// <summary>Identifies one interactive top prompt.</summary>
internal enum TopPromptKind {
	Delay,
	MaximumTasks,
	Window,
	WindowName,
	Locate,
	OtherFilterCaseSensitive,
	OtherFilterIgnoreCase,
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

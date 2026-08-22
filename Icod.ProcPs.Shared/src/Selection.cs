namespace Icod.ProcPs.Shared;

using Icod.CommandFramework.Processes;

/// <summary>Models ProcPs process-selection criteria with OR semantics inside a criterion and AND semantics between criteria.</summary>
public sealed class ProcProcessSelection {
	/// <summary>Gets or sets whether the final selection result is inverted.</summary>
	public bool Invert { get; init; }
	/// <summary>Gets selected process identifiers.</summary>
	public IReadOnlySet<int> ProcessIds { get; init; } = new HashSet<int>();
	/// <summary>Gets selected parent process identifiers.</summary>
	public IReadOnlySet<int> ParentProcessIds { get; init; } = new HashSet<int>();
	/// <summary>Gets selected process-group identifiers.</summary>
	public IReadOnlySet<int> ProcessGroupIds { get; init; } = new HashSet<int>();
	/// <summary>Gets selected session identifiers.</summary>
	public IReadOnlySet<int> SessionIds { get; init; } = new HashSet<int>();
	/// <summary>Gets selected real user identifiers.</summary>
	public IReadOnlySet<uint> RealUserIds { get; init; } = new HashSet<uint>();
	/// <summary>Gets selected effective user identifiers.</summary>
	public IReadOnlySet<uint> EffectiveUserIds { get; init; } = new HashSet<uint>();
	/// <summary>Gets selected real group identifiers.</summary>
	public IReadOnlySet<uint> RealGroupIds { get; init; } = new HashSet<uint>();
	/// <summary>Gets selected effective group identifiers.</summary>
	public IReadOnlySet<uint> EffectiveGroupIds { get; init; } = new HashSet<uint>();
	/// <summary>Gets selected terminal names.</summary>
	public IReadOnlySet<string> TerminalNames { get; init; } = new HashSet<string>( StringComparer.Ordinal );
	/// <summary>Gets selected task states.</summary>
	public IReadOnlySet<ProcProcessState> States { get; init; } = new HashSet<ProcProcessState>();
	/// <summary>Gets an optional predicate over the short command name.</summary>
	public Func<string, bool>? CommandNamePredicate { get; init; }
	/// <summary>Gets an optional predicate over the reconstructed full command line.</summary>
	public Func<string, bool>? CommandLinePredicate { get; init; }
}

/// <summary>Applies ProcPs selection semantics to process snapshots.</summary>
public static class ProcProcessSelectionEngine {
	/// <summary>Determines whether a snapshot matches the supplied selection.</summary>
	public static bool Matches( ProcProcessSnapshot process, ProcProcessSelection selection ) {
		ArgumentNullException.ThrowIfNull( process );
		ArgumentNullException.ThrowIfNull( selection );
		var matched = MatchesSet( selection.ProcessIds, process.ProcessId )
			&& MatchesObservedSet( selection.ParentProcessIds, process.ParentProcessId )
			&& MatchesObservedSet( selection.ProcessGroupIds, process.ProcessGroupId )
			&& MatchesObservedSet( selection.SessionIds, process.SessionId )
			&& MatchesObservedSet( selection.RealUserIds, process.RealUserId )
			&& MatchesObservedSet( selection.EffectiveUserIds, process.EffectiveUserId )
			&& MatchesObservedSet( selection.RealGroupIds, process.RealGroupId )
			&& MatchesObservedSet( selection.EffectiveGroupIds, process.EffectiveGroupId )
			&& MatchesTerminal( selection.TerminalNames, process.Terminal )
			&& MatchesObservedSet( selection.States, process.State )
			&& MatchesCommandName( selection.CommandNamePredicate, process.CommandName )
			&& MatchesCommandLine( selection.CommandLinePredicate, process.CommandLineArguments );
		return selection.Invert ? !matched : matched;
	}

	/// <summary>Selects matching processes while preserving input order.</summary>
	public static IReadOnlyList<ProcProcessSnapshot> Select( IEnumerable<ProcProcessSnapshot> processes, ProcProcessSelection selection ) {
		ArgumentNullException.ThrowIfNull( processes );
		ArgumentNullException.ThrowIfNull( selection );
		return processes.Where( process => Matches( process, selection ) ).ToArray();
	}

	private static bool MatchesSet<T>( IReadOnlySet<T> values, T actual ) => 0 == values.Count || values.Contains( actual );
	private static bool MatchesObservedSet<T>( IReadOnlySet<T> values, ProcObservedValue<T> actual ) => 0 == values.Count || ( actual.HasValue && values.Contains( actual.Value ) );
	private static bool MatchesTerminal( IReadOnlySet<string> terminals, ProcObservedValue<ProcTerminalInfo> terminal ) {
		if ( 0 == terminals.Count ) return true;
		if ( !terminal.HasValue ) return false;
		var value = terminal.Value.Name ?? terminal.Value.DeviceNumber.ToString( System.Globalization.CultureInfo.InvariantCulture );
		return terminals.Contains( value );
	}
	private static bool MatchesCommandName( Func<string, bool>? predicate, ProcObservedValue<string> name ) => null == predicate || ( name.HasValue && predicate( name.Value ) );
	private static bool MatchesCommandLine( Func<string, bool>? predicate, ProcObservedValue<IReadOnlyList<string>> arguments ) => null == predicate || ( arguments.HasValue && predicate( string.Join( " ", arguments.Value ) ) );
}

/// <summary>Adapts ProcPs process snapshots to the shared signal, wait, and priority providers.</summary>
public sealed class ProcProcessControlAdapter {
	private readonly IProcessInspector _inspector;
	private readonly IProcessSignalProvider _signals;
	private readonly IProcessPrioritySelectorProvider _priorities;
	/// <summary>Initializes a ProcPs process-control adapter over cross-suite providers.</summary>
	public ProcProcessControlAdapter( IProcessInspector inspector, IProcessSignalProvider signals, IProcessPrioritySelectorProvider priorities ) {
		ArgumentNullException.ThrowIfNull( inspector );
		ArgumentNullException.ThrowIfNull( signals );
		ArgumentNullException.ThrowIfNull( priorities );
		this._inspector = inspector;
		this._signals = signals;
		this._priorities = priorities;
	}
	/// <summary>Creates an adapter over the shared system providers.</summary>
	public static ProcProcessControlAdapter CreateSystem() => new( SystemProcessInspector.Instance, SystemProcessSignalProvider.Instance, SystemProcessPrioritySelectorProvider.Instance );
	/// <summary>Delivers a signal, optionally with a queued integer value, to one reuse-protected process identity.</summary>
	public Task<ProcessOperationResult> SignalAsync( ProcProcessSnapshot process, ProcessSignal signal, int? queuedValue = null, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( process );
		ArgumentNullException.ThrowIfNull( signal );
		return this._signals.DeliverAsync( ProcessTarget.ForProcess( process.Identity ), signal, queuedValue, cancellationToken );
	}
	/// <summary>Waits for one arbitrary reuse-protected process to terminate.</summary>
	public Task<ProcessOperationResult<ProcessTermination>> WaitAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( process );
		return this._inspector.WaitAsync( process.Identity, cancellationToken );
	}
	/// <summary>Reads one process's portable priority value.</summary>
	public ProcessOperationResult<ProcessPriorityValue> GetPriority( ProcProcessSnapshot process ) {
		ArgumentNullException.ThrowIfNull( process );
		return this._priorities.GetPriority( ProcessPriorityTarget.ForProcess( process.Identity ) );
	}
	/// <summary>Changes one process's portable priority value.</summary>
	public ProcessOperationResult SetPriority( ProcProcessSnapshot process, int niceValue ) {
		ArgumentNullException.ThrowIfNull( process );
		return this._priorities.SetPriority( ProcessPriorityTarget.ForProcess( process.Identity ), niceValue );
	}
}

/// <summary>Parses reusable comma-separated ProcPs selection operands without imposing command-specific option policy.</summary>
public static class ProcSelectionGrammar {
	/// <summary>Parses a comma- or whitespace-separated list of nonnegative integer identifiers.</summary>
	public static IReadOnlySet<int> ParseIdentifiers( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var values = new HashSet<int>();
		foreach ( var token in SplitList( text ) ) {
			if ( !int.TryParse( token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value ) || 0 > value ) {
				throw new FormatException( $"Invalid process selection identifier '{token}'." );
			}
			values.Add( value );
		}
		return values;
	}

	/// <summary>Parses a comma- or whitespace-separated list of nonnegative user/group identifiers.</summary>
	public static IReadOnlySet<uint> ParseUnsignedIdentifiers( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var values = new HashSet<uint>();
		foreach ( var token in SplitList( text ) ) {
			if ( !uint.TryParse( token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value ) ) {
				throw new FormatException( $"Invalid user/group selection identifier '{token}'." );
			}
			values.Add( value );
		}
		return values;
	}

	/// <summary>Parses a comma- or whitespace-separated terminal-name list.</summary>
	public static IReadOnlySet<string> ParseTerminalNames( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		return new HashSet<string>( SplitList( text ), StringComparer.Ordinal );
	}

	/// <summary>Parses compact or separated Linux task-state letters.</summary>
	public static IReadOnlySet<ProcProcessState> ParseStates( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var states = new HashSet<ProcProcessState>();
		foreach ( var character in text.Where( character => ',' != character && !char.IsWhiteSpace( character ) ) ) {
			var state = LinuxProcParsers.MapProcessState( character );
			if ( ProcProcessState.Unknown == state ) throw new FormatException( $"Unknown process state '{character}'." );
			states.Add( state );
		}
		return states;
	}

	private static IEnumerable<string> SplitList( string text ) => text
		.Split( new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
}

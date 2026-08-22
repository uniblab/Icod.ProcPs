namespace Icod.ProcPs.Pkill.Tests;

using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;

internal static class TestSupport {
	internal static ProcProcessSnapshot Process(
		int pid,
		string name,
		ulong start,
		int parent = 1,
		int group = 1,
		int session = 1,
		uint uid = 1000,
		string[]? arguments = null
	) => new( new ProcessIdentity( pid, new ProcessReuseToken( "test", start.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) {
		CommandName = Value( name ),
		CommandLineArguments = Value<IReadOnlyList<string>>( arguments ?? [ name ] ),
		State = Value( ProcProcessState.Sleeping ),
		ParentProcessId = Value( parent ),
		ProcessGroupId = Value( group ),
		SessionId = Value( session ),
		RealUserId = Value( uid ),
		EffectiveUserId = Value( uid ),
		RealGroupId = Value( uid ),
		EffectiveGroupId = Value( uid ),
		Terminal = Value( new ProcTerminalInfo( 0, "/dev/pts/0" ) ),
		Namespaces = Value<IReadOnlyDictionary<string, ProcNamespaceInfo>>(
			new Dictionary<string, ProcNamespaceInfo>( StringComparer.Ordinal ) {
				[ "ipc" ] = new( "ipc", "ipc:[1]", 1 ),
				[ "mnt" ] = new( "mnt", "mnt:[2]", 2 ),
				[ "net" ] = new( "net", "net:[3]", 3 ),
				[ "pid" ] = new( "pid", "pid:[4]", 4 ),
				[ "user" ] = new( "user", "user:[5]", 5 ),
				[ "uts" ] = new( "uts", "uts:[6]", 6 )
			}
		),
		Container = Value( new ProcContainerInfo( "/test.slice" ) ),
		StartTimeTicks = Value( start ),
		LifetimeStable = Value( true )
	};
	internal static ProcObservedValue<T> Value<T>( T value ) => ProcObservedValue<T>.Available(
		value,
		ProcObservationSource.Configuration,
		ObservationFidelity.Exact
	);
	internal static string Text( MemoryStream stream ) => System.Text.Encoding.UTF8.GetString( stream.ToArray() );
}

internal sealed class FakeProcessProvider : IProcProcessProvider {
	private readonly IReadOnlyList<ProcProcessSnapshot> processes;
	internal FakeProcessProvider( params ProcProcessSnapshot[] processes ) => this.processes = processes;
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration | ProcProcessCapabilities.Identity;
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default )
		=> Task.FromResult( new ProcProcessCollection( this.processes ) );
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
		var process = this.processes.FirstOrDefault( candidate => candidate.ProcessId == processId );
		return Task.FromResult(
			null == process
				? ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished )
				: TestSupport.Value( process )
		);
	}
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
}

internal sealed class FakeSupplementProvider : IProcMatchSupplementProvider {
	private readonly Dictionary<int, ProcMatchSupplement> supplements = [];
	internal FakeSupplementProvider Add( int pid, double ageSeconds = 120, params string[] environment ) {
		this.supplements[ pid ] = new ProcMatchSupplement {
			ThreadGroupId = pid,
			Elapsed = TestSupport.Value( TimeSpan.FromSeconds( ageSeconds ) ),
			Environment = TestSupport.Value<IReadOnlyList<string>>( environment )
		};
		return this;
	}
	public Task<IReadOnlyList<ProcMatchCandidate>> GetCandidatesAsync(
		IReadOnlyList<ProcProcessSnapshot> processes,
		bool includeLightweightTasks,
		CancellationToken cancellationToken = default
	) {
		var result = processes.Select( process => new ProcMatchCandidate(
			process,
			this.supplements.TryGetValue( process.ProcessId, out var supplement )
				? supplement
				: new ProcMatchSupplement {
					ThreadGroupId = process.ProcessId,
					Elapsed = TestSupport.Value( TimeSpan.FromSeconds( 120 ) ),
					Environment = TestSupport.Value<IReadOnlyList<string>>( Array.Empty<string>() )
				}
		) ).ToArray();
		return Task.FromResult<IReadOnlyList<ProcMatchCandidate>>( result );
	}
}

internal sealed class FakeControl : IProcMatchControl {
	internal List<(int Pid, int Signal, int? Queue)> Signals { get; } = [];
	internal List<int> Waits { get; } = [];
	internal HashSet<int> SignalFailures { get; } = [];
	internal HashSet<int> VanishedWaits { get; } = [];
	internal List<int> Releases { get; } = [];
	internal HashSet<int> VanishedReleases { get; } = [];
	internal HashSet<int> ReleaseFailures { get; } = [];
	internal bool CancelWaits { get; set; }
	public ProcessOperationResult<ProcessSignal> ParseSignal( string text ) => ProcessSignalCatalog.Parse( text );
	public ProcessOperationResult<ProcessSignalDisposition> ObserveDisposition( ProcProcessSnapshot process, ProcessSignal signal )
		=> ProcessOperationResult<ProcessSignalDisposition>.Success( ProcessSignalDisposition.Caught );
	public Task<ProcessOperationResult> SignalAsync( ProcProcessSnapshot process, ProcessSignal signal, int? queuedValue = null, CancellationToken cancellationToken = default ) {
		this.Signals.Add( ( process.ProcessId, signal.Number, queuedValue ) );
		return Task.FromResult(
			this.SignalFailures.Contains( process.ProcessId )
				? ProcessOperationResult.Failure( ProcessOperationStatus.AccessDenied, "denied" )
				: ProcessOperationResult.Success()
		);
	}
	public Task<ProcessOperationResult<ProcessTermination>> WaitAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default ) {
		this.Waits.Add( process.ProcessId );
		if ( this.CancelWaits ) return Task.FromResult( ProcessOperationResult<ProcessTermination>.Failure( ProcessOperationStatus.Canceled, "canceled" ) );
		if ( this.VanishedWaits.Contains( process.ProcessId ) ) return Task.FromResult( ProcessOperationResult<ProcessTermination>.Failure( ProcessOperationStatus.Vanished, "vanished" ) );
		return Task.FromResult( ProcessOperationResult<ProcessTermination>.Success( ProcessTermination.Exited( 0 ) ) );
	}
	public ProcessOperationResult Release( ProcProcessSnapshot process ) {
		this.Releases.Add( process.ProcessId );
		if ( this.VanishedReleases.Contains( process.ProcessId ) ) return ProcessOperationResult.Failure( ProcessOperationStatus.Vanished, "vanished" );
		if ( this.ReleaseFailures.Contains( process.ProcessId ) ) return ProcessOperationResult.Failure( ProcessOperationStatus.AccessDenied, "denied" );
		return ProcessOperationResult.Success();
	}
}

internal sealed class NumericAccounts : IProcAccountResolver {
	public bool TryResolveUser( string text, out uint id ) => uint.TryParse( text, out id );
	public bool TryResolveGroup( string text, out uint id ) => uint.TryParse( text, out id );
}

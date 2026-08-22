namespace Icod.ProcPs.PidOf.Tests;

using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;

/// <summary>Provides reusable test fixtures and helpers.</summary>
internal static class TestSupport {
	/// <summary>Performs the process operation.</summary>
	internal static ProcProcessSnapshot Process( int pid, string name, ulong start = 1, int parent = 1, string[]? arguments = null, bool commandLineAvailable = true )
		=> new( new ProcessIdentity( pid, new ProcessReuseToken( "test", start.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) {
			CommandName = Value( name ),
			CommandLineArguments = commandLineAvailable ? Value<IReadOnlyList<string>>( arguments ?? [ name ] ) : ProcObservedValue<IReadOnlyList<string>>.Missing( ProcObservationAvailability.Unsupported ),
			State = Value( ProcProcessState.Sleeping ),
			ParentProcessId = Value( parent ),
			StartTimeTicks = Value( start ),
			LifetimeStable = Value( true )
		};

	/// <summary>Performs the value operation.</summary>
	internal static ProcObservedValue<T> Value<T>( T value )
		=> ProcObservedValue<T>.Available( value, ProcObservationSource.Configuration, ObservationFidelity.Exact );

	/// <summary>Performs the text operation.</summary>
	internal static string Text( MemoryStream stream ) => System.Text.Encoding.UTF8.GetString( stream.ToArray() );
}

/// <summary>Provides a test double for process provider.</summary>
internal sealed class FakeProcessProvider : IProcProcessProvider {
	private readonly IReadOnlyList<ProcProcessSnapshot> processes;
	/// <summary>Initializes a new instance of the <see cref="FakeProcessProvider"/> type.</summary>
	internal FakeProcessProvider( params ProcProcessSnapshot[] processes ) => this.processes = processes;
	/// <summary>Gets the capabilities exposed by this provider.</summary>
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration | ProcProcessCapabilities.Identity;
	/// <summary>Gets processes asynchronously.</summary>
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default )
		=> Task.FromResult( new ProcProcessCollection( this.processes ) );
	/// <summary>Gets process asynchronously.</summary>
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
		var process = this.processes.FirstOrDefault( item => item.ProcessId == processId );
		return Task.FromResult( null == process ? ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished ) : TestSupport.Value( process ) );
	}
	/// <summary>Gets memory maps asynchronously.</summary>
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
}

/// <summary>Provides a test double for path provider.</summary>
internal sealed class FakePathProvider : IProcProcessPathProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcProcessPathInfo>> values = [];
	/// <summary>Performs the missing operation.</summary>
	internal FakePathProvider Missing( int pid, ProcObservationAvailability availability ) {
		this.values[ pid ] = ProcObservedValue<ProcProcessPathInfo>.Missing( availability );
		return this;
	}
	/// <summary>Performs the add operation.</summary>
	internal FakePathProvider Add( int pid, string? executable = null, string? root = "/", string? cwd = "/work" ) {
		this.values[ pid ] = TestSupport.Value( new ProcProcessPathInfo {
			ExecutablePath = null == executable ? ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable ) : TestSupport.Value( executable ),
			RootPath = null == root ? ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported ) : TestSupport.Value( root ),
			WorkingDirectory = null == cwd ? ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported ) : TestSupport.Value( cwd )
		} );
		return this;
	}
	/// <summary>Observes async.</summary>
	public Task<ProcObservedValue<ProcProcessPathInfo>> ObserveAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( process.ProcessId, out var value ) ? value : TestSupport.Value( new ProcProcessPathInfo() ) );
}

/// <summary>Provides a test double for supplement provider.</summary>
internal sealed class FakeSupplementProvider : IProcMatchSupplementProvider {
	private readonly IReadOnlyList<ProcProcessSnapshot>? tasks;
	/// <summary>Initializes a new instance of the <see cref="FakeSupplementProvider"/> type.</summary>
	internal FakeSupplementProvider( params ProcProcessSnapshot[] tasks ) => this.tasks = tasks;
	/// <summary>Gets candidates asynchronously.</summary>
	public Task<IReadOnlyList<ProcMatchCandidate>> GetCandidatesAsync( IReadOnlyList<ProcProcessSnapshot> processes, bool includeLightweightTasks, CancellationToken cancellationToken = default ) {
		var source = includeLightweightTasks && null != this.tasks && 0 < this.tasks.Count ? this.tasks : processes;
		return Task.FromResult<IReadOnlyList<ProcMatchCandidate>>( source.Select( process => new ProcMatchCandidate( process, new ProcMatchSupplement { ThreadGroupId = process.ProcessId } ) ).ToArray() );
	}
}

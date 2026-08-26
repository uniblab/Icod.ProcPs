namespace Icod.ProcPs.Pwdx.Tests;

using Icod.Processes;
using Icod.ProcPs.Shared;
using ObservationFidelity = Icod.ProcPs.Shared.ProcObservationFidelity;

/// <summary>Provides reusable test fixtures and helpers.</summary>
internal static class TestSupport {
	/// <summary>Performs the process operation.</summary>
	internal static ProcProcessSnapshot Process( int pid )
		=> new( new ProcessIdentity( pid, new ProcessReuseToken( "test", pid.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) {
			CommandName = Value( "worker" ),
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
	private readonly Dictionary<int, ProcObservedValue<ProcProcessSnapshot>> values = [];
	/// <summary>Performs the add operation.</summary>
	internal FakeProcessProvider Add( int pid ) { this.values[ pid ] = TestSupport.Value( TestSupport.Process( pid ) ); return this; }
	/// <summary>Performs the missing operation.</summary>
	internal FakeProcessProvider Missing( int pid, ProcObservationAvailability availability, string? diagnostic = null ) { this.values[ pid ] = ProcObservedValue<ProcProcessSnapshot>.Missing( availability, diagnostic ); return this; }
	/// <summary>Gets the capabilities exposed by this provider.</summary>
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Identity;
	/// <summary>Gets processes asynchronously.</summary>
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) => Task.FromResult( new ProcProcessCollection( this.values.Values.Where( value => value.HasValue ).Select( value => value.Value ).ToArray() ) );
	/// <summary>Gets process asynchronously.</summary>
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( processId, out var value ) ? value : ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished ) );
	/// <summary>Gets memory maps asynchronously.</summary>
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
}

/// <summary>Provides a test double for path provider.</summary>
internal sealed class FakePathProvider : IProcProcessPathProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcProcessPathInfo>> values = [];
	/// <summary>Performs the working directory operation.</summary>
	internal FakePathProvider WorkingDirectory( int pid, string cwd ) {
		this.values[ pid ] = TestSupport.Value( new ProcProcessPathInfo { WorkingDirectory = TestSupport.Value( cwd ) } );
		return this;
	}
	/// <summary>Performs the missing working directory operation.</summary>
	internal FakePathProvider MissingWorkingDirectory( int pid, ProcObservationAvailability availability, string diagnostic ) {
		this.values[ pid ] = TestSupport.Value( new ProcProcessPathInfo { WorkingDirectory = ProcObservedValue<string>.Missing( availability, diagnostic ) } );
		return this;
	}
	/// <summary>Observes async.</summary>
	public Task<ProcObservedValue<ProcProcessPathInfo>> ObserveAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( process.ProcessId, out var value ) ? value : ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Unavailable ) );
}

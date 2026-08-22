namespace Icod.ProcPs.Pwdx.Tests;

using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;

internal static class TestSupport {
	internal static ProcProcessSnapshot Process( int pid )
		=> new( new ProcessIdentity( pid, new ProcessReuseToken( "test", pid.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) {
			CommandName = Value( "worker" ),
			LifetimeStable = Value( true )
		};
	internal static ProcObservedValue<T> Value<T>( T value )
		=> ProcObservedValue<T>.Available( value, ProcObservationSource.Configuration, ObservationFidelity.Exact );
	internal static string Text( MemoryStream stream ) => System.Text.Encoding.UTF8.GetString( stream.ToArray() );
}

internal sealed class FakeProcessProvider : IProcProcessProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcProcessSnapshot>> values = [];
	internal FakeProcessProvider Add( int pid ) { this.values[ pid ] = TestSupport.Value( TestSupport.Process( pid ) ); return this; }
	internal FakeProcessProvider Missing( int pid, ProcObservationAvailability availability, string? diagnostic = null ) { this.values[ pid ] = ProcObservedValue<ProcProcessSnapshot>.Missing( availability, diagnostic ); return this; }
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Identity;
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) => Task.FromResult( new ProcProcessCollection( this.values.Values.Where( value => value.HasValue ).Select( value => value.Value ).ToArray() ) );
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( processId, out var value ) ? value : ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished ) );
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
}

internal sealed class FakePathProvider : IProcProcessPathProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcProcessPathInfo>> values = [];
	internal FakePathProvider WorkingDirectory( int pid, string cwd ) {
		this.values[ pid ] = TestSupport.Value( new ProcProcessPathInfo { WorkingDirectory = TestSupport.Value( cwd ) } );
		return this;
	}
	internal FakePathProvider MissingWorkingDirectory( int pid, ProcObservationAvailability availability, string diagnostic ) {
		this.values[ pid ] = TestSupport.Value( new ProcProcessPathInfo { WorkingDirectory = ProcObservedValue<string>.Missing( availability, diagnostic ) } );
		return this;
	}
	public Task<ProcObservedValue<ProcProcessPathInfo>> ObserveAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( process.ProcessId, out var value ) ? value : ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Unavailable ) );
}

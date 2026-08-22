namespace Icod.ProcPs.Pmap.Tests;

using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;

internal static class TestSupport {
	internal static ProcObservedValue<T> Value<T>( T value )
		=> ProcObservedValue<T>.Available( value, ProcObservationSource.Configuration, ObservationFidelity.Exact );
	internal static ProcProcessSnapshot Process( int pid, string name = "demo", params string[] arguments )
		=> new( new ProcessIdentity( pid, new ProcessReuseToken( "test", pid.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) {
			CommandName = Value( name ),
			CommandLineArguments = Value<IReadOnlyList<string>>( 0 == arguments.Length ? new[] { name } : arguments ),
			LifetimeStable = Value( true )
		};
	internal static ProcMemoryMapRegion Region(
		ulong start,
		ulong end,
		string permissions = "r-xp",
		ulong offset = 0,
		string device = "08:01",
		ulong inode = 1,
		string? path = "/usr/bin/demo",
		IEnumerable<ProcMemoryMapMetric>? metrics = null,
		string? vmFlags = null
	) => new( new ProcMemoryMapEntry( start, end, permissions, offset, device, inode, path ), metrics, vmFlags );
	internal static ProcMemoryMapSet Maps( params ProcMemoryMapRegion[] regions ) => new( regions, regions.Any( static region => 0 < region.Metrics.Count || null != region.VmFlags ) );
	internal static string Text( MemoryStream stream ) => System.Text.Encoding.UTF8.GetString( stream.ToArray() );
}

internal sealed class FakeProcessProvider : IProcProcessProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcProcessSnapshot>> values = [];
	internal FakeProcessProvider Add( int pid, string name = "demo", params string[] arguments ) { this.values[ pid ] = TestSupport.Value( TestSupport.Process( pid, name, arguments ) ); return this; }
	internal FakeProcessProvider Missing( int pid, ProcObservationAvailability availability, string? diagnostic = null ) { this.values[ pid ] = ProcObservedValue<ProcProcessSnapshot>.Missing( availability, diagnostic ); return this; }
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Identity | ProcProcessCapabilities.MemoryMaps;
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default )
		=> Task.FromResult( new ProcProcessCollection( this.values.Values.Where( static value => value.HasValue ).Select( static value => value.Value ) ) );
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( processId, out var value ) ? value : ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished ) );
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
}

internal sealed class FakeMemoryMapProvider : IProcMemoryMapProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcMemoryMapSet>> values = [];
	internal FakeMemoryMapProvider Add( int pid, ProcMemoryMapSet maps ) { this.values[ pid ] = TestSupport.Value( maps ); return this; }
	internal FakeMemoryMapProvider Missing( int pid, ProcObservationAvailability availability, string? diagnostic = null ) { this.values[ pid ] = ProcObservedValue<ProcMemoryMapSet>.Missing( availability, diagnostic ); return this; }
	public Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync( ProcProcessSnapshot process, bool detailed = false, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( process.ProcessId, out var value ) ? value : ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Vanished ) );
}

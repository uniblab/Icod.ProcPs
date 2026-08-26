/*
	Icod.ProcPs.Pmap.Tests
	Tests for the pmap command implementation.
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

namespace Icod.ProcPs.Pmap.Tests;

using Icod.Processes;
using Icod.ProcPs.Shared;
using ObservationFidelity = Icod.ProcPs.Shared.ProcObservationFidelity;

/// <summary>Provides reusable test fixtures and helpers.</summary>
internal static class TestSupport {
	/// <summary>Performs the value operation.</summary>
	internal static ProcObservedValue<T> Value<T>( T value )
		=> ProcObservedValue<T>.Available( value, ProcObservationSource.Configuration, ObservationFidelity.Exact );
	/// <summary>Performs the process operation.</summary>
	internal static ProcProcessSnapshot Process( int pid, string name = "demo", params string[] arguments )
		=> new( new ProcessIdentity( pid, new ProcessReuseToken( "test", pid.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) {
			CommandName = Value( name ),
			CommandLineArguments = Value<IReadOnlyList<string>>( 0 == arguments.Length ? new[] { name } : arguments ),
			LifetimeStable = Value( true )
		};
	/// <summary>Performs the region operation.</summary>
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
	/// <summary>Performs the maps operation.</summary>
	internal static ProcMemoryMapSet Maps( params ProcMemoryMapRegion[] regions ) => new( regions, regions.Any( static region => 0 < region.Metrics.Count || null != region.VmFlags ) );
	/// <summary>Performs the text operation.</summary>
	internal static string Text( MemoryStream stream ) => System.Text.Encoding.UTF8.GetString( stream.ToArray() );
}

/// <summary>Provides a test double for process provider.</summary>
internal sealed class FakeProcessProvider : IProcProcessProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcProcessSnapshot>> values = [];
	/// <summary>Performs the add operation.</summary>
	internal FakeProcessProvider Add( int pid, string name = "demo", params string[] arguments ) { this.values[ pid ] = TestSupport.Value( TestSupport.Process( pid, name, arguments ) ); return this; }
	/// <summary>Performs the missing operation.</summary>
	internal FakeProcessProvider Missing( int pid, ProcObservationAvailability availability, string? diagnostic = null ) { this.values[ pid ] = ProcObservedValue<ProcProcessSnapshot>.Missing( availability, diagnostic ); return this; }
	/// <summary>Gets the capabilities exposed by this provider.</summary>
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Identity | ProcProcessCapabilities.MemoryMaps;
	/// <summary>Gets processes asynchronously.</summary>
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default )
		=> Task.FromResult( new ProcProcessCollection( this.values.Values.Where( static value => value.HasValue ).Select( static value => value.Value ) ) );
	/// <summary>Gets process asynchronously.</summary>
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( processId, out var value ) ? value : ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished ) );
	/// <summary>Gets memory maps asynchronously.</summary>
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default )
		=> Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
}

/// <summary>Provides a test double for memory map provider.</summary>
internal sealed class FakeMemoryMapProvider : IProcMemoryMapProvider {
	private readonly Dictionary<int, ProcObservedValue<ProcMemoryMapSet>> values = [];
	/// <summary>Performs the add operation.</summary>
	internal FakeMemoryMapProvider Add( int pid, ProcMemoryMapSet maps ) { this.values[ pid ] = TestSupport.Value( maps ); return this; }
	/// <summary>Performs the missing operation.</summary>
	internal FakeMemoryMapProvider Missing( int pid, ProcObservationAvailability availability, string? diagnostic = null ) { this.values[ pid ] = ProcObservedValue<ProcMemoryMapSet>.Missing( availability, diagnostic ); return this; }
	/// <summary>Observes async.</summary>
	public Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync( ProcProcessSnapshot process, bool detailed = false, CancellationToken cancellationToken = default )
		=> Task.FromResult( this.values.TryGetValue( process.ProcessId, out var value ) ? value : ProcObservedValue<ProcMemoryMapSet>.Missing( ProcObservationAvailability.Vanished ) );
}

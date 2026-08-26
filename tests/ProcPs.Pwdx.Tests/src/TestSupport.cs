/*
	Icod.ProcPs.Pwdx.Tests
	Tests for the pwdx command implementation.
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

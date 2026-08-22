namespace Icod.ProcPs.Shared.Tests;

using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;
using Xunit;

public sealed class MemoryMapProviderTests {
	[Fact]
	public async Task LinuxProviderFixtureRunsOnEveryHostAndReplacesInvalidUtf8() {
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "icod-procps-pmap-" + Guid.NewGuid().ToString( "N" ) );
		var processRoot = System.IO.Path.Combine( root, "101" );
		Directory.CreateDirectory( processRoot );
		try {
			var prefix = System.Text.Encoding.UTF8.GetBytes( "00001000-00002000 r-xp 00000000 08:01 123 /tmp/na" );
			var suffix = System.Text.Encoding.UTF8.GetBytes( "me.so\n" );
			var bytes = new byte[ prefix.Length + 1 + suffix.Length ];
			prefix.CopyTo( bytes, 0 );
			bytes[ prefix.Length ] = 0xff;
			suffix.CopyTo( bytes, prefix.Length + 1 );
			await File.WriteAllBytesAsync( System.IO.Path.Combine( processRoot, "maps" ), bytes );

			var identity = Identity( 101, "first" );
			var provider = new LinuxProcMemoryMapProvider( new SequenceInspector( identity, identity ), root );
			var observed = await provider.ObserveAsync( new ProcProcessSnapshot( identity ) );

			Assert.True( observed.HasValue, observed.Diagnostic );
			var region = Assert.Single( observed.Value.Regions );
			Assert.Equal( "/tmp/na?me.so", region.Map.Path );
			Assert.Equal( ProcObservationSource.LinuxProcfs, observed.Source );
			Assert.Equal( Icod.CommandFramework.Host.ObservationFidelity.Exact, observed.Fidelity );
		} finally {
			Directory.Delete( root, true );
		}
	}

	[Fact]
	public async Task LinuxProviderRejectsPidReuseAfterMapRead() {
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "icod-procps-pmap-" + Guid.NewGuid().ToString( "N" ) );
		var processRoot = System.IO.Path.Combine( root, "101" );
		Directory.CreateDirectory( processRoot );
		try {
			await File.WriteAllTextAsync( System.IO.Path.Combine( processRoot, "maps" ), "00001000-00002000 r-xp 00000000 08:01 123 /tmp/demo\n" );
			var expected = Identity( 101, "first" );
			var reused = Identity( 101, "second" );
			var provider = new LinuxProcMemoryMapProvider( new SequenceInspector( expected, reused ), root );

			var observed = await provider.ObserveAsync( new ProcProcessSnapshot( expected ) );

			Assert.False( observed.HasValue );
			Assert.Equal( ProcObservationAvailability.Reused, observed.Availability );
		} finally {
			Directory.Delete( root, true );
		}
	}

	private static ProcessIdentity Identity( int processId, string token )
		=> new( processId, new ProcessReuseToken( "fixture", token ) );

	private sealed class SequenceInspector : IProcessInspector {
		private readonly Queue<ProcessIdentity> identities;
		public SequenceInspector( params ProcessIdentity[] identities ) => this.identities = new Queue<ProcessIdentity>( identities );
		public ProcessControlCapabilities Capabilities => ProcessControlCapabilities.ProcessIdentity | ProcessControlCapabilities.ReuseToken;
		public ProcessOperationResult<ProcessIdentity> ObserveIdentity( int processId ) {
			if ( 0 == this.identities.Count ) return ProcessOperationResult<ProcessIdentity>.Failure( ProcessOperationStatus.Vanished );
			var identity = this.identities.Dequeue();
			return identity.ProcessId == processId
				? ProcessOperationResult<ProcessIdentity>.Success( identity )
				: ProcessOperationResult<ProcessIdentity>.Failure( ProcessOperationStatus.Vanished );
		}
		public ProcessOperationResult<bool> ObserveLiveness( ProcessTarget target )
			=> ProcessOperationResult<bool>.Failure( ProcessOperationStatus.Unsupported );
		public Task<ProcessOperationResult<ProcessTermination>> WaitAsync( ProcessIdentity identity, CancellationToken cancellationToken = default )
			=> Task.FromResult( ProcessOperationResult<ProcessTermination>.Failure( ProcessOperationStatus.Unsupported ) );
	}
}

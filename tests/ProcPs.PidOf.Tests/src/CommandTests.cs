namespace Icod.ProcPs.PidOf.Tests;

using Icod.ProcPs.Shared;
using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task MatchesExecutableIdentityAndPrintsDescendingPids() {
		var first = TestSupport.Process( 101, "worker", arguments: [ "worker" ] );
		var second = TestSupport.Process( 202, "different", arguments: [ "different" ] );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( first, second ),
			pathProvider: new FakePathProvider().Add( 101, "/usr/bin/worker" ).Add( 202, "/opt/worker" )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"202 101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task ExecutableIdentityStillWorksWhenPlatformCannotObserveCommandLine() {
		var process = TestSupport.Process( 101, "worker", commandLineAvailable: false );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( process ),
			pathProvider: new FakePathProvider().Add( 101, "/bin/worker" )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task SingleShotAndCustomSeparatorFollowPidofProfile() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-s", "-S", ",", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker" ), TestSupport.Process( 202, "worker" ) ),
			pathProvider: new FakePathProvider().Add( 101, "/bin/worker" ).Add( 202, "/bin/worker" )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"202{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task ScriptsTooMatchesInterpreterScriptArgument() {
		var script = TestSupport.Process( 101, "job.py", arguments: [ "/usr/bin/python3", "/srv/job.py" ] );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-x", "job.py" ],
			stdout: output,
			processProvider: new FakeProcessProvider( script ),
			pathProvider: new FakePathProvider().Add( 101, "/usr/bin/python3" )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task OmitPidAndParentMacroExcludeMatches() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-o", "101,%PPID", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker" ), TestSupport.Process( 202, "worker" ), TestSupport.Process( 303, "worker" ) ),
			pathProvider: new FakePathProvider().Add( 101, "/bin/worker" ).Add( 202, "/bin/worker" ).Add( 303, "/bin/worker" ),
			currentParentProcessIdProvider: static () => 202
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"303{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task CheckRootFiltersOnlyWhenPrivileged() {
		var current = TestSupport.Process( Environment.ProcessId, "pidof" );
		var same = TestSupport.Process( 101, "worker" );
		var other = TestSupport.Process( 202, "worker" );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-c", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( current, same, other ),
			pathProvider: new FakePathProvider().Add( Environment.ProcessId, "/bin/pidof", "/root-a" ).Add( 101, "/bin/worker", "/root-a" ).Add( 202, "/bin/worker", "/root-b" ),
			privilegedRootCheckProvider: static () => true
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task LightweightModeUsesSupplementedTasks() {
		var process = TestSupport.Process( 101, "leader" );
		var task = TestSupport.Process( 102, "worker" );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-t", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( process ),
			pathProvider: new FakePathProvider().Add( 102, "/bin/worker" ),
			supplements: new FakeSupplementProvider( task )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"102{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task ReusedProcessIsNotReportedFromStaleSnapshot() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker" ) ),
			pathProvider: new FakePathProvider().Missing( 101, ProcObservationAvailability.Reused )
		);
		Assert.Equal( 1, status );
		Assert.Empty( output.ToArray() );
	}

	[Fact]
	public async Task QuietMatchWritesNothingAndNoMatchIsFailure() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-q", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker" ) ),
			pathProvider: new FakePathProvider().Add( 101, "/bin/worker" )
		);
		Assert.Equal( 0, status );
		Assert.Empty( output.ToArray() );

		status = await Command.RunAsync(
			[ "missing" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker" ) ),
			pathProvider: new FakePathProvider().Add( 101, "/bin/worker" )
		);
		Assert.Equal( 1, status );
	}

	[Fact]
	public async Task HelpUsesHostNewlinesWithoutCrLfDoubling() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync( [ "--help" ], stdout: output );
		Assert.Equal( 0, status );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} pidof [options]", TestSupport.Text( output ) );
	}
}

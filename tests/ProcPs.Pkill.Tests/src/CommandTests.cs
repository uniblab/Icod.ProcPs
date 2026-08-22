namespace Icod.ProcPs.Pkill.Tests;

using Xunit;
using Icod.CommandFramework.Processes;

public sealed class CommandTests {
	[Fact]
	public async Task SignalSpellingAndQueueValueReachSharedControl() {
		var control = new FakeControl();
		var status = await Command.RunAsync(
			[ "-USR1", "-q", "7", "-p", "101" ],
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		var delivered = Assert.Single( control.Signals );
		Assert.Equal( 101, delivered.Pid );
		Assert.Equal( 7, delivered.Queue );
		Assert.Equal( "USR1", ProcessSignalCatalog.Translate( delivered.Signal ).Value!.Name );
	}

	[Fact]
	public async Task PartialSignalFailureStillSucceedsWhenOneTargetWasSignalled() {
		var control = new FakeControl();
		control.SignalFailures.Add( 202 );
		using var error = new MemoryStream();
		var status = await Command.RunAsync(
			[ "worker" ],
			stderr: error,
			processProvider: new FakeProcessProvider(
				TestSupport.Process( 101, "worker", 10 ),
				TestSupport.Process( 202, "worker", 20 )
			),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Contains( "202", TestSupport.Text( error ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task CountReportsMatchedTargetsNotSuccessfulSignals() {
		var control = new FakeControl();
		control.SignalFailures.Add( 202 );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-c", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider(
				TestSupport.Process( 101, "worker", 10 ),
				TestSupport.Process( 202, "worker", 20 )
			),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"2{Environment.NewLine}", TestSupport.Text( output ) );
	}


	[Fact]
	public async Task ShortMreleaseRequestsReleaseAfterSuccessfulSignal() {
		var control = new FakeControl();
		var status = await Command.RunAsync(
			[ "-m", "-p", "101" ],
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( new[] { 101 }, control.Releases );
	}

	[Fact]
	public async Task MreleaseMayBeCombinedWithQueuedSignal() {
		var control = new FakeControl();
		var status = await Command.RunAsync(
			[ "-m", "-q", "12", "-p", "101" ],
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		var delivered = Assert.Single( control.Signals );
		Assert.Equal( 12, delivered.Queue );
		Assert.Equal( new[] { 101 }, control.Releases );
	}

	[Fact]
	public async Task VanishedMreleaseDoesNotDowngradeSuccessfulKill() {
		var control = new FakeControl();
		control.VanishedReleases.Add( 101 );
		var status = await Command.RunAsync(
			[ "-m", "-p", "101" ],
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
	}

	[Fact]
	public async Task NoMatchesReturnsOneAndDoesNotSignal() {
		var control = new FakeControl();
		var status = await Command.RunAsync(
			[ "missing" ],
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 1, status );
		Assert.Empty( control.Signals );
	}
}

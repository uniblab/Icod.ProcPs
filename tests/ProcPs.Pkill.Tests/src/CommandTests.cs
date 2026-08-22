namespace Icod.ProcPs.Pkill.Tests;

using Xunit;
using Icod.CommandFramework.Processes;

/// <summary>Contains tests for command.</summary>
public sealed class CommandTests {
	/// <summary>Verifies that signal spelling and queue value reach shared control.</summary>
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

	/// <summary>Verifies that partial signal failure still succeeds when one target was signalled.</summary>
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

	/// <summary>Verifies that count reports matched targets not successful signals.</summary>
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


	/// <summary>Verifies that short mrelease requests release after successful signal.</summary>
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

	/// <summary>Verifies that mrelease may be combined with queued signal.</summary>
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

	/// <summary>Verifies that vanished mrelease does not downgrade successful kill.</summary>
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

	/// <summary>Verifies that no matches returns one and does not signal.</summary>
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

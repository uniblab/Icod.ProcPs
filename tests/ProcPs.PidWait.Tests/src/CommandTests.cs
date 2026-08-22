namespace Icod.ProcPs.PidWait.Tests;

using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task WaitsAllMatchingProcessesThroughSharedControl() {
		var control = new FakeControl();
		var status = await Command.RunAsync(
			[ "worker" ],
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
		Assert.Equal( new[] { 101, 202 }, control.Waits );
	}

	[Fact]
	public async Task VanishedTargetIsIgnoredWhenAnotherWaitCompletes() {
		var control = new FakeControl();
		control.VanishedWaits.Add( 101 );
		var status = await Command.RunAsync(
			[ "worker" ],
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
	}

	[Fact]
	public async Task AllVanishedTargetsReturnNoMatchStatus() {
		var control = new FakeControl();
		control.VanishedWaits.UnionWith( new[] { 101, 202 } );
		var status = await Command.RunAsync(
			[ "worker" ],
			processProvider: new FakeProcessProvider(
				TestSupport.Process( 101, "worker", 10 ),
				TestSupport.Process( 202, "worker", 20 )
			),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 1, status );
	}


	[Fact]
	public async Task EchoReportsTargetBeforeWaiting() {
		var control = new FakeControl();
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-e", "-p", "101" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"waiting for worker (pid 101){Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task CountIsPrintedBeforeCanceledWaitReturnsFatalStatus() {
		var control = new FakeControl { CancelWaits = true };
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-c", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 3, status );
		Assert.Equal( $"1{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task CanceledWaitReturnsFatalStatus() {
		var control = new FakeControl { CancelWaits = true };
		var status = await Command.RunAsync(
			[ "-p", "101" ],
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: control,
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 3, status );
	}
}

namespace Icod.ProcPs.PidWait.Tests;

using Xunit;

/// <summary>Contains tests for command.</summary>
public sealed class CommandTests {
	/// <summary>Verifies that waits all matching processes through shared control.</summary>
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

	/// <summary>Verifies that vanished target is ignored when another wait completes.</summary>
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

	/// <summary>Verifies that all vanished targets return no match status.</summary>
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


	/// <summary>Verifies that echo reports target before waiting.</summary>
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

	/// <summary>Verifies that count is printed before canceled wait returns fatal status.</summary>
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

	/// <summary>Verifies that canceled wait returns fatal status.</summary>
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

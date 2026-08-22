namespace Icod.ProcPs.Pgrep.Tests;

using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task MatchesGnuExtendedPatternAndPrintsPid() {
		var provider = new FakeProcessProvider(
			TestSupport.Process( 101, "alpha", 10 ),
			TestSupport.Process( 202, "beta", 20 )
		);
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "alpha|beta" ],
			stdout: output,
			processProvider: provider,
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}202{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task NewestBreaksEqualStartTimeByLargerPid() {
		var provider = new FakeProcessProvider(
			TestSupport.Process( 101, "worker", 10 ),
			TestSupport.Process( 202, "worker", 10 )
		);
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-n", "worker" ],
			stdout: output,
			processProvider: provider,
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"202{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task SelectionCriteriaAreAndedAndEnvironmentSelectorsAreOred() {
		var process = TestSupport.Process( 101, "worker", 10, parent: 44, uid: 1000 );
		var supplements = new FakeSupplementProvider().Add( 101, 500, "ROLE=worker", "PATH=/bin" );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-P", "44", "-u", "1000", "--env", "ROLE=worker", "--older", "60" ],
			stdout: output,
			processProvider: new FakeProcessProvider( process ),
			supplements: supplements,
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task FullListingUsesShellQuoteWithoutChangingPatternTarget() {
		var process = TestSupport.Process( 101, "worker", 10, arguments: [ "worker", "two words", "a'b" ] );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-a", "-Q", "worker" ],
			stdout: output,
			processProvider: new FakeProcessProvider( process ),
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Contains( "'two words'", TestSupport.Text( output ), StringComparison.Ordinal );
		Assert.Contains( "'a'\\''b'", TestSupport.Text( output ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task CountReturnsNoMatchStatusWhenZero() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-c", "missing" ],
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 1, status );
		Assert.Equal( $"0{Environment.NewLine}", TestSupport.Text( output ) );
	}


	[Fact]
	public async Task NewestAloneCountsAsSelectionCriterion() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-n" ],
			stdout: output,
			processProvider: new FakeProcessProvider(
				TestSupport.Process( 101, "older", 10 ),
				TestSupport.Process( 202, "newer", 20 )
			),
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"202{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task ZeroProcessGroupAndSessionMeanCurrentProcessContext() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-g", "0", "-s", "0" ],
			stdout: output,
			processProvider: new FakeProcessProvider(
				TestSupport.Process( 999, "pgrep", 30, group: 44, session: 55 ),
				TestSupport.Process( 101, "worker", 10, group: 44, session: 55 ),
				TestSupport.Process( 202, "other", 20, group: 45, session: 55 )
			),
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task PidFileMayBeReadFromStandardInput() {
		using var input = new MemoryStream( System.Text.Encoding.UTF8.GetBytes( "101\n" ) );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-F", "-" ],
			stdin: input,
			stdout: output,
			processProvider: new FakeProcessProvider( TestSupport.Process( 101, "worker", 10 ) ),
			supplements: new FakeSupplementProvider(),
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task EnvironmentListUsesOrSemantics() {
		var process = TestSupport.Process( 101, "worker", 10 );
		var supplements = new FakeSupplementProvider().Add( 101, 120, "ZONE=west" );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "--env", "ROLE=worker,ZONE=west" ],
			stdout: output,
			processProvider: new FakeProcessProvider( process ),
			supplements: supplements,
			control: new FakeControl(),
			accountResolver: new NumericAccounts(),
			currentProcessIdProvider: static () => 999
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task RequireHandlerUsesSelectedSignalDisposition() {
		var control = new FakeControl();
		control.HandlerPids.Add( 202 );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "--signal", "USR1", "-H", "worker" ],
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
		Assert.Equal( $"202{Environment.NewLine}", TestSupport.Text( output ) );
	}

	[Fact]
	public async Task MultiplePatternsAreSyntaxError() {
		using var error = new MemoryStream();
		var status = await Command.RunAsync( [ "one", "two" ], stderr: error, control: new FakeControl(), accountResolver: new NumericAccounts() );
		Assert.Equal( 2, status );
		Assert.Contains( "only one pattern", TestSupport.Text( error ), StringComparison.Ordinal );
	}
}

namespace Icod.ProcPs.Pwdx.Tests;

using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Contains tests for command.</summary>
public sealed class CommandTests {
	/// <summary>Verifies that prints working directories for multiple targets.</summary>
	[Fact]
	public async Task PrintsWorkingDirectoriesForMultipleTargets() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "101", "202" ],
			stdout: output,
			processProvider: new FakeProcessProvider().Add( 101 ).Add( 202 ),
			pathProvider: new FakePathProvider().WorkingDirectory( 101, "/one" ).WorkingDirectory( 202, "/two" )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"101: /one{Environment.NewLine}202: /two{Environment.NewLine}", TestSupport.Text( output ) );
	}

	/// <summary>Verifies that proc operand is accepted and preserved in output.</summary>
	[Fact]
	public async Task ProcOperandIsAcceptedAndPreservedInOutput() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "/proc/101" ],
			stdout: output,
			processProvider: new FakeProcessProvider().Add( 101 ),
			pathProvider: new FakePathProvider().WorkingDirectory( 101, "/srv" )
		);
		Assert.Equal( 0, status );
		Assert.Equal( $"/proc/101: /srv{Environment.NewLine}", TestSupport.Text( output ) );
	}

	/// <summary>Verifies that vanished target does not prevent later targets.</summary>
	[Fact]
	public async Task VanishedTargetDoesNotPreventLaterTargets() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = await Command.RunAsync(
			[ "101", "202" ],
			stdout: output,
			stderr: error,
			processProvider: new FakeProcessProvider().Missing( 101, ProcObservationAvailability.Vanished ).Add( 202 ),
			pathProvider: new FakePathProvider().WorkingDirectory( 202, "/two" )
		);
		Assert.Equal( 1, status );
		Assert.Equal( $"202: /two{Environment.NewLine}", TestSupport.Text( output ) );
		Assert.Contains( $"101: No such process{Environment.NewLine}", TestSupport.Text( error ), StringComparison.Ordinal );
	}

	/// <summary>Verifies that unsupported working directory is controlled failure.</summary>
	[Fact]
	public async Task UnsupportedWorkingDirectoryIsControlledFailure() {
		using var error = new MemoryStream();
		var status = await Command.RunAsync(
			[ "101" ],
			stderr: error,
			processProvider: new FakeProcessProvider().Add( 101 ),
			pathProvider: new FakePathProvider().MissingWorkingDirectory( 101, ProcObservationAvailability.Unsupported, "not supported here" )
		);
		Assert.Equal( 1, status );
		Assert.Equal( $"101: not supported here{Environment.NewLine}", TestSupport.Text( error ) );
	}

	/// <summary>Verifies that invalid target fails immediately.</summary>
	[Fact]
	public async Task InvalidTargetFailsImmediately() {
		using var error = new MemoryStream();
		var status = await Command.RunAsync( [ "not-a-pid", "202" ], stderr: error, processProvider: new FakeProcessProvider().Add( 202 ), pathProvider: new FakePathProvider().WorkingDirectory( 202, "/two" ) );
		Assert.Equal( 1, status );
		Assert.Equal( $"pwdx: invalid process id: not-a-pid{Environment.NewLine}", TestSupport.Text( error ) );
	}

	/// <summary>Verifies that help uses host newlines without cr lf doubling.</summary>
	[Fact]
	public async Task HelpUsesHostNewlinesWithoutCrLfDoubling() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync( [ "--help" ], stdout: output );
		Assert.Equal( 0, status );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} pwdx [options]", TestSupport.Text( output ) );
	}
}

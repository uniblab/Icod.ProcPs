namespace Icod.ProcPs.Shared.Tests;

using Icod.CommandFramework.Processes;
using Xunit;

/// <summary>Exercises shared observations added for ProcPs process-reporting commands.</summary>
public sealed class ProcessReportingSupplementTests {
	/// <summary>Verifies Linux procfs status and security-label data are observable through the shared supplement provider.</summary>
	[Fact]
	public async Task ReadsLinuxStatusAndSecurityLabelWhenAvailable() {
		var processId = Environment.ProcessId;
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"icod-procps-reporting-{Guid.NewGuid():N}" );
		try {
			var processRoot = System.IO.Path.Combine( root, processId.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
			Directory.CreateDirectory( System.IO.Path.Combine( processRoot, "attr" ) );
			await File.WriteAllBytesAsync( System.IO.Path.Combine( processRoot, "environ" ), [] );
			await File.WriteAllTextAsync(
				System.IO.Path.Combine( processRoot, "status" ),
				string.Concat(
					"Name:\tfixture\n",
					"SigBlk:\t0000000000000004\n",
					"CapEff:\t0000000000000025\n"
				)
			);
			await File.WriteAllTextAsync( System.IO.Path.Combine( processRoot, "attr", "current" ), "fixture_u:fixture_r:fixture_t:s0\n" );

			var provider = new SystemProcMatchSupplementProvider( root );
			var snapshot = new ProcProcessSnapshot( new ProcessIdentity( processId ) );
			var candidates = await provider.GetCandidatesAsync( [ snapshot ], false );
			var supplement = Assert.Single( candidates ).Supplement;

			if ( OperatingSystem.IsLinux() ) {
				Assert.True( supplement.LinuxStatusFields.HasValue );
				Assert.Equal( "0000000000000004", supplement.LinuxStatusFields.Value[ "SigBlk" ] );
				Assert.Equal( "0000000000000025", supplement.LinuxStatusFields.Value[ "CapEff" ] );
				Assert.True( supplement.SecurityLabel.HasValue );
				Assert.Equal( "fixture_u:fixture_r:fixture_t:s0", supplement.SecurityLabel.Value );
			} else {
				Assert.False( supplement.LinuxStatusFields.HasValue );
				Assert.False( supplement.SecurityLabel.HasValue );
			}
		} finally {
			if ( Directory.Exists( root ) ) {
				Directory.Delete( root, true );
			}
		}
	}
	/// <summary>Verifies the reusable process-reporting catalog carries canonical names and procps aliases needed by ps-family consumers.</summary>
	[Fact]
	public void ProcessReportCatalogIncludesCompatibilityAliases() {
		Assert.True( ProcReportFieldCatalog.TryGet( "lwp", out var lwp ) );
		Assert.True( ProcReportFieldCatalog.TryGet( "spid", out var spid ) );
		Assert.Equal( ProcReportFieldKind.ThreadId, lwp.Kind );
		Assert.Same( lwp, spid );
		Assert.True( ProcReportFieldCatalog.TryGet( "capeff", out var capability ) );
		Assert.Equal( ProcReportFieldKind.CapabilityEffective, capability.Kind );
	}

}

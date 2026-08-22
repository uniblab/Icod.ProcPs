namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Contains tests for process path provider.</summary>
public sealed class ProcessPathProviderTests {
	/// <summary>Verifies that system provider observes current executable without weakening identity.</summary>
	[Fact]
	public async Task SystemProviderObservesCurrentExecutableWithoutWeakeningIdentity() {
		var process = await SystemProcProcessProvider.Instance.GetProcessAsync( Environment.ProcessId );
		Assert.True( process.HasValue );
		var observed = await SystemProcProcessPathProvider.Instance.ObserveAsync( process.Value );
		Assert.True( observed.HasValue );
		Assert.True( observed.Value.ExecutablePath.HasValue );
		Assert.False( string.IsNullOrWhiteSpace( observed.Value.ExecutablePath.Value ) );
	}

	/// <summary>Verifies that current working directory capability matches supported platform policy.</summary>
	[Fact]
	public async Task CurrentWorkingDirectoryCapabilityMatchesSupportedPlatformPolicy() {
		var process = await SystemProcProcessProvider.Instance.GetProcessAsync( Environment.ProcessId );
		Assert.True( process.HasValue );
		var observed = await SystemProcProcessPathProvider.Instance.ObserveAsync( process.Value );
		Assert.True( observed.HasValue );
		if ( OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ) {
			Assert.True( observed.Value.WorkingDirectory.HasValue );
			Assert.False( string.IsNullOrWhiteSpace( observed.Value.WorkingDirectory.Value ) );
		} else if ( OperatingSystem.IsWindows() ) {
			Assert.False( observed.Value.WorkingDirectory.HasValue );
			Assert.Equal( ProcObservationAvailability.Unsupported, observed.Value.WorkingDirectory.Availability );
		}
	}
}

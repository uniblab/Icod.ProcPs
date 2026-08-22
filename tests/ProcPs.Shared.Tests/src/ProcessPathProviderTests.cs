namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using Xunit;

public sealed class ProcessPathProviderTests {
	[Fact]
	public async Task SystemProviderObservesCurrentExecutableWithoutWeakeningIdentity() {
		var process = await SystemProcProcessProvider.Instance.GetProcessAsync( Environment.ProcessId );
		Assert.True( process.HasValue );
		var observed = await SystemProcProcessPathProvider.Instance.ObserveAsync( process.Value );
		Assert.True( observed.HasValue );
		Assert.True( observed.Value.ExecutablePath.HasValue );
		Assert.False( string.IsNullOrWhiteSpace( observed.Value.ExecutablePath.Value ) );
	}

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

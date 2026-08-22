namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using System.Globalization;
using Icod.CommandFramework.Processes;
using Xunit;

public sealed class ProviderTests {
	[Fact]
	public async Task FixtureProcRootProducesDetailedSnapshot() {
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "icod-procps-" + Guid.NewGuid().ToString( "N" ) );
		var pid = Environment.ProcessId;
		var processRoot = System.IO.Path.Combine( root, pid.ToString( CultureInfo.InvariantCulture ) );
		Directory.CreateDirectory( System.IO.Path.Combine( processRoot, "ns" ) );
		Directory.CreateDirectory( System.IO.Path.Combine( processRoot, "fd" ) );
		try {
			await File.WriteAllTextAsync( System.IO.Path.Combine( processRoot, "stat" ), $"{pid} (fixture) S 1 {pid} {pid} 0 0 0 0 0 0 0 10 20 0 0 20 5 3 0 777 4096 2" );
			await File.WriteAllTextAsync( System.IO.Path.Combine( processRoot, "status" ), "Uid:\t1000\t1001\t1002\t1003\nGid:\t2000\t2001\t2002\t2003\nNSpid:\t42\t7\n" );
			await File.WriteAllBytesAsync( System.IO.Path.Combine( processRoot, "cmdline" ), new byte[] { (byte)'a', 0, (byte)'b', 0 } );
			await File.WriteAllTextAsync( System.IO.Path.Combine( processRoot, "cgroup" ), "0::/system.slice/docker-0123456789abcdef.scope\n" );
			await File.WriteAllTextAsync( System.IO.Path.Combine( processRoot, "maps" ), "00400000-00452000 r-xp 00000000 08:02 123 /tmp/file\n" );
			var provider = new LinuxProcProcessProvider( SystemProcessInspector.Instance, root );
			var observed = await provider.GetProcessAsync( pid );
			Assert.True( observed.HasValue, observed.Diagnostic );
			Assert.Equal( "fixture", observed.Value.CommandName.Value );
			Assert.Equal( new[] { "a", "b" }, observed.Value.CommandLineArguments.Value );
			Assert.Equal( 1001U, observed.Value.EffectiveUserId.Value );
			Assert.Equal( "0123456789abcdef", observed.Value.Container.Value.ContainerId );
			Assert.True( observed.Value.LifetimeStable.Value );
			var maps = await provider.GetMemoryMapsAsync( pid );
			Assert.True( maps.HasValue );
			Assert.Single( maps.Value );
		} finally {
			Directory.Delete( root, true );
		}
	}

	[Fact]
	public async Task SystemProviderCanObserveCurrentProcess() {
		var provider = SystemProcProcessProvider.Instance;
		var observed = await provider.GetProcessAsync( Environment.ProcessId );
		Assert.True( observed.HasValue, observed.Diagnostic );
		Assert.Equal( Environment.ProcessId, observed.Value.ProcessId );
		Assert.True( observed.Value.CommandName.HasValue );
	}
	[Fact]
	public void SystemProcessProviderSelectsThePrimaryPlatformBackend() {
		var capabilities = SystemProcProcessProvider.Instance.Capabilities;
		if ( OperatingSystem.IsLinux() ) {
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.Namespaces ) );
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.MemoryMaps ) );
		} else if ( OperatingSystem.IsWindows() ) {
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.Parentage ) );
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.PlatformSessions ) );
			Assert.False( capabilities.HasFlag( ProcProcessCapabilities.Sessions ) );
		} else if ( OperatingSystem.IsMacOS() ) {
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.Parentage ) );
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.ProcessGroups ) );
			Assert.True( capabilities.HasFlag( ProcProcessCapabilities.Sessions ) );
		}
	}

	[Fact]
	public async Task PortableProviderDoesNotMislabelPlatformSessionAsPosixSession() {
		var provider = new DotNetProcProcessProvider( SystemProcessInspector.Instance );
		Assert.False( provider.Capabilities.HasFlag( ProcProcessCapabilities.Sessions ) );
		Assert.False( provider.Capabilities.HasFlag( ProcProcessCapabilities.PlatformSessions ) );
		var observed = await provider.GetProcessAsync( Environment.ProcessId );
		Assert.True( observed.HasValue, observed.Diagnostic );
		Assert.False( observed.Value.SessionId.HasValue );
		Assert.False( observed.Value.PlatformSessionId.HasValue );
	}

	[Fact]
	public async Task WindowsProviderUsesNativeParentAndPlatformSessionWhenRunningOnWindows() {
		if ( !OperatingSystem.IsWindows() ) return;
		var provider = new WindowsProcProcessProvider( SystemProcessInspector.Instance );
		Assert.True( provider.Capabilities.HasFlag( ProcProcessCapabilities.Parentage ) );
		Assert.True( provider.Capabilities.HasFlag( ProcProcessCapabilities.PlatformSessions ) );
		Assert.False( provider.Capabilities.HasFlag( ProcProcessCapabilities.Sessions ) );
		var observed = await provider.GetProcessAsync( Environment.ProcessId );
		Assert.True( observed.HasValue, observed.Diagnostic );
		Assert.True( observed.Value.ParentProcessId.HasValue, observed.Value.ParentProcessId.Diagnostic );
		Assert.Equal( ProcObservationSource.WindowsNativeApi, observed.Value.ParentProcessId.Source );
		Assert.True( observed.Value.PlatformSessionId.HasValue, observed.Value.PlatformSessionId.Diagnostic );
		Assert.Equal( ProcObservationSource.WindowsNativeApi, observed.Value.PlatformSessionId.Source );
		Assert.False( observed.Value.SessionId.HasValue );
	}

	[Fact]
	public async Task MacOsProviderUsesDarwinAndPosixMetadataWhenRunningOnMacOs() {
		if ( !OperatingSystem.IsMacOS() ) return;
		var provider = new MacOsProcProcessProvider( SystemProcessInspector.Instance );
		Assert.True( provider.Capabilities.HasFlag( ProcProcessCapabilities.Parentage ) );
		Assert.True( provider.Capabilities.HasFlag( ProcProcessCapabilities.ProcessGroups ) );
		Assert.True( provider.Capabilities.HasFlag( ProcProcessCapabilities.Sessions ) );
		Assert.True( provider.Capabilities.HasFlag( ProcProcessCapabilities.Users ) );
		var observed = await provider.GetProcessAsync( Environment.ProcessId );
		Assert.True( observed.HasValue, observed.Diagnostic );
		Assert.True( observed.Value.ParentProcessId.HasValue, observed.Value.ParentProcessId.Diagnostic );
		Assert.True( observed.Value.ProcessGroupId.HasValue, observed.Value.ProcessGroupId.Diagnostic );
		Assert.True( observed.Value.SessionId.HasValue, observed.Value.SessionId.Diagnostic );
		Assert.True( observed.Value.RealUserId.HasValue, observed.Value.RealUserId.Diagnostic );
		Assert.True( observed.Value.EffectiveUserId.HasValue, observed.Value.EffectiveUserId.Diagnostic );
		Assert.True( observed.Value.NiceValue.HasValue, observed.Value.NiceValue.Diagnostic );
		Assert.Equal( ProcObservationSource.DarwinLibProc, observed.Value.ParentProcessId.Source );
		Assert.Equal( ProcObservationSource.PosixLibc, observed.Value.SessionId.Source );
	}

}

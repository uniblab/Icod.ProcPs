namespace Icod.ProcPs.Pmap.Tests;

using Icod.ProcPs.Shared;
using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task BasicModePrintsMappingsAndTotal() {
		using var output = new MemoryStream();
		var maps = TestSupport.Maps(
			TestSupport.Region( 0x1000, 0x3000, "r-xp", path: "/usr/bin/demo" ),
			TestSupport.Region( 0x4000, 0x5000, "rw-s", path: null )
		);
		var status = await Command.RunAsync(
			[ "101" ], stdout: output,
			processProvider: new FakeProcessProvider().Add( 101, "demo", "/usr/bin/demo", "--flag" ),
			memoryMapProvider: new FakeMemoryMapProvider().Add( 101, maps )
		);
		var text = TestSupport.Text( output );
		Assert.Equal( 0, status );
		Assert.StartsWith( $"101:   /usr/bin/demo --flag{Environment.NewLine}0000000000001000      8K r-x-- demo{Environment.NewLine}", text );
		Assert.Contains( $"0000000000004000      4K rw-s-   [ anon ]{Environment.NewLine}", text, StringComparison.Ordinal );
		Assert.EndsWith( $" total               12K{Environment.NewLine}", text );
	}

	[Fact]
	public async Task ShowPathAndKernelNameControlMappingPresentation() {
		var maps = TestSupport.Maps(
			TestSupport.Region( 0x1000, 0x2000, path: "/opt/demo/bin/tool" ),
			TestSupport.Region( 0x3000, 0x4000, path: "[vdso]" )
		);
		using var plain = new MemoryStream();
		await Command.RunAsync( [ "101" ], stdout: plain, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, maps ) );
		Assert.Contains( "tool", TestSupport.Text( plain ), StringComparison.Ordinal );
		Assert.DoesNotContain( "/opt/demo/bin/tool", TestSupport.Text( plain ), StringComparison.Ordinal );
		Assert.DoesNotContain( "[vdso]", TestSupport.Text( plain ), StringComparison.Ordinal );

		using var named = new MemoryStream();
		await Command.RunAsync( [ "-p", "-k", "101" ], stdout: named, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, maps ) );
		Assert.Contains( "/opt/demo/bin/tool", TestSupport.Text( named ), StringComparison.Ordinal );
		Assert.Contains( "[vdso]", TestSupport.Text( named ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task QuietSuppressesFooterButNotProcessBanner() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			[ "-q", "101" ], stdout: output,
			processProvider: new FakeProcessProvider().Add( 101 ),
			memoryMapProvider: new FakeMemoryMapProvider().Add( 101, TestSupport.Maps( TestSupport.Region( 0x1000, 0x2000 ) ) )
		);
		Assert.Equal( 0, status );
		Assert.StartsWith( $"101:   demo{Environment.NewLine}", TestSupport.Text( output ) );
		Assert.DoesNotContain( " total ", TestSupport.Text( output ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task RangeSelectsOnlyOverlappingMappings() {
		using var output = new MemoryStream();
		var maps = TestSupport.Maps(
			TestSupport.Region( 0x1000, 0x3000, path: "/one" ),
			TestSupport.Region( 0x4000, 0x5000, path: "/two" )
		);
		var status = await Command.RunAsync( [ "-A", "2000,2fff", "101" ], stdout: output, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, maps ) );
		Assert.Equal( 0, status );
		Assert.Contains( "one", TestSupport.Text( output ), StringComparison.Ordinal );
		Assert.DoesNotContain( "two", TestSupport.Text( output ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task DeviceModePrintsOffsetsDevicesAndTotals() {
		using var output = new MemoryStream();
		var maps = TestSupport.Maps(
			TestSupport.Region( 0x1000, 0x3000, "rw-p", 0x2000, "08:01" ),
			TestSupport.Region( 0x4000, 0x5000, "r--s", 0, "00:05", path: "[shared]" )
		);
		var status = await Command.RunAsync( [ "-d", "101" ], stdout: output, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, maps ) );
		var text = TestSupport.Text( output );
		Assert.Equal( 0, status );
		Assert.Contains( $"Address           Kbytes Mode  Offset           Device    Mapping{Environment.NewLine}", text, StringComparison.Ordinal );
		Assert.Contains( "0000000000002000", text, StringComparison.Ordinal );
		Assert.Contains( "008:00001", text, StringComparison.Ordinal );
		Assert.EndsWith( $"mapped: 12K    writeable/private: 8K    shared: 4K{Environment.NewLine}", text );
	}

	[Fact]
	public async Task ExtendedModeUsesSmapsRssAndDirtyFields() {
		using var output = new MemoryStream();
		var metrics = new[] {
			new ProcMemoryMapMetric( "Rss", 7, "kB" ),
			new ProcMemoryMapMetric( "Shared_Dirty", 2, "kB" ),
			new ProcMemoryMapMetric( "Private_Dirty", 3, "kB" )
		};
		var status = await Command.RunAsync( [ "-x", "101" ], stdout: output, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, TestSupport.Maps( TestSupport.Region( 0x1000, 0x3000, metrics: metrics ) ) ) );
		var text = TestSupport.Text( output );
		Assert.Equal( 0, status );
		Assert.Contains( "Kbytes     RSS   Dirty", text, StringComparison.Ordinal );
		Assert.Contains( "      8       7       5", text, StringComparison.Ordinal );
		Assert.Contains( "total kB", text, StringComparison.Ordinal );
	}

	[Fact]
	public async Task DynamicModesPreserveKernelDetailFieldPolicy() {
		var metrics = new[] {
			new ProcMemoryMapMetric( "Size", 8, "kB" ),
			new ProcMemoryMapMetric( "Rss", 7, "kB" ),
			new ProcMemoryMapMetric( "Private_Dirty", 3, "kB" ),
			new ProcMemoryMapMetric( "AnonHugePages", 0, "kB" )
		};
		var mapSet = TestSupport.Maps( TestSupport.Region( 0x1000, 0x3000, metrics: metrics, vmFlags: "rd ex mr mw me" ) );
		using var compact = new MemoryStream();
		await Command.RunAsync( [ "-X", "101" ], stdout: compact, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, mapSet ) );
		Assert.Contains( "Size", TestSupport.Text( compact ), StringComparison.Ordinal );
		Assert.DoesNotContain( "Private_Dirty", TestSupport.Text( compact ), StringComparison.Ordinal );
		Assert.DoesNotContain( "VmFlags", TestSupport.Text( compact ), StringComparison.Ordinal );

		using var all = new MemoryStream();
		await Command.RunAsync( [ "-XX", "101" ], stdout: all, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, mapSet ) );
		Assert.Contains( "Private_Dirty", TestSupport.Text( all ), StringComparison.Ordinal );
		Assert.Contains( "AnonHugePages", TestSupport.Text( all ), StringComparison.Ordinal );
		Assert.Contains( "VmFlags", TestSupport.Text( all ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task Utf8MappingNamesRoundTrip() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync( [ "-p", "101" ], stdout: output, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, TestSupport.Maps( TestSupport.Region( 0x1000, 0x2000, path: "/tmp/naïve-世界.so" ) ) ) );
		Assert.Equal( 0, status );
		Assert.Contains( "/tmp/naïve-世界.so", TestSupport.Text( output ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task VanishedProcessContributesProcpsMissingStatusBit() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = await Command.RunAsync( [ "101" ], stdout: output, stderr: error, processProvider: new FakeProcessProvider().Missing( 101, ProcObservationAvailability.Vanished ), memoryMapProvider: new FakeMemoryMapProvider() );
		Assert.Equal( 42, status );
		Assert.Equal( string.Empty, TestSupport.Text( output ) );
		Assert.Equal( string.Empty, TestSupport.Text( error ) );
	}

	[Fact]
	public async Task UnsupportedMapsProduceControlledDiagnosticAfterBanner() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = await Command.RunAsync( [ "101" ], stdout: output, stderr: error, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Missing( 101, ProcObservationAvailability.Unsupported, "no complete map API" ) );
		Assert.Equal( 1, status );
		Assert.Equal( $"101:   demo{Environment.NewLine}", TestSupport.Text( output ) );
		Assert.Equal( $"pmap: cannot examine PID 101: no complete map API{Environment.NewLine}", TestSupport.Text( error ) );
	}

	[Fact]
	public async Task AccessDeniedMapProducesPrivilegeDiagnostic() {
		using var error = new MemoryStream();
		var status = await Command.RunAsync( [ "101" ], stderr: error, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Missing( 101, ProcObservationAvailability.AccessDenied, "permission denied by fixture" ) );
		Assert.Equal( 1, status );
		Assert.Equal( $"pmap: cannot examine PID 101: permission denied by fixture{Environment.NewLine}", TestSupport.Text( error ) );
	}

	[Fact]
	public async Task ProcPidOperandAndModeConflictsFollowProfile() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync( [ "/proc/101" ], stdout: output, processProvider: new FakeProcessProvider().Add( 101 ), memoryMapProvider: new FakeMemoryMapProvider().Add( 101, TestSupport.Maps( TestSupport.Region( 0x1000, 0x2000 ) ) ) );
		Assert.Equal( 0, status );
		Assert.StartsWith( $"101:   demo{Environment.NewLine}", TestSupport.Text( output ) );

		using var error = new MemoryStream();
		status = await Command.RunAsync( [ "-x", "-d", "101" ], stderr: error );
		Assert.Equal( 1, status );
		Assert.Contains( "mutually exclusive", TestSupport.Text( error ), StringComparison.Ordinal );
	}

	[Fact]
	public async Task HelpUsesHostNewlinesWithoutCrLfDoubling() {
		using var output = new MemoryStream();
		var status = await Command.RunAsync( [ "--help" ], stdout: output );
		Assert.Equal( 0, status );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} pmap [options]", TestSupport.Text( output ) );
	}

	[Fact]
	public void SmapsParserPreservesMetricsFlagsAndUtf8Name() {
		var text = string.Join( "\r\n", new[] {
			"00001000-00003000 rw-p 00000000 08:01 123 /tmp/naïve-世界.so",
			"Size:                  8 kB",
			"Rss:                   7 kB",
			"Private_Dirty:         3 kB",
			"VmFlags: rd wr mr mw me ac sd",
			"00004000-00005000 r-xp 00001000 08:01 124 /lib/two.so",
			"Size:                  4 kB",
			"Rss:                   4 kB",
			string.Empty
		} );
		var parsed = ProcMemoryMapParsers.ParseSmaps( text );
		Assert.True( parsed.IsDetailed );
		Assert.Equal( 2, parsed.Regions.Count );
		Assert.Equal( "/tmp/naïve-世界.so", parsed.Regions[ 0 ].Map.Path );
		Assert.Equal( 7UL, parsed.Regions[ 0 ].GetMetric( "Rss" ).GetValueOrDefault() );
		Assert.Equal( "rd wr mr mw me ac sd", parsed.Regions[ 0 ].VmFlags );
	}

	[Fact]
	public async Task SystemMapProviderAdvertisesLinuxOnlyEquivalentSemantics() {
		var process = await SystemProcProcessProvider.Instance.GetProcessAsync( Environment.ProcessId );
		Assert.True( process.HasValue );
		var maps = await SystemProcMemoryMapProvider.Instance.ObserveAsync( process.Value );
		if ( OperatingSystem.IsLinux() ) {
			Assert.True( maps.HasValue );
			Assert.True( 0 < maps.Value.Regions.Count );
		} else {
			Assert.False( maps.HasValue );
			Assert.Equal( ProcObservationAvailability.Unsupported, maps.Availability );
		}
	}
}

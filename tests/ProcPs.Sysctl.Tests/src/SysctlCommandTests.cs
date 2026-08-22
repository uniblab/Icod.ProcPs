// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Sysctl.Tests;

using System.Text;
using Xunit;

/// <summary>Exercises procps-compatible <c>sysctl</c> behavior through an injected backend.</summary>
public sealed class SysctlCommandTests {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );

	/// <summary>Reads dot and slash key forms from the same kernel parameter.</summary>
	[Fact]
	public async Task ReadsDotAndSlashKeyForms() {
		var backend = CreateBackend();
		backend.Values["net/ipv4/conf/eth0.foo/rp_filter"] = "1";
		var dot = await RunAsync( [ "kernel.hostname" ], backend );
		var slash = await RunAsync( [ "kernel/hostname" ], backend );
		var mixed = await RunAsync( [ "net/ipv4/conf/eth0.foo/rp_filter" ], backend );

		Assert.Equal( 0, dot.Status );
		Assert.Equal( $"kernel.hostname = icod{Environment.NewLine}", dot.Output );
		Assert.Equal( dot.Output, slash.Output );
		Assert.Equal( $"net.ipv4.conf.eth0/foo.rp_filter = 1{Environment.NewLine}", mixed.Output );
	}

	/// <summary>Supports names-only, values-only, and binary output modes.</summary>
	[Fact]
	public async Task SupportsReadOutputModes() {
		var backend = CreateBackend();
		var names = await RunAsync( [ "-N", "kernel.hostname" ], backend );
		var values = await RunAsync( [ "-n", "kernel.hostname" ], backend );
		var binary = await RunAsync( [ "-b", "kernel.hostname" ], backend );

		Assert.Equal( $"kernel.hostname{Environment.NewLine}", names.Output );
		Assert.Equal( $"icod{Environment.NewLine}", values.Output );
		Assert.Equal( "icod", binary.Output );
	}

	/// <summary>Writes values and honors quiet output.</summary>
	[Fact]
	public async Task WritesValuesAndHonorsQuietMode() {
		var backend = CreateBackend();
		var normal = await RunAsync( [ "-w", "kernel.hostname=dotunix" ], backend );
		var quiet = await RunAsync( [ "-q", "-w", "kernel.hostname=quiet" ], backend );

		Assert.Equal( 0, normal.Status );
		Assert.Equal( $"kernel.hostname = dotunix{Environment.NewLine}", normal.Output );
		Assert.Equal( "quiet", backend.Values["kernel/hostname"] );
		Assert.Equal( string.Empty, quiet.Output );
	}

	/// <summary>Dry-run validates and reports a write without mutating the backend.</summary>
	[Fact]
	public async Task DryRunDoesNotMutateValue() {
		var backend = CreateBackend();
		var result = await RunAsync( [ "--dry-run", "-w", "kernel.hostname=dotunix" ], backend );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "icod", backend.Values["kernel/hostname"] );
		Assert.Equal( $"kernel.hostname = dotunix{Environment.NewLine}", result.Output );
		Assert.Contains( "kernel/hostname", backend.DryRunKeys );
	}

	/// <summary>Reports malformed write operands as command-line syntax failures.</summary>
	[Fact]
	public async Task WriteModeRequiresAssignment() {
		var backend = CreateBackend();
		var result = await RunAsync( [ "-w", "kernel.hostname" ], backend );

		Assert.Equal( 1, result.Status );
		Assert.Contains( "invalid syntax", result.Error );
	}

	/// <summary>All-mode filtering excludes deprecated and side-effect keys unless explicitly requested.</summary>
	[Fact]
	public async Task AllModeFiltersDeprecatedAndVerbotenKeys() {
		var backend = CreateBackend();
		backend.Values["net/ipv4/neigh/default/base_reachable_time"] = "30";
		backend.Values["net/ipv6/conf/all/stat_refresh"] = "1";
		var normal = await RunAsync( [ "-a", "-r", "^(kernel|net)" ], backend );
		var deprecated = await RunAsync( [ "-a", "--deprecated", "-r", "^(kernel|net)" ], backend );

		Assert.Contains( "kernel.hostname = icod", normal.Output );
		Assert.DoesNotContain( "base_reachable_time", normal.Output );
		Assert.DoesNotContain( "stat_refresh", normal.Output );
		Assert.Contains( "base_reachable_time", deprecated.Output );
		Assert.DoesNotContain( "stat_refresh", deprecated.Output );
	}

	/// <summary>Unknown keys fail normally and are suppressed by the procps ignore option.</summary>
	[Fact]
	public async Task IgnoreOptionSuppressesUnknownKeyFailures() {
		var backend = CreateBackend();
		var normal = await RunAsync( [ "missing.key" ], backend );
		var ignored = await RunAsync( [ "-e", "missing.key" ], backend );

		Assert.Equal( 1, normal.Status );
		Assert.Contains( "/proc/sys/missing/key", normal.Error );
		Assert.Equal( 0, ignored.Status );
		Assert.Equal( string.Empty, ignored.Error );
	}

	/// <summary>Read failures retain procps additive exit status behavior across multiple operands.</summary>
	[Fact]
	public async Task MultipleReadFailuresAccumulateStatus() {
		var backend = CreateBackend();
		var result = await RunAsync( [ "missing.one", "missing.two" ], backend );

		Assert.Equal( 2, result.Status );
	}

	/// <summary>Write permission failures are reported and return failure.</summary>
	[Fact]
	public async Task PermissionDeniedWriteReturnsFailure() {
		var backend = CreateBackend();
		backend.PermissionDeniedKeys.Add( "kernel/hostname" );
		var result = await RunAsync( [ "-w", "kernel.hostname=dotunix" ], backend );

		Assert.Equal( 1, result.Status );
		Assert.Contains( "permission denied", result.Error.ToLowerInvariant() );
		Assert.Equal( "icod", backend.Values["kernel/hostname"] );
	}

	/// <summary>Preload mode applies multiple configuration files in command-line order.</summary>
	[Fact]
	public async Task PreloadAppliesFilesInOrder() {
		var backend = CreateBackend();
		backend.ConfigurationFiles["first.conf"] = [ "kernel.hostname = first" ];
		backend.ConfigurationFiles["second.conf"] = [ "kernel.hostname = second" ];
		var result = await RunAsync( [ "-p", "first.conf", "second.conf" ], backend );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "second", backend.Values["kernel/hostname"] );
		Assert.Equal( new[] { "kernel/hostname=first", "kernel/hostname=second" }, backend.WriteLog );
	}

	/// <summary>Configuration glob assignments honor explicit glob exclusions.</summary>
	[Fact]
	public async Task ConfigurationGlobsHonorExclusions() {
		var backend = CreateBackend();
		backend.Values["net/ipv4/conf/eth0/rp_filter"] = "0";
		backend.Values["net/ipv4/conf/lo/rp_filter"] = "0";
		backend.ConfigurationFiles["network.conf"] = [
			"net.ipv4.conf.*.rp_filter = 1",
			"-net.ipv4.conf.lo.rp_filter"
		];
		var result = await RunAsync( [ "-p", "network.conf" ], backend );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "1", backend.Values["net/ipv4/conf/eth0/rp_filter"] );
		Assert.Equal( "0", backend.Values["net/ipv4/conf/lo/rp_filter"] );
	}

	/// <summary>Explicit configuration assignments override matching glob assignments.</summary>
	[Fact]
	public async Task ExplicitConfigurationAssignmentOverridesGlob() {
		var backend = CreateBackend();
		backend.Values["net/ipv4/conf/eth0/rp_filter"] = "0";
		backend.Values["net/ipv4/conf/lo/rp_filter"] = "0";
		backend.ConfigurationFiles["network.conf"] = [
			"net.ipv4.conf.*.rp_filter = 1",
			"net.ipv4.conf.lo.rp_filter = 2"
		];
		var result = await RunAsync( [ "-p", "network.conf" ], backend );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "1", backend.Values["net/ipv4/conf/eth0/rp_filter"] );
		Assert.Equal( "2", backend.Values["net/ipv4/conf/lo/rp_filter"] );
	}

	/// <summary>A leading dash on a configuration assignment ignores a write failure.</summary>
	[Fact]
	public async Task ConfigurationLeadingDashIgnoresWriteFailure() {
		var backend = CreateBackend();
		backend.PermissionDeniedKeys.Add( "kernel/hostname" );
		backend.ConfigurationFiles["ignored.conf"] = [ "-kernel.hostname = blocked" ];
		var result = await RunAsync( [ "-p", "ignored.conf" ], backend );

		Assert.Equal( 0, result.Status );
		Assert.Contains( "ignoring", result.Error );
	}

	/// <summary>A leading dash does not bypass procps' owner-write-bit safety check.</summary>
	[Fact]
	public async Task ConfigurationLeadingDashDoesNotIgnoreOwnerWriteBitRefusal() {
		var backend = CreateBackend();
		backend.NotWritableKeys.Add( "kernel/hostname" );
		backend.ConfigurationFiles["readonly.conf"] = [ "-kernel.hostname = blocked" ];
		var result = await RunAsync( [ "-p", "readonly.conf" ], backend );

		Assert.Equal( 1, result.Status );
		Assert.Contains( "Operation not permitted", result.Error );
	}

	/// <summary>System mode applies same-name precedence, global lexical order, and /etc/sysctl.conf last.</summary>
	[Fact]
	public async Task SystemModeUsesProcpsConfigurationPrecedence() {
		var backend = CreateBackend();
		backend.ConfigurationDirectories["/etc/sysctl.d"] = [ "/etc/sysctl.d/20-z.conf", "/etc/sysctl.d/50-same.conf" ];
		backend.ConfigurationDirectories["/usr/lib/sysctl.d"] = [ "/usr/lib/sysctl.d/10-a.conf", "/usr/lib/sysctl.d/50-same.conf" ];
		backend.ConfigurationFiles["/usr/lib/sysctl.d/10-a.conf"] = [ "kernel.hostname = a" ];
		backend.ConfigurationFiles["/etc/sysctl.d/20-z.conf"] = [ "kernel.hostname = z" ];
		backend.ConfigurationFiles["/etc/sysctl.d/50-same.conf"] = [ "kernel.hostname = etc" ];
		backend.ConfigurationFiles["/usr/lib/sysctl.d/50-same.conf"] = [ "kernel.hostname = vendor" ];
		backend.ConfigurationFiles["/etc/sysctl.conf"] = [ "kernel.hostname = final" ];
		var result = await RunAsync( [ "--system", "-q" ], backend );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "final", backend.Values["kernel/hostname"] );
		Assert.Equal(
			new[] { "kernel/hostname=a", "kernel/hostname=z", "kernel/hostname=etc", "kernel/hostname=final" },
			backend.WriteLog
		);
		Assert.DoesNotContain( "vendor", string.Join( "|", backend.WriteLog ) );
	}

	/// <summary>Preload mode can consume configuration text from injected standard input.</summary>
	[Fact]
	public async Task PreloadCanReadStandardInput() {
		var backend = CreateBackend();
		using var input = new MemoryStream( Utf8.GetBytes( "kernel.hostname = stdin\n" ) );
		var result = await RunAsync( [ "-p", "-" ], backend, stdin: input );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "stdin", backend.Values["kernel/hostname"] );
	}

	/// <summary>Name-only and quiet options are rejected together for direct operations.</summary>
	[Fact]
	public async Task NamesAndQuietCannotCoexist() {
		var backend = CreateBackend();
		var result = await RunAsync( [ "-Nq", "kernel.hostname" ], backend );

		Assert.Equal( 1, result.Status );
		Assert.Contains( "cannot coexist", result.Error );
	}

	/// <summary>Help and version work without a Linux backend.</summary>
	[Fact]
	public async Task HelpAndVersionRemainPortable() {
		var backend = CreateBackend( supported: false );
		var help = await RunAsync( [ "--help" ], backend );
		var version = await RunAsync( [ "--version" ], backend );

		Assert.Equal( 0, help.Status );
		Assert.Contains( "Usage:", help.Output );
		Assert.Equal( 0, version.Status );
		Assert.Contains( "procps-ng 4.0.6", version.Output );
	}

	/// <summary>Operational requests fail explicitly when Linux /proc/sys capability is unavailable.</summary>
	[Fact]
	public async Task UnsupportedBackendReturnsControlledFailure() {
		var backend = CreateBackend( supported: false );
		var result = await RunAsync( [ "kernel.hostname" ], backend );

		Assert.Equal( 1, result.Status );
		Assert.Contains( "Linux /proc/sys", result.Error );
	}

	/// <summary>Cancellation maps to the conventional command cancellation status.</summary>
	[Fact]
	public async Task CancellationReturnsStatus130() {
		var backend = CreateBackend();
		using var source = new CancellationTokenSource();
		source.Cancel();
		var result = await RunAsync( [ "kernel.hostname" ], backend, cancellationToken: source.Token );

		Assert.Equal( 130, result.Status );
	}

	private static FakeSysctlBackend CreateBackend( bool supported = true ) {
		var backend = new FakeSysctlBackend {
			IsSupported = supported,
			UnsupportedReason = "Linux /proc/sys kernel parameter access is not supported on this platform"
		};
		backend.Values["kernel/hostname"] = "icod";
		backend.Values["kernel/pid_max"] = "4194304";
		backend.Values["vm/swappiness"] = "60";
		return backend;
	}

	private static async Task<CommandResult> RunAsync(
		string[] args,
		FakeSysctlBackend backend,
		Stream? stdin = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( backend );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = await Command.RunAsync(
			args,
			stdin: stdin ?? Stream.Null,
			stdout: output,
			stderr: error,
			backend: backend,
			cancellationToken: cancellationToken
		);
		return new CommandResult( status, Decode( output ), Decode( error ) );
	}

	private static string Decode( MemoryStream stream ) {
		ArgumentNullException.ThrowIfNull( stream );
		return Utf8.GetString( stream.ToArray() );
	}

	private sealed record CommandResult( int Status, string Output, string Error );

	private sealed class FakeSysctlBackend : ISysctlBackend {
		public bool IsSupported { get; set; } = true;

		public string UnsupportedReason { get; set; } = "unsupported";

		public Dictionary<string, string> Values { get; } = new( StringComparer.Ordinal );

		public Dictionary<string, IReadOnlyList<string>> ConfigurationFiles { get; } = new( StringComparer.Ordinal );

		public Dictionary<string, IReadOnlyList<string>> ConfigurationDirectories { get; } = new( StringComparer.Ordinal );

		public HashSet<string> PermissionDeniedKeys { get; } = new( StringComparer.Ordinal );

		public HashSet<string> NotWritableKeys { get; } = new( StringComparer.Ordinal );

		public List<string> DryRunKeys { get; } = [];

		public List<string> WriteLog { get; } = [];

		public Task<string> ReadValueAsync( string key, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( key );
			cancellationToken.ThrowIfCancellationRequested();
			if ( this.Values.TryGetValue( key, out var value ) ) {
				return Task.FromResult( value );
			}
			var prefix = string.Concat( key.TrimEnd( '/' ), "/" );
			if ( this.Values.Keys.Any( candidate => candidate.StartsWith( prefix, StringComparison.Ordinal ) ) ) {
				throw new SysctlBackendException( SysctlBackendFailureKind.IsDirectory, "Is a directory" );
			}
			throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" );
		}

		public Task WriteValueAsync( string key, string value, bool dryRun, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( key );
			ArgumentNullException.ThrowIfNull( value );
			cancellationToken.ThrowIfCancellationRequested();
			if ( !this.Values.ContainsKey( key ) ) {
				throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" );
			}
			if ( this.NotWritableKeys.Contains( key ) ) {
				throw new SysctlBackendException( SysctlBackendFailureKind.NotWritable, "Operation not permitted" );
			}
			if ( this.PermissionDeniedKeys.Contains( key ) ) {
				throw new SysctlBackendException( SysctlBackendFailureKind.PermissionDenied, "Permission denied" );
			}
			if ( dryRun ) {
				this.DryRunKeys.Add( key );
				return Task.CompletedTask;
			}
			this.Values[key] = value;
			this.WriteLog.Add( string.Concat( key, "=", value ) );
			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<string>> EnumerateKeysAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult<IReadOnlyList<string>>( this.Values.Keys.OrderBy( static key => key, StringComparer.Ordinal ).ToArray() );
		}

		public Task<IReadOnlyList<string>> ExpandConfigurationFilesAsync( string pattern, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( pattern );
			cancellationToken.ThrowIfCancellationRequested();
			if ( 0 > pattern.IndexOfAny( [ '*', '?', '[' ] ) ) {
				return Task.FromResult<IReadOnlyList<string>>( [ pattern ] );
			}
			var matches = this.ConfigurationFiles.Keys
				.Where( candidate => GlobMatch( pattern, candidate ) )
				.OrderBy( static candidate => candidate, StringComparer.Ordinal )
				.ToArray();
			if ( 0 == matches.Length ) {
				return Task.FromResult<IReadOnlyList<string>>( [ pattern ] );
			}
			return Task.FromResult<IReadOnlyList<string>>( matches );
		}

		public Task<IReadOnlyList<string>> EnumerateConfigurationFilesAsync( string directory, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( directory );
			cancellationToken.ThrowIfCancellationRequested();
			if ( this.ConfigurationDirectories.TryGetValue( directory, out var files ) ) {
				return Task.FromResult( files );
			}
			return Task.FromResult<IReadOnlyList<string>>( [] );
		}

		public Task<bool> ConfigurationFileExistsAsync( string path, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( path );
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( this.ConfigurationFiles.ContainsKey( path ) );
		}

		public Task<IReadOnlyList<string>> ReadConfigurationFileAsync( string path, CancellationToken cancellationToken = default ) {
			ArgumentNullException.ThrowIfNull( path );
			cancellationToken.ThrowIfCancellationRequested();
			if ( this.ConfigurationFiles.TryGetValue( path, out var lines ) ) {
				return Task.FromResult( lines );
			}
			throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" );
		}

		private static bool GlobMatch( string pattern, string value ) {
			ArgumentNullException.ThrowIfNull( pattern );
			ArgumentNullException.ThrowIfNull( value );
			var patternIndex = 0;
			var valueIndex = 0;
			var starIndex = -1;
			var retryValueIndex = -1;
			while ( valueIndex < value.Length ) {
				if ( patternIndex < pattern.Length && ( '?' == pattern[patternIndex] || pattern[patternIndex] == value[valueIndex] ) ) {
					patternIndex++;
					valueIndex++;
					continue;
				}
				if ( patternIndex < pattern.Length && '*' == pattern[patternIndex] ) {
					starIndex = patternIndex++;
					retryValueIndex = valueIndex;
					continue;
				}
				if ( 0 <= starIndex ) {
					patternIndex = starIndex + 1;
					valueIndex = ++retryValueIndex;
					continue;
				}
				return false;
			}
			while ( patternIndex < pattern.Length && '*' == pattern[patternIndex] ) {
				patternIndex++;
			}
			return patternIndex == pattern.Length;
		}
	}
}

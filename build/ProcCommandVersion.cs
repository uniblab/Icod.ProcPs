namespace Icod.ProcPs;

using System.Reflection;

/// <summary>Formats standalone Icod.ProcPs command identity and compatibility versions.</summary>
internal static class ProcCommandVersion {
	private const string ProcpsNgCompatibilityVersion = "4.0.6";

	internal static string Format(
		string productName,
		Assembly assembly
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( productName );
		ArgumentNullException.ThrowIfNull( assembly );
		return $"{productName} ({GetVersionText( assembly )}) inspired by procps-ng {ProcpsNgCompatibilityVersion}";
	}

	private static string GetVersionText( Assembly assembly ) {
		ArgumentNullException.ThrowIfNull( assembly );
		string? informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;
		if ( !string.IsNullOrWhiteSpace( informationalVersion ) ) {
			int metadataSeparator = informationalVersion.IndexOf( '+' );
			if ( 0 <= metadataSeparator ) {
				return informationalVersion[ ..metadataSeparator ];
			}
			return informationalVersion;
		}

		Version? assemblyVersion = assembly.GetName().Version;
		if ( assemblyVersion is null ) {
			return "unknown";
		}
		return assemblyVersion.ToString( 3 );
	}
}

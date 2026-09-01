/*
	top
	Interactively display processes and system activity.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Icod.ProcPs.Top;

using System.Globalization;
using System.Text;
using Icod.Processes;
using Icod.ProcPs.Shared;

/// <summary>Identifies one procps bottom-window presentation.</summary>
internal enum TopBottomWindowKind {
	Capabilities,
	ControlGroups,
	CommandLine,
	LoggedMessages,
	Environment,
	Namespaces,
	SupplementaryGroups
}

/// <summary>Tracks the transient selection and content of one bottom window.</summary>
internal sealed class TopBottomWindowState {
	private IReadOnlyList<string> elements = [];

	internal TopBottomWindowState(
		TopBottomWindowKind kind
	) {
		if ( !Enum.IsDefined( kind ) ) {
			throw new ArgumentOutOfRangeException( nameof( kind ) );
		}
		this.Kind = kind;
		this.Title = TitleFor( kind );
	}

	internal TopBottomWindowKind Kind { get; }
	internal string Title { get; private set; }
	internal IReadOnlyList<string> Elements => this.elements;
	internal int SelectedIndex { get; private set; }
	internal ProcessIdentity? TargetIdentity { get; private set; }
	internal TopSample? SourceSample { get; private set; }

	internal bool IsCurrent(
		ProcessIdentity identity,
		TopSample sample
	) {
		ArgumentNullException.ThrowIfNull( identity );
		ArgumentNullException.ThrowIfNull( sample );
		return this.TargetIdentity is not null
			&& this.TargetIdentity.Equals( identity )
			&& ReferenceEquals( this.SourceSample, sample );
	}

	internal void ReplaceContent(
		ProcessIdentity? targetIdentity,
		TopSample sample,
		string title,
		IEnumerable<string> elements
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentException.ThrowIfNullOrWhiteSpace( title );
		ArgumentNullException.ThrowIfNull( elements );

		this.TargetIdentity = targetIdentity;
		this.SourceSample = sample;
		this.Title = title;
		this.elements = elements.ToArray();
		if ( 0 == this.elements.Count ) {
			this.elements = [ "unavailable" ];
		}
		if ( this.elements.Count <= this.SelectedIndex ) {
			this.SelectedIndex = this.elements.Count - 1;
		}
		if ( 0 > this.SelectedIndex ) {
			this.SelectedIndex = 0;
		}
	}

	internal void MoveSelection(
		int direction
	) {
		if ( direction is not -1 and not 1 ) {
			throw new ArgumentOutOfRangeException( nameof( direction ) );
		}
		if ( 0 == this.elements.Count ) {
			return;
		}
		this.SelectedIndex += direction;
		if ( 0 > this.SelectedIndex ) {
			this.SelectedIndex = this.elements.Count - 1;
		} else if ( this.elements.Count <= this.SelectedIndex ) {
			this.SelectedIndex = 0;
		}
	}

	internal static string TitleFor(
		TopBottomWindowKind kind
	) {
		return kind switch {
			TopBottomWindowKind.Capabilities => "Capabilities",
			TopBottomWindowKind.ControlGroups => "Control Groups",
			TopBottomWindowKind.CommandLine => "Command Line",
			TopBottomWindowKind.LoggedMessages => "Logged Messages",
			TopBottomWindowKind.Environment => "Environment",
			TopBottomWindowKind.Namespaces => "Namespaces",
			TopBottomWindowKind.SupplementaryGroups => "Supplementary Groups",
			_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
		};
	}
}

/// <summary>Handles procps bottom-window control keys and selection movement.</summary>
internal static class TopBottomWindowCommands {
	internal static bool TryHandle(
		TopInputEvent input,
		TopRuntimeState state,
		out TopCommandAction action
	) {
		ArgumentNullException.ThrowIfNull( state );

		if (
			TopInputKey.Tab == input.Key
			&& state.BottomWindow is not null
		) {
			int direction = 0 != ( input.Modifiers & TopInputModifiers.Shift )
				? -1
				: 1;
			state.BottomWindow.MoveSelection(
				direction
			);
			action = TopCommandAction.Rerender;
			return true;
		}
		if (
			TopInputKey.Character != input.Key
			|| !input.Character.HasValue
			|| 0 == ( input.Modifiers & TopInputModifiers.Control )
		) {
			action = TopCommandAction.None;
			return false;
		}

		char key = NormalizeControlCharacter(
			input.Character.Value.Value
		);
		TopBottomWindowKind? kind = key switch {
			'a' => TopBottomWindowKind.Capabilities,
			'g' => TopBottomWindowKind.ControlGroups,
			'k' => TopBottomWindowKind.CommandLine,
			'l' => TopBottomWindowKind.LoggedMessages,
			'n' => TopBottomWindowKind.Environment,
			'p' => TopBottomWindowKind.Namespaces,
			'u' => TopBottomWindowKind.SupplementaryGroups,
			_ => null
		};
		if ( !kind.HasValue ) {
			action = TopCommandAction.None;
			return false;
		}

		if ( state.BottomWindow?.Kind == kind.Value ) {
			state.BottomWindow = null;
		} else {
			state.BottomWindow = new TopBottomWindowState(
				kind.Value
			);
		}
		state.Message = null;
		action = TopCommandAction.Rerender;
		return true;
	}

	private static char NormalizeControlCharacter(
		int value
	) {
		if ( 1 <= value && value <= 26 ) {
			return (char)( 'a' + value - 1 );
		}
		if ( 'A' <= value && value <= 'Z' ) {
			return char.ToLowerInvariant( (char)value );
		}
		if ( 'a' <= value && value <= 'z' ) {
			return (char)value;
		}
		return '\0';
	}
}

/// <summary>Refreshes bottom-window content from the currently displayed task.</summary>
internal static class TopBottomWindowController {
	private static readonly (string Field, string Label)[] CapabilityFields = [
		( "CapInh", "Inheritable" ),
		( "CapPrm", "Permitted" ),
		( "CapEff", "Effective" ),
		( "CapBnd", "Bounding" ),
		( "CapAmb", "Ambient" )
	];

	private static readonly string[] CapabilityNames = [
		"CHOWN", "DAC_OVERRIDE", "DAC_READ_SEARCH", "FOWNER", "FSETID",
		"KILL", "SETGID", "SETUID", "SETPCAP", "LINUX_IMMUTABLE",
		"NET_BIND_SERVICE", "NET_BROADCAST", "NET_ADMIN", "NET_RAW", "IPC_LOCK",
		"IPC_OWNER", "SYS_MODULE", "SYS_RAWIO", "SYS_CHROOT", "SYS_PTRACE",
		"SYS_PACCT", "SYS_ADMIN", "SYS_BOOT", "SYS_NICE", "SYS_RESOURCE",
		"SYS_TIME", "SYS_TTY_CONFIG", "MKNOD", "LEASE", "AUDIT_WRITE",
		"AUDIT_CONTROL", "SETFCAP", "MAC_OVERRIDE", "MAC_ADMIN", "SYSLOG",
		"WAKE_ALARM", "BLOCK_SUSPEND", "AUDIT_READ", "PERFMON", "BPF",
		"CHECKPOINT_RESTORE"
	];

	internal static async Task RefreshAsync(
		TopSample sample,
		TopRuntimeState state,
		IProcMatchSupplementProvider supplementProvider,
		IProcAccountDisplayResolver accountResolver,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		ArgumentNullException.ThrowIfNull( supplementProvider );
		ArgumentNullException.ThrowIfNull( accountResolver );

		TopBottomWindowState? bottom = state.BottomWindow;
		if ( bottom is null ) {
			return;
		}
		if ( TopBottomWindowKind.LoggedMessages == bottom.Kind ) {
			bottom.ReplaceContent(
				null,
				sample,
				TopBottomWindowState.TitleFor( bottom.Kind ),
				0 == state.MessageHistory.Count
					? [ "no messages have been logged" ]
					: state.MessageHistory
			);
			return;
		}

		TopTaskRow? row = TopRenderer.GetTopmostTask(
			sample,
			state
		);
		if ( row is null ) {
			bottom.ReplaceContent(
				null,
				sample,
				TopBottomWindowState.TitleFor( bottom.Kind ),
				[ "no task is displayed" ]
			);
			return;
		}
		ProcProcessSnapshot process = row.Process;
		if ( bottom.IsCurrent( process.Identity, sample ) ) {
			return;
		}

		IReadOnlyList<string> elements;
		switch ( bottom.Kind ) {
			case TopBottomWindowKind.ControlGroups:
				elements = BuildControlGroups( process );
				break;
			case TopBottomWindowKind.CommandLine:
				elements = BuildCommandLine( process );
				break;
			case TopBottomWindowKind.Namespaces:
				elements = BuildNamespaces( process );
				break;
			case TopBottomWindowKind.Capabilities:
			case TopBottomWindowKind.Environment:
			case TopBottomWindowKind.SupplementaryGroups: {
				ProcMatchSupplement? supplement = await ReadSupplementAsync(
					process,
					supplementProvider,
					cancellationToken
				).ConfigureAwait( false );
				elements = bottom.Kind switch {
					TopBottomWindowKind.Capabilities => BuildCapabilities( supplement ),
					TopBottomWindowKind.Environment => BuildEnvironment( supplement ),
					TopBottomWindowKind.SupplementaryGroups => BuildGroups(
						supplement,
						accountResolver
					),
					_ => throw new InvalidOperationException(
						"The selected bottom window is not supplement-backed."
					)
				};
				break;
			}
			default:
				throw new InvalidOperationException(
					$"Unsupported bottom window kind '{bottom.Kind}'."
				);
		}

		bottom.ReplaceContent(
			process.Identity,
			sample,
			$"{TopBottomWindowState.TitleFor( bottom.Kind )} - pid {process.ProcessId}",
			elements
		);
	}

	private static IReadOnlyList<string> BuildControlGroups(
		ProcProcessSnapshot process
	) {
		ArgumentNullException.ThrowIfNull( process );
		if ( !process.Container.HasValue ) {
			return [ "control-group information is unavailable" ];
		}
		ProcContainerInfo container = process.Container.Value;
		var result = new List<string> {
			$"path: {container.CgroupPath}"
		};
		if ( !string.IsNullOrEmpty( container.Runtime ) ) {
			result.Add( $"runtime: {container.Runtime}" );
		}
		if ( !string.IsNullOrEmpty( container.ContainerId ) ) {
			result.Add( $"container: {container.ContainerId}" );
		}
		return result;
	}

	private static IReadOnlyList<string> BuildCommandLine(
		ProcProcessSnapshot process
	) {
		ArgumentNullException.ThrowIfNull( process );
		if (
			!process.CommandLineArguments.HasValue
			|| 0 == process.CommandLineArguments.Value.Count
		) {
			return [ "command line is unavailable" ];
		}
		return process.CommandLineArguments.Value
			.Select(
				( value, index ) => $"[{index}] {value}"
			)
			.ToArray();
	}

	private static IReadOnlyList<string> BuildNamespaces(
		ProcProcessSnapshot process
	) {
		ArgumentNullException.ThrowIfNull( process );
		if (
			!process.Namespaces.HasValue
			|| 0 == process.Namespaces.Value.Count
		) {
			return [ "namespace information is unavailable" ];
		}
		return process.Namespaces.Value
			.OrderBy(
				pair => pair.Key,
				StringComparer.Ordinal
			)
			.Select(
				pair => $"{pair.Key}: {pair.Value.LinkTarget}"
			)
			.ToArray();
	}

	private static IReadOnlyList<string> BuildCapabilities(
		ProcMatchSupplement? supplement
	) {
		if (
			supplement is null
			|| !supplement.LinuxStatusFields.HasValue
		) {
			return [ "capability information is unavailable" ];
		}
		IReadOnlyDictionary<string, string> fields = supplement.LinuxStatusFields.Value;
		var result = new List<string>();
		foreach ( var definition in CapabilityFields ) {
			if ( !fields.TryGetValue( definition.Field, out string? text ) ) {
				continue;
			}
			result.Add(
				$"{definition.Label}: {FormatCapabilityMask( text )}"
			);
		}
		return 0 == result.Count
			? [ "capability information is unavailable" ]
			: result;
	}

	private static IReadOnlyList<string> BuildEnvironment(
		ProcMatchSupplement? supplement
	) {
		if (
			supplement is null
			|| !supplement.Environment.HasValue
			|| 0 == supplement.Environment.Value.Count
		) {
			return [ "environment is unavailable or empty" ];
		}
		return supplement.Environment.Value.ToArray();
	}

	private static IReadOnlyList<string> BuildGroups(
		ProcMatchSupplement? supplement,
		IProcAccountDisplayResolver accountResolver
	) {
		ArgumentNullException.ThrowIfNull( accountResolver );
		if (
			supplement is null
			|| !supplement.LinuxStatusFields.HasValue
			|| !supplement.LinuxStatusFields.Value.TryGetValue(
				"Groups",
				out string? text
			)
		) {
			return [ "supplementary groups are unavailable" ];
		}
		var result = new List<string>();
		foreach ( string token in text.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
		) ) {
			if ( !uint.TryParse(
				token,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out uint groupId
			) ) {
				continue;
			}
			result.Add(
				accountResolver.TryGetGroupName( groupId, out string name )
					? $"{groupId} ({name})"
					: groupId.ToString( CultureInfo.InvariantCulture )
			);
		}
		return 0 == result.Count
			? [ "no supplementary groups are reported" ]
			: result;
	}

	private static async Task<ProcMatchSupplement?> ReadSupplementAsync(
		ProcProcessSnapshot process,
		IProcMatchSupplementProvider supplementProvider,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( process );
		ArgumentNullException.ThrowIfNull( supplementProvider );

		IReadOnlyList<ProcMatchCandidate> candidates = await supplementProvider
			.GetCandidatesAsync(
				[ process ],
				false,
				cancellationToken
			)
			.ConfigureAwait( false );
		foreach ( ProcMatchCandidate candidate in candidates ) {
			if ( candidate.Process.Identity.Equals( process.Identity ) ) {
				return candidate.Supplement;
			}
		}
		return 0 < candidates.Count
			? candidates[ 0 ].Supplement
			: null;
	}

	private static string FormatCapabilityMask(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );
		if ( !ulong.TryParse(
			text,
			NumberStyles.AllowHexSpecifier,
			CultureInfo.InvariantCulture,
			out ulong mask
		) ) {
			return text;
		}
		var names = new List<string>();
		for ( int index = 0; index < CapabilityNames.Length; index++ ) {
			if ( 0UL != ( mask & ( 1UL << index ) ) ) {
				names.Add( CapabilityNames[ index ] );
			}
		}
		return 0 == names.Count
			? $"{text} [none]"
			: $"{text} [{string.Join( ", ", names )}]";
	}
}

/// <summary>Overlays the active procps bottom window on a normal top frame.</summary>
internal static class TopBottomWindowRenderer {
	internal static TopRenderFrame Apply(
		TopRenderFrame frame,
		TopBottomWindowState? bottom
	) {
		ArgumentNullException.ThrowIfNull( frame );
		if ( bottom is null ) {
			return frame;
		}

		var lines = frame.Lines.ToList();
		while ( lines.Count < frame.Rows ) {
			lines.Add( new TopRenderLine( string.Empty ) );
		}
		int maximumRows = Math.Max(
			2,
			frame.Rows / 3
		);
		int desiredRows = Math.Max(
			2,
			bottom.Elements.Count + 1
		);
		int windowRows = Math.Min(
			frame.Rows,
			Math.Min(
				maximumRows,
				desiredRows
			)
		);
		int contentRows = windowRows - 1;
		int firstElement = Math.Clamp(
			bottom.SelectedIndex - contentRows + 1,
			0,
			Math.Max(
				0,
				bottom.Elements.Count - contentRows
			)
		);
		int firstRow = frame.Rows - windowRows;
		lines[ firstRow ] = new TopRenderLine(
			LimitRunes(
				$"{bottom.Title}  [Tab/Shift+Tab selects; same Ctrl key or = closes]",
				frame.Columns
			),
			TopLineStyle.Header
		);
		for ( int row = 0; row < contentRows; row++ ) {
			int elementIndex = firstElement + row;
			if ( bottom.Elements.Count <= elementIndex ) {
				lines[ firstRow + row + 1 ] = new TopRenderLine( string.Empty );
				continue;
			}
			bool selected = elementIndex == bottom.SelectedIndex;
			string prefix = selected ? "> " : "  ";
			lines[ firstRow + row + 1 ] = new TopRenderLine(
				LimitRunes(
					prefix + Sanitize( bottom.Elements[ elementIndex ] ),
					frame.Columns
				),
				selected
					? TopLineStyle.HighlightReverse
					: TopLineStyle.Message
			);
		}
		return new TopRenderFrame(
			lines,
			frame.Columns,
			frame.Rows,
			frame.BoldEnabled
		);
	}

	internal static string Sanitize(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );
		var builder = new StringBuilder();
		foreach ( Rune rune in text.EnumerateRunes() ) {
			int value = rune.Value;
			if (
				value < 0x20
				|| ( 0x7f <= value && value <= 0x9f )
			) {
				builder.Append( ' ' );
			} else {
				builder.Append( rune.ToString() );
			}
		}
		return builder.ToString();
	}

	private static string LimitRunes(
		string text,
		int width
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		if ( text.EnumerateRunes().Count() <= width ) {
			return text;
		}
		var builder = new StringBuilder();
		int count = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			if ( width <= count ) {
				break;
			}
			builder.Append( rune.ToString() );
			count++;
		}
		return builder.ToString();
	}
}

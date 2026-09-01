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

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

/// <summary>Identifies the source used by one procps top Inspect entry.</summary>
internal enum TopInspectEntryType {
	File,
	Pipe
}

/// <summary>Represents one persistent procps top Inspect entry.</summary>
internal sealed class TopInspectEntry {
	internal TopInspectEntry(
		TopInspectEntryType type,
		string name,
		string format
	) {
		if ( !Enum.IsDefined( typeof( TopInspectEntryType ), type ) ) {
			throw new ArgumentOutOfRangeException( nameof( type ) );
		}
		ValidateComponent(
			name,
			nameof( name )
		);
		ValidateComponent(
			format,
			nameof( format )
		);

		this.Type = type;
		this.Name = name;
		this.Format = format;
	}

	internal TopInspectEntryType Type { get; }
	internal string Name { get; }
	internal string Format { get; }

	internal string Expand(
		int processId
	) {
		if ( 1 > processId ) {
			throw new ArgumentOutOfRangeException( nameof( processId ) );
		}
		return this.Format.Replace(
			"%d",
			processId.ToString(
				CultureInfo.InvariantCulture
			),
			StringComparison.Ordinal
		);
	}

	internal string ToNativeLine() {
		string type = ( TopInspectEntryType.File == this.Type )
			? "file"
			: "pipe"
		;
		return string.Concat(
			type,
			"\t",
			this.Name,
			"\t",
			this.Format
		);
	}

	internal static TopInspectEntry ParseNative(
		string line
	) {
		ArgumentNullException.ThrowIfNull( line );

		string[] parts = line.Split(
			'\t'
		);
		if ( 3 != parts.Length ) {
			throw new FormatException(
				"a procps Inspect entry must contain exactly two tab separators"
			);
		}

		TopInspectEntryType type;
		if ( string.Equals( parts[ 0 ], "file", StringComparison.Ordinal ) ) {
			type = TopInspectEntryType.File;
		} else if ( string.Equals( parts[ 0 ], "pipe", StringComparison.Ordinal ) ) {
			type = TopInspectEntryType.Pipe;
		} else {
			throw new FormatException(
				$"unsupported procps Inspect entry type '{parts[ 0 ]}'"
			);
		}

		try {
			return new TopInspectEntry(
				type,
				parts[ 1 ],
				parts[ 2 ]
			);
		} catch ( ArgumentException exception ) {
			throw new FormatException(
				"the procps Inspect entry contains an invalid name or format",
				exception
			);
		}
	}

	private static void ValidateComponent(
		string value,
		string parameterName
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( value );
		ArgumentException.ThrowIfNullOrWhiteSpace( parameterName );

		if (
			value.Contains( '\t' )
			|| value.Contains( '\r' )
			|| value.Contains( '\n' )
		) {
			throw new ArgumentException(
				"Inspect entry values cannot contain tabs or line breaks.",
				parameterName
			);
		}
	}
}

/// <summary>Identifies the result of one input event handled by the Inspect screen.</summary>
internal enum TopInspectInputResult {
	None,
	Changed,
	Close
}

/// <summary>Owns one paused procps-compatible Inspect chooser and output viewer.</summary>
internal sealed class TopInspectSession {
	private readonly IReadOnlyList<TopInspectEntry> entries;
	private IReadOnlyList<string> lines = Array.Empty<string>();

	internal TopInspectSession(
		int processId,
		IReadOnlyList<TopInspectEntry> entries
	) {
		if ( 1 > processId ) {
			throw new ArgumentOutOfRangeException( nameof( processId ) );
		}
		ArgumentNullException.ThrowIfNull( entries );
		if ( 0 == entries.Count ) {
			throw new ArgumentException(
				"At least one Inspect entry is required.",
				nameof( entries )
			);
		}

		this.ProcessId = processId;
		this.entries = entries.ToArray();
	}

	internal int ProcessId { get; }
	internal IReadOnlyList<TopInspectEntry> Entries => this.entries;
	internal int SelectedIndex { get; private set; }
	internal TopInspectEntry? ActiveEntry { get; private set; }
	internal IReadOnlyList<string> Lines => this.lines;
	internal int VerticalOffset { get; private set; }
	internal int HorizontalOffset { get; private set; }
	internal bool ShowSource { get; private set; }
	internal bool SearchActive { get; private set; }
	internal string SearchBuffer { get; private set; } = string.Empty;
	internal string? SearchText { get; private set; }
	internal string? Message { get; private set; }
	internal bool ViewingOutput => this.ActiveEntry is not null;

	internal async ValueTask<TopInspectInputResult> HandleInputAsync(
		TopInputEvent input,
		TopTerminalDimensions dimensions,
		CancellationToken cancellationToken
	) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( this.SearchActive ) {
			return this.HandleSearchInput(
				input
			);
		}
		if ( !this.ViewingOutput ) {
			return await this.HandleChooserInputAsync(
				input,
				cancellationToken
			).ConfigureAwait( false );
		}
		return this.HandleViewerInput(
			input,
			dimensions
		);
	}

	private async ValueTask<TopInspectInputResult> HandleChooserInputAsync(
		TopInputEvent input,
		CancellationToken cancellationToken
	) {
		if ( input.Key is TopInputKey.Escape or TopInputKey.EndOfInput ) {
			return TopInspectInputResult.Close;
		}
		if ( TopInputKey.Left == input.Key || TopInputKey.Up == input.Key ) {
			this.SelectedIndex = (
				this.SelectedIndex
				+ this.entries.Count
				- 1
			) % this.entries.Count;
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Right == input.Key || TopInputKey.Down == input.Key ) {
			this.SelectedIndex = (
				this.SelectedIndex + 1
			) % this.entries.Count;
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Home == input.Key ) {
			this.SelectedIndex = 0;
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.End == input.Key ) {
			this.SelectedIndex = this.entries.Count - 1;
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Enter == input.Key ) {
			await this.OpenSelectedAsync(
				cancellationToken
			).ConfigureAwait( false );
			return TopInspectInputResult.Changed;
		}
		if (
			TopInputKey.Character == input.Key
			&& input.Character.HasValue
			&& input.Character.Value.Value is 'q' or 'Q'
		) {
			return TopInspectInputResult.Close;
		}
		return TopInspectInputResult.None;
	}

	private TopInspectInputResult HandleViewerInput(
		TopInputEvent input,
		TopTerminalDimensions dimensions
	) {
		if ( input.Key is TopInputKey.Escape or TopInputKey.EndOfInput ) {
			return TopInspectInputResult.Close;
		}

		int pageSize = Math.Max(
			1,
			dimensions.Rows - 4
		);
		if ( TopInputKey.Up == input.Key ) {
			this.VerticalOffset = Math.Max(
				0,
				this.VerticalOffset - 1
			);
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Down == input.Key ) {
			this.VerticalOffset = Math.Min(
				Math.Max( 0, this.lines.Count - 1 ),
				this.VerticalOffset + 1
			);
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.PageUp == input.Key ) {
			this.ScrollPage(
				-pageSize
			);
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.PageDown == input.Key ) {
			this.ScrollPage(
				pageSize
			);
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Home == input.Key ) {
			this.VerticalOffset = 0;
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.End == input.Key ) {
			this.VerticalOffset = Math.Max(
				0,
				this.lines.Count - pageSize
			);
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Left == input.Key ) {
			this.HorizontalOffset = Math.Max(
				0,
				this.HorizontalOffset - 8
			);
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Right == input.Key ) {
			this.HorizontalOffset += 8;
			return TopInspectInputResult.Changed;
		}
		if (
			TopInputKey.Character != input.Key
			|| !input.Character.HasValue
		) {
			return TopInspectInputResult.None;
		}

		int value = input.Character.Value.Value;
		if ( 'q' == value || 'Q' == value ) {
			return TopInspectInputResult.Close;
		}
		if ( ' ' == value ) {
			this.ScrollPage(
				pageSize
			);
			return TopInspectInputResult.Changed;
		}
		if ( 'b' == value ) {
			this.ScrollPage(
				-pageSize
			);
			return TopInspectInputResult.Changed;
		}
		if ( 'g' == value ) {
			this.VerticalOffset = 0;
			return TopInspectInputResult.Changed;
		}
		if ( 'G' == value ) {
			this.VerticalOffset = Math.Max(
				0,
				this.lines.Count - pageSize
			);
			return TopInspectInputResult.Changed;
		}
		if ( '=' == value ) {
			this.ShowSource = !this.ShowSource;
			return TopInspectInputResult.Changed;
		}
		if ( '/' == value || 'L' == value ) {
			this.SearchActive = true;
			this.SearchBuffer = string.Empty;
			this.Message = null;
			return TopInspectInputResult.Changed;
		}
		if ( 'n' == value || '&' == value ) {
			if ( string.IsNullOrEmpty( this.SearchText ) ) {
				this.Message = "no Inspect search string is active";
			} else {
				this.Find(
					this.SearchText,
					this.VerticalOffset + 1
				);
			}
			return TopInspectInputResult.Changed;
		}
		return TopInspectInputResult.None;
	}

	private TopInspectInputResult HandleSearchInput(
		TopInputEvent input
	) {
		if ( TopInputKey.Escape == input.Key ) {
			this.SearchActive = false;
			this.SearchBuffer = string.Empty;
			this.Message = "Inspect search canceled";
			return TopInspectInputResult.Changed;
		}
		if (
			TopInputKey.Backspace == input.Key
			|| TopInputKey.Delete == input.Key
		) {
			this.SearchBuffer = RemoveLastRune(
				this.SearchBuffer
			);
			return TopInspectInputResult.Changed;
		}
		if (
			TopInputKey.Character == input.Key
			&& input.Character.HasValue
			&& !Rune.IsControl( input.Character.Value )
		) {
			this.SearchBuffer += input.Character.Value.ToString();
			return TopInspectInputResult.Changed;
		}
		if ( TopInputKey.Enter != input.Key ) {
			return TopInspectInputResult.None;
		}

		this.SearchActive = false;
		this.SearchText = this.SearchBuffer;
		this.SearchBuffer = string.Empty;
		if ( string.IsNullOrEmpty( this.SearchText ) ) {
			this.Message = "Inspect search disabled";
			return TopInspectInputResult.Changed;
		}
		this.Find(
			this.SearchText,
			this.VerticalOffset
		);
		return TopInspectInputResult.Changed;
	}

	private async ValueTask OpenSelectedAsync(
		CancellationToken cancellationToken
	) {
		TopInspectEntry entry = this.entries[
			this.SelectedIndex
		];
		this.ActiveEntry = entry;
		this.VerticalOffset = 0;
		this.HorizontalOffset = 0;
		this.ShowSource = false;
		this.SearchActive = false;
		this.SearchBuffer = string.Empty;
		this.SearchText = null;
		this.Message = null;

		try {
			this.lines = await TopInspectExecutor.ExecuteAsync(
				entry,
				this.ProcessId,
				cancellationToken
			).ConfigureAwait( false );
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or Win32Exception
		) {
			this.lines = [
				$"Inspect failed: {exception.Message}"
			];
		}
	}

	private void ScrollPage(
		int amount
	) {
		this.VerticalOffset = Math.Clamp(
			this.VerticalOffset + amount,
			0,
			Math.Max( 0, this.lines.Count - 1 )
		);
	}

	private void Find(
		string text,
		int startIndex
	) {
		for (
			int index = Math.Max( 0, startIndex );
			index < this.lines.Count;
			index++
		) {
			if (
				this.lines[ index ].Contains(
					text,
					StringComparison.Ordinal
				)
			) {
				this.VerticalOffset = index;
				this.Message = null;
				return;
			}
		}
		this.Message = $"Inspect string not found: {text}";
	}

	private static string RemoveLastRune(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );
		if ( 0 == text.Length ) {
			return text;
		}

		int index = text.Length;
		do {
			index--;
		} while (
			0 < index
			&& char.IsLowSurrogate( text[ index ] )
		);
		return text[
			..index
		];
	}
}

/// <summary>Executes one procps Inspect file or pipe entry.</summary>
internal static class TopInspectExecutor {
	internal static async ValueTask<IReadOnlyList<string>> ExecuteAsync(
		TopInspectEntry entry,
		int processId,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( entry );
		if ( 1 > processId ) {
			throw new ArgumentOutOfRangeException( nameof( processId ) );
		}
		cancellationToken.ThrowIfCancellationRequested();

		string expanded = entry.Expand(
			processId
		);
		string text;
		if ( TopInspectEntryType.File == entry.Type ) {
			text = await File.ReadAllTextAsync(
				expanded,
				Encoding.UTF8,
				cancellationToken
			).ConfigureAwait( false );
		} else {
			text = await ExecutePipeAsync(
				expanded,
				cancellationToken
			).ConfigureAwait( false );
		}
		return NormalizeLines(
			text
		);
	}

	private static async Task<string> ExecutePipeAsync(
		string command,
		CancellationToken cancellationToken
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( command );
		cancellationToken.ThrowIfCancellationRequested();

		var startInfo = new ProcessStartInfo {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = false,
			CreateNoWindow = true
		};
		if ( OperatingSystem.IsWindows() ) {
			startInfo.FileName = Environment.GetEnvironmentVariable(
				"COMSPEC"
			) ?? "cmd.exe";
			startInfo.ArgumentList.Add(
				"/d"
			);
			startInfo.ArgumentList.Add(
				"/s"
			);
			startInfo.ArgumentList.Add(
				"/c"
			);
			startInfo.ArgumentList.Add(
				command
			);
		} else {
			startInfo.FileName = "/bin/sh";
			startInfo.ArgumentList.Add(
				"-c"
			);
			startInfo.ArgumentList.Add(
				command
			);
		}

		using var process = new Process {
			StartInfo = startInfo
		};
		if ( !process.Start() ) {
			throw new InvalidOperationException(
				"unable to start the Inspect pipeline"
			);
		}

		Task<string> outputTask = process.StandardOutput.ReadToEndAsync(
			cancellationToken
		);
		Task<string> errorTask = process.StandardError.ReadToEndAsync(
			cancellationToken
		);
		try {
			await process.WaitForExitAsync(
				cancellationToken
			).ConfigureAwait( false );
			string output = await outputTask.ConfigureAwait( false );
			_ = await errorTask.ConfigureAwait( false );
			return output;
		} catch {
			if ( !process.HasExited ) {
				process.Kill(
					entireProcessTree: true
				);
			}
			throw;
		}
	}

	private static IReadOnlyList<string> NormalizeLines(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );

		string normalized = text.Replace(
			"\r\n",
			"\n",
			StringComparison.Ordinal
		).Replace(
			'\r',
			'\n'
		);
		string[] result = normalized.Split(
			'\n'
		);
		if (
			0 < result.Length
			&& 0 == result[ ^1 ].Length
		) {
			result = result[
				..^1
			];
		}
		if ( 0 == result.Length ) {
			return [
				string.Empty
			];
		}
		for ( int index = 0; index < result.Length; index++ ) {
			result[ index ] = result[ index ].Replace(
				"\t",
				"^I",
				StringComparison.Ordinal
			);
		}
		return result;
	}
}

/// <summary>Renders the terminal-independent procps Inspect chooser and pager.</summary>
internal static class TopInspectRenderer {
	internal static TopRenderFrame Render(
		TopInspectSession session,
		TopTerminalDimensions dimensions,
		bool boldEnabled
	) {
		ArgumentNullException.ThrowIfNull( session );
		if (
			1 > dimensions.Columns
			|| 1 > dimensions.Rows
		) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		var lines = new List<TopRenderLine>(
			dimensions.Rows
		);
		if ( !session.ViewingOutput ) {
			RenderChooser(
				lines,
				session,
				dimensions.Columns
			);
		} else {
			RenderViewer(
				lines,
				session,
				dimensions
			);
		}
		while ( lines.Count < dimensions.Rows ) {
			lines.Add(
				new TopRenderLine( string.Empty )
			);
		}
		return new TopRenderFrame(
			lines,
			dimensions.Columns,
			dimensions.Rows,
			boldEnabled
		);
	}

	private static void RenderChooser(
		List<TopRenderLine> lines,
		TopInspectSession session,
		int width
	) {
		lines.Add(
			new TopRenderLine(
				Limit(
					$"Inspection Pause at pid {session.ProcessId}",
					width
				),
				TopLineStyle.Header
			)
		);
		lines.Add(
			new TopRenderLine(
				Limit(
					"Use: left/right then <Enter>; q/Esc exits",
					width
				),
				TopLineStyle.Dim
			)
		);

		var options = new StringBuilder(
			"Options:"
		);
		for ( int index = 0; index < session.Entries.Count; index++ ) {
			TopInspectEntry entry = session.Entries[
				index
			];
			options.Append(
				' '
			);
			if ( index == session.SelectedIndex ) {
				options.Append(
					'['
				);
			}
			options.Append(
				entry.Name
			);
			if ( index == session.SelectedIndex ) {
				options.Append(
					']'
				);
			}
		}
		lines.Add(
			new TopRenderLine(
				Limit(
					options.ToString(),
					width
				),
				TopLineStyle.Summary
			)
		);

		TopInspectEntry selected = session.Entries[
			session.SelectedIndex
		];
		lines.Add(
			new TopRenderLine(
				Limit(
					$"{TypeLabel( selected.Type )}: {selected.Expand( session.ProcessId )}",
					width
				),
				TopLineStyle.Message
			)
		);
	}

	private static void RenderViewer(
		List<TopRenderLine> lines,
		TopInspectSession session,
		TopTerminalDimensions dimensions
	) {
		TopInspectEntry active = session.ActiveEntry
			?? throw new InvalidOperationException(
				"The Inspect viewer has no active entry."
			);
		lines.Add(
			new TopRenderLine(
				Limit(
					$"Inspect: {active.Name} (pid {session.ProcessId})",
					dimensions.Columns
				),
				TopLineStyle.Header
			)
		);
		lines.Add(
			new TopRenderLine(
				Limit(
					"arrows/PgUp/PgDn/Home/End; /=find; n=next; ==source; q=exit",
					dimensions.Columns
				),
				TopLineStyle.Dim
			)
		);
		string status = ( session.ShowSource )
			? $"{TypeLabel( active.Type )}: {active.Expand( session.ProcessId )}"
			: $"line {Math.Min( session.Lines.Count, session.VerticalOffset + 1 )}/{session.Lines.Count}"
		;
		lines.Add(
			new TopRenderLine(
				Limit(
					status,
					dimensions.Columns
				),
				TopLineStyle.Summary
			)
		);

		int bodyRows = Math.Max(
			1,
			dimensions.Rows - 4
		);
		int end = Math.Min(
			session.Lines.Count,
			session.VerticalOffset + bodyRows
		);
		for ( int index = session.VerticalOffset; index < end; index++ ) {
			lines.Add(
				new TopRenderLine(
					Slice(
						session.Lines[ index ],
						session.HorizontalOffset,
						dimensions.Columns
					)
				)
			);
		}
		while ( lines.Count < dimensions.Rows - 1 ) {
			lines.Add(
				new TopRenderLine( string.Empty )
			);
		}

		string footer;
		TopLineStyle footerStyle;
		if ( session.SearchActive ) {
			footer = $"/{session.SearchBuffer}";
			footerStyle = TopLineStyle.Prompt;
		} else if ( !string.IsNullOrEmpty( session.Message ) ) {
			footer = session.Message;
			footerStyle = TopLineStyle.Message;
		} else if ( !string.IsNullOrEmpty( session.SearchText ) ) {
			footer = $"search: {session.SearchText}";
			footerStyle = TopLineStyle.Dim;
		} else {
			footer = string.Empty;
			footerStyle = TopLineStyle.Default;
		}
		lines.Add(
			new TopRenderLine(
				Limit(
					footer,
					dimensions.Columns
				),
				footerStyle
			)
		);
	}

	private static string TypeLabel(
		TopInspectEntryType type
	) {
		return ( TopInspectEntryType.File == type )
			? "file"
			: "pipe"
		;
	}

	private static string Limit(
		string text,
		int width
	) {
		return Slice(
			text,
			0,
			width
		);
	}

	private static string Slice(
		string text,
		int offset,
		int width
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegative( offset );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		var builder = new StringBuilder();
		int runeIndex = 0;
		int outputRunes = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			if ( runeIndex < offset ) {
				runeIndex++;
				continue;
			}
			if ( width <= outputRunes ) {
				break;
			}
			builder.Append(
				rune.ToString()
			);
			runeIndex++;
			outputRunes++;
		}
		return builder.ToString();
	}
}

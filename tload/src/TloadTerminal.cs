/*
	tload
	Graphically display the current system load average.
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

namespace Icod.ProcPs.Tload;

using System.Text;
using Icod.TermInfo;
using Icod.Terminal;

/// <summary>Represents terminal geometry required by <c>tload</c>.</summary>
internal readonly record struct TloadTerminalDimensions(
	int Columns,
	int Rows
);

/// <summary>Creates the output-only terminal session used by <c>tload</c>.</summary>
internal interface ITloadTerminalSessionFactory {
	ValueTask<ITloadTerminalSession> OpenAsync(
		string? terminalPath,
		Stream standardOutput,
		CancellationToken cancellationToken = default
	);
}

/// <summary>Abstracts the output-only terminal surface consumed by <c>tload</c>.</summary>
internal interface ITloadTerminalSession : IAsyncDisposable {
	TloadTerminalDimensions GetDimensions();

	ValueTask WriteFrameAsync(
		string frame,
		CancellationToken cancellationToken = default
	);
}

/// <summary>Opens the system output endpoint used by <c>tload</c>.</summary>
internal sealed class SystemTloadTerminalSessionFactory
	: ITloadTerminalSessionFactory {
	internal static SystemTloadTerminalSessionFactory Instance { get; } = new();

	private SystemTloadTerminalSessionFactory() {
	}

	public ValueTask<ITloadTerminalSession> OpenAsync(
		string? terminalPath,
		Stream standardOutput,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( standardOutput );
		cancellationToken.ThrowIfCancellationRequested();

		if ( terminalPath is null ) {
			return ValueTask.FromResult<ITloadTerminalSession>(
				new SystemTloadTerminalSession(
					standardOutput,
					ownsOutput: false,
					TerminalEndpoint.StandardOutput,
					SystemTerminalControlProvider.Instance,
					ResolveCursorHome()
				)
			);
		}

		ArgumentException.ThrowIfNullOrWhiteSpace( terminalPath );
		var output = new FileStream(
			terminalPath,
			FileMode.Open,
			FileAccess.Write,
			FileShare.ReadWrite
		);
		return ValueTask.FromResult<ITloadTerminalSession>(
			new SystemTloadTerminalSession(
				output,
				ownsOutput: true,
				TerminalEndpoint.ForPath( terminalPath ),
				SystemTerminalControlProvider.Instance,
				ResolveCursorHome()
			)
		);
	}

	private static string ResolveCursorHome() {
		TerminalDescription fallback = OperatingSystem.IsWindows()
			? TerminalProfiles.WinConsole
			: TerminalProfiles.Ansi
		;
		string? terminalName = Environment.GetEnvironmentVariable( "TERM" );
		TerminalDescription terminal = TerminalDatabase.BuiltIn.Resolve(
			terminalName,
			fallback
		);
		return terminal.GetString( StringCapability.CursorHome ) ?? "\u001b[H";
	}
}

/// <summary>Writes <c>tload</c> frames to a borrowed or owned terminal output stream.</summary>
internal sealed class SystemTloadTerminalSession : ITloadTerminalSession {
	private static readonly TloadTerminalDimensions DefaultDimensions = new( 80, 25 );
	private readonly Stream output;
	private readonly bool ownsOutput;
	private readonly TerminalEndpoint endpoint;
	private readonly ITerminalControlProvider controlProvider;
	private readonly byte[] cursorHome;

	internal SystemTloadTerminalSession(
		Stream output,
		bool ownsOutput,
		TerminalEndpoint endpoint,
		ITerminalControlProvider controlProvider,
		string cursorHome
	) {
		ArgumentNullException.ThrowIfNull( output );
		ArgumentNullException.ThrowIfNull( endpoint );
		ArgumentNullException.ThrowIfNull( controlProvider );
		ArgumentNullException.ThrowIfNull( cursorHome );
		if ( !output.CanWrite ) {
			throw new ArgumentException(
				"The tload terminal output stream must be writable.",
				nameof( output )
			);
		}

		this.output = output;
		this.ownsOutput = ownsOutput;
		this.endpoint = endpoint;
		this.controlProvider = controlProvider;
		this.cursorHome = Encoding.Latin1.GetBytes( cursorHome );
	}

	public TloadTerminalDimensions GetDimensions() {
		TerminalControlResult<TerminalSize> result = this.controlProvider.GetSize(
			this.endpoint
		);
		if ( !result.IsAvailable ) {
			return DefaultDimensions;
		}

		TerminalSize size = result.GetRequiredValue();
		return new TloadTerminalDimensions(
			size.Columns,
			size.Rows
		);
	}

	public async ValueTask WriteFrameAsync(
		string frame,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( frame );
		cancellationToken.ThrowIfCancellationRequested();

		await this.output.WriteAsync(
			this.cursorHome,
			cancellationToken
		).ConfigureAwait( false );
		byte[] frameBytes = Encoding.UTF8.GetBytes( frame );
		await this.output.WriteAsync(
			frameBytes,
			cancellationToken
		).ConfigureAwait( false );
		await this.output.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	public async ValueTask DisposeAsync() {
		if ( !this.ownsOutput ) {
			return;
		}

		await this.output.DisposeAsync().ConfigureAwait( false );
	}
}

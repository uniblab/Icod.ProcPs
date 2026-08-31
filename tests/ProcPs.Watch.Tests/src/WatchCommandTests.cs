/*
	Icod.ProcPs.Watch.Tests
	Tests for the watch command implementation.
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

namespace Icod.ProcPs.Watch.Tests;

using System.Globalization;
using System.Text;
using Icod.DCurses;
using Icod.Processes;
using Icod.Timing;
using Xunit;

/// <summary>Exercises the DCurses-backed procps-ng compatible <c>watch</c> migration.</summary>
public sealed class WatchCommandTests {
	[Fact]
	public async Task ExecModePreservesArgumentBoundariesAndDisposesTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 50, 6 ) );
		FakeExecutor executor = new(
			clock,
			Execution.Success( "alpha\n" ),
			Execution.Success( "alpha\n" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--equexit=1", "tool", "two words", "three" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, executor.Options.Count );
		Assert.Equal( "tool", executor.Options[ 0 ].FileName );
		Assert.Equal(
			new[] { "two words", "three" },
			executor.Options[ 0 ].Arguments.ToArray()
		);
		Assert.Contains( "alpha", VisibleText( terminal.Frames[ 0 ].Screen ), StringComparison.Ordinal );
		Assert.True( terminal.Disposed );
		Assert.Equal( string.Empty, result.Stderr );
	}

	[Fact]
	public async Task DefaultModeUsesHostShell() {
		FakeClock clock = new();
		FakeExecutor executor = new(
			clock,
			Execution.Success( "ok" ),
			Execution.Success( "ok" )
		);

		CommandResult result = await RunAsync(
			[ "--equexit", "1", "echo", "hello" ],
			new FakeTerminal( clock, new WatchTerminalDimensions( 40, 5 ) ),
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		ProcessRunOptions options = executor.Options[ 0 ];
		if ( OperatingSystem.IsWindows() ) {
			Assert.Equal( "cmd.exe", options.FileName );
			Assert.Equal( new[] { "/D", "/S", "/C", "echo hello" }, options.Arguments.ToArray() );
		} else {
			Assert.Equal( "/bin/sh", options.FileName );
			Assert.Equal( new[] { "-c", "echo hello" }, options.Arguments.ToArray() );
		}
	}

	[Fact]
	public async Task FixedDelayWaitsFullIntervalAfterCommand() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		FakeExecutor executor = new(
			clock,
			new Execution( "same", string.Empty, 0, TimeSpan.FromMilliseconds( 100 ) ),
			new Execution( "same", string.Empty, 0, TimeSpan.FromMilliseconds( 100 ) )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--interval", "0.25", "--equexit=1", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Single( terminal.Waits );
		Assert.Equal( TimeSpan.FromMilliseconds( 250 ), terminal.Waits[ 0 ] );
	}

	[Fact]
	public async Task PreciseIntervalIncludesCommandRunningTime() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		FakeExecutor executor = new(
			clock,
			new Execution( "same", string.Empty, 0, TimeSpan.FromMilliseconds( 100 ) ),
			new Execution( "same", string.Empty, 0, TimeSpan.FromMilliseconds( 100 ) )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--precise", "--interval=0.25", "--equexit=1", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Single( terminal.Waits );
		Assert.Equal( TimeSpan.FromMilliseconds( 150 ), terminal.Waits[ 0 ] );
	}

	[Fact]
	public async Task WatchIntervalEnvironmentControlsCadence() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		FakeExecutor executor = new(
			clock,
			Execution.Success( "same" ),
			Execution.Success( "same" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--equexit=1", "tool" ],
			terminal,
			executor,
			clock,
			environmentProvider: name => "WATCH_INTERVAL" == name ? "0,3" : null
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( TimeSpan.FromMilliseconds( 300 ), terminal.Waits[ 0 ] );
	}

	[Fact]
	public async Task DifferencesProduceSemanticReverseHighlight() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 30, 4 ) );
		FakeExecutor executor = new(
			clock,
			Execution.Success( "alpha" ),
			Execution.Success( "alpHa" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--no-title", "--differences", "--chgexit", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.NotNull( terminal.Frames[ 1 ].Highlights );
		Assert.True( terminal.Frames[ 1 ].Highlights![ 3 ] );
		Assert.Equal( "H", terminal.Frames[ 1 ].Screen.GetCell( 0, 3 ).Content );
	}

	[Theory]
	[InlineData( "-d1" )]
	[InlineData( "--differences=1" )]
	[InlineData( "--differences=permanent" )]
	public async Task AttachedDifferencesArgumentEnablesPermanentHighlight(
		string option
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( option );
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 30, 4 )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent( WatchTerminalEventKind.Timeout )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent( WatchTerminalEventKind.Timeout )
		);
		terminal.Events.Enqueue( Input( 'q' ) );
		FakeExecutor executor = new(
			clock,
			Execution.Success( "alpha" ),
			Execution.Success( "alpHa" ),
			Execution.Success( "alpha" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--no-title", option, "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 3, executor.Options.Count );
		Assert.Equal( 3, terminal.Frames.Count );
		Assert.NotNull( terminal.Frames[ 1 ].Highlights );
		Assert.NotNull( terminal.Frames[ 2 ].Highlights );
		Assert.True( terminal.Frames[ 1 ].Highlights![ 3 ] );
		Assert.True(
			terminal.Frames[ 2 ].Highlights![ 3 ],
			"Permanent highlighting should retain a prior visible difference."
		);
	}

	[Fact]
	public async Task ColorOptionTranslatesSgrToSemanticCursesStyle() {
		FakeClock coloredClock = new();
		FakeTerminal colored = new( coloredClock, new WatchTerminalDimensions( 30, 4 ) );
		CommandResult coloredResult = await RunAsync(
			[ "--exec", "--no-title", "--color", "--equexit=1", "tool" ],
			colored,
			new FakeExecutor(
				coloredClock,
				Execution.Success( "\u001b[31mred\u001b[0m" ),
				Execution.Success( "\u001b[31mred\u001b[0m" )
			),
			coloredClock
		);

		Assert.Equal( 0, coloredResult.ExitCode );
		CursesStyle redStyle = colored.Frames[ 0 ].Screen.GetCell( 0, 0 ).Style;
		Assert.Equal( CursesColorKind.Indexed, redStyle.Foreground.Kind );
		Assert.Equal( 1, redStyle.Foreground.Index );

		FakeClock plainClock = new();
		FakeTerminal plain = new( plainClock, new WatchTerminalDimensions( 30, 4 ) );
		CommandResult plainResult = await RunAsync(
			[ "--exec", "--no-title", "--no-color", "--equexit=1", "tool" ],
			plain,
			new FakeExecutor(
				plainClock,
				Execution.Success( "\u001b[31mred\u001b[0m" ),
				Execution.Success( "\u001b[31mred\u001b[0m" )
			),
			plainClock
		);

		Assert.Equal( 0, plainResult.ExitCode );
		Assert.True( plain.Frames[ 0 ].Screen.GetCell( 0, 0 ).Style.IsDefault );
		Assert.StartsWith(
			"[31mred[0m",
			VisibleRow(
				plain.Frames[ 0 ].Screen,
				0
			),
			StringComparison.Ordinal
		);
	}

	[Fact]
	public void IndexedSgrAndBoldResetMatchProcpsColorProfile() {
		WatchScreen screen = WatchScreen.Create(
			"\u001b[1;21;38;5;200;48;5;17mX",
			new WatchTerminalDimensions( 10, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: true
		);

		CursesStyle style = screen.GetCell( 0, 0 ).Style;
		Assert.Equal( CursesTextAttributes.None, style.Attributes );
		Assert.Equal( CursesColorKind.Indexed, style.Foreground.Kind );
		Assert.Equal( 200, style.Foreground.Index );
		Assert.Equal( CursesColorKind.Indexed, style.Background.Kind );
		Assert.Equal( 17, style.Background.Index );
	}

	[Fact]
	public void UnsupportedSgrStopsRemainingAttributesAndRgbIsNotInterpreted() {
		WatchScreen unsupported = WatchScreen.Create(
			"\u001b[1;6;31mX",
			new WatchTerminalDimensions( 10, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: true
		);
		CursesStyle unsupportedStyle = unsupported.GetCell( 0, 0 ).Style;
		Assert.True(
			0 != (
				unsupportedStyle.Attributes
				& CursesTextAttributes.Bold
			)
		);
		Assert.True( unsupportedStyle.Foreground.IsDefault );

		WatchScreen rgb = WatchScreen.Create(
			"\u001b[38;2;1;2;3mX",
			new WatchTerminalDimensions( 10, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: true
		);
		Assert.True( rgb.GetCell( 0, 0 ).Style.IsDefault );
	}

	[Fact]
	public void PrivateAnsiSequenceConsumesOnlyProcpsPrefix() {
		WatchScreen screen = WatchScreen.Create(
			"\u001b[?25lX",
			new WatchTerminalDimensions( 10, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: true
		);

		Assert.StartsWith(
			"25lX",
			VisibleRow(
				screen,
				0
			),
			StringComparison.Ordinal
		);
	}

	[Fact]
	public void NoWrapDiscardedTailResetsAnsiBeforeNextLine() {
		WatchScreen screen = WatchScreen.Create(
			"\u001b[31mAB\u001b[32mignored\nX",
			new WatchTerminalDimensions( 1, 2 ),
			noTitle: true,
			noWrap: true,
			preserveColor: true
		);

		CursesStyle firstStyle = screen.GetCell( 0, 0 ).Style;
		Assert.Equal( 1, firstStyle.Foreground.Index );
		Assert.Equal( "X", screen.GetCell( 1, 0 ).Content );
		Assert.True( screen.GetCell( 1, 0 ).Style.IsDefault );
	}

	[Fact]
	public async Task BeepAndErrorExitPropagateChildStatus() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		terminal.Events.Enqueue(
			Input(
				' ',
				availableDuringZeroTimeout: true
			)
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent( WatchTerminalEventKind.Repaint )
		);
		terminal.Events.Enqueue( Input( 'x' ) );

		CommandResult result = await RunAsync(
			[ "--exec", "--beep", "--errexit", "tool" ],
			terminal,
			new FakeExecutor( clock, Execution.Exit( 7, "failed" ) ),
			clock
		);

		Assert.Equal( 7, result.ExitCode );
		Assert.Equal( 1, terminal.AlertCount );
		Assert.Single( terminal.Frames );
		Assert.Equal( 1, terminal.RepaintCount );
		Assert.Equal( 2, terminal.StatusMessages.Count );
		Assert.All(
			terminal.StatusMessages,
			static message => Assert.Contains(
				"press a key to exit",
				message,
				StringComparison.Ordinal
			)
		);
		Assert.Equal( 4, terminal.Waits.Count );
		Assert.Equal( TimeSpan.Zero, terminal.Waits[ 0 ] );
		Assert.Equal( TimeSpan.Zero, terminal.Waits[ 1 ] );
	}

	[Fact]
	public async Task ChildBellAlertsWithoutBeepOption() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 20, 3 )
		);
		FakeExecutor executor = new(
			clock,
			Execution.Success( "a\ab" ),
			Execution.Success( "a\ab" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--no-title", "--equexit=1", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, executor.Options.Count );
		Assert.Equal( 2, terminal.AlertCount );
		Assert.Equal( 2, terminal.Frames.Count );
	}

	[Fact]
	public void OrdinaryScreenDoesNotCountBellBeyondConsumedBody() {
		WatchScreen screen = WatchScreen.Create(
			"A\n\a",
			new WatchTerminalDimensions( 1, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: false,
			alertCount: out int alertCount
		);

		Assert.Equal( 0, alertCount );
		Assert.Equal( "A", screen.GetCell( 0, 0 ).Content );
	}

	[Fact]
	public void FollowScreenCountsBellAfterScrollingPastFirstBody() {
		WatchScreen screen = WatchScreen.AppendFollow(
			null,
			"A\n\a",
			new WatchTerminalDimensions( 1, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: false,
			alertCount: out int alertCount
		);

		Assert.Equal( 1, alertCount );
		Assert.Equal( string.Empty, screen.GetCell( 0, 0 ).Content );
	}

	[Fact]
	public void NoWrapDoesNotCountBellInDiscardedLineTail() {
		_ = WatchScreen.Create(
			"AB\a\n",
			new WatchTerminalDimensions( 1, 2 ),
			noTitle: true,
			noWrap: true,
			preserveColor: false,
			alertCount: out int alertCount
		);

		Assert.Equal( 0, alertCount );
	}

	[Fact]
	public async Task EqualExitCountsUnchangedVisibleCycles() {
		FakeClock clock = new();
		FakeExecutor executor = new(
			clock,
			Execution.Success( "same" ),
			Execution.Success( "same" ),
			Execution.Success( "same" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--equexit=2", "tool" ],
			new FakeTerminal( clock, new WatchTerminalDimensions( 40, 5 ) ),
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 3, executor.Options.Count );
	}

	[Fact]
	public async Task ChangeExitUsesVisibleNoWrapOutputOnly() {
		FakeClock clock = new();
		FakeExecutor executor = new(
			clock,
			Execution.Success( "abcdeX" ),
			Execution.Success( "abcdeY" ),
			Execution.Success( "abcdZZ" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--no-title", "--no-wrap", "--chgexit", "tool" ],
			new FakeTerminal( clock, new WatchTerminalDimensions( 5, 3 ) ),
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 3, executor.Options.Count );
	}

	[Fact]
	public async Task ResizeDoesNotCountAsVisibleChange() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 5, 3 ) );
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				WatchTerminalEventKind.Resize,
				new WatchTerminalDimensions( 6, 3 )
			)
		);
		FakeExecutor executor = new(
			clock,
			Execution.Success( "abcdeX" ),
			Execution.Success( "abcdeY" ),
			Execution.Success( "abcdZZ" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--no-title", "--no-wrap", "--chgexit", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 3, executor.Options.Count );
		Assert.Equal( 3, terminal.Frames.Count );
		Assert.Equal( 5, terminal.Frames[ 0 ].Screen.Width );
		Assert.Equal( 6, terminal.Frames[ 1 ].Screen.Width );
	}

	[Fact]
	public async Task NoRerunRedrawsPreviousOutputAfterResize() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 10, 4 ) );
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				WatchTerminalEventKind.Resize,
				new WatchTerminalDimensions( 12, 4 )
			)
		);
		FakeExecutor executor = new(
			clock,
			Execution.Success( "same" ),
			Execution.Success( "same" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--no-rerun", "--equexit=1", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, executor.Options.Count );
		Assert.Equal( 3, terminal.Frames.Count );
		Assert.Equal( 12, terminal.Frames[ 1 ].Screen.Width );
	}

	[Fact]
	public async Task StandardOutputAndErrorShareDisplayStream() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		terminal.Events.Enqueue( Input( 'x' ) );

		CommandResult result = await RunAsync(
			[ "--exec", "--errexit", "tool" ],
			terminal,
			new FakeExecutor(
				clock,
				new Execution( "out", "err", 3, TimeSpan.Zero )
			),
			clock
		);

		Assert.Equal( 3, result.ExitCode );
		string visible = VisibleText( terminal.Frames[ 0 ].Screen );
		Assert.Contains( "out", visible, StringComparison.Ordinal );
		Assert.Contains( "err", visible, StringComparison.Ordinal );
	}

	[Fact]
	public void UnicodeWideTextUsesDisplayColumnsAndContinuationCells() {
		WatchScreen screen = WatchScreen.Create(
			"A🙂B",
			new WatchTerminalDimensions( 4, 1 ),
			noTitle: true,
			noWrap: false,
			preserveColor: false
		);

		Assert.Equal( "A", screen.GetCell( 0, 0 ).Content );
		Assert.Equal( "🙂", screen.GetCell( 0, 1 ).Content );
		Assert.Equal( 2, screen.GetCell( 0, 1 ).DisplayWidth );
		Assert.True( screen.GetCell( 0, 2 ).IsContinuation );
		Assert.Equal( "B", screen.GetCell( 0, 3 ).Content );
	}

	[Fact]
	public async Task HeaderCanBeSuppressed() {
		FakeClock titledClock = new();
		FakeTerminal titled = new(
			titledClock,
			new WatchTerminalDimensions( 50, 5 )
		);
		CommandResult titledResult = await RunAsync(
			[ "--exec", "--equexit=1", "tool" ],
			titled,
			new FakeExecutor(
				titledClock,
				new Execution( "same", string.Empty, 0, TimeSpan.FromMilliseconds( 125 ) ),
				new Execution( "same", string.Empty, 0, TimeSpan.FromMilliseconds( 125 ) )
			),
			titledClock
		);

		Assert.Equal( 0, titledResult.ExitCode );
		Assert.Equal( 2, titled.Frames[ 0 ].HeaderLines.Count );
		string intervalText = 2d.ToString(
			"0.0",
			CultureInfo.CurrentCulture
		);
		Assert.Contains(
			$"Every {intervalText}s: tool",
			titled.Frames[ 0 ].HeaderLines[ 0 ],
			StringComparison.Ordinal
		);
		DateTimeOffset now = new(
			2026,
			8,
			25,
			12,
			34,
			56,
			TimeSpan.Zero
		);
		Assert.EndsWith(
			$"test-host: {now.ToString( "G", CultureInfo.CurrentCulture )}",
			titled.Frames[ 0 ].HeaderLines[ 0 ],
			StringComparison.Ordinal
		);
		string elapsedText = 0.125d.ToString(
			"0.000",
			CultureInfo.CurrentCulture
		);
		Assert.EndsWith(
			$"in {elapsedText}s (0)",
			titled.Frames[ 0 ].HeaderLines[ 1 ],
			StringComparison.Ordinal
		);

		FakeClock untitledClock = new();
		FakeTerminal untitled = new(
			untitledClock,
			new WatchTerminalDimensions( 50, 5 )
		);
		CommandResult untitledResult = await RunAsync(
			[ "--exec", "--no-title", "--equexit=1", "tool" ],
			untitled,
			new FakeExecutor(
				untitledClock,
				Execution.Success( "same" ),
				Execution.Success( "same" )
			),
			untitledClock
		);

		Assert.Equal( 0, untitledResult.ExitCode );
		Assert.Empty( untitled.Frames[ 0 ].HeaderLines );
	}

	[Fact]
	public async Task ResumeRepaintAndInterruptReturnCanceledAndDisposeTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent( WatchTerminalEventKind.Repaint )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent( WatchTerminalEventKind.Interrupt )
		);

		FakeExecutor executor = new(
			clock,
			Execution.Success( "first" )
		);
		CommandResult result = await RunAsync(
			[ "--exec", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Single( executor.Options );
		Assert.Single( terminal.Frames );
		Assert.Equal( 1, terminal.RepaintCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task QuitInputExitsBeforeNextExecution() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 40, 5 )
		);
		terminal.Events.Enqueue(
			Input(
				'q'
			)
		);
		FakeExecutor executor = new(
			clock,
			Execution.Success( "first" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Single( executor.Options );
		Assert.Single( terminal.Frames );
		Assert.Single( terminal.Waits );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task SpaceInputRequestsImmediateExecution() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 40, 5 )
		);
		terminal.Events.Enqueue(
			Input(
				' '
			)
		);
		FakeExecutor executor = new(
			clock,
			Execution.Success( "same" ),
			Execution.Success( "same" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--equexit=1", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, executor.Options.Count );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Single( terminal.Waits );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task ScreenshotInputWritesVisibleFrameOncePerWait() {
		string directory = Path.Combine(
			Path.GetTempPath(),
			"Icod.ProcPs.Watch.Tests",
			Guid.NewGuid().ToString(
				"N"
			)
		);
		Directory.CreateDirectory(
			directory
		);
		try {
			FakeClock clock = new();
			FakeTerminal terminal = new(
				clock,
				new WatchTerminalDimensions( 80, 5 )
			);
			terminal.Events.Enqueue(
				Input(
					's'
				)
			);
			terminal.Events.Enqueue(
				Input(
					's'
				)
			);
			terminal.Events.Enqueue(
				Input(
					'q'
				)
			);
			FakeExecutor executor = new(
				clock,
				Execution.Success( "capture" )
			);

			CommandResult result = await RunAsync(
				[ "--exec", "--shotsdir", directory, "tool" ],
				terminal,
				executor,
				clock
			);

			Assert.Equal( 0, result.ExitCode );
			string[] files = Directory.GetFiles(
				directory
			);
			Assert.Single( files );
			Assert.Equal(
				"watch_20260825-123456",
				Path.GetFileName(
					files[ 0 ]
				)
			);

			string content = await File.ReadAllTextAsync(
				files[ 0 ]
			);
			string[] lines = content.Split(
				'\n'
			);
			Assert.Equal( 6, lines.Length );
			Assert.StartsWith(
				$"Every {2d.ToString(
					"0.0",
					CultureInfo.CurrentCulture
				)}s: tool",
				lines[ 0 ],
				StringComparison.Ordinal
			);
			Assert.StartsWith(
				"capture",
				lines[ 2 ],
				StringComparison.Ordinal
			);
			for ( int index = 0; index < 5; index++ ) {
				Assert.Equal(
					80,
					lines[ index ].Length
				);
			}
			Assert.True( terminal.Disposed );
		} finally {
			Directory.Delete(
				directory,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task VersionDoesNotOpenInteractiveTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );

		CommandResult result = await RunAsync(
			[ "--version" ],
			terminal,
			new FakeExecutor( clock ),
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( global::Icod.ProcPs.Tests.ProcPsTestVersion.FormatCommand( "Icod.ProcPs.Watch" ), result.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, terminal.OpenCount );
		Assert.False( terminal.Disposed );
	}

	[Fact]
	public async Task FollowConflictIsControlledWithoutOpeningTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );
		FakeExecutor executor = new( clock );

		CommandResult result = await RunAsync(
			[ "--follow", "--chgexit", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "conflicts", result.Stderr, StringComparison.Ordinal );
		Assert.Empty( executor.Options );
		Assert.Equal( 0, terminal.OpenCount );
		Assert.False( terminal.Disposed );
	}

	[Fact]
	public async Task FollowRetainsCursorAndScrollsAcrossExecutions() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 8, 3 )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent( WatchTerminalEventKind.Timeout )
		);
		terminal.Events.Enqueue(
			Input( 'q' )
		);
		FakeExecutor executor = new(
			clock,
			Execution.Success( "one\ntwo" ),
			Execution.Success( "!\nthree\nfour" )
		);

		CommandResult result = await RunAsync(
			[
				"--exec",
				"--follow",
				"--no-title",
				"--interval=0.1",
				"tool"
			],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, executor.Options.Count );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.StartsWith(
			"one",
			VisibleRow( terminal.Frames[ 0 ].Screen, 0 ),
			StringComparison.Ordinal
		);
		Assert.StartsWith(
			"two!",
			VisibleRow( terminal.Frames[ 1 ].Screen, 0 ),
			StringComparison.Ordinal
		);
		Assert.StartsWith(
			"three",
			VisibleRow( terminal.Frames[ 1 ].Screen, 1 ),
			StringComparison.Ordinal
		);
		Assert.StartsWith(
			"four",
			VisibleRow( terminal.Frames[ 1 ].Screen, 2 ),
			StringComparison.Ordinal
		);
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task FollowNoRerunResizeRepaintsRetainedBody() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 8, 3 )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				WatchTerminalEventKind.Resize,
				new WatchTerminalDimensions( 10, 4 )
			)
		);
		terminal.Events.Enqueue( Input( 'q' ) );
		FakeExecutor executor = new(
			clock,
			Execution.Success( "one\ntwo" )
		);

		CommandResult result = await RunAsync(
			[ "--exec", "--follow", "--no-title", "--no-rerun", "tool" ],
			terminal,
			executor,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Single( executor.Options );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 10, terminal.Frames[ 1 ].Screen.Width );
		Assert.Equal( 4, terminal.Frames[ 1 ].Screen.Height );
		Assert.StartsWith(
			"one",
			VisibleRow( terminal.Frames[ 1 ].Screen, 0 ),
			StringComparison.Ordinal
		);
		Assert.StartsWith(
			"two",
			VisibleRow( terminal.Frames[ 1 ].Screen, 1 ),
			StringComparison.Ordinal
		);
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task HelpDoesNotOpenInteractiveTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new WatchTerminalDimensions( 40, 5 ) );

		CommandResult result = await RunAsync(
			[ "--help" ],
			terminal,
			new FakeExecutor( clock ),
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "Usage:", result.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, terminal.OpenCount );
		Assert.False( terminal.Disposed );
	}

	[Fact]
	public async Task NonInteractiveTerminalReturnsUsageFailureAndDisposes() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new WatchTerminalDimensions( 40, 5 ),
			isInteractive: false
		);

		CommandResult result = await RunAsync(
			[ "--exec", "tool" ],
			terminal,
			new FakeExecutor( clock ),
			clock
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "interactive terminal", result.Stderr, StringComparison.Ordinal );
		Assert.True( terminal.Disposed );
	}

	private static string VisibleText(
		WatchScreen screen
	) {
		ArgumentNullException.ThrowIfNull( screen );
		StringBuilder builder = new();
		for ( int row = 0; row < screen.Height; row++ ) {
			for ( int column = 0; column < screen.Width; column++ ) {
				WatchCell cell = screen.GetCell( row, column );
				if ( !cell.IsContinuation ) {
					builder.Append( 0 == cell.Content.Length ? " " : cell.Content );
				}
			}
		}
		return builder.ToString();
	}

	private static string VisibleRow(
		WatchScreen screen,
		int row
	) {
		ArgumentNullException.ThrowIfNull( screen );
		if ( 0 > row || screen.Height <= row ) {
			throw new ArgumentOutOfRangeException( nameof( row ) );
		}

		StringBuilder builder = new();
		for ( int column = 0; column < screen.Width; column++ ) {
			WatchCell cell = screen.GetCell(
				row,
				column
			);
			if ( !cell.IsContinuation ) {
				builder.Append(
					0 == cell.Content.Length ? " " : cell.Content
				);
			}
		}
		return builder.ToString();
	}

	private static ScheduledTerminalEvent Input(
		char value,
		bool availableDuringZeroTimeout = false
	) {
		return new ScheduledTerminalEvent(
			WatchTerminalEventKind.Input,
			Input: new WatchInputEvent(
				WatchInputKey.Character,
				new Rune( value )
			),
			AvailableDuringZeroTimeout: availableDuringZeroTimeout
		);
	}

	private static async Task<CommandResult> RunAsync(
		IReadOnlyList<string> args,
		FakeTerminal terminal,
		FakeExecutor executor,
		FakeClock clock,
		Func<string, string?>? environmentProvider = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminal );
		ArgumentNullException.ThrowIfNull( executor );
		ArgumentNullException.ThrowIfNull( clock );

		using MemoryStream stdout = new();
		using MemoryStream stderr = new();
		int exitCode = await Command.RunAsyncCore(
			args,
			stdout,
			stderr,
			executor,
			new FakeTerminalFactory( terminal ),
			clock,
			environmentProvider ?? ( static _ => null ),
			static () => new DateTimeOffset( 2026, 8, 25, 12, 34, 56, TimeSpan.Zero ),
			static () => "test-host",
			CancellationToken.None
		);
		return new CommandResult(
			exitCode,
			Encoding.UTF8.GetString( stdout.ToArray() ),
			Encoding.UTF8.GetString( stderr.ToArray() )
		);
	}

	private sealed record CommandResult(
		int ExitCode,
		string Stdout,
		string Stderr
	);

	private sealed record Execution(
		string StandardOutput,
		string StandardError,
		int ExitCode,
		TimeSpan Elapsed
	) {
		internal static Execution Success(
			string output
		) {
			return new Execution( output, string.Empty, 0, TimeSpan.Zero );
		}

		internal static Execution Exit(
			int exitCode,
			string output
		) {
			return new Execution( output, string.Empty, exitCode, TimeSpan.Zero );
		}
	}

	private sealed class FakeExecutor : IProcessExecutor {
		private readonly Queue<Execution> executions;
		private readonly FakeClock clock;

		internal FakeExecutor(
			FakeClock clock,
			params Execution[] executions
		) {
			ArgumentNullException.ThrowIfNull( clock );
			ArgumentNullException.ThrowIfNull( executions );
			this.clock = clock;
			this.executions = new Queue<Execution>( executions );
		}

		internal List<ProcessRunOptions> Options {
			get;
		} = [];

		public async Task<ProcessResult> RunAsync(
			ProcessRunOptions options,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( options );
			cancellationToken.ThrowIfCancellationRequested();
			this.Options.Add( options );
			if ( 0 == this.executions.Count ) {
				throw new InvalidOperationException( "No scripted process execution remains." );
			}

			Execution execution = this.executions.Dequeue();
			if ( 0 < execution.StandardOutput.Length && options.StandardOutput is not null ) {
				await options.StandardOutput.WriteAsync(
					Encoding.UTF8.GetBytes( execution.StandardOutput ),
					cancellationToken
				).ConfigureAwait( false );
			}
			if ( 0 < execution.StandardError.Length && options.StandardError is not null ) {
				await options.StandardError.WriteAsync(
					Encoding.UTF8.GetBytes( execution.StandardError ),
					cancellationToken
				).ConfigureAwait( false );
			}
			this.clock.Advance( execution.Elapsed );
			return ProcessResult.FromTermination(
				ProcessTermination.Exited( execution.ExitCode ),
				elapsed: execution.Elapsed
			);
		}
	}

	private sealed class FakeClock : IMonotonicClock {
		private long ticks;

		public long GetTimestamp() {
			return this.ticks;
		}

		public TimeSpan GetElapsedTime(
			long startingTimestamp,
			long endingTimestamp
		) {
			return TimeSpan.FromTicks( endingTimestamp - startingTimestamp );
		}

		public ValueTask DelayAsync(
			TimeSpan delay,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > delay ) {
				throw new ArgumentOutOfRangeException( nameof( delay ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			this.Advance( delay );
			return ValueTask.CompletedTask;
		}

		internal void Advance(
			TimeSpan duration
		) {
			if ( TimeSpan.Zero > duration ) {
				throw new ArgumentOutOfRangeException( nameof( duration ) );
			}
			this.ticks = checked( this.ticks + duration.Ticks );
		}
	}

	private sealed class FakeTerminalFactory : IWatchTerminalSessionFactory {
		private readonly FakeTerminal terminal;

		internal FakeTerminalFactory(
			FakeTerminal terminal
		) {
			ArgumentNullException.ThrowIfNull( terminal );
			this.terminal = terminal;
		}

		public ValueTask<IWatchTerminalSession> OpenAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.terminal.OpenCount++;
			return ValueTask.FromResult<IWatchTerminalSession>( this.terminal );
		}
	}

	private sealed record ScheduledTerminalEvent(
		WatchTerminalEventKind Kind,
		WatchTerminalDimensions? Dimensions = null,
		WatchInputEvent? Input = null,
		bool AvailableDuringZeroTimeout = false
	);

	private sealed class FakeTerminal : IWatchTerminalSession {
		private readonly FakeClock clock;
		private WatchTerminalDimensions dimensions;

		internal FakeTerminal(
			FakeClock clock,
			WatchTerminalDimensions dimensions,
			bool isInteractive = true
		) {
			ArgumentNullException.ThrowIfNull( clock );
			this.clock = clock;
			this.dimensions = dimensions;
			this.IsInteractive = isInteractive;
		}

		public bool IsInteractive {
			get;
		}

		public CancellationToken TerminationToken => CancellationToken.None;

		internal Queue<ScheduledTerminalEvent> Events {
			get;
		} = new();

		internal List<WatchRenderFrame> Frames {
			get;
		} = [];

		internal List<string> StatusMessages {
			get;
		} = [];

		internal List<TimeSpan> Waits {
			get;
		} = [];

		internal int AlertCount {
			get;
			private set;
		}

		internal int RepaintCount {
			get;
			private set;
		}

		internal int OpenCount {
			get;
			set;
		}

		internal bool Disposed {
			get;
			private set;
		}

		public WatchTerminalDimensions GetDimensions() {
			return this.dimensions;
		}

		public ValueTask<WatchTerminalEvent> ReadEventAsync(
			TimeSpan timeout,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > timeout ) {
				throw new ArgumentOutOfRangeException( nameof( timeout ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			this.Waits.Add( timeout );
			if ( 0 < this.Events.Count ) {
				ScheduledTerminalEvent scripted = this.Events.Peek();
				if (
					TimeSpan.Zero == timeout
					&& !scripted.AvailableDuringZeroTimeout
				) {
					return ValueTask.FromResult(
						new WatchTerminalEvent( WatchTerminalEventKind.Timeout )
					);
				}
				_ = this.Events.Dequeue();
				if ( scripted.Dimensions.HasValue ) {
					this.dimensions = scripted.Dimensions.Value;
				}
				return ValueTask.FromResult(
					new WatchTerminalEvent(
						scripted.Kind,
						scripted.Input
					)
				);
			}

			this.clock.Advance( timeout );
			return ValueTask.FromResult(
				new WatchTerminalEvent( WatchTerminalEventKind.Timeout )
			);
		}

		public ValueTask RenderAsync(
			WatchRenderFrame frame,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( frame );
			cancellationToken.ThrowIfCancellationRequested();
			this.Frames.Add( frame );
			return ValueTask.CompletedTask;
		}

		public ValueTask ShowStatusAsync(
			string message,
			CancellationToken cancellationToken = default
		) {
			ArgumentException.ThrowIfNullOrWhiteSpace( message );
			cancellationToken.ThrowIfCancellationRequested();
			this.StatusMessages.Add( message );
			return ValueTask.CompletedTask;
		}

		public ValueTask RepaintAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.RepaintCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask AlertAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.AlertCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask DisposeAsync() {
			this.Disposed = true;
			return ValueTask.CompletedTask;
		}
	}
}

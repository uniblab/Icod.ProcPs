/*
	Icod.ProcPs.Top.Tests
	Tests for top interactive color mapping.
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

namespace Icod.ProcPs.Top.Tests;

using System.Text;
using Xunit;

/// <summary>Exercises procps-compatible color mapping interaction and render metadata.</summary>
public sealed class TopColorInteractionTests {
	[Fact]
	public void ColorManagerEditsFiveTargetsAndCommits() {
		TopRuntimeState state = new();
		var manager = new TopColorManagerState(
			state
		);
		state.ColorManager = manager;

		Assert.Equal(
			TopColorTarget.Tasks,
			manager.Target
		);
		Assert.True( state.ColorsEnabled );

		Assert.Equal(
			TopColorManagerInputResult.Changed,
			manager.HandleInput( Character( 'S' ), state )
		);
		_ = manager.HandleInput( Character( '7' ), state );
		_ = manager.HandleInput( Character( 'M' ), state );
		_ = manager.HandleInput( Character( '@' ), state );
		_ = manager.HandleInput( Character( 'H' ), state );
		_ = manager.HandleInput( Character( '6' ), state );
		_ = manager.HandleInput( Character( 'T' ), state );
		_ = manager.HandleInput( Character( '5' ), state );
		_ = manager.HandleInput( Character( 'X' ), state );
		_ = manager.HandleInput( Character( '4' ), state );

		Assert.Equal(
			TopColorManagerInputResult.Commit,
			manager.HandleInput(
				new TopInputEvent(
					TopInputKey.Enter,
					null
				),
				state
			)
		);
		Assert.Equal(
			new TopColorPalette( 7, -1, 6, 5, 4 ),
			state.Colors
		);
	}

	[Fact]
	public void ColorManagerCancelRestoresWindowsAndBoldState() {
		TopRuntimeState state = new() {
			BoldEnabled = true,
			ColorsEnabled = false,
			Colors = new TopColorPalette( 20, 21, 22, 23, 24 )
		};
		var manager = new TopColorManagerState(
			state
		);
		state.ColorManager = manager;

		_ = manager.HandleInput( Character( 'B' ), state );
		_ = manager.HandleInput( Character( 'b' ), state );
		_ = manager.HandleInput( Character( 'S' ), state );
		_ = manager.HandleInput( Character( '7' ), state );
		_ = manager.HandleInput( Character( 'a' ), state );
		_ = manager.HandleInput( Character( 'T' ), state );
		_ = manager.HandleInput( Character( '2' ), state );

		Assert.Equal(
			TopColorManagerInputResult.Cancel,
			manager.HandleInput(
				Character( 'q' ),
				state
			)
		);
		Assert.Equal( 0, state.CurrentWindowIndex );
		Assert.True( state.BoldEnabled );
		Assert.True( state.HighlightBold );
		Assert.False( state.ColorsEnabled );
		Assert.Equal(
			new TopColorPalette( 20, 21, 22, 23, 24 ),
			state.Colors
		);
		state.ActivateWindow( 1 );
		Assert.Equal(
			TopColorPalette.ForWindow( 1 ),
			state.Colors
		);
	}

	[Fact]
	public void ColorManagerCyclesEntireNativeRange() {
		TopRuntimeState state = new() {
			Colors = new TopColorPalette( 1, 1, 3, 255, 1 )
		};
		var manager = new TopColorManagerState(
			state
		);

		_ = manager.HandleInput(
			new TopInputEvent(
				TopInputKey.Up,
				null
			),
			state
		);
		Assert.Equal( -1, state.Colors.Tasks );

		_ = manager.HandleInput(
			new TopInputEvent(
				TopInputKey.Down,
				null
			),
			state
		);
		Assert.Equal( 255, state.Colors.Tasks );
	}

	[Fact]
	public void ColorManagerSwitchesWindowsAndKeepsTheirEditsIndependent() {
		TopRuntimeState state = new() {
			Colors = new TopColorPalette( 10, 11, 12, 13, 14 )
		};
		var manager = new TopColorManagerState(
			state
		);

		_ = manager.HandleInput( Character( 'a' ), state );
		Assert.Equal( 1, state.CurrentWindowIndex );
		Assert.True( state.ColorsEnabled );
		_ = manager.HandleInput( Character( '7' ), state );
		Assert.Equal( 7, state.Colors.Tasks );

		_ = manager.HandleInput( Character( 'w' ), state );
		Assert.Equal( 0, state.CurrentWindowIndex );
		Assert.Equal(
			new TopColorPalette( 10, 11, 12, 13, 14 ),
			state.Colors
		);

		Assert.Equal(
			TopColorManagerInputResult.Commit,
			manager.HandleInput(
				new TopInputEvent(
					TopInputKey.Enter,
					null
				),
				state
			)
		);
		state.ActivateWindow( 1 );
		Assert.Equal( 7, state.Colors.Tasks );
	}

	[Fact]
	public void ColorManagerRendererCarriesPaletteForegrounds() {
		TopRuntimeState state = new() {
			Colors = new TopColorPalette( 10, 11, 12, 13, 14 )
		};
		state.ColorManager = new TopColorManagerState(
			state
		);

		TopRenderFrame frame = TopRenderer.RenderColorManager(
			state,
			new TopTerminalDimensions(
				80,
				12
			)
		);

		Assert.Equal( (int?)12, frame.Lines[ 0 ].ForegroundColor );
		Assert.Equal( (int?)10, frame.Lines[ 2 ].ForegroundColor );
		Assert.Equal( (int?)11, frame.Lines[ 3 ].ForegroundColor );
		Assert.Equal( (int?)12, frame.Lines[ 4 ].ForegroundColor );
		Assert.Equal( (int?)13, frame.Lines[ 5 ].ForegroundColor );
		Assert.Equal( (int?)14, frame.Lines[ 6 ].ForegroundColor );
		Assert.Equal(
			TopLineStyle.HighlightReverse,
			frame.Lines[ 5 ].Style
		);
	}

	private static TopInputEvent Character(
		char value
	) {
		return new TopInputEvent(
			TopInputKey.Character,
			new Rune(
				value
			)
		);
	}
}

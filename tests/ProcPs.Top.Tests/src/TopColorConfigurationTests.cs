/*
	Icod.ProcPs.Top.Tests
	Tests for top color-state persistence.
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

using Xunit;

/// <summary>Exercises procps-compatible top color defaults and persistence.</summary>
public sealed class TopColorConfigurationTests {
	[Fact]
	public void DefaultWindowsUseProcpsColorPalettes() {
		TopRuntimeState state = new();

		Assert.Equal(
			new TopColorPalette( 1, 1, 3, -1, 1 ),
			state.Windows[ 0 ].Colors
		);
		Assert.Equal(
			new TopColorPalette( 6, 6, 7, -1, 6 ),
			state.Windows[ 1 ].Colors
		);
		Assert.Equal(
			new TopColorPalette( 5, 5, 4, -1, 5 ),
			state.Windows[ 2 ].Colors
		);
		Assert.Equal(
			new TopColorPalette( 3, 3, 2, -1, 3 ),
			state.Windows[ 3 ].Colors
		);
	}

	[Fact]
	public void WindowActivationKeepsColorStateIndependent() {
		TopRuntimeState state = new() {
			ColorsEnabled = false,
			Colors = new TopColorPalette( 10, 11, 12, 13, 14 )
		};

		state.ActivateWindow( 1 );

		Assert.True( state.ColorsEnabled );
		Assert.Equal(
			TopColorPalette.ForWindow( 1 ),
			state.Colors
		);

		state.ActivateWindow( 0 );

		Assert.False( state.ColorsEnabled );
		Assert.Equal(
			new TopColorPalette( 10, 11, 12, 13, 14 ),
			state.Colors
		);
	}

	[Fact]
	public void JsonRoundTripPreservesPerWindowColorState() {
		TopRuntimeState source = new() {
			ColorsEnabled = false,
			Colors = new TopColorPalette( 42, 43, 44, 45, 46 )
		};
		source.ActivateWindow( 2 );
		source.Colors = new TopColorPalette( 100, 101, 102, 103, 104 );

		string serialized = TopConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState restored = new();
		TopConfigurationCodec.Apply(
			serialized,
			restored
		);

		Assert.Equal( 2, restored.CurrentWindowIndex );
		Assert.Equal(
			new TopColorPalette( 100, 101, 102, 103, 104 ),
			restored.Colors
		);
		restored.ActivateWindow( 0 );
		Assert.False( restored.ColorsEnabled );
		Assert.Equal(
			new TopColorPalette( 42, 43, 44, 45, 46 ),
			restored.Colors
		);
	}

	[Fact]
	public void NativeRoundTripPreservesPerWindowColorState() {
		TopRuntimeState source = new() {
			ColorsEnabled = false,
			Colors = new TopColorPalette( 200, 201, 202, 203, 204 )
		};
		source.ActivateWindow( 1 );
		source.Colors = new TopColorPalette( -1, 7, 6, 5, 4 );

		string serialized = TopProcpsConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState restored = new();
		TopProcpsConfigurationCodec.Apply(
			serialized,
			restored
		);

		Assert.Equal( 1, restored.CurrentWindowIndex );
		Assert.True( restored.ColorsEnabled );
		Assert.Equal(
			new TopColorPalette( -1, 7, 6, 5, 4 ),
			restored.Colors
		);
		restored.ActivateWindow( 0 );
		Assert.False( restored.ColorsEnabled );
		Assert.Equal(
			new TopColorPalette( 200, 201, 202, 203, 204 ),
			restored.Colors
		);
	}

	[Fact]
	public void NativeReadUsesCanonicalAccentWhenTaskXyIsAbsent() {
		string serialized = TopProcpsConfigurationCodec.Serialize(
			new TopRuntimeState()
		).Replace(
			", task_xy=1",
			string.Empty,
			StringComparison.Ordinal
		);

		TopRuntimeState restored = new();
		TopProcpsConfigurationCodec.Apply(
			serialized,
			restored
		);

		Assert.Equal( 1, restored.Colors.TaskAccent );
	}

	[Fact]
	public void NativeReadRejectsColorOutsideProcpsRange() {
		string serialized = TopProcpsConfigurationCodec.Serialize(
			new TopRuntimeState()
		).Replace(
			"summclr=1",
			"summclr=256",
			StringComparison.Ordinal
		);

		Assert.Throws<FormatException>(
			() => TopProcpsConfigurationCodec.Apply(
				serialized,
				new TopRuntimeState()
			)
		);
	}
}

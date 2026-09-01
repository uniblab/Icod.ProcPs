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

/// <summary>Identifies one configurable procps color target.</summary>
internal enum TopColorTarget {
	Summary,
	Messages,
	Header,
	Tasks,
	TaskAccent
}

/// <summary>Describes the result of one color-mapping input event.</summary>
internal enum TopColorManagerInputResult {
	None,
	Changed,
	Commit,
	Cancel
}

/// <summary>Tracks the temporary state used by the procps-compatible color mapping screen.</summary>
internal sealed class TopColorManagerState {
	private readonly TopWindowState[] savedWindows;
	private readonly int savedWindowIndex;
	private readonly bool savedBoldEnabled;

	internal TopColorManagerState(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		state.SynchronizeCurrentWindow();
		this.savedWindows = state.Windows
			.Select(
				window => window.Clone()
			)
			.ToArray();
		this.savedWindowIndex = state.CurrentWindowIndex;
		this.savedBoldEnabled = state.BoldEnabled;
		this.Target = TopColorTarget.Tasks;
		state.ColorsEnabled = true;
		state.Message = null;
	}

	internal TopColorTarget Target { get; private set; }

	internal TopColorManagerInputResult HandleInput(
		TopInputEvent input,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		if ( TopInputKey.Escape == input.Key ) {
			this.Restore(
				state
			);
			return TopColorManagerInputResult.Cancel;
		}
		if ( TopInputKey.Enter == input.Key ) {
			state.SynchronizeCurrentWindow();
			return TopColorManagerInputResult.Commit;
		}
		if ( TopInputKey.Up == input.Key ) {
			this.SetSelectedColor(
				state,
				NextColor(
					GetColor(
						state.Colors,
						this.Target
					)
				)
			);
			return TopColorManagerInputResult.Changed;
		}
		if ( TopInputKey.Down == input.Key ) {
			this.SetSelectedColor(
				state,
				PreviousColor(
					GetColor(
						state.Colors,
						this.Target
					)
				)
			);
			return TopColorManagerInputResult.Changed;
		}
		if (
			TopInputKey.Character != input.Key
			|| !input.Character.HasValue
		) {
			return TopColorManagerInputResult.None;
		}

		int value = input.Character.Value.Value;
		char key = ( 0x7f >= value )
			? (char)value
			: '\0'
		;
		switch ( key ) {
			case 'S':
				this.Target = TopColorTarget.Summary;
				return TopColorManagerInputResult.Changed;
			case 'M':
				this.Target = TopColorTarget.Messages;
				return TopColorManagerInputResult.Changed;
			case 'H':
				this.Target = TopColorTarget.Header;
				return TopColorManagerInputResult.Changed;
			case 'T':
				this.Target = TopColorTarget.Tasks;
				return TopColorManagerInputResult.Changed;
			case 'X':
				this.Target = TopColorTarget.TaskAccent;
				return TopColorManagerInputResult.Changed;
			case '@':
				this.SetSelectedColor(
					state,
					-1
				);
				return TopColorManagerInputResult.Changed;
			case >= '0' and <= '7':
				this.SetSelectedColor(
					state,
					key - '0'
				);
				return TopColorManagerInputResult.Changed;
			case 'B':
				state.BoldEnabled = !state.BoldEnabled;
				return TopColorManagerInputResult.Changed;
			case 'b':
				state.HighlightBold = !state.HighlightBold;
				return TopColorManagerInputResult.Changed;
			case 'z':
				state.ColorsEnabled = !state.ColorsEnabled;
				return TopColorManagerInputResult.Changed;
			case 'a':
				this.ActivateRelativeWindow(
					state,
					1
				);
				return TopColorManagerInputResult.Changed;
			case 'w':
				this.ActivateRelativeWindow(
					state,
					-1
				);
				return TopColorManagerInputResult.Changed;
			case 'q':
			case 'Q':
				this.Restore(
					state
				);
				return TopColorManagerInputResult.Cancel;
			default:
				return TopColorManagerInputResult.None;
		}
	}

	internal void Restore(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		state.RestoreWindows(
			this.savedWindows,
			this.savedWindowIndex
		);
		state.BoldEnabled = this.savedBoldEnabled;
		state.Message = null;
	}

	internal static int GetColor(
		TopColorPalette palette,
		TopColorTarget target
	) {
		return target switch {
			TopColorTarget.Summary => palette.Summary,
			TopColorTarget.Messages => palette.Messages,
			TopColorTarget.Header => palette.Header,
			TopColorTarget.Tasks => palette.Tasks,
			TopColorTarget.TaskAccent => palette.TaskAccent,
			_ => throw new ArgumentOutOfRangeException(
				nameof( target )
			)
		};
	}

	private void SetSelectedColor(
		TopRuntimeState state,
		int value
	) {
		ArgumentNullException.ThrowIfNull( state );

		TopColorPalette current = state.Colors;
		state.Colors = this.Target switch {
			TopColorTarget.Summary => new TopColorPalette(
				value,
				current.Messages,
				current.Header,
				current.Tasks,
				current.TaskAccent
			),
			TopColorTarget.Messages => new TopColorPalette(
				current.Summary,
				value,
				current.Header,
				current.Tasks,
				current.TaskAccent
			),
			TopColorTarget.Header => new TopColorPalette(
				current.Summary,
				current.Messages,
				value,
				current.Tasks,
				current.TaskAccent
			),
			TopColorTarget.Tasks => new TopColorPalette(
				current.Summary,
				current.Messages,
				current.Header,
				value,
				current.TaskAccent
			),
			TopColorTarget.TaskAccent => new TopColorPalette(
				current.Summary,
				current.Messages,
				current.Header,
				current.Tasks,
				value
			),
			_ => throw new InvalidOperationException(
				"The color mapping target was not recognized."
			)
		};
	}

	private void ActivateRelativeWindow(
		TopRuntimeState state,
		int delta
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( delta is not -1 and not 1 ) {
			throw new ArgumentOutOfRangeException(
				nameof( delta )
			);
		}

		int windowIndex = (
			state.CurrentWindowIndex
			+ delta
			+ TopRuntimeState.WindowCount
		) % TopRuntimeState.WindowCount;
		state.ActivateWindow(
			windowIndex
		);
		state.ColorsEnabled = true;
		this.Target = TopColorTarget.Tasks;
	}

	private static int NextColor(
		int color
	) {
		if ( color is < -1 or > 255 ) {
			throw new ArgumentOutOfRangeException(
				nameof( color )
			);
		}
		return ( 255 == color )
			? -1
			: color + 1
		;
	}

	private static int PreviousColor(
		int color
	) {
		if ( color is < -1 or > 255 ) {
			throw new ArgumentOutOfRangeException(
				nameof( color )
			);
		}
		return ( -1 == color )
			? 255
			: color - 1
		;
	}
}

/*
	top
	Forest projection support for interactive task display.
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

using Icod.Processes;

/// <summary>Builds reuse-aware focused and collapsed forest projections.</summary>
internal static class TopForestProjection {
	internal static bool HasChildren(
		IReadOnlyList<TopTaskRow> tasks,
		ProcessIdentity identity
	) {
		ArgumentNullException.ThrowIfNull( tasks );
		ArgumentNullException.ThrowIfNull( identity );

		TopTaskRow? parent = tasks.FirstOrDefault(
			row => row.Process.Identity.Equals( identity )
		);
		if ( parent is null ) {
			return false;
		}
		int processId = parent.Process.ProcessId;
		return tasks.Any(
			row => row.Process.ParentProcessId.HasValue
				&& row.Process.ParentProcessId.Value == processId
		);
	}

	internal static List<TopTaskRow> Order(
		IReadOnlyList<TopTaskRow> tasks,
		Comparison<TopTaskRow> comparison,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( tasks );
		ArgumentNullException.ThrowIfNull( comparison );
		ArgumentNullException.ThrowIfNull( state );

		var byId = tasks
			.GroupBy( row => row.Process.ProcessId )
			.ToDictionary(
				group => group.Key,
				group => group.First()
			);
		var children = new Dictionary<int, List<TopTaskRow>>();
		var roots = new List<TopTaskRow>();
		foreach ( TopTaskRow row in tasks ) {
			if (
				row.Process.ParentProcessId.HasValue
				&& byId.ContainsKey(
					row.Process.ParentProcessId.Value
				)
			) {
				if (
					!children.TryGetValue(
						row.Process.ParentProcessId.Value,
						out List<TopTaskRow>? list
					)
				) {
					list = [];
					children.Add(
						row.Process.ParentProcessId.Value,
						list
					);
				}
				list.Add( row );
			} else {
				roots.Add( row );
			}
		}
		roots.Sort( comparison );
		foreach ( List<TopTaskRow> list in children.Values ) {
			list.Sort( comparison );
		}

		TopTaskRow? focus = null;
		if ( state.ForestFocus is not null ) {
			ProcessIdentity identity = state.ForestFocus;
			focus = tasks.FirstOrDefault(
				row => row.Process.Identity.Equals( identity )
			);
		}

		var subtreeCpu = new Dictionary<ProcessIdentity, double>();
		var activeCpu = new HashSet<ProcessIdentity>();
		double TotalCpu( TopTaskRow row ) {
			ProcessIdentity identity = row.Process.Identity;
			if (
				subtreeCpu.TryGetValue(
					identity,
					out double cached
				)
			) {
				return cached;
			}
			if ( !activeCpu.Add( identity ) ) {
				return row.CpuPercentIrix;
			}

			double total = row.CpuPercentIrix;
			if (
				children.TryGetValue(
					row.Process.ProcessId,
					out List<TopTaskRow>? list
				)
			) {
				foreach ( TopTaskRow child in list ) {
					total += TotalCpu( child );
				}
			}
			activeCpu.Remove( identity );
			subtreeCpu[ identity ] = total;
			return total;
		}

		var result = new List<TopTaskRow>( tasks.Count );
		var visited = new HashSet<int>();
		void Add( TopTaskRow row, int depth ) {
			if ( !visited.Add( row.Process.ProcessId ) ) {
				return;
			}

			bool collapsed = state.CollapsedForestParents.Contains(
				row.Process.Identity
			);
			double cpuPercent = ( collapsed )
				? TotalCpu( row )
				: row.CpuPercentIrix
			;
			result.Add(
				row.CreateForestPresentation(
					cpuPercent,
					depth
				)
			);
			if ( collapsed ) {
				return;
			}
			if (
				children.TryGetValue(
					row.Process.ProcessId,
					out List<TopTaskRow>? list
				)
			) {
				foreach ( TopTaskRow child in list ) {
					Add(
						child,
						depth + 1
					);
				}
			}
		}

		if ( state.ForestFocus is not null ) {
			if ( focus is not null ) {
				Add(
					focus,
					0
				);
			}
			return result;
		}

		foreach ( TopTaskRow root in roots ) {
			Add(
				root,
				0
			);
		}
		foreach ( TopTaskRow row in tasks ) {
			Add(
				row,
				0
			);
		}
		return result;
	}
}

namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using Icod.Processes;
using ObservationFidelity = Icod.ProcPs.Shared.ProcObservationFidelity;
using Xunit;

/// <summary>Contains tests for selection and presentation.</summary>
public sealed class SelectionAndPresentationTests {
	/// <summary>Verifies that selection uses or within criterion and and across criteria.</summary>
	[Fact]
	public void SelectionUsesOrWithinCriterionAndAndAcrossCriteria() {
		var first = Snapshot( 10, 1, 1000, "alpha" );
		var second = Snapshot( 11, 2, 1000, "beta" );
		var third = Snapshot( 12, 1, 2000, "gamma" );
		var selection = new ProcProcessSelection {
			ParentProcessIds = new HashSet<int> { 1, 9 },
			EffectiveUserIds = new HashSet<uint> { 1000 },
			CommandNamePredicate = name => name.StartsWith( "a", StringComparison.Ordinal )
		};
		var selected = ProcProcessSelectionEngine.Select( new[] { first, second, third }, selection );
		Assert.Single( selected );
		Assert.Equal( 10, selected[ 0 ].ProcessId );
	}

	/// <summary>Verifies that selection grammar parses lists and states.</summary>
	[Fact]
	public void SelectionGrammarParsesListsAndStates() {
		Assert.Equal( new[] { 1, 2, 3 }, ProcSelectionGrammar.ParseIdentifiers( "1,2 3" ).OrderBy( value => value ) );
		Assert.Equal( new[] { ProcProcessState.Running, ProcProcessState.Sleeping }, ProcSelectionGrammar.ParseStates( "RS" ).OrderBy( value => value ) );
	}

	/// <summary>Verifies that relationship index collects children by parent pid.</summary>
	[Fact]
	public void RelationshipIndexCollectsChildrenByParentPid() {
		var index = ProcProcessRelations.BuildChildrenIndex( new[] { Snapshot( 10, 1, 0, "a" ), Snapshot( 11, 1, 0, "b" ), Snapshot( 12, 2, 0, "c" ) } );
		Assert.Equal( new[] { 10, 11 }, index[ 1 ].Select( child => child.ProcessId ) );
		Assert.Equal( 12, Assert.Single( index[ 2 ] ).ProcessId );
	}

	/// <summary>Verifies that sorter is stable across equal keys.</summary>
	[Fact]
	public void SorterIsStableAcrossEqualKeys() {
		var first = Snapshot( 11, 1, 1000, "same" );
		var second = Snapshot( 10, 1, 1000, "same" );
		var sorted = ProcProcessSorter.Sort( new[] { first, second }, new[] { new ProcSortKey( ProcFieldKind.Command ) } );
		Assert.Equal( new[] { 11, 10 }, sorted.Select( process => process.ProcessId ) );
	}

	/// <summary>Verifies that personalities recognize compatibility names.</summary>
	[Theory]
	[InlineData( "linux", ProcPersonality.Linux )]
	[InlineData( "sysv", ProcPersonality.Posix )]
	[InlineData( "bsd", ProcPersonality.Bsd )]
	[InlineData( "sunos4", ProcPersonality.SunOs4 )]
	[InlineData( "hpux", ProcPersonality.Hp )]
	public void PersonalitiesRecognizeCompatibilityNames( string text, ProcPersonality expected ) {
		Assert.True( ProcPersonalityResolver.TryParse( text, out var actual ) );
		Assert.Equal( expected, actual );
	}

	/// <summary>Verifies that screen builder uses catalog and configured sort.</summary>
	[Fact]
	public void ScreenBuilderUsesCatalogAndConfiguredSort() {
		var configuration = new ProcDisplayConfiguration(
			new[] { ProcFieldCatalog.Find( "pid" )!, ProcFieldCatalog.Find( "comm" )! },
			new[] { new ProcSortKey( ProcFieldKind.Pid ) }
		);
		var frame = ProcScreenBuilder.Build( 0, TimeSpan.Zero, new[] { Snapshot( 2, 1, 0, "b" ), Snapshot( 1, 1, 0, "a" ) }, configuration );
		Assert.Equal( new[] { "PID", "COMMAND" }, frame.Headers );
		Assert.Equal( new[] { 1, 2 }, frame.Rows.Select( row => row.ProcessId ) );
	}

	private static ProcProcessSnapshot Snapshot( int pid, int parent, uint user, string command ) => new( new ProcessIdentity( pid ) ) {
		ParentProcessId = ProcObservedValue<int>.Available( parent, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact ),
		EffectiveUserId = ProcObservedValue<uint>.Available( user, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact ),
		CommandName = ProcObservedValue<string>.Available( command, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact )
	};
}

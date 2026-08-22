namespace Icod.ProcPs.Shared;

using System.Globalization;

/// <summary>Identifies a field that can be displayed in a ProcPs process report.</summary>
public enum ProcFieldKind {
	/// <summary>Selects the pid process-report field.</summary>
	Pid,
	/// <summary>Selects the parent pid process-report field.</summary>
	ParentPid,
	/// <summary>Selects the process group process-report field.</summary>
	ProcessGroup,
	/// <summary>Selects the session process-report field.</summary>
	Session,
	/// <summary>Selects the command process-report field.</summary>
	Command,
	/// <summary>Selects the command line process-report field.</summary>
	CommandLine,
	/// <summary>Selects the state process-report field.</summary>
	State,
	/// <summary>Selects the effective user process-report field.</summary>
	EffectiveUser,
	/// <summary>Selects the terminal process-report field.</summary>
	Terminal,
	/// <summary>Selects the nice process-report field.</summary>
	Nice,
	/// <summary>Selects the threads process-report field.</summary>
	Threads,
	/// <summary>Selects the resident memory process-report field.</summary>
	ResidentMemory,
	/// <summary>Selects the virtual memory process-report field.</summary>
	VirtualMemory }
/// <summary>Specifies how a process-report field is aligned.</summary>
public enum ProcFieldAlignment {
	/// <summary>Aligns field text to the left.</summary>
	Left,
	/// <summary>Aligns field text to the right.</summary>
	Right }
/// <summary>Describes one process-report field.</summary>
public sealed class ProcFieldDefinition {
	/// <summary>Gets the semantic field kind.</summary>
	public ProcFieldKind Kind{get;}
	/// <summary>Gets the canonical field name.</summary>
	public string Name{get;}
	/// <summary>Gets the default column heading.</summary>
	public string Header{get;}
	/// <summary>Gets the minimum display width.</summary>
	public int MinimumWidth{get;}
	/// <summary>Gets the field alignment.</summary>
	public ProcFieldAlignment Alignment{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcFieldDefinition"/> type.</summary>
	public ProcFieldDefinition(ProcFieldKind kind,string name,string header,int minimumWidth,ProcFieldAlignment alignment){ArgumentException.ThrowIfNullOrWhiteSpace(name);ArgumentNullException.ThrowIfNull(header);ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);this.Kind=kind;this.Name=name;this.Header=header;this.MinimumWidth=minimumWidth;this.Alignment=alignment;} }
/// <summary>Provides the catalog of fields supported by the shared ProcPs presentation layer.</summary>
public static class ProcFieldCatalog {
	private static readonly IReadOnlyList<ProcFieldDefinition> DefinitionsValue = new[]{new ProcFieldDefinition(ProcFieldKind.Pid,"pid","PID",5,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.ParentPid,"ppid","PPID",5,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.ProcessGroup,"pgid","PGID",5,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.Session,"sid","SID",5,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.EffectiveUser,"euid","EUID",5,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.Terminal,"tty","TTY",8,ProcFieldAlignment.Left),new ProcFieldDefinition(ProcFieldKind.State,"state","S",1,ProcFieldAlignment.Left),new ProcFieldDefinition(ProcFieldKind.Nice,"ni","NI",3,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.Threads,"nlwp","NLWP",4,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.ResidentMemory,"rss","RSS",6,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.VirtualMemory,"vsz","VSZ",7,ProcFieldAlignment.Right),new ProcFieldDefinition(ProcFieldKind.Command,"comm","COMMAND",15,ProcFieldAlignment.Left),new ProcFieldDefinition(ProcFieldKind.CommandLine,"args","COMMAND",20,ProcFieldAlignment.Left)};
	/// <summary>Gets all registered process-report field definitions.</summary>
	public static IReadOnlyList<ProcFieldDefinition> Definitions=>DefinitionsValue;
	/// <summary>Finds a field definition by name.</summary>
	public static ProcFieldDefinition? Find(string name){ArgumentException.ThrowIfNullOrWhiteSpace(name);return DefinitionsValue.FirstOrDefault(field=>string.Equals(field.Name,name,StringComparison.OrdinalIgnoreCase));}
	/// <summary>Formats one field from a process snapshot.</summary>
	public static string Format(ProcProcessSnapshot process,ProcFieldKind kind){ArgumentNullException.ThrowIfNull(process);return kind switch{ProcFieldKind.Pid=>process.ProcessId.ToString(CultureInfo.InvariantCulture),ProcFieldKind.ParentPid=>FormatObserved(process.ParentProcessId),ProcFieldKind.ProcessGroup=>FormatObserved(process.ProcessGroupId),ProcFieldKind.Session=>FormatObserved(process.SessionId),ProcFieldKind.Command=>process.CommandName.HasValue?process.CommandName.Value:"-",ProcFieldKind.CommandLine=>process.CommandLineArguments.HasValue?string.Join(" ",process.CommandLineArguments.Value):"-",ProcFieldKind.State=>process.State.HasValue?StateCode(process.State.Value):"-",ProcFieldKind.EffectiveUser=>FormatObserved(process.EffectiveUserId),ProcFieldKind.Terminal=>process.Terminal.HasValue?process.Terminal.Value.Name??process.Terminal.Value.DeviceNumber.ToString(CultureInfo.InvariantCulture):"-",ProcFieldKind.Nice=>FormatObserved(process.NiceValue),ProcFieldKind.Threads=>FormatObserved(process.ThreadCount),ProcFieldKind.ResidentMemory=>FormatObserved(process.ResidentMemoryBytes),ProcFieldKind.VirtualMemory=>FormatObserved(process.VirtualMemoryBytes),_=>throw new ArgumentOutOfRangeException(nameof(kind))};}
	private static string FormatObserved<T>(ProcObservedValue<T> value)where T:IFormattable=>value.HasValue?value.Value.ToString(null,CultureInfo.InvariantCulture):"-"; private static string StateCode(ProcProcessState state)=>state switch{ProcProcessState.Running=>"R",ProcProcessState.Sleeping=>"S",ProcProcessState.DiskSleep=>"D",ProcProcessState.Stopped=>"T",ProcProcessState.TracingStop=>"t",ProcProcessState.Zombie=>"Z",ProcProcessState.Dead=>"X",ProcProcessState.Idle=>"I",ProcProcessState.Waking=>"W",ProcProcessState.Parked=>"P",_=>"?"};
}
/// <summary>Specifies a process-report compatibility personality.</summary>
public enum ProcPersonality {
	/// <summary>Selects the linux compatibility personality.</summary>
	Linux,
	/// <summary>Selects the posix compatibility personality.</summary>
	Posix,
	/// <summary>Selects the bsd compatibility personality.</summary>
	Bsd,
	/// <summary>Selects the sun os4 compatibility personality.</summary>
	SunOs4,
	/// <summary>Selects the digital compatibility personality.</summary>
	Digital,
	/// <summary>Selects the hp compatibility personality.</summary>
	Hp,
	/// <summary>Selects the aix compatibility personality.</summary>
	Aix }
/// <summary>Resolves process-report compatibility personalities from names and environment settings.</summary>
public static class ProcPersonalityResolver {
	/// <summary>Attempts to parse a process-report compatibility personality name.</summary>
	public static bool TryParse(string? text,out ProcPersonality personality){personality=ProcPersonality.Linux;if(string.IsNullOrWhiteSpace(text))return false;switch(text.Trim().ToLowerInvariant()){case "linux":personality=ProcPersonality.Linux;return true;case "posix":case "sysv":case "unix":personality=ProcPersonality.Posix;return true;case "bsd":personality=ProcPersonality.Bsd;return true;case "sunos4":case "sun":personality=ProcPersonality.SunOs4;return true;case "digital":case "tru64":personality=ProcPersonality.Digital;return true;case "hp":case "hpux":personality=ProcPersonality.Hp;return true;case "aix":personality=ProcPersonality.Aix;return true;default:return false;}}
	/// <summary>Resolves a process-report personality from the supported environment variables.</summary>
	public static ProcPersonality ResolveEnvironment(IReadOnlyDictionary<string,string?> environment){ArgumentNullException.ThrowIfNull(environment);foreach(var name in new[]{"PS_PERSONALITY","CMD_ENV"})if(environment.TryGetValue(name,out var value)&&TryParse(value,out var personality))return personality;return ProcPersonality.Linux;} }
/// <summary>Describes one process-report sort key.</summary>
public sealed class ProcSortKey {
	/// <summary>Gets the field used for sorting.</summary>
	public ProcFieldKind Field{get;}
	/// <summary>Gets whether this key sorts in descending order.</summary>
	public bool Descending{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcSortKey"/> type.</summary>
	public ProcSortKey(ProcFieldKind field,bool descending=false){this.Field=field;this.Descending=descending;} }
/// <summary>Provides stable sorting for process snapshots.</summary>
public static class ProcProcessSorter {
	/// <summary>Sorts process snapshots according to the supplied keys.</summary>
	public static IReadOnlyList<ProcProcessSnapshot> Sort(IEnumerable<ProcProcessSnapshot> processes,IEnumerable<ProcSortKey> keys){ArgumentNullException.ThrowIfNull(processes);ArgumentNullException.ThrowIfNull(keys);var keyArray=keys.ToArray();var indexed=processes.Select((process,index)=>(process,index)).ToList();indexed.Sort((left,right)=>{foreach(var key in keyArray){var comparison=CompareField(left.process,right.process,key.Field);if(0!=comparison)return key.Descending?-comparison:comparison;}return left.index.CompareTo(right.index);});return indexed.Select(item=>item.process).ToArray();} private static int CompareField(ProcProcessSnapshot left,ProcProcessSnapshot right,ProcFieldKind field)=>field switch{ProcFieldKind.Pid=>left.ProcessId.CompareTo(right.ProcessId),ProcFieldKind.ParentPid=>CompareObserved(left.ParentProcessId,right.ParentProcessId),ProcFieldKind.ProcessGroup=>CompareObserved(left.ProcessGroupId,right.ProcessGroupId),ProcFieldKind.Session=>CompareObserved(left.SessionId,right.SessionId),ProcFieldKind.EffectiveUser=>CompareObserved(left.EffectiveUserId,right.EffectiveUserId),ProcFieldKind.Nice=>CompareObserved(left.NiceValue,right.NiceValue),ProcFieldKind.Threads=>CompareObserved(left.ThreadCount,right.ThreadCount),ProcFieldKind.ResidentMemory=>CompareObserved(left.ResidentMemoryBytes,right.ResidentMemoryBytes),ProcFieldKind.VirtualMemory=>CompareObserved(left.VirtualMemoryBytes,right.VirtualMemoryBytes),_=>StringComparer.Ordinal.Compare(ProcFieldCatalog.Format(left,field),ProcFieldCatalog.Format(right,field))}; private static int CompareObserved<T>(ProcObservedValue<T> left,ProcObservedValue<T> right)where T:IComparable<T>{if(left.HasValue&&right.HasValue)return left.Value.CompareTo(right.Value);if(left.HasValue)return -1;return right.HasValue?1:0;} }
/// <summary>Describes the fields, sorting, personality, and width of a process display.</summary>
public sealed class ProcDisplayConfiguration {
	/// <summary>Gets the fields displayed in each row.</summary>
	public IReadOnlyList<ProcFieldDefinition> Fields{get;}
	/// <summary>Gets the ordered sort keys.</summary>
	public IReadOnlyList<ProcSortKey> SortKeys{get;}
	/// <summary>Gets the selected compatibility personality.</summary>
	public ProcPersonality Personality{get;}
	/// <summary>Gets the requested display width, or null when unconstrained.</summary>
	public int? Width{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcDisplayConfiguration"/> type.</summary>
	public ProcDisplayConfiguration(IEnumerable<ProcFieldDefinition> fields,IEnumerable<ProcSortKey>? sortKeys=null,ProcPersonality personality=ProcPersonality.Linux,int? width=null){ArgumentNullException.ThrowIfNull(fields);if(width is <=0)throw new ArgumentOutOfRangeException(nameof(width));this.Fields=fields.ToArray();this.SortKeys=null==sortKeys?Array.Empty<ProcSortKey>():sortKeys.ToArray();this.Personality=personality;this.Width=width;} }
/// <summary>Represents one rendered process row.</summary>
public sealed class ProcScreenRow {
	/// <summary>Gets the process identifier represented by the row.</summary>
	public int ProcessId{get;}
	/// <summary>Gets the formatted cells in display order.</summary>
	public IReadOnlyList<string> Cells{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcScreenRow"/> type.</summary>
	public ProcScreenRow(int processId,IEnumerable<string> cells){ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);ArgumentNullException.ThrowIfNull(cells);this.ProcessId=processId;this.Cells=cells.ToArray();} }
/// <summary>Represents one complete process-screen frame.</summary>
public sealed class ProcScreenFrame {
	/// <summary>Gets the sample sequence number.</summary>
	public long Sequence{get;}
	/// <summary>Gets the elapsed time represented by the frame.</summary>
	public TimeSpan Elapsed{get;}
	/// <summary>Gets the column headings.</summary>
	public IReadOnlyList<string> Headers{get;}
	/// <summary>Gets the rendered process rows.</summary>
	public IReadOnlyList<ProcScreenRow> Rows{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcScreenFrame"/> type.</summary>
	public ProcScreenFrame(long sequence,TimeSpan elapsed,IEnumerable<string> headers,IEnumerable<ProcScreenRow> rows){ArgumentOutOfRangeException.ThrowIfNegative(sequence);if(TimeSpan.Zero>elapsed)throw new ArgumentOutOfRangeException(nameof(elapsed));ArgumentNullException.ThrowIfNull(headers);ArgumentNullException.ThrowIfNull(rows);this.Sequence=sequence;this.Elapsed=elapsed;this.Headers=headers.ToArray();this.Rows=rows.ToArray();} }
/// <summary>Builds process-screen frames from process snapshots.</summary>
public static class ProcScreenBuilder {
	/// <summary>Builds a rendered process-screen frame from process snapshots.</summary>
	public static ProcScreenFrame Build(long sequence,TimeSpan elapsed,IEnumerable<ProcProcessSnapshot> processes,ProcDisplayConfiguration configuration){ArgumentNullException.ThrowIfNull(processes);ArgumentNullException.ThrowIfNull(configuration);var sorted=ProcProcessSorter.Sort(processes,configuration.SortKeys);var rows=sorted.Select(process=>new ProcScreenRow(process.ProcessId,configuration.Fields.Select(field=>ProcFieldCatalog.Format(process,field.Kind))));return new ProcScreenFrame(sequence,elapsed,configuration.Fields.Select(field=>field.Header),rows);} }

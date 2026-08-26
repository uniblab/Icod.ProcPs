namespace Icod.ProcPs.Shared;

using System.Globalization;
using System.Runtime.InteropServices;

/// <summary>Represents cumulative procps-style CPU time counters.</summary>
public sealed class ProcCpuTimes {
	/// <summary>Gets cumulative user-mode CPU ticks.</summary>
	public ulong User { get; }
	/// <summary>Gets cumulative niced user-mode CPU ticks.</summary>
	public ulong Nice { get; }
	/// <summary>Gets cumulative kernel-mode CPU ticks.</summary>
	public ulong System { get; }
	/// <summary>Gets cumulative idle CPU ticks.</summary>
	public ulong Idle { get; }
	/// <summary>Gets cumulative I/O-wait CPU ticks.</summary>
	public ulong IoWait { get; }
	/// <summary>Gets cumulative hardware-interrupt CPU ticks.</summary>
	public ulong Irq { get; }
	/// <summary>Gets cumulative software-interrupt CPU ticks.</summary>
	public ulong SoftIrq { get; }
	/// <summary>Gets cumulative stolen CPU ticks.</summary>
	public ulong Steal { get; }
	/// <summary>Gets cumulative guest CPU ticks.</summary>
	public ulong Guest { get; }
	/// <summary>Gets cumulative niced-guest CPU ticks.</summary>
	public ulong GuestNice { get; }
	/// <summary>Gets the cumulative CPU ticks used as the procps divisor.</summary>
	public ulong Total => unchecked( this.User + this.Nice + this.System + this.Idle + this.IoWait + this.Irq + this.SoftIrq + this.Steal );
	/// <summary>Initializes a new instance of the <see cref="ProcCpuTimes"/> type.</summary>
	public ProcCpuTimes( ulong user, ulong nice, ulong system, ulong idle, ulong ioWait, ulong irq, ulong softIrq, ulong steal, ulong guest, ulong guestNice ) {
		this.User = user; this.Nice = nice; this.System = system; this.Idle = idle; this.IoWait = ioWait; this.Irq = irq; this.SoftIrq = softIrq; this.Steal = steal; this.Guest = guest; this.GuestNice = guestNice;
	}
}
/// <summary>Represents portable CPU activity counters used for interval calculations.</summary>
public sealed class ProcCpuActivity {
	/// <summary>Gets cumulative user CPU activity units.</summary>
	public ulong User { get; }
	/// <summary>Gets cumulative system CPU activity units.</summary>
	public ulong System { get; }
	/// <summary>Gets cumulative idle CPU activity units.</summary>
	public ulong Idle { get; }
	/// <summary>Gets cumulative nice CPU activity units when available.</summary>
	public ulong? Nice { get; }
	/// <summary>Gets cumulative wait CPU activity units when available.</summary>
	public ulong? Wait { get; }
	/// <summary>Gets cumulative activity not represented by the primary CPU categories.</summary>
	public ulong? Other { get; }
	/// <summary>Gets the native counter width used for wrap-aware deltas.</summary>
	public int CounterBitWidth { get; }
	/// <summary>Gets the total represented CPU activity units.</summary>
	public ulong Total => unchecked( this.User + this.System + this.Idle + ( this.Nice ?? 0UL ) + ( this.Wait ?? 0UL ) + ( this.Other ?? 0UL ) );
	/// <summary>Initializes a new instance of the <see cref="ProcCpuActivity"/> type.</summary>
	public ProcCpuActivity( ulong user, ulong system, ulong idle, ulong? nice = null, ulong? wait = null, ulong? other = null, int counterBitWidth = 64 ) { if ( 1 > counterBitWidth || 64 < counterBitWidth ) throw new ArgumentOutOfRangeException( nameof( counterBitWidth ) ); this.User=user; this.System=system; this.Idle=idle; this.Nice=nice; this.Wait=wait; this.Other=other; this.CounterBitWidth=counterBitWidth; }
}
/// <summary>Represents the one-, five-, and fifteen-minute system load averages.</summary>
public sealed class ProcLoadAverages {
	/// <summary>Gets the one-minute load average.</summary>
	public double OneMinute{get;}
	/// <summary>Gets the five-minute load average.</summary>
	public double FiveMinutes{get;}
	/// <summary>Gets the fifteen-minute load average.</summary>
	public double FifteenMinutes{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcLoadAverages"/> type.</summary>
	public ProcLoadAverages(double oneMinute,double fiveMinutes,double fifteenMinutes){this.OneMinute=oneMinute;this.FiveMinutes=fiveMinutes;this.FifteenMinutes=fifteenMinutes;} }
/// <summary>Represents Linux load-average data including runnable-task metadata.</summary>
public sealed class ProcLoadAverage {
	/// <summary>Gets the one-minute load average.</summary>
	public double OneMinute{get;}
	/// <summary>Gets the five-minute load average.</summary>
	public double FiveMinutes{get;}
	/// <summary>Gets the fifteen-minute load average.</summary>
	public double FifteenMinutes{get;}
	/// <summary>Gets the number of runnable scheduling entities.</summary>
	public int Runnable{get;}
	/// <summary>Gets the total number of scheduling entities.</summary>
	public int TotalEntities{get;}
	/// <summary>Gets the most recently allocated process identifier reported by procfs.</summary>
	public int LastProcessId{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcLoadAverage"/> type.</summary>
	public ProcLoadAverage(double oneMinute,double fiveMinutes,double fifteenMinutes,int runnable,int totalEntities,int lastProcessId){this.OneMinute=oneMinute;this.FiveMinutes=fiveMinutes;this.FifteenMinutes=fifteenMinutes;this.Runnable=runnable;this.TotalEntities=totalEntities;this.LastProcessId=lastProcessId;} }
/// <summary>Represents observed system or container uptime information.</summary>
public sealed class ProcUptimeInfo {
	/// <summary>Gets the observed uptime duration.</summary>
	public TimeSpan Uptime{get;}
	/// <summary>Gets aggregate idle time when the provider exposes it.</summary>
	public TimeSpan? IdleTime{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcUptimeInfo"/> type.</summary>
	public ProcUptimeInfo(TimeSpan uptime,TimeSpan? idleTime){if(TimeSpan.Zero>uptime)throw new ArgumentOutOfRangeException(nameof(uptime));this.Uptime=uptime;this.IdleTime=idleTime;} }
/// <summary>Represents normalized system memory and swap information.</summary>
public sealed class ProcMemoryInfo {
	/// <summary>Gets the normalized and provider-specific memory fields.</summary>
	public IReadOnlyDictionary<string,ulong> Fields{get;}
	/// <summary>Gets total physical memory in bytes when available.</summary>
	public ulong? TotalBytes{get;}
	/// <summary>Gets free physical memory in bytes when available.</summary>
	public ulong? FreeBytes{get;}
	/// <summary>Gets memory available for new workloads in bytes when available.</summary>
	public ulong? AvailableBytes{get;}
	/// <summary>Gets buffer memory in bytes when available.</summary>
	public ulong? BuffersBytes{get;}
	/// <summary>Gets reclaimable cache memory in bytes when available.</summary>
	public ulong? CacheBytes{get;}
	/// <summary>Gets shared memory in bytes when available.</summary>
	public ulong? SharedBytes{get;}
	/// <summary>Gets total swap capacity in bytes when available.</summary>
	public ulong? SwapTotalBytes{get;}
	/// <summary>Gets free swap capacity in bytes when available.</summary>
	public ulong? SwapFreeBytes{get;}
	/// <summary>Gets the memory commit limit in bytes when available.</summary>
	public ulong? CommitLimitBytes{get;}
	/// <summary>Gets committed virtual memory in bytes when available.</summary>
	public ulong? CommittedBytes{get;}
	/// <summary>Gets total low memory in bytes when available.</summary>
	public ulong? LowTotalBytes{get;}
	/// <summary>Gets free low memory in bytes when available.</summary>
	public ulong? LowFreeBytes{get;}
	/// <summary>Gets total high memory in bytes when available.</summary>
	public ulong? HighTotalBytes{get;}
	/// <summary>Gets free high memory in bytes when available.</summary>
	public ulong? HighFreeBytes{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcMemoryInfo"/> type.</summary>
	public ProcMemoryInfo(IReadOnlyDictionary<string,ulong> fields){ArgumentNullException.ThrowIfNull(fields);this.Fields=fields;this.TotalBytes=Get(fields,"MemTotal");this.FreeBytes=Get(fields,"MemFree");this.AvailableBytes=Get(fields,"MemAvailable");this.BuffersBytes=Get(fields,"Buffers");this.CacheBytes=SaturatingAdd(Get(fields,"Cached"),Get(fields,"SReclaimable"));this.SharedBytes=Get(fields,"Shmem");this.SwapTotalBytes=Get(fields,"SwapTotal");this.SwapFreeBytes=Get(fields,"SwapFree");this.CommitLimitBytes=Get(fields,"CommitLimit");this.CommittedBytes=Get(fields,"Committed_AS");this.LowTotalBytes=Get(fields,"LowTotal");this.LowFreeBytes=Get(fields,"LowFree");this.HighTotalBytes=Get(fields,"HighTotal");this.HighFreeBytes=Get(fields,"HighFree");}
	/// <summary>Initializes a new instance of the <see cref="ProcMemoryInfo"/> type.</summary>
	public ProcMemoryInfo(ulong? totalBytes,ulong? freeBytes,ulong? availableBytes,ulong? buffersBytes=null,ulong? cacheBytes=null,ulong? sharedBytes=null,ulong? swapTotalBytes=null,ulong? swapFreeBytes=null,ulong? commitLimitBytes=null,ulong? committedBytes=null,ulong? lowTotalBytes=null,ulong? lowFreeBytes=null,ulong? highTotalBytes=null,ulong? highFreeBytes=null,IReadOnlyDictionary<string,ulong>? fields=null){this.Fields=fields??new Dictionary<string,ulong>(StringComparer.Ordinal);this.TotalBytes=totalBytes;this.FreeBytes=freeBytes;this.AvailableBytes=availableBytes;this.BuffersBytes=buffersBytes;this.CacheBytes=cacheBytes;this.SharedBytes=sharedBytes;this.SwapTotalBytes=swapTotalBytes;this.SwapFreeBytes=swapFreeBytes;this.CommitLimitBytes=commitLimitBytes;this.CommittedBytes=committedBytes;this.LowTotalBytes=lowTotalBytes;this.LowFreeBytes=lowFreeBytes;this.HighTotalBytes=highTotalBytes;this.HighFreeBytes=highFreeBytes;}
	private static ulong? Get(IReadOnlyDictionary<string,ulong> fields,string key)=>fields.TryGetValue(key,out var value)?value:null; private static ulong? SaturatingAdd(ulong? left,ulong? right){if(!left.HasValue&&!right.HasValue)return null;var l=left??0UL;var r=right??0UL;return ulong.MaxValue-l<r?ulong.MaxValue:l+r;}
}
/// <summary>Represents one observed slab-cache entry.</summary>
public sealed class ProcSlabEntry {
	/// <summary>Gets the slab-cache name.</summary>
	public string Name{get;}
	/// <summary>Gets the number of active objects.</summary>
	public ulong ActiveObjects{get;}
	/// <summary>Gets the total number of allocated objects.</summary>
	public ulong TotalObjects{get;}
	/// <summary>Gets the size of one object in bytes.</summary>
	public ulong ObjectSizeBytes{get;}
	/// <summary>Gets the number of objects stored in each slab.</summary>
	public ulong ObjectsPerSlab{get;}
	/// <summary>Gets the number of pages backing each slab.</summary>
	public ulong PagesPerSlab{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcSlabEntry"/> type.</summary>
	public ProcSlabEntry(string name,ulong activeObjects,ulong totalObjects,ulong objectSizeBytes,ulong objectsPerSlab,ulong pagesPerSlab){ArgumentException.ThrowIfNullOrWhiteSpace(name);this.Name=name;this.ActiveObjects=activeObjects;this.TotalObjects=totalObjects;this.ObjectSizeBytes=objectSizeBytes;this.ObjectsPerSlab=objectsPerSlab;this.PagesPerSlab=pagesPerSlab;} }
/// <summary>Represents observed huge-page counters.</summary>
public sealed class ProcHugePageInfo {
	/// <summary>Gets the total configured huge-page count.</summary>
	public ulong TotalPages{get;}
	/// <summary>Gets the free huge-page count.</summary>
	public ulong FreePages{get;}
	/// <summary>Gets the reserved huge-page count.</summary>
	public ulong ReservedPages{get;}
	/// <summary>Gets the surplus huge-page count.</summary>
	public ulong SurplusPages{get;}
	/// <summary>Gets the huge-page size in bytes.</summary>
	public ulong PageSizeBytes{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcHugePageInfo"/> type.</summary>
	public ProcHugePageInfo(ulong totalPages,ulong freePages,ulong reservedPages,ulong surplusPages,ulong pageSizeBytes){this.TotalPages=totalPages;this.FreePages=freePages;this.ReservedPages=reservedPages;this.SurplusPages=surplusPages;this.PageSizeBytes=pageSizeBytes;} }
/// <summary>Represents an observed count of logged-in user sessions.</summary>
public sealed class ProcUserSessionInfo {
	/// <summary>Gets the number of observed logged-in user sessions.</summary>
	public int Count{get;}
	/// <summary>Initializes a new instance of the <see cref="ProcUserSessionInfo"/> type.</summary>
	public ProcUserSessionInfo(int count){ArgumentOutOfRangeException.ThrowIfNegative(count);this.Count=count;} }
/// <summary>Contains a point-in-time set of system metric observations.</summary>
public sealed class ProcSystemSnapshot {
	/// <summary>Gets raw procps-style CPU time counters.</summary>
	public ProcObservedValue<ProcCpuTimes> Cpu{get;init;}=ProcObservedValue<ProcCpuTimes>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets portable CPU activity counters.</summary>
	public ProcObservedValue<ProcCpuActivity> CpuActivity{get;init;}=ProcObservedValue<ProcCpuActivity>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets system memory information.</summary>
	public ProcObservedValue<ProcMemoryInfo> Memory{get;init;}=ProcObservedValue<ProcMemoryInfo>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets Linux load-average data including runnable-task metadata.</summary>
	public ProcObservedValue<ProcLoadAverage> LoadAverage{get;init;}=ProcObservedValue<ProcLoadAverage>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets neutral one-, five-, and fifteen-minute load averages.</summary>
	public ProcObservedValue<ProcLoadAverages> LoadAverages{get;init;}=ProcObservedValue<ProcLoadAverages>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets system uptime information.</summary>
	public ProcObservedValue<ProcUptimeInfo> Uptime{get;init;}=ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets virtual-memory event counters.</summary>
	public ProcObservedValue<IReadOnlyDictionary<string,ulong>> VirtualMemory{get;init;}=ProcObservedValue<IReadOnlyDictionary<string,ulong>>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets slab-cache information.</summary>
	public ProcObservedValue<IReadOnlyList<ProcSlabEntry>> Slab{get;init;}=ProcObservedValue<IReadOnlyList<ProcSlabEntry>>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets huge-page information.</summary>
	public ProcObservedValue<ProcHugePageInfo> HugePages{get;init;}=ProcObservedValue<ProcHugePageInfo>.Missing(ProcObservationAvailability.Unavailable);
	/// <summary>Gets logged-in user-session information.</summary>
	public ProcObservedValue<ProcUserSessionInfo> UserSessions{get;init;}=ProcObservedValue<ProcUserSessionInfo>.Missing(ProcObservationAvailability.Unavailable); }
/// <summary>Defines the system metrics consumed by ProcPs commands.</summary>
public interface IProcSystemMetricsProvider {
	/// <summary>Gets the system metrics this provider can supply.</summary>
	ProcSystemCapabilities Capabilities{get;}
	/// <summary>Captures the available system metrics asynchronously.</summary>
	Task<ProcSystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken=default);
	/// <summary>Gets the current normalized memory observation asynchronously.</summary>
	async Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync(CancellationToken cancellationToken=default)=>(await this.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Memory;
	/// <summary>Gets system or container uptime asynchronously.</summary>
	async Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync(bool containerMode,CancellationToken cancellationToken=default){if(containerMode)return ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unsupported,"Container uptime is not exposed by this provider.");return(await this.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Uptime;} }
/// <summary>Selects the best available system-metrics provider for the current host.</summary>
public sealed class SystemProcSystemMetricsProvider:IProcSystemMetricsProvider { private readonly IProcSystemMetricsProvider _inner;
	/// <summary>Gets the shared system instance.</summary>
	public static SystemProcSystemMetricsProvider Instance{get;}=new();
	/// <summary>Gets the capabilities of the selected host provider.</summary>
	public ProcSystemCapabilities Capabilities=>this._inner.Capabilities;
	/// <summary>Initializes a new instance of the <see cref="SystemProcSystemMetricsProvider"/> type.</summary>
	public SystemProcSystemMetricsProvider(){this._inner=OperatingSystem.IsLinux()?new LinuxProcSystemMetricsProvider():OperatingSystem.IsWindows()?new WindowsProcSystemMetricsProvider():OperatingSystem.IsMacOS()?new MacOsProcSystemMetricsProvider():new PortableProcSystemMetricsProvider();}
	/// <summary>Captures the available system metrics asynchronously.</summary>
	public Task<ProcSystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken=default)=>this._inner.GetSnapshotAsync(cancellationToken);
	/// <summary>Gets the current normalized memory observation asynchronously.</summary>
	public Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync(CancellationToken cancellationToken=default)=>this._inner.GetMemoryAsync(cancellationToken);
	/// <summary>Gets system or container uptime asynchronously.</summary>
	public Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync(bool containerMode,CancellationToken cancellationToken=default)=>this._inner.GetUptimeAsync(containerMode,cancellationToken); }
/// <summary>Provides procps-compatible system metrics from Linux procfs and native APIs.</summary>
public sealed class LinuxProcSystemMetricsProvider:IProcSystemMetricsProvider {
	private const short UserProcessRecord=7; private static readonly object UserSessionSync=new(); private readonly string _procRoot;
	/// <summary>Gets the system metrics supplied by the Linux provider.</summary>
	public ProcSystemCapabilities Capabilities=>ProcSystemCapabilities.Memory|ProcSystemCapabilities.Swap|ProcSystemCapabilities.CpuActivity|ProcSystemCapabilities.LoadAverage|ProcSystemCapabilities.Uptime|ProcSystemCapabilities.VirtualMemory|ProcSystemCapabilities.Slab|ProcSystemCapabilities.HugePages|ProcSystemCapabilities.UserSessions;
	/// <summary>Initializes a new instance of the <see cref="LinuxProcSystemMetricsProvider"/> type.</summary>
	public LinuxProcSystemMetricsProvider(string procRoot="/proc"){ArgumentException.ThrowIfNullOrWhiteSpace(procRoot);this._procRoot=procRoot;}
	/// <summary>Gets the current normalized memory observation asynchronously.</summary>
	public Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync(CancellationToken cancellationToken=default)=>ObserveFileAsync("meminfo",text=>new ProcMemoryInfo(LinuxProcParsers.ParseMemInfo(text)),cancellationToken);
	/// <summary>Gets system or container uptime asynchronously.</summary>
	public async Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync(bool containerMode,CancellationToken cancellationToken=default){if(!containerMode)return await ObserveFileAsync("uptime",ParseUptime,cancellationToken).ConfigureAwait(false);if(!OperatingSystem.IsLinux())return ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unsupported,"Container uptime with procps-ng semantics is available only on Linux.");var system=await ObserveFileAsync("uptime",ParseUptime,cancellationToken).ConfigureAwait(false);if(!system.HasValue)return system;var init=await ObserveFileAsync(System.IO.Path.Combine("1","stat"),LinuxProcParsers.ParseProcessStat,cancellationToken).ConfigureAwait(false);if(!init.HasValue)return ProcObservedValue<ProcUptimeInfo>.Missing(init.Availability,init.Diagnostic);try{var ticks=LinuxSystemNative.SysConf(LinuxSystemNative.ClockTicksPerSecond);if(0>=ticks)return ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unavailable,"sysconf(_SC_CLK_TCK) did not return a positive clock frequency.");var start=init.Value.StartTimeTicks/(double)ticks;return ProcObservedValue<ProcUptimeInfo>.Available(new ProcUptimeInfo(TimeSpan.FromSeconds(Math.Max(0d,system.Value.Uptime.TotalSeconds-start)),null),ProcObservationSource.Derived,ObservationFidelity.Exact);}catch(DllNotFoundException ex){return ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unsupported,ex.Message);}catch(EntryPointNotFoundException ex){return ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unsupported,ex.Message);}}
	/// <summary>Captures the available system metrics asynchronously.</summary>
	public async Task<ProcSystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken=default){var stat=await ObserveFileAsync("stat",ParseCpu,cancellationToken).ConfigureAwait(false);var memory=await ObserveFileAsync("meminfo",text=>new ProcMemoryInfo(LinuxProcParsers.ParseMemInfo(text)),cancellationToken).ConfigureAwait(false);var load=await ObserveFileAsync("loadavg",ParseLoadAverage,cancellationToken).ConfigureAwait(false);var uptime=await ObserveFileAsync("uptime",ParseUptime,cancellationToken).ConfigureAwait(false);var vm=await ObserveFileAsync<IReadOnlyDictionary<string,ulong>>("vmstat",LinuxProcParsers.ParseCounterFile,cancellationToken).ConfigureAwait(false);var slab=await ObserveFileAsync<IReadOnlyList<ProcSlabEntry>>("slabinfo",ParseSlabInfo,cancellationToken).ConfigureAwait(false);var huge=memory.HasValue?ParseHugePages(memory.Value):ProcObservedValue<ProcHugePageInfo>.Missing(memory.Availability,memory.Diagnostic);var users=ObserveUserSessions();var neutralCpu=stat.HasValue?ProcObservedValue<ProcCpuActivity>.Available(new ProcCpuActivity(stat.Value.User,SaturatingAdd(stat.Value.System,SaturatingAdd(stat.Value.Irq,stat.Value.SoftIrq)),stat.Value.Idle,stat.Value.Nice,stat.Value.IoWait,stat.Value.Steal),ProcObservationSource.LinuxProcfs,ObservationFidelity.Exact):ProcObservedValue<ProcCpuActivity>.Missing(stat.Availability,stat.Diagnostic);var neutralLoad=load.HasValue?ProcObservedValue<ProcLoadAverages>.Available(new ProcLoadAverages(load.Value.OneMinute,load.Value.FiveMinutes,load.Value.FifteenMinutes),ProcObservationSource.LinuxProcfs,ObservationFidelity.Exact):ProcObservedValue<ProcLoadAverages>.Missing(load.Availability,load.Diagnostic);return new ProcSystemSnapshot{Cpu=stat,CpuActivity=neutralCpu,Memory=memory,LoadAverage=load,LoadAverages=neutralLoad,Uptime=uptime,VirtualMemory=vm,Slab=slab,HugePages=huge,UserSessions=users};}
	private static ProcObservedValue<ProcUserSessionInfo> ObserveUserSessions(){if(!OperatingSystem.IsLinux())return ProcObservedValue<ProcUserSessionInfo>.Missing(ProcObservationAvailability.Unsupported,"The Linux user-session provider is available only on Linux.");lock(UserSessionSync){var opened=false;try{LinuxSessionNative.SetUtmpxEnt();opened=true;var count=0;while(true){var entry=LinuxSessionNative.GetUtmpxEnt();if(IntPtr.Zero==entry)break;if(UserProcessRecord==Marshal.ReadInt16(entry))count++;}return ProcObservedValue<ProcUserSessionInfo>.Available(new ProcUserSessionInfo(count),ProcObservationSource.PosixLibc,ObservationFidelity.Equivalent);}catch(DllNotFoundException ex){return ProcObservedValue<ProcUserSessionInfo>.Missing(ProcObservationAvailability.Unsupported,ex.Message);}catch(EntryPointNotFoundException ex){return ProcObservedValue<ProcUserSessionInfo>.Missing(ProcObservationAvailability.Unsupported,ex.Message);}finally{if(opened){try{LinuxSessionNative.EndUtmpxEnt();}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}}}}
	private static class LinuxSystemNative{public const int ClockTicksPerSecond=2;[DllImport("libc",EntryPoint="sysconf",ExactSpelling=true,SetLastError=true)]public static extern long SysConf(int name);} private static class LinuxSessionNative{[DllImport("libc",EntryPoint="setutxent",ExactSpelling=true)]internal static extern void SetUtmpxEnt();[DllImport("libc",EntryPoint="getutxent",ExactSpelling=true)]internal static extern IntPtr GetUtmpxEnt();[DllImport("libc",EntryPoint="endutxent",ExactSpelling=true)]internal static extern void EndUtmpxEnt();}
	private async Task<ProcObservedValue<T>> ObserveFileAsync<T>(string fileName,Func<string,T> parser,CancellationToken cancellationToken){try{var text=await File.ReadAllTextAsync(System.IO.Path.Combine(this._procRoot,fileName),cancellationToken).ConfigureAwait(false);return ProcObservedValue<T>.Available(parser(text),ProcObservationSource.LinuxProcfs,ObservationFidelity.Exact);}catch(UnauthorizedAccessException ex){return ProcObservedValue<T>.Missing(ProcObservationAvailability.AccessDenied,ex.Message);}catch(FileNotFoundException ex){return ProcObservedValue<T>.Missing(ProcObservationAvailability.Unavailable,ex.Message);}catch(DirectoryNotFoundException ex){return ProcObservedValue<T>.Missing(ProcObservationAvailability.Unavailable,ex.Message);}catch(IOException ex){return ProcObservedValue<T>.Missing(ProcObservationAvailability.Unavailable,ex.Message);}catch(FormatException ex){return ProcObservedValue<T>.Missing(ProcObservationAvailability.Malformed,ex.Message);}catch(OverflowException ex){return ProcObservedValue<T>.Missing(ProcObservationAvailability.Malformed,ex.Message);}}
	/// <summary>Parses the aggregate CPU line from Linux <c>/proc/stat</c>.</summary>
	public static ProcCpuTimes ParseCpu(string text){var line=text.Split('\n').FirstOrDefault(value=>value.StartsWith("cpu ",StringComparison.Ordinal))??throw new FormatException("The aggregate cpu line is missing.");var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(5>fields.Length)throw new FormatException("The aggregate cpu line is incomplete.");ulong Read(int index)=>index<fields.Length?ulong.Parse(fields[index],NumberStyles.None,CultureInfo.InvariantCulture):0UL;return new ProcCpuTimes(Read(1),Read(2),Read(3),Read(4),Read(5),Read(6),Read(7),Read(8),Read(9),Read(10));}
	/// <summary>Parses Linux <c>/proc/loadavg</c> content.</summary>
	public static ProcLoadAverage ParseLoadAverage(string text){var fields=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(5>fields.Length)throw new FormatException("Malformed /proc/loadavg.");var entities=fields[3].Split('/',2);if(2!=entities.Length)throw new FormatException("Malformed loadavg runnable/total field.");return new ProcLoadAverage(double.Parse(fields[0],NumberStyles.Float,CultureInfo.InvariantCulture),double.Parse(fields[1],NumberStyles.Float,CultureInfo.InvariantCulture),double.Parse(fields[2],NumberStyles.Float,CultureInfo.InvariantCulture),int.Parse(entities[0],NumberStyles.None,CultureInfo.InvariantCulture),int.Parse(entities[1],NumberStyles.None,CultureInfo.InvariantCulture),int.Parse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture));}
	/// <summary>Parses Linux <c>/proc/uptime</c> content.</summary>
	public static ProcUptimeInfo ParseUptime(string text){var fields=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(1>fields.Length)throw new FormatException("Malformed /proc/uptime.");var uptime=TimeSpan.FromSeconds(double.Parse(fields[0],NumberStyles.Float,CultureInfo.InvariantCulture));TimeSpan? idle=1<fields.Length?TimeSpan.FromSeconds(double.Parse(fields[1],NumberStyles.Float,CultureInfo.InvariantCulture)):null;return new ProcUptimeInfo(uptime,idle);}
	/// <summary>Parses Linux slab-cache information.</summary>
	public static IReadOnlyList<ProcSlabEntry> ParseSlabInfo(string text){var entries=new List<ProcSlabEntry>();foreach(var line in text.Split('\n',StringSplitOptions.RemoveEmptyEntries)){if(line.StartsWith("slabinfo",StringComparison.Ordinal)||line.StartsWith("#",StringComparison.Ordinal))continue;var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(6>fields.Length)continue;if(!ulong.TryParse(fields[1],NumberStyles.None,CultureInfo.InvariantCulture,out var active))continue;if(!ulong.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out var total))continue;if(!ulong.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out var size))continue;if(!ulong.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out var perSlab))continue;if(!ulong.TryParse(fields[5],NumberStyles.None,CultureInfo.InvariantCulture,out var pages))continue;entries.Add(new ProcSlabEntry(fields[0],active,total,size,perSlab,pages));}return entries;}
	private static ProcObservedValue<ProcHugePageInfo> ParseHugePages(ProcMemoryInfo memory){ulong Read(string key)=>memory.Fields.TryGetValue(key,out var value)?value:0UL;return ProcObservedValue<ProcHugePageInfo>.Available(new ProcHugePageInfo(Read("HugePages_Total"),Read("HugePages_Free"),Read("HugePages_Rsvd"),Read("HugePages_Surp"),Read("Hugepagesize")),ProcObservationSource.LinuxProcfs,ObservationFidelity.Exact);} private static ulong SaturatingAdd(ulong left,ulong right)=>ulong.MaxValue-left<right?ulong.MaxValue:left+right;
}
/// <summary>Provides the portable fallback system-metrics profile.</summary>
public sealed class PortableProcSystemMetricsProvider:IProcSystemMetricsProvider {
	/// <summary>Gets the system metrics supplied by the portable provider.</summary>
	public ProcSystemCapabilities Capabilities=>ProcSystemCapabilities.Uptime;
	/// <summary>Gets the current normalized memory observation asynchronously.</summary>
	public async Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync(CancellationToken cancellationToken=default)=>(await this.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Memory;
	/// <summary>Gets system or container uptime asynchronously.</summary>
	public async Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync(bool containerMode,CancellationToken cancellationToken=default){cancellationToken.ThrowIfCancellationRequested();if(containerMode)return ProcObservedValue<ProcUptimeInfo>.Missing(ProcObservationAvailability.Unsupported,"Container uptime with procps-ng semantics is not exposed by the portable provider.");return(await this.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Uptime;}
	/// <summary>Captures the available system metrics asynchronously.</summary>
	public Task<ProcSystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken=default){cancellationToken.ThrowIfCancellationRequested();var unsupported="Linux procps-ng system metric semantics are not exposed by the portable provider.";return Task.FromResult(new ProcSystemSnapshot{Cpu=ProcObservedValue<ProcCpuTimes>.Missing(ProcObservationAvailability.Unsupported,unsupported),CpuActivity=ProcObservedValue<ProcCpuActivity>.Missing(ProcObservationAvailability.Unsupported,unsupported),Memory=ProcObservedValue<ProcMemoryInfo>.Missing(ProcObservationAvailability.Unsupported,unsupported),LoadAverage=ProcObservedValue<ProcLoadAverage>.Missing(ProcObservationAvailability.Unsupported,unsupported),LoadAverages=ProcObservedValue<ProcLoadAverages>.Missing(ProcObservationAvailability.Unsupported,unsupported),Uptime=ProcObservedValue<ProcUptimeInfo>.Available(new ProcUptimeInfo(TimeSpan.FromMilliseconds(Environment.TickCount64),null),ProcObservationSource.PlatformApi,ObservationFidelity.Equivalent),VirtualMemory=ProcObservedValue<IReadOnlyDictionary<string,ulong>>.Missing(ProcObservationAvailability.Unsupported,unsupported),Slab=ProcObservedValue<IReadOnlyList<ProcSlabEntry>>.Missing(ProcObservationAvailability.Unsupported,unsupported),HugePages=ProcObservedValue<ProcHugePageInfo>.Missing(ProcObservationAvailability.Unsupported,unsupported),UserSessions=ProcObservedValue<ProcUserSessionInfo>.Missing(ProcObservationAvailability.Unsupported,unsupported)});} }

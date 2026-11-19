using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Oxyn.ConsoleMode.Cheats;

/// <summary>Zone de mémoire lisible et inscriptible d'un autre processus.</summary>
public readonly record struct MemRegion(long Base, long Size, long AllocationBase)
{
    public long End => Base + Size;
}

/// <summary>
/// Lecture / écriture de la mémoire d'un autre processus (l'émulateur) : la mémoire de la console émulée y est
/// une grande zone allouée par l'émulateur. Fonctionne aussi quand le processus est suspendu.
/// </summary>
public sealed class ProcessMemory : IDisposable
{
    private const uint VmOperation = 0x0008, VmRead = 0x0010, VmWrite = 0x0020, QueryInformation = 0x0400;
    private const uint MemCommit = 0x1000, MemPrivate = 0x20000, MemMapped = 0x40000;
    private const uint PageReadWrite = 0x04, PageWriteCopy = 0x08, PageExecuteReadWrite = 0x40, PageGuard = 0x100, PageNoCache = 0x200;

    private IntPtr _h;

    public int Pid { get; }

    public ProcessMemory(int pid)
    {
        Pid = pid;
        _h = OpenProcess(VmOperation | VmRead | VmWrite | QueryInformation, false, pid);
        if (_h == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Accès à la mémoire du processus {pid} refusé");
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) CloseHandle(_h);
        _h = IntPtr.Zero;
    }

    /// <summary>Zones engagées, en lecture-écriture (mémoire privée ou partagée, pas les fichiers .exe/.dll), par adresse croissante.</summary>
    public List<MemRegion> Regions()
    {
        var list = new List<MemRegion>();
        long addr = 0;
        while (Query(addr, out var mbi))
        {
            var size = (long)mbi.RegionSize;
            if (size <= 0) break;
            var prot = mbi.Protect;
            var writable = (prot & (PageReadWrite | PageWriteCopy | PageExecuteReadWrite)) != 0 && (prot & (PageGuard | PageNoCache)) == 0;
            if (mbi.State == MemCommit && writable && (mbi.Type == MemPrivate || mbi.Type == MemMapped))
                list.Add(new MemRegion((long)mbi.BaseAddress, size, (long)mbi.AllocationBase));
            addr = (long)mbi.BaseAddress + size;
            if (addr <= 0) break;
        }
        return list;
    }

    /// <summary>
    /// Zones lisibles regroupées quand elles se touchent : la mémoire de l'émulateur est découpée en milliers de petites
    /// zones voisines, les lire d'un bloc évite autant d'appels à Windows.
    /// </summary>
    public List<MemRegion> Spans()
    {
        var spans = new List<MemRegion>();
        foreach (var r in Regions())
        {
            if (spans.Count > 0 && spans[^1].End == r.Base)
            {
                var s = spans[^1];
                spans[^1] = s with { Size = r.End - s.Base };
            }
            else spans.Add(r);
        }
        return spans;
    }

    /// <summary>Taille totale des allocations (zones contiguës de même base d'allocation), par base.</summary>
    public static Dictionary<long, long> AllocationSizes(IEnumerable<MemRegion> regions)
    {
        var map = new Dictionary<long, long>();
        foreach (var r in regions)
            map[r.AllocationBase] = Math.Max(map.TryGetValue(r.AllocationBase, out var s) ? s : 0, r.End - r.AllocationBase);
        return map;
    }

    /// <summary>Lit <paramref name="length"/> octets au début de <paramref name="buffer"/> (tableau épinglé par le marshaling).</summary>
    public bool Read(long addr, byte[] buffer, int length)
    {
        if (length <= 0 || length > buffer.Length) return false;
        return ReadProcessMemory(_h, (IntPtr)addr, buffer, (IntPtr)length, out var n) && (long)n == length;
    }

    public bool Read(long addr, byte[] buffer) => Read(addr, buffer, buffer.Length);

    public bool Write(long addr, byte[] data) =>
        data.Length > 0 && WriteProcessMemory(_h, (IntPtr)addr, data, (IntPtr)data.Length, out var n) && (long)n == data.Length;

    private bool Query(long addr, out MemoryBasicInformation mbi) =>
        VirtualQueryEx(_h, (IntPtr)addr, out mbi, (IntPtr)Marshal.SizeOf<MemoryBasicInformation>()) != IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll")]
    private static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation info, IntPtr length);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll")]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);
}

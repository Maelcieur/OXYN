using System.Runtime.InteropServices;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Met le jeu en pause (processus suspendu) pendant que le menu de la console ou le menu Guide est ouvert :
/// les jeux lisent la manette directement (XInput), même sans le focus ; suspendu, le jeu ne voit plus aucune touche.
/// Tout processus suspendu est relâché à la fermeture d'OXYN (même en cas de plantage géré).
/// Processus protégé (anti-triche) : l'ouverture échoue, le jeu n'est simplement pas mis en pause.
/// </summary>
public static class GameSuspender
{
    private const uint ProcessSuspendResume = 0x0800;
    private static readonly HashSet<int> Suspended = new();
    private static readonly object Gate = new();

    static GameSuspender()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ResumeAll();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ResumeAll();
    }

    public static bool IsSuspended(int pid) { lock (Gate) return Suspended.Contains(pid); }

    /// <summary>Suspend le processus (une seule fois). Renvoie faux si impossible.</summary>
    public static bool Suspend(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return false;
        lock (Gate)
        {
            if (Suspended.Contains(pid)) return true;
            if (!Call(pid, suspend: true)) return false;
            Suspended.Add(pid);
        }
        Log.Info($"Mode console : jeu mis en pause (processus {pid}).");
        return true;
    }

    /// <summary>Relâche le processus s'il a été suspendu par nous.</summary>
    public static void Resume(int pid)
    {
        lock (Gate)
        {
            if (!Suspended.Remove(pid)) return;
            Call(pid, suspend: false);
        }
        Log.Info($"Mode console : jeu repris (processus {pid}).");
    }

    public static void ResumeAll()
    {
        int[] pids;
        lock (Gate) pids = Suspended.ToArray();
        foreach (var pid in pids) Resume(pid);
    }

    private static bool Call(int pid, bool suspend)
    {
        var h = OpenProcess(ProcessSuspendResume, false, pid);
        if (h == IntPtr.Zero)
        {
            Log.WarnOnce("suspend-open-" + pid, $"Mode console : pause du jeu impossible (processus {pid} protégé ?).");
            return false;
        }
        try
        {
            var rc = suspend ? NtSuspendProcess(h) : NtResumeProcess(h);
            if (rc != 0) Log.WarnOnce("suspend-rc-" + pid, $"Mode console : {(suspend ? "pause" : "reprise")} du processus {pid} refusée (0x{rc:X8}).");
            return rc == 0;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr process);
}

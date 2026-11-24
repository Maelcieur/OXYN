using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace Oxyn.SensorBridge;

/// <summary>
/// Pont des capteurs d'OXYN. Lancé en administrateur par la tâche planifiée « OXYN-SensorBridge » (à l'ouverture de
/// session), il lit tous les capteurs du PC avec LibreHardwareMonitor et le pilote PawnIO (signé, compatible avec
/// l'Intégrité de la mémoire) : processeur, carte mère (températures, ventilateurs, tensions), cartes graphiques,
/// mémoire (dont la température des barrettes DDR5) et disques. OXYN, qui tourne sans droits administrateur, lit le
/// résultat dans C:\ProgramData\Oxyn\sensors.json.
/// </summary>
internal static class Program
{
    private const string Dir = @"C:\ProgramData\Oxyn";
    private static readonly string OutFile = Path.Combine(Dir, "sensors.json");
    // Ancien fichier du pont PowerShell : toujours écrit, pour une version d'OXYN qui ne lit pas encore sensors.json.
    private static readonly string LegacyTempFile = Path.Combine(Dir, "cputemp.txt");
    private static readonly string LogFile = Path.Combine(Dir, "sensor-bridge.log");
    private const long MaxLogBytes = 256 * 1024;

    // Processeur, carte mère, cartes graphiques : toutes les 2 s. Barrettes de mémoire (bus SMBus, lent) et disques
    // (SMART) : leur température change lentement, toutes les 30 s.
    private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(30);

    private static void Main()
    {
        // Une seule copie : la tâche peut être relancée par OXYN pendant qu'une copie tourne encore.
        using var mutex = new Mutex(true, @"Global\Oxyn-SensorBridge", out var createdNew);
        if (!createdNew)
        {
            try
            {
                if (!mutex.WaitOne(0)) return;
            }
            catch (AbandonedMutexException) { /* l'ancienne copie s'est arrêtée brutalement : on prend la suite */ }
        }

        Directory.CreateDirectory(Dir);
        Log($"Démarrage du pont des capteurs {typeof(Program).Assembly.GetName().Version} (PawnIO {(PawnIoInstalled() ? "installé" : "ABSENT : processeur, carte mère et mémoire ne seront pas lus")}).");

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsMotherboardEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
            IsPsuEnabled = true
        };
        try
        {
            computer.Open();
        }
        catch (Exception ex)
        {
            Log("Ouverture des capteurs impossible : " + ex);
            return;
        }

        var lastSlow = DateTime.MinValue;
        var failures = 0;
        var announced = false;
        while (true)
        {
            var started = DateTime.UtcNow;
            try
            {
                var slow = started - lastSlow >= SlowInterval;
                if (slow) lastSlow = started;
                foreach (var hardware in computer.Hardware)
                    if (slow || !IsSlow(hardware))
                        Update(hardware);

                var sensors = Collect(computer.Hardware).ToList();
                Write(sensors);
                if (!announced)
                {
                    announced = true;
                    Log($"Premières mesures : {sensors.Count} capteurs ({string.Join(", ", computer.Hardware.Select(h => h.Name))}).");
                }
                if (failures > 0) Log($"Lecture de nouveau correcte après {failures} échec(s).");
                failures = 0;
            }
            catch (Exception ex)
            {
                // Une erreur passagère (fichier verrouillé, capteur qui disparaît) ne doit pas arrêter le pont.
                if (failures++ == 0 || failures % 100 == 0) Log($"Échec de lecture ({failures}) : {ex.Message}");
            }

            var wait = FastInterval - (DateTime.UtcNow - started);
            Thread.Sleep(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>Barrettes de mémoire et disques : lus toutes les 30 s seulement.</summary>
    private static bool IsSlow(IHardware hardware)
        => hardware.HardwareType == HardwareType.Storage
           || hardware.HardwareType == HardwareType.Memory && hardware.Identifier.ToString().StartsWith("/memory/", StringComparison.Ordinal);

    private static void Update(IHardware hardware)
    {
        try
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) Update(sub);
        }
        catch (Exception ex)
        {
            // Un composant en échec ne doit pas empêcher la lecture des autres.
            LogOnce("update:" + hardware.Identifier, $"Lecture de « {hardware.Name} » en échec : {ex.Message}");
        }
    }

    private sealed class Reading
    {
        public string Id = "", Hardware = "", HardwareType = "", Name = "", Type = "";
        public float Value;
    }

    private static IEnumerable<Reading> Collect(IEnumerable<IHardware> hardware)
    {
        foreach (var h in hardware)
        {
            foreach (var s in h.Sensors)
                if (s.Value is { } v && !float.IsNaN(v) && !float.IsInfinity(v))
                    yield return new Reading
                    {
                        Id = s.Identifier.ToString(), Hardware = h.Name, HardwareType = h.HardwareType.ToString(),
                        Name = s.Name, Type = s.SensorType.ToString(), Value = v
                    };
            foreach (var r in Collect(h.SubHardware)) yield return r;
        }
    }

    /// <summary>Écriture atomique (fichier temporaire puis remplacement) : OXYN ne lit jamais un fichier à moitié écrit.</summary>
    private static void Write(List<Reading> sensors)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append("{\"v\":1,\"t\":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Append(",\"sensors\":[");
        for (var i = 0; i < sensors.Count; i++)
        {
            var s = sensors[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(Json(s.Id))
              .Append(",\"hw\":").Append(Json(s.Hardware))
              .Append(",\"hwType\":").Append(Json(s.HardwareType))
              .Append(",\"name\":").Append(Json(s.Name))
              .Append(",\"type\":").Append(Json(s.Type))
              .Append(",\"value\":").Append(s.Value.ToString("R", CultureInfo.InvariantCulture)).Append('}');
        }
        sb.Append("]}");
        Replace(OutFile, sb.ToString());

        // Ancien format : température du processeur seule (Tctl/Tdie des Ryzen, Package des Intel).
        var cpu = sensors.Where(s => s.HardwareType == "Cpu" && s.Type == "Temperature" && s.Value > 0)
            .OrderBy(s => s.Name.Contains("Tctl") || s.Name.Contains("Package") ? 0 : 1).FirstOrDefault();
        if (cpu != null) Replace(LegacyTempFile, cpu.Value.ToString("0.0", CultureInfo.InvariantCulture));
    }

    private static void Replace(string path, string content)
    {
        var temp = path + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null, true);
        else File.Move(temp, path);
    }

    private static string Json(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>LibreHardwareMonitor n'utilise PawnIO que si son installateur officiel l'a inscrit.</summary>
    private static bool PawnIoInstalled()
    {
        using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        return key != null;
    }

    private static readonly HashSet<string> Logged = new();

    private static void LogOnce(string key, string message)
    {
        if (Logged.Add(key)) Log(message);
    }

    private static void Log(string message)
    {
        try
        {
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxLogBytes)
            {
                File.Delete(LogFile + ".old");
                File.Move(LogFile, LogFile + ".old");
            }
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{System.Diagnostics.Process.GetCurrentProcess().Id}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { /* journal indisponible : sans conséquence */ }
    }
}

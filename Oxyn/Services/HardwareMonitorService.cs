using System.IO;
using LibreHardwareMonitor.Hardware;
using Oxyn.Infrastructure;

namespace Oxyn.Services;

public sealed class HardwareSnapshot
{
    public float? CpuTemperature { get; init; }
    public float? CpuLoad { get; init; }
    public float? GpuTemperature { get; init; }
    public float? GpuLoad { get; init; }
    public string CpuName { get; init; } = "CPU";
    public string GpuName { get; init; } = "GPU";

    public float? RamUsedGb { get; init; }
    public float? RamTotalGb { get; init; }
    public float? RamLoad { get; init; }

    /// <summary>Puissance du processeur (capteur « Package »), null si le pilote est bloqué.</summary>
    public float? CpuPower { get; init; }
    /// <summary>Puissance cumulée des cartes graphiques dédiées (l'iGPU est déjà compté dans le CPU).</summary>
    public float? GpuPower { get; init; }

    /// <summary>Fréquence moyenne des cœurs, en MHz.</summary>
    public float? CpuClockMhz { get; init; }

    /// <summary>Fréquences du processeur graphique et de sa mémoire, en MHz.</summary>
    public float? GpuCoreClockMhz { get; init; }
    public float? GpuMemoryClockMhz { get; init; }
    /// <summary>Mémoire vidéo utilisée et totale, en Mo.</summary>
    public float? GpuMemoryUsedMb { get; init; }
    public float? GpuMemoryTotalMb { get; init; }

    /// <summary>Tous les capteurs lus (module Barres de capteurs) ; null si cette lecture n'est pas demandée.</summary>
    public IReadOnlyList<SensorReading>? Sensors { get; init; }
}

public sealed class HardwareMonitorService : IDisposable
{
    // Ancien pont de température (CpuTempBridge.ps1 et le SDK AMD Ryzen Master, avant la 2.2.0), encore écrit par le
    // pont des capteurs : repli si sensors.json manque (tâche pas encore recréée après une mise à jour silencieuse).
    // ProgramData (pas AppData) car la tâche tourne sous un compte différent de l'utilisateur
    // courant. On ignore le fichier s'il est trop vieux : la tâche pont n'est peut-être pas
    // configurée ou plus active, mieux vaut afficher "indisponible" qu'une valeur figée.
    private const string BridgeFileNew = @"C:\ProgramData\Oxyn\cputemp.txt";
    // Ancien nom (SysDeck) : la tâche d'avant le renommage tourne encore tant que l'installateur n'a pas recréé la sienne.
    private const string BridgeFileLegacy = @"C:\ProgramData\SysDeck\cputemp.txt";
    private static string BridgeFilePath => File.Exists(BridgeFileNew) || !File.Exists(BridgeFileLegacy) ? BridgeFileNew : BridgeFileLegacy;
    private static readonly TimeSpan BridgeFileMaxAge = TimeSpan.FromSeconds(15);
    private const long BridgeFileMaxBytes = 64; // une température tient en quelques octets
    private const double BridgeMinCelsius = 0, BridgeMaxCelsius = 150;

    // LibreHardwareMonitor n'est pas prévu pour des accès concurrents. Read() tourne maintenant sur un
    // thread de fond (il gelait l'interface) : ce verrou le sérialise avec le changement de MemoryEnabled
    // (thread de l'interface) et avec Dispose.
    private readonly object _sync = new();
    private bool _disposed;

    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly CpuFrequencyCounter _cpuFrequency = new();

    /// <summary>Mesures d'une carte graphique ; la carte dédiée est préférée à l'iGPU du processeur.</summary>
    private sealed record GpuReading(string Name, bool Integrated, float? Temperature, float? Load,
        float? CoreClock, float? MemoryClock, float? MemoryUsed, float? MemoryTotal);

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true
        };
        try
        {
            _computer.Open();
        }
        catch (Exception ex)
        {
            // Pilote refusé ou matériel inattendu : OXYN démarre quand même, sans ces mesures.
            Log.Error("Ouverture des capteurs matériels impossible (processeur, carte graphique, mémoire)", ex);
        }
    }

    /// <summary>
    /// Lecture de la RAM, coupée quand son module est masqué. Processeur et carte graphique restent
    /// toujours lus : le module Consommation a besoin de leur charge et de leur puissance.
    /// </summary>
    public bool MemoryEnabled
    {
        get { lock (_sync) return _computer.IsMemoryEnabled; }
        set
        {
            lock (_sync)
            {
                if (_disposed || _computer.IsMemoryEnabled == value) return;
                _computer.IsMemoryEnabled = value;
            }
        }
    }

    private bool _allSensors;

    /// <summary>
    /// Lecture de tous les capteurs (module Barres de capteurs affiché) : carte mère comprise (ventilateurs, si le
    /// pilote de bas niveau est permis par Windows), et liste complète dans <see cref="HardwareSnapshot.Sensors"/>.
    /// </summary>
    public bool AllSensorsEnabled
    {
        get { lock (_sync) return _allSensors; }
        set
        {
            lock (_sync)
            {
                if (_disposed || _allSensors == value) return;
                _allSensors = value;
                _computer.IsMotherboardEnabled = value;
                if (value) _computer.IsMemoryEnabled = true;
            }
        }
    }

    /// <summary>Peut être appelé depuis un thread de fond ; les lectures sont sérialisées.</summary>
    public HardwareSnapshot Read()
    {
        lock (_sync)
            return ReadLocked();
    }

    private HardwareSnapshot ReadLocked()
    {
        if (_disposed) return new HardwareSnapshot();

        // Pont des capteurs actif (tâche élevée + PawnIO) : toutes les mesures en viennent, OXYN ne lit rien lui-même.
        if (SensorBridgeReader.TryRead() is { Count: > 0 } bridge)
            return FromBridge(bridge);
        if (SensorBridgeReader.StaleSinceUtc is { } staleSince)
            RestartBridgeIfStale(staleSince);

        float? cpuTemp = null, cpuLoad = null;
        float? ramUsed = null, ramAvailable = null, ramLoad = null;
        float? cpuPower = null, gpuPower = null, cpuClock = null;
        string cpuName = "CPU";
        var gpus = new List<GpuReading>();
        List<SensorReading>? sensors = _allSensors ? new() : null;

        // LibreHardwareMonitor peut lever une exception en essayant d'accéder aux capteurs CPU si
        // son pilote (bloqué par l'Intégrité de la mémoire) est indisponible, selon la génération de
        // processeur. Sans ce try/catch dédié, une telle exception empêcherait GPU/RAM d'être lus et
        // sauterait complètement le repli via CpuTempBridge (voir TryReadBridgeTemperature).
        try
        {
            _computer.Accept(_visitor);
            if (sensors is not null) CollectSensors(_computer.Hardware, sensors);

            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    cpuName = hardware.Name;
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            cpuTemp = sensor.Value;
                        else if (sensor.SensorType == SensorType.Temperature && cpuTemp is null)
                            cpuTemp = sensor.Value;

                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            cpuLoad = sensor.Value;

                        if (sensor.SensorType == SensorType.Power && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            cpuPower = sensor.Value;

                        if (sensor.SensorType == SensorType.Clock && sensor.Name.Equals("Cores (Average)", StringComparison.OrdinalIgnoreCase))
                            cpuClock = sensor.Value;
                    }
                }
                else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    var integrated = IsIntegratedGpu(hardware.Name);
                    gpus.Add(ReadGpu(hardware, integrated));
                    if (!integrated && ReadGpuPower(hardware) is { } watts)
                        gpuPower = (gpuPower ?? 0) + watts;
                }
                else if (hardware.HardwareType == HardwareType.Memory)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                            ramUsed = sensor.Value;
                        else if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Available", StringComparison.OrdinalIgnoreCase))
                            ramAvailable = sensor.Value;
                        else if (sensor.SensorType == SensorType.Load)
                            ramLoad = sensor.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Repli via CpuTempBridge ci-dessous malgré l'échec de LibreHardwareMonitor.
            Log.WarnOnce("hardware-read", "Lecture des capteurs matériels en échec", ex);
        }

        // pas juste "??=" : un capteur présent mais non lisible (pilote bloqué) renvoie souvent 0
        // plutôt que null, ce qui empêcherait le repli de se déclencher.
        if (cpuTemp is not > 0)
            cpuTemp = TryReadBridgeTemperature();
        if (cpuClock is not > 0)
            cpuClock = _cpuFrequency.ReadMhz();

        // Carte dédiée en priorité : avant, la dernière carte lue l'emportait (parfois l'iGPU du Ryzen).
        var gpu = gpus.FirstOrDefault(g => !g.Integrated) ?? gpus.FirstOrDefault();

        return new HardwareSnapshot
        {
            CpuTemperature = cpuTemp is > 0 ? cpuTemp : null,
            CpuLoad = cpuLoad,
            GpuTemperature = gpu?.Temperature is > 0 ? gpu.Temperature : null,
            GpuLoad = gpu?.Load,
            CpuName = cpuName,
            GpuName = gpu?.Name ?? "GPU",
            CpuClockMhz = cpuClock is > 0 ? cpuClock : null,
            GpuCoreClockMhz = gpu?.CoreClock is > 0 ? gpu.CoreClock : null,
            GpuMemoryClockMhz = gpu?.MemoryClock is > 0 ? gpu.MemoryClock : null,
            GpuMemoryUsedMb = gpu?.MemoryUsed,
            GpuMemoryTotalMb = gpu?.MemoryTotal,
            RamUsedGb = ramUsed,
            RamTotalGb = ramUsed is { } u && ramAvailable is { } a ? u + a : null,
            RamLoad = ramLoad,
            // Un capteur présent mais non lisible (pilote bloqué) renvoie 0 : on le traite comme absent.
            CpuPower = cpuPower is > 0 ? cpuPower : null,
            GpuPower = gpuPower,
            Sensors = sensors
        };
    }

    /// <summary>Instantané construit à partir des mesures du pont des capteurs.</summary>
    private HardwareSnapshot FromBridge(IReadOnlyList<BridgeSensor> raw)
    {
        var all = raw.Select(s => s.Normalized()).ToList(); // octets -> Go / Mo selon la version du pont
        static float? Find(IEnumerable<BridgeSensor> list, string type, Func<string, bool> name)
            => list.FirstOrDefault(s => s.Type == type && name(s.Name))?.Value;

        // Processeur : Tctl/Tdie (Ryzen) ou Package (Intel), puissance réelle, fréquence effective (ce que font vraiment
        // les cœurs, et non la fréquence des cœurs actifs, toujours au maximum).
        var cpu = all.Where(s => s.HardwareType == "Cpu").ToList();
        var cpuTemp = Find(cpu, "Temperature", n => n.Contains("Tctl") || n.Contains("Package"))
                      ?? cpu.FirstOrDefault(s => s.Type == "Temperature" && s.Value > 0)?.Value;
        var cpuClock = Find(cpu, "Clock", n => n == "Cores (Average Effective)") ?? Find(cpu, "Clock", n => n == "Cores (Average)");
        if (cpuClock is not > 0) cpuClock = _cpuFrequency.ReadMhz();
        var cpuPower = Find(cpu, "Power", n => n.Contains("Package", StringComparison.OrdinalIgnoreCase));

        // Cartes graphiques, regroupées par composant (« /gpu-nvidia/0 »…) ; la carte dédiée est préférée à l'iGPU.
        var gpus = new List<(GpuReading Reading, float? Power)>();
        foreach (var g in all.Where(s => s.HardwareType is "GpuNvidia" or "GpuAmd" or "GpuIntel")
                     .GroupBy(s => string.Join('/', s.Id.Split('/').Take(3))))
        {
            var list = g.ToList();
            var name = list[0].Hardware;
            var power = list.Where(s => s.Type == "Power" && s.Value > 0).ToList();
            var total = power.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("PPT", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Board", StringComparison.OrdinalIgnoreCase))?.Value ?? (power.Count > 0 ? power.Max(s => s.Value) : null);
            gpus.Add((new GpuReading(name, IsIntegratedGpu(name),
                list.FirstOrDefault(s => s.Type == "Temperature")?.Value,
                Find(list, "Load", n => n.Contains("Core", StringComparison.OrdinalIgnoreCase)),
                Find(list, "Clock", n => n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)),
                Find(list, "Clock", n => n.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase)),
                // VRAM en Mo : « SmallData » (Mo) ou, selon la version du pont, « Data » (Go).
                Find(list, "SmallData", n => n.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase))
                    ?? Find(list, "Data", n => n.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase)) * 1024,
                Find(list, "SmallData", n => n.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
                    ?? Find(list, "Data", n => n.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase)) * 1024), total));
        }
        var gpu = gpus.Where(g => !g.Reading.Integrated).Select(g => g.Reading).FirstOrDefault() ?? gpus.Select(g => g.Reading).FirstOrDefault();
        var dedicatedPower = gpus.Where(g => !g.Reading.Integrated && g.Power is not null).Select(g => g.Power!.Value).ToList();
        float? gpuPower = dedicatedPower.Count > 0 ? dedicatedPower.Sum() : null;

        // Mémoire vive : composant « /ram » (la mémoire virtuelle « /vram » a des capteurs de mêmes noms).
        var ram = all.Where(s => s.Id.StartsWith("/ram/", StringComparison.Ordinal)).ToList();
        var ramUsed = Find(ram, "Data", n => n == "Memory Used");
        var ramAvailable = Find(ram, "Data", n => n == "Memory Available");

        return new HardwareSnapshot
        {
            CpuTemperature = cpuTemp is > 0 ? cpuTemp : null,
            CpuLoad = Find(cpu, "Load", n => n.Contains("Total", StringComparison.OrdinalIgnoreCase)),
            CpuName = cpu.FirstOrDefault()?.Hardware ?? "CPU",
            CpuPower = cpuPower is > 0 ? cpuPower : null,
            CpuClockMhz = cpuClock is > 0 ? cpuClock : null,
            GpuTemperature = gpu?.Temperature is > 0 ? gpu.Temperature : null,
            GpuLoad = gpu?.Load,
            GpuName = gpu?.Name ?? "GPU",
            GpuCoreClockMhz = gpu?.CoreClock is > 0 ? gpu.CoreClock : null,
            GpuMemoryClockMhz = gpu?.MemoryClock is > 0 ? gpu.MemoryClock : null,
            GpuMemoryUsedMb = gpu?.MemoryUsed,
            GpuMemoryTotalMb = gpu?.MemoryTotal,
            GpuPower = gpuPower,
            RamUsedGb = ramUsed,
            RamTotalGb = ramUsed is { } u && ramAvailable is { } a ? u + a : null,
            RamLoad = Find(ram, "Load", n => n == "Memory"),
            Sensors = _allSensors ? all.Select(SensorCatalog.FromBridge).OfType<SensorReading>().ToList() : null
        };
    }

    private static void CollectSensors(IEnumerable<IHardware> hardware, List<SensorReading> into)
    {
        foreach (var h in hardware)
        {
            foreach (var sensor in h.Sensors)
                if (SensorCatalog.FromSensor(h, sensor) is { } reading) into.Add(reading);
            CollectSensors(h.SubHardware, into);
        }
    }

    /// <summary>
    /// iGPU (« AMD Radeon(TM) Graphics », « Intel(R) UHD Graphics 770 »…) : sa consommation fait
    /// déjà partie de celle du processeur. Les cartes dédiées portent un nom de modèle (RX, Arc, RTX…).
    /// </summary>
    private static bool IsIntegratedGpu(string name)
        => name.Contains("Graphics", StringComparison.OrdinalIgnoreCase)
           && !name.Contains("RX", StringComparison.Ordinal)
           && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
           && !name.Contains("Pro", StringComparison.OrdinalIgnoreCase);

    private static GpuReading ReadGpu(IHardware gpu, bool integrated)
    {
        float? Find(SensorType type, string name)
            => gpu.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

        return new GpuReading(
            gpu.Name,
            integrated,
            gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature)?.Value,
            gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))?.Value,
            Find(SensorType.Clock, "GPU Core"),
            Find(SensorType.Clock, "GPU Memory"),
            Find(SensorType.SmallData, "GPU Memory Used"),
            Find(SensorType.SmallData, "GPU Memory Total"));
    }

    /// <summary>Puissance totale de la carte : « GPU Package » (NVIDIA), « Total »/« PPT »/« Board » (AMD, Intel).</summary>
    private static float? ReadGpuPower(IHardware gpu)
    {
        var sensors = gpu.Sensors.Where(s => s.SensorType == SensorType.Power && s.Value is > 0).ToList();
        var total = sensors.FirstOrDefault(s =>
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("PPT", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("Board", StringComparison.OrdinalIgnoreCase));
        return total?.Value ?? sensors.Select(s => s.Value).Max();
    }

    private static float? TryReadBridgeTemperature()
    {
        try
        {
            var info = new FileInfo(BridgeFilePath);
            if (!info.Exists) return null;
            if (DateTime.UtcNow - info.LastWriteTimeUtc > BridgeFileMaxAge)
            {
                RestartBridgeIfStale(info.LastWriteTimeUtc);
                return null;
            }

            // Le dossier est dans ProgramData, donc potentiellement modifiable par un autre compte :
            // on n'en fait pas confiance. Un fichier démesuré n'est même pas lu.
            if (info.Length > BridgeFileMaxBytes)
            {
                Log.WarnOnce("hardware-bridge-invalid", $"Pont de température ignoré : fichier trop volumineux ({info.Length} octets)");
                return null;
            }

            var text = File.ReadAllText(BridgeFilePath).Trim();
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                && value is >= BridgeMinCelsius and <= BridgeMaxCelsius)
                return (float)value;

            Log.WarnOnce("hardware-bridge-invalid", "Pont de température ignoré : contenu invalide ou hors plage (0 à 150 °C attendus)");
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int? RunTask(string name)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", $"/Run /TN \"{name}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return null;
        if (!p.WaitForExit(5000))
        {
            try { p.Kill(); } catch { /* déjà terminé */ }
            return null;
        }
        return p.ExitCode;
    }

    private static DateTime _lastBridgeRestart = DateTime.MinValue;
    private static int _bridgeRestarts;

    /// <summary>
    /// Le pont n'écrit plus depuis 30 s (tâche arrêtée à l'ouverture de session, boucle tuée...) : on relance la tâche
    /// planifiée « OXYN-CpuTempBridge » (possible sans droits administrateur). Au plus toutes les 2 minutes au début,
    /// puis toutes les 30 minutes après 5 essais (inutile d'insister si c'est l'outil AMD qui échoue).
    /// </summary>
    private static void RestartBridgeIfStale(DateTime lastWriteUtc)
    {
        var now = DateTime.UtcNow;
        if (now - lastWriteUtc < TimeSpan.FromSeconds(30)) return;
        var wait = _bridgeRestarts < 5 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(30);
        if (now - _lastBridgeRestart < wait) return;
        _lastBridgeRestart = now;
        _bridgeRestarts++;
        // En tâche de fond : schtasks peut prendre plusieurs secondes et Read() tient le verrou des capteurs.
        _ = Task.Run(() =>
        {
            try
            {
                // Pont des capteurs, sinon l'ancien pont de température (OXYN, puis SysDeck avant le renommage).
                var code = RunTask("OXYN-SensorBridge");
                if (code != 0) code = RunTask("OXYN-CpuTempBridge");
                if (code != 0) code = RunTask("SysDeck-CpuTempBridge");
                Log.Info($"Température CPU : le pont n'écrit plus depuis {(now - lastWriteUtc).TotalMinutes:0} min, tâche relancée (code {code}).");
            }
            catch (Exception ex)
            {
                Log.WarnOnce("bridge-restart", "Température CPU : relance du pont impossible", ex);
            }
        });
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _cpuFrequency.Dispose();
            try { _computer.Close(); }
            catch (Exception ex) { Log.Warn("Fermeture des capteurs matériels", ex); }
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}

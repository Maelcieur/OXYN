using System.IO;
using System.Text.Json;
using Oxyn.Infrastructure;

namespace Oxyn.Services;

/// <summary>Une mesure écrite par le pont des capteurs (Oxyn.SensorBridge).</summary>
public sealed record BridgeSensor(string Id, string Hardware, string HardwareType, string Name, string Type, float Value)
{
    /// <summary>
    /// Mesure dans l'unité attendue par OXYN : certaines versions du pont donnent les quantités en octets
    /// (« Data » attendu en Go, « SmallData » en Mo) ; une valeur de plus de 100 000 ne peut être que des octets.
    /// </summary>
    public BridgeSensor Normalized() => Type switch
    {
        "Data" when Value > 100_000 => this with { Value = Value / (1024f * 1024f * 1024f) },
        "SmallData" when Value > 10_000_000 => this with { Value = Value / (1024f * 1024f) },
        _ => this
    };
}

/// <summary>
/// Lit C:\ProgramData\Oxyn\sensors.json, écrit toutes les 2 s par le pont des capteurs (tâche planifiée élevée,
/// LibreHardwareMonitor + PawnIO). Null si le fichier manque, est trop vieux (pont arrêté) ou invalide : OXYN revient
/// alors à sa propre lecture des capteurs.
/// </summary>
public static class SensorBridgeReader
{
    public const string FilePath = @"C:\ProgramData\Oxyn\sensors.json";
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);
    // Dossier lisible par tous : on ne fait pas confiance au contenu. Quelques centaines de capteurs tiennent en ~100 Ko.
    private const long MaxBytes = 4 * 1024 * 1024;

    private static DateTime _cachedStamp;
    private static IReadOnlyList<BridgeSensor>? _cached;

    /// <summary>Le fichier existe mais n'est plus mis à jour (le pont s'est arrêté) : date de sa dernière écriture.</summary>
    public static DateTime? StaleSinceUtc { get; private set; }

    public static IReadOnlyList<BridgeSensor>? TryRead()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists)
            {
                StaleSinceUtc = null;
                return null;
            }
            if (DateTime.UtcNow - info.LastWriteTimeUtc > MaxAge)
            {
                StaleSinceUtc = info.LastWriteTimeUtc;
                return null;
            }
            StaleSinceUtc = null;
            if (info.LastWriteTimeUtc == _cachedStamp && _cached is not null) return _cached;
            if (info.Length > MaxBytes)
            {
                Log.WarnOnce("sensor-bridge-size", $"Pont des capteurs ignoré : fichier trop volumineux ({info.Length} octets)");
                return null;
            }

            var list = Parse(File.ReadAllText(FilePath));
            _cachedStamp = info.LastWriteTimeUtc;
            _cached = list;
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remplacé pile pendant la lecture : la mesure précédente reste valable.
            return _cached;
        }
        catch (JsonException ex)
        {
            Log.WarnOnce("sensor-bridge-json", "Pont des capteurs ignoré : fichier invalide", ex);
            return null;
        }
    }

    /// <summary>
    /// LibreHardwareMonitor 0.9.7 (celui du pont) donne les quantités de mémoire en octets, la 0.9.6 en Go (Data) et en Mo
    /// (SmallData), unités qu'attend OXYN. Aucune mémoire ne dépasse un million de Go ou de Mo : au-delà, ce sont des octets.
    /// </summary>
    public static float NormalizeSize(string type, float value) => type switch
    {
        "Data" when value > 1_000_000 => value / (1024f * 1024f * 1024f),
        "SmallData" when value > 1_000_000 => value / (1024f * 1024f),
        _ => value
    };

    public static List<BridgeSensor> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<BridgeSensor>();
        if (!doc.RootElement.TryGetProperty("sensors", out var sensors) || sensors.ValueKind != JsonValueKind.Array) return list;
        foreach (var s in sensors.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("value", out var v) || !v.TryGetSingle(out var value)
                || float.IsNaN(value) || float.IsInfinity(value))
                continue;
            string Str(string name) => s.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            var type = Str("type");
            list.Add(new BridgeSensor(Str("id"), Str("hw"), Str("hwType"), Str("name"), type, NormalizeSize(type, value)));
        }
        return list;
    }
}

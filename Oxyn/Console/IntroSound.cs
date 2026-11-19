using System.IO;
using NAudio.Wave;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Bande-son ORIGINALE de l'intro « Orbite », synthétisée par le code (souffles, impact, accord, carillon) : aucun son
/// enregistré. Chaque évènement est calé sur l'animation de <see cref="ConsoleIntro"/> (temps en secondes).
/// </summary>
public static class IntroSound
{
    public const int Rate = 44100;

    /// <summary>
    /// Intro « Orbite » : grondement qui monte pendant la spirale, souffle d'aspiration, clic + impact au verrouillage
    /// (3,05 s), accord lumineux et carillon, puis le nom qui s'écrit.
    /// </summary>
    public static float[] Orbit()
    {
        var (l, r, n) = Tracks(8.0);
        var rnd = new Random(61);
        Drone(l, r, 0.3, 2.6, 55, 110, 0.07);          // grondement qui monte
        Whoosh(l, r, rnd, 0.9, 2.0, true, 0.09);        // les touches qui tournent
        Whoosh(l, r, rnd, 0.6, 2.4, true, 0.06);        // aspiration du vortex
        Drone(l, r, 1.2, 1.8, 70, 140, 0.04);
        Blip(l, r, 3.0, 1900, 0.03, 0.09, 0);           // verrouillage
        Boom(l, r, rnd, 3.05, 0.55);
        Pad(l, r, 3.15, new[] { 50, 57, 62, 69, 74 }, 4.8, 0.12);   // Ré ouvert, lumineux
        foreach (var (midi, i) in new[] { 81, 86, 88, 93 }.Select((m, i) => (m, i)))
            Bell(l, r, 3.1 + i * 0.06, midi, 1.4, 0.022, -0.3 + i * 0.2);   // éclat
        for (var i = 0; i < 4; i++) Blip(l, r, 4.45 + i * 0.12, 1500 + i * 160, 0.012, 0.018, -0.3 + i * 0.2);   // lettres
        return Finish(l, r, n, 0.2, 0.28, 1.2);
    }

    /// <summary>Son de la touche A puis de « Bienvenue » : clic, souffle, deux notes claires et un accord doux.</summary>
    public static float[] Welcome()
    {
        var (l, r, n) = Tracks(3.4);
        var rnd = new Random(53);
        Blip(l, r, 0, 1800, 0.03, 0.08, 0);
        Boom(l, r, rnd, 0.02, 0.3);
        Whoosh(l, r, rnd, 0.0, 0.8, true, 0.05);
        Bell(l, r, 0.05, 81, 0.9, 0.05, -0.15);
        Bell(l, r, 0.14, 88, 0.9, 0.045, 0.15);
        Pad(l, r, 0.3, new[] { 50, 57, 62, 66, 69 }, 2.9, 0.1);
        return Finish(l, r, n, 0.24, 0.28, 1.2);
    }

    private static (float[] L, float[] R, int N) Tracks(double seconds)
    {
        var n = (int)(seconds * Rate);
        return (new float[n], new float[n], n);
    }

    /// <summary>Écho stéréo puis limitation douce, en piste entrelacée.</summary>
    private static float[] Finish(float[] l, float[] r, int n, double echo, double feedback, double drive)
    {
        Echo(l, r, echo, feedback);
        var stereo = new float[n * 2];
        for (var i = 0; i < n; i++)
        {
            stereo[i * 2] = (float)Math.Tanh(l[i] * drive);
            stereo[i * 2 + 1] = (float)Math.Tanh(r[i] * drive);
        }
        return stereo;
    }

    /// <summary>Bip « carré » (harmoniques impaires) très court.</summary>
    private static void Blip(float[] l, float[] r, double at, double freq, double len, double vol, double pan)
    {
        var start = (int)(at * Rate);
        for (var k = 0; k < len * Rate; k++)
        {
            var t = k / (double)Rate;
            var env = Math.Min(1, t / 0.002) * Math.Min(1, (len - t) / 0.004);
            var v = Math.Sin(2 * Math.PI * freq * t) + Math.Sin(6 * Math.PI * freq * t) / 3 + Math.Sin(10 * Math.PI * freq * t) / 5;
            Add(l, r, start + k, v * env * vol, pan);
        }
    }

    /// <summary>Grondement en dent de scie dont la hauteur et la brillance montent (filtre passe-bas qui s'ouvre).</summary>
    private static void Drone(float[] l, float[] r, double at, double len, double f0, double f1, double vol)
    {
        var start = (int)(at * Rate);
        double phase = 0, lp = 0;
        for (var k = 0; k < len * Rate; k++)
        {
            var p = k / (len * Rate);
            var f = f0 + (f1 - f0) * p;
            phase += f / Rate;
            var saw = 2 * (phase - Math.Floor(phase + 0.5));
            var cut = 0.005 + 0.05 * p;
            lp += cut * (saw - lp);
            var env = Math.Min(1, p / 0.8) * Math.Min(1, (1 - p) / 0.2);
            Add(l, r, start + k, lp * env * vol * 3, 0);
        }
    }

    /// <summary>
    /// Joue la piste. Avec <paramref name="deviceId"/> (sortie de la TV), le son va DIRECTEMENT sur la TV, même si
    /// Windows change la sortie par défaut pendant la resynchronisation HDMI. Renvoie le lecteur (à arrêter pour couper).
    /// </summary>
    public static IDisposable? Play(float[] stereo, string? deviceId = null)
    {
        try
        {
            var bytes = new byte[stereo.Length * 4];
            Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
            var stream = new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2));
            var output = CreateOutput(deviceId);
            output.Init(stream);
            output.Play();
            return new Player(output, stream);
        }
        catch (Exception ex)
        {
            Log.Warn("Intro : lecture du son impossible", ex);
            return null;
        }
    }

    /// <summary>Sortie WASAPI sur l'appareil demandé (la TV), sinon la sortie par défaut.</summary>
    public static IWavePlayer CreateOutput(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                return new WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 120);
            }
            catch (Exception ex)
            {
                Log.WarnOnce("intro-device", "Intro : sortie de la TV introuvable, sortie par défaut utilisée", ex);
            }
        }
        return new WaveOutEvent { DesiredLatency = 120 };
    }

    private sealed class Player(IWavePlayer output, WaveStream stream) : IDisposable
    {
        public void Dispose()
        {
            try { output.Stop(); } catch { }
            output.Dispose();
            stream.Dispose();
        }
    }

    // ---------- Synthèse ----------

    private static double Freq(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    private static void Add(float[] l, float[] r, int i, double v, double pan)
    {
        if (i < 0 || i >= l.Length) return;
        l[i] += (float)(v * Math.Min(1, 1 - pan));
        r[i] += (float)(v * Math.Min(1, 1 + pan));
    }

    /// <summary>Cloche : partiels inharmoniques, attaque nette, décroissance exponentielle.</summary>
    private static void Bell(float[] l, float[] r, double at, int midi, double len, double vol, double pan)
    {
        var f = Freq(midi);
        var start = (int)(at * Rate);
        var count = (int)(len * Rate);
        for (var k = 0; k < count; k++)
        {
            var t = k / (double)Rate;
            var env = Math.Min(1, t / 0.004) * Math.Exp(-t * 4.0 / len * 2);
            var v = Math.Sin(2 * Math.PI * f * t) + 0.45 * Math.Sin(2 * Math.PI * f * 2.76 * t) * Math.Exp(-t * 6)
                    + 0.25 * Math.Sin(2 * Math.PI * f * 5.4 * t) * Math.Exp(-t * 10);
            Add(l, r, start + k, v * env * vol, pan);
        }
    }

    /// <summary>Nappe : sinus légèrement désaccordés, attaque et relâchement doux.</summary>
    private static void Pad(float[] l, float[] r, double at, int[] notes, double len, double vol)
    {
        var start = (int)(at * Rate);
        var count = (int)(len * Rate);
        foreach (var (note, idx) in notes.Select((n, i) => (n, i)))
        {
            var f = Freq(note);
            var pan = notes.Length == 1 ? 0 : -0.5 + idx / (double)(notes.Length - 1);
            for (var k = 0; k < count; k++)
            {
                var t = k / (double)Rate;
                var env = Math.Min(1, t / 0.25) * Math.Min(1, (len - t) / (len * 0.6));
                var v = Math.Sin(2 * Math.PI * f * t) * 0.6 + Math.Sin(2 * Math.PI * f * 1.003 * t) * 0.4 + Math.Sin(2 * Math.PI * f * 2 * t) * 0.15;
                Add(l, r, start + k, v * env * vol / notes.Length * 2.2, pan);
            }
        }
    }

    /// <summary>Souffle : bruit filtré dont la brillance monte (ou descend).</summary>
    private static void Whoosh(float[] l, float[] r, Random rnd, double at, double len, bool rising, double vol)
    {
        var start = (int)(at * Rate);
        var count = (int)(len * Rate);
        double lp = 0;
        for (var k = 0; k < count; k++)
        {
            var p = k / (double)count;
            var cutoff = rising ? 0.02 + p * 0.25 : 0.27 - p * 0.25;
            lp += cutoff * (rnd.NextDouble() * 2 - 1 - lp);
            var env = Math.Sin(Math.PI * p);
            Add(l, r, start + k, lp * env * vol * 3, rising ? -0.6 + 1.2 * p : 0.6 - 1.2 * p);
        }
    }

    /// <summary>Impact grave : sinus qui plonge (90 -> 38 Hz) + souffle bref.</summary>
    private static void Boom(float[] l, float[] r, Random rnd, double at, double vol)
    {
        var start = (int)(at * Rate);
        double phase = 0;
        for (var k = 0; k < Rate * 1.6; k++)
        {
            var t = k / (double)Rate;
            var f = 38 + 52 * Math.Exp(-t * 6);
            phase += 2 * Math.PI * f / Rate;
            var v = Math.Sin(phase) * Math.Exp(-t * 2.2) + (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 25) * 0.25;
            Add(l, r, start + k, v * vol, 0);
        }
    }

    private static void Echo(float[] l, float[] r, double delay, double feedback)
    {
        var d = (int)(delay * Rate);
        for (var i = d; i < l.Length; i++)
        {
            l[i] += (float)(r[i - d] * feedback);
            r[i] += (float)(l[i - d] * feedback);
        }
    }
}

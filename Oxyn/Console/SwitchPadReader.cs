using HidSharp;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Lecture d'une manette Switch (Pro ou Joy-Con) par HID, sur un thread de fond : passe la manette en rapports complets
/// (0x30) et garde le dernier état décodé. Utilisé par le mode console (GamepadService) et par la surveillance de la
/// combinaison de lancement (GamepadComboWatcher) : plusieurs lecteurs peuvent ouvrir la même manette.
/// </summary>
internal sealed class SwitchPadReader
{
    /// <summary>
    /// Manettes qui parlent le protocole Switch (rapports 0x30, sous-commandes) : Nintendo, et manettes sous licence qui
    /// se présentent comme une manette Pro. HORI « Lic Pro Controller » (vérifiée le 21/11) : mêmes rapports, mais la
    /// batterie annoncée est FIXE (toujours « pleine », 0x80) : elle est donc tenue pour inconnue.
    /// </summary>
    private static readonly (int Vid, int Pid, ControllerKind Kind, string Name, bool RealBattery)[] SwitchModels =
    {
        (0x057E, 0x2009, ControllerKind.SwitchPro, "Manette Switch Pro", true),
        (0x057E, 0x2006, ControllerKind.JoyConLeft, "Joy-Con (G)", true),
        (0x057E, 0x2007, ControllerKind.JoyConRight, "Joy-Con (D)", true),
        (0x0F0D, 0x00F6, ControllerKind.SwitchPro, "Manette Switch HORI", false),
    };

    /// <summary>Ouvre les manettes Switch branchées qui ne sont pas encore dans <paramref name="pads"/> (appelant : verrou pris).</summary>
    public static void Scan(Dictionary<string, SwitchPadReader> pads)
    {
        try
        {
            foreach (var (vid, pid, kind, name, realBattery) in SwitchModels)
            {
                foreach (var dev in DeviceList.Local.GetHidDevices(vid, pid))
                {
                    var path = dev.DevicePath;
                    if (pads.ContainsKey(path)) continue;
                    if (!dev.TryOpen(out HidStream stream))
                    {
                        Log.WarnOnce("gamepad-hid-" + path, $"{name} : ouverture impossible (utilisée par Steam ?)");
                        continue;
                    }
                    var pad = new SwitchPadReader(path, name, kind, stream, dev.GetMaxOutputReportLength()) { RealBattery = realBattery };
                    pads[path] = pad;
                    pad.Start();
                }
            }
        }
        catch (Exception ex)
        {
            Log.WarnOnce("gamepad-scan", "Recherche des manettes Switch impossible", ex);
        }
    }

    // ---------- Une manette ----------

    private readonly HidStream _stream;
    private readonly int _outLen;
    private byte _packet;
    private volatile bool _closed;

    public string Path { get; }
    public string Name { get; }
    public ControllerKind Kind { get; }
    /// <summary>Faux : la manette annonce une batterie fixe, qui ne veut rien dire (affichée comme inconnue).</summary>
    public bool RealBattery { get; init; } = true;
    public volatile GamepadDecoder.SwitchInput? Latest;
    /// <summary>Mêmes boutons au format XInput (combinaisons de lancement et de volume, GamepadComboWatcher).</summary>
    public volatile GamepadDecoder.XInputLike? LatestXInput;
    public volatile bool Dead;

    public SwitchPadReader(string path, string name, ControllerKind kind, HidStream stream, int outLen)
    {
        Path = path; Name = name; Kind = kind; _stream = stream; _outLen = Math.Max(outLen, 49);
        _stream.ReadTimeout = 500;
    }

    public void Start() => new Thread(Read) { IsBackground = true, Name = "Oxyn.SwitchPad" }.Start();

    public void Close()
    {
        _closed = true;
        try { _stream.Dispose(); } catch { }
    }

    private void Read()
    {
        long lastSet;
        try
        {
            var buf = new byte[Math.Max(_stream.Device.GetMaxInputReportLength(), 64)];
            SetFullMode();
            lastSet = Environment.TickCount64;
            while (!_closed)
            {
                int n;
                try { n = _stream.Read(buf, 0, buf.Length); }
                catch (TimeoutException) { continue; }
                if (n <= 0) continue;
                if (buf[0] == 0x30)
                {
                    var inp = GamepadDecoder.ParseSwitchReport(buf, n, Kind);
                    if (inp != null) Latest = inp;
                    LatestXInput = GamepadDecoder.SwitchToXInput(buf, n, Kind);
                }
                else if (buf[0] == 0x3F && Environment.TickCount64 - lastSet > 1000)
                {
                    lastSet = Environment.TickCount64;
                    SetFullMode();     // mode simple : on demande le mode complet 0x30
                }
            }
        }
        catch (Exception ex)
        {
            if (!_closed) Log.WarnOnce("gamepad-read-" + Path, $"{Name} : lecture interrompue", ex);
        }
        Dead = true;
    }

    /// <summary>Sous-commande 0x03 (mode du rapport d'entrée) avec l'argument 0x30.</summary>
    private void SetFullMode()
    {
        try
        {
            var o = new byte[_outLen];
            o[0] = 0x01;
            o[1] = (byte)(_packet++ & 0x0F);
            o[2] = 0x00; o[3] = 0x01; o[4] = 0x40; o[5] = 0x40;   // vibration neutre
            o[6] = 0x00; o[7] = 0x01; o[8] = 0x40; o[9] = 0x40;
            o[10] = 0x03; o[11] = 0x30;
            _stream.Write(o);
        }
        catch (Exception ex)
        {
            Log.WarnOnce("gamepad-write-" + Path, $"{Name} : changement de mode impossible", ex);
        }
    }
}

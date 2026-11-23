namespace Oxyn.ConsoleMode;

/// <summary>Boutons logiques de navigation, communs à toutes les manettes.</summary>
[Flags]
public enum PadButtons
{
    None = 0,
    Up = 1, Down = 2, Left = 4, Right = 8,
    A = 16, B = 32, X = 64, Y = 128,
    Menu = 256, Guide = 512,
    PrevTab = 1024, NextTab = 2048,
    VolumeDown = 4096, VolumeUp = 8192,   // gâchettes LT / RT
}

public enum ControllerKind { Xbox, SwitchPro, JoyConLeft, JoyConRight }

/// <summary>Manette connectée telle qu'exposée à l'interface. BatteryPercent = -1 si inconnu. Active = dernière manette utilisée.</summary>
public sealed record ControllerInfo(string Id, string Name, ControllerKind Kind, int BatteryPercent, bool Charging, bool Wired, bool Active = false)
{
    /// <summary>Nombre de crans de batterie à afficher (0 à 4).</summary>
    public int Bars => BatteryPercent < 0 ? 0 : GamepadDecoder.BatteryBars(BatteryPercent);
}

/// <summary>
/// Alertes de batterie des manettes : une à 50 %, une quand la batterie devient faible (10 % ; 25 % pour les manettes
/// Switch, qui ne donnent leur niveau que par quarts). Une seule fois par seuil ; rien pendant la charge ; réarmé quand
/// la manette est rechargée. Une manette déjà faible à sa connexion est signalée tout de suite.
/// </summary>
public sealed class BatteryAlertTracker
{
    private const int None = 101;
    private readonly Dictionary<string, int> _notified = new();

    public static int LowThreshold(ControllerKind kind) => kind == ControllerKind.Xbox ? 10 : 25;

    /// <summary>Alertes à afficher pour cette liste de manettes (numéro = position dans la liste, à partir de 1).</summary>
    public List<(ControllerInfo Controller, int Number, bool Low)> Check(IReadOnlyList<ControllerInfo> list)
    {
        var alerts = new List<(ControllerInfo, int, bool)>();
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.Wired || c.BatteryPercent < 0) continue;
            var low = LowThreshold(c.Kind);
            var pct = c.BatteryPercent;
            if (!_notified.TryGetValue(c.Id, out var done))
            {
                // Première fois vue : on part de son niveau actuel ; déjà faible = signalée tout de suite.
                _notified[c.Id] = pct <= low ? low : pct <= 50 ? 50 : None;
                if (pct <= low && !c.Charging) alerts.Add((c, i + 1, true));
                continue;
            }
            if (c.Charging || pct > 55)
            {
                _notified[c.Id] = pct > 55 ? None : pct > low ? 50 : low;   // rechargée : seuils réarmés
                continue;
            }
            if (pct <= low && done > low) { _notified[c.Id] = low; alerts.Add((c, i + 1, true)); }
            else if (pct <= 50 && done > 50) { _notified[c.Id] = 50; alerts.Add((c, i + 1, false)); }
        }
        return alerts;
    }
}

/// <summary>Décodage pur des rapports de manettes (testé sans matériel).</summary>
public static class GamepadDecoder
{
    // ---------- XInput ----------

    public const ushort XDpadUp = 0x0001, XDpadDown = 0x0002, XDpadLeft = 0x0004, XDpadRight = 0x0008,
        XStart = 0x0010, XBack = 0x0020, XLb = 0x0100, XRb = 0x0200, XGuide = 0x0400,
        XA = 0x1000, XB = 0x2000, XX = 0x4000, XY = 0x8000;

    public static PadButtons FromXInput(ushort buttons, short lx, short ly, byte lt = 0, byte rt = 0)
    {
        var r = PadButtons.None;
        if ((buttons & XDpadUp) != 0) r |= PadButtons.Up;
        if ((buttons & XDpadDown) != 0) r |= PadButtons.Down;
        if ((buttons & XDpadLeft) != 0) r |= PadButtons.Left;
        if ((buttons & XDpadRight) != 0) r |= PadButtons.Right;
        if ((buttons & XA) != 0) r |= PadButtons.A;
        if ((buttons & XB) != 0) r |= PadButtons.B;
        if ((buttons & XX) != 0) r |= PadButtons.X;
        if ((buttons & XY) != 0) r |= PadButtons.Y;
        if ((buttons & XStart) != 0) r |= PadButtons.Menu;   // View ⧉ est réservé à View + LT / RT (volume)
        if ((buttons & XGuide) != 0) r |= PadButtons.Guide;
        if ((buttons & XLb) != 0) r |= PadButtons.PrevTab;
        if ((buttons & XRb) != 0) r |= PadButtons.NextTab;
        if (lt > 160) r |= PadButtons.VolumeDown;
        if (rt > 160) r |= PadButtons.VolumeUp;
        const int dead = 16000;
        if (lx < -dead) r |= PadButtons.Left; else if (lx > dead) r |= PadButtons.Right;
        if (ly > dead) r |= PadButtons.Up; else if (ly < -dead) r |= PadButtons.Down;
        return r;
    }

    /// <summary>Niveau XInput (type de pile, niveau 0 vide / 1 faible / 2 moyen / 3 plein) vers pourcentage, filaire.</summary>
    public static (int Percent, bool Wired) FromXInputBattery(byte type, byte level)
    {
        if (type == 1) return (-1, true);          // filaire : pas de pile
        if (type == 0 || type == 255) return (-1, false);
        return level switch { 0 => (5, false), 1 => (25, false), 2 => (60, false), _ => (100, false) };
    }

    /// <summary>Pourcentage vers 4 crans (1 cran dès qu'il reste de la charge).</summary>
    public static int BatteryBars(int percent) =>
        percent <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(percent / 25.0), 1, 4);

    // ---------- Nintendo Switch (Pro Controller, Joy-Con) ----------

    public sealed record SwitchInput(PadButtons Buttons, int BatteryPercent, bool Charging, bool Wired);

    /// <summary>Boutons au format XInput (masque wButtons + gâchettes 0-255).</summary>
    public sealed record XInputLike(ushort Buttons, byte LT, byte RT);

    /// <summary>
    /// Rapport complet 0x30 d'une manette Switch traduit en boutons XInput, pour les combinaisons pensées pour la
    /// manette Xbox : − = View, + = Menu, L / R = LB / RB, ZL / ZR = gâchettes, croix et A B X Y à leur place logique.
    /// </summary>
    public static XInputLike? SwitchToXInput(byte[] r, int length, ControllerKind kind)
    {
        if (r == null || length < 6 || r[0] != 0x30) return null;
        byte right = r[3], shared = r[4], left = r[5];
        ushort b = 0;
        byte lt = 0, rt = 0;
        if (kind != ControllerKind.JoyConLeft)
        {
            if ((right & 0x08) != 0) b |= 0x1000;   // A
            if ((right & 0x04) != 0) b |= 0x2000;   // B
            if ((right & 0x02) != 0) b |= 0x4000;   // X
            if ((right & 0x01) != 0) b |= 0x8000;   // Y
            if ((right & 0x40) != 0) b |= 0x0200;   // R -> RB
            if ((right & 0x80) != 0) rt = 255;      // ZR
            if ((shared & 0x02) != 0) b |= 0x0010;  // + -> Menu (Start)
        }
        if (kind != ControllerKind.JoyConRight)
        {
            if ((left & 0x02) != 0) b |= 0x0001;    // haut
            if ((left & 0x01) != 0) b |= 0x0002;    // bas
            if ((left & 0x08) != 0) b |= 0x0004;    // gauche
            if ((left & 0x04) != 0) b |= 0x0008;    // droite
            if ((left & 0x40) != 0) b |= 0x0100;    // L -> LB
            if ((left & 0x80) != 0) lt = 255;       // ZL
            if ((shared & 0x01) != 0) b |= 0x0020;  // − -> View (Back)
        }
        return new XInputLike(b, lt, rt);
    }

    /// <summary>
    /// Analyse un rapport d'entrée complet 0x30 (octet 0 = identifiant). Octet 2 : quartet haut = batterie
    /// (0 à 8, bit de poids faible = en charge), bit 0 = alimentation USB. Octets 3 à 5 : boutons.
    /// Renvoie null pour tout autre rapport (0x3F simple, réponses de sous-commande 0x21...).
    /// </summary>
    public static SwitchInput? ParseSwitchReport(byte[] r, int length, ControllerKind kind)
    {
        if (r == null || length < 12 || r[0] != 0x30) return null;
        var nibble = r[2] >> 4;
        var level = Math.Min(nibble & 0xE, 8);          // 8 plein, 6, 4, 2, 0 vide
        var charging = (nibble & 1) != 0;
        var wired = (r[2] & 1) != 0;
        var percent = level * 100 / 8;

        byte right = r[3], shared = r[4], left = r[5];
        var b = PadButtons.None;
        var isPro = kind == ControllerKind.SwitchPro;
        var hasLeft = isPro || kind == ControllerKind.JoyConLeft;
        var hasRight = isPro || kind == ControllerKind.JoyConRight;

        if (hasRight)
        {
            if ((right & 0x08) != 0) b |= PadButtons.A;
            if ((right & 0x04) != 0) b |= PadButtons.B;
            if ((right & 0x02) != 0) b |= PadButtons.X;
            if ((right & 0x01) != 0) b |= PadButtons.Y;
            if ((right & 0x40) != 0) b |= PadButtons.NextTab;
            if ((shared & 0x02) != 0) b |= PadButtons.Menu;   // Plus
            if ((shared & 0x10) != 0) b |= PadButtons.Guide;  // Home
        }
        if (hasLeft)
        {
            if ((left & 0x02) != 0) b |= PadButtons.Up;
            if ((left & 0x01) != 0) b |= PadButtons.Down;
            if ((left & 0x08) != 0) b |= PadButtons.Left;
            if ((left & 0x04) != 0) b |= PadButtons.Right;
            if ((left & 0x40) != 0) b |= PadButtons.PrevTab;
            if ((shared & 0x01) != 0) b |= PadButtons.Menu;   // Moins
        }
        if (kind == ControllerKind.JoyConLeft)
        {
            // Joy-Con seul tenu à l'horizontale : SL / SR servent de B / A.
            if ((left & 0x10) != 0) b |= PadButtons.A;
            if ((left & 0x20) != 0) b |= PadButtons.B;
        }
        else if (kind == ControllerKind.JoyConRight)
        {
            if ((right & 0x10) != 0) b |= PadButtons.A;
            if ((right & 0x20) != 0) b |= PadButtons.B;
        }

        // Stick gauche (octets 6-8) pour Pro et Joy-Con L ; stick droit (9-11) pour Joy-Con R. Centre ~2048.
        int off = kind == ControllerKind.JoyConRight ? 9 : 6;
        int sx = r[off] | ((r[off + 1] & 0x0F) << 8);
        int sy = (r[off + 1] >> 4) | (r[off + 2] << 4);
        const int dead = 700;
        var dx = sx - 2048; var dy = sy - 2048;
        if (kind == ControllerKind.JoyConLeft) { (dx, dy) = (-dy, dx); }      // tenu sur le côté
        else if (kind == ControllerKind.JoyConRight) { (dx, dy) = (dy, -dx); }
        if (sx != 0 || sy != 0)
        {
            if (dx < -dead) b |= PadButtons.Left; else if (dx > dead) b |= PadButtons.Right;
            if (dy > dead) b |= PadButtons.Up; else if (dy < -dead) b |= PadButtons.Down;
        }
        return new SwitchInput(b, percent, charging, wired);
    }
}

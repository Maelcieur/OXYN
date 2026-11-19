using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn.ConsoleMode;

/// <summary>Matière du boîtier : boîtier plastique (DVD, Switch...), boîtier CD, boîte en carton (cartouches).</summary>
public enum CaseKind { Plastic, Jewel, Cardboard }

/// <summary>
/// Format et couleurs d'un boîtier selon la plateforme (dimensions réelles en mm, couleurs génériques : pas de logo).
/// </summary>
public sealed record CaseStyle(string Label, double WidthMm, double HeightMm, double DepthMm, CaseKind Kind,
    Color Body, Color Band, Color BandText, double BandRatio)
{
    private static readonly Color Black = Color.FromRgb(0x12, 0x13, 0x16);
    private static readonly Color White = Color.FromRgb(0xEE, 0xF0, 0xF3);
    private static readonly Color Grey = Color.FromRgb(0x5A, 0x60, 0x6B);

    /// <summary>Boîtier correspondant au nom de plateforme (tel que saisi : « Switch 2 », « PS2 », « Xbox 360 »...).</summary>
    public static CaseStyle For(string? platform)
    {
        var p = (platform ?? "").Trim().ToLowerInvariant().Replace("-", " ");
        bool Has(params string[] keys) => keys.Any(k => p.Contains(k));
        bool Is(params string[] keys) => keys.Any(k => p == k);

        // Sony
        if (Is("ps1", "psx", "psone", "ps one", "playstation", "playstation 1") || Has("playstation 1"))
            return new("PS1", 142, 125, 10, CaseKind.Jewel, Black, Black, White, 0);
        if (Has("ps2", "playstation 2")) return new("PS2", 135, 190, 14, CaseKind.Plastic, Black, Color.FromRgb(0x1A, 0x1C, 0x22), White, 0.06);
        if (Has("ps3", "playstation 3")) return new("PS3", 136, 170, 14, CaseKind.Plastic, Color.FromRgb(0x1E, 0x3F, 0x8C), Black, White, 0.075);
        if (Has("ps4", "playstation 4")) return new("PS4", 135, 170, 14, CaseKind.Plastic, Color.FromRgb(0x16, 0x46, 0xB0), Color.FromRgb(0x0B, 0x3F, 0xAE), White, 0.075);
        if (Has("ps5", "playstation 5")) return new("PS5", 135, 170, 14, CaseKind.Plastic, Color.FromRgb(0x16, 0x46, 0xB0), White, Color.FromRgb(0x20, 0x24, 0x2C), 0.075);
        if (Has("psp")) return new("PSP", 105, 180, 14, CaseKind.Plastic, Black, Color.FromRgb(0x1A, 0x1C, 0x22), White, 0.07);
        if (Has("vita")) return new("PS VITA", 105, 135, 12, CaseKind.Plastic, Color.FromRgb(0x1E, 0x2E, 0x6A), Color.FromRgb(0x1A, 0x2A, 0x5E), White, 0.08);

        // Microsoft
        if (Has("series")) return new("XBOX SERIES", 135, 170, 14, CaseKind.Plastic, Color.FromRgb(0x10, 0x7C, 0x10), Black, White, 0.075);
        if (Has("xbox one", "xone")) return new("XBOX ONE", 135, 170, 14, CaseKind.Plastic, Color.FromRgb(0x10, 0x7C, 0x10), Color.FromRgb(0x10, 0x7C, 0x10), White, 0.075);
        if (Has("360")) return new("XBOX 360", 135, 190, 14, CaseKind.Plastic, Color.FromRgb(0x4C, 0xA8, 0x3A), Color.FromRgb(0xE4, 0xE8, 0xEC), Color.FromRgb(0x3A, 0x8A, 0x2A), 0.07);
        if (Has("xbox")) return new("XBOX", 135, 190, 14, CaseKind.Plastic, Black, Color.FromRgb(0x2E, 0x7D, 0x32), White, 0.07);

        // Nintendo
        if (Has("switch 2", "switch2")) return new("SWITCH 2", 105, 170, 11, CaseKind.Plastic, Color.FromRgb(0xD5, 0x12, 0x1E), Color.FromRgb(0xE4, 0x1E, 0x2B), White, 0.07);
        if (Has("switch")) return new("SWITCH", 105, 170, 11, CaseKind.Plastic, Color.FromRgb(0xD5, 0x12, 0x1E), Color.FromRgb(0xE4, 0x1E, 0x2B), White, 0.07);
        if (Has("wii u", "wiiu")) return new("WII U", 135, 170, 14, CaseKind.Plastic, Color.FromRgb(0x00, 0x9A, 0xC7), Color.FromRgb(0x00, 0x8C, 0xBA), White, 0.075);
        if (Has("wii")) return new("WII", 135, 190, 14, CaseKind.Plastic, White, White, Grey, 0.07);
        if (Has("gamecube", "game cube", "ngc", "gcn")) return new("GAMECUBE", 135, 190, 14, CaseKind.Plastic, Black, Color.FromRgb(0x4B, 0x3B, 0x8F), White, 0.07);
        if (Has("3ds", "2ds")) return new("3DS", 125, 137, 11, CaseKind.Plastic, White, Color.FromRgb(0xD0, 0x1E, 0x2B), White, 0.08);
        if (Is("ds", "nds", "nintendo ds") || Has("nintendo ds")) return new("DS", 125, 137, 11, CaseKind.Plastic, Black, Color.FromRgb(0xE8, 0xE9, 0xEC), Grey, 0.08);
        if (Has("game boy", "gameboy", "gba", "gbc") || Is("gb"))
            return new(p.Contains("color") || p.Contains("gbc") ? "GBC" : p.Contains("advance") || p.Contains("gba") ? "GBA" : "GB", 127, 127, 28, CaseKind.Cardboard, Color.FromRgb(0x5B, 0x4F, 0xC8), Color.FromRgb(0x5B, 0x4F, 0xC8), White, 0);
        if (Has("64")) return new("N64", 177, 127, 38, CaseKind.Cardboard, Color.FromRgb(0x2D, 0x2D, 0x34), Color.FromRgb(0x2D, 0x2D, 0x34), White, 0);
        if (Has("snes", "super nintendo", "super nes", "super famicom"))
            return new("SNES", 185, 135, 38, CaseKind.Cardboard, Color.FromRgb(0x6E, 0x6A, 0x8C), Color.FromRgb(0x6E, 0x6A, 0x8C), White, 0);
        if (Has("nes", "famicom", "nintendo entertainment")) return new("NES", 127, 178, 38, CaseKind.Cardboard, Black, Black, White, 0);

        // Sega et autres
        if (Has("dreamcast")) return new("DREAMCAST", 142, 125, 14, CaseKind.Jewel, White, White, Grey, 0);
        if (Has("saturn")) return new("SATURN", 142, 125, 20, CaseKind.Jewel, Black, Black, White, 0);
        if (Has("mega drive", "megadrive", "genesis")) return new("MEGA DRIVE", 135, 190, 18, CaseKind.Plastic, Black, Color.FromRgb(0xB8, 0x1E, 0x24), White, 0.06);
        if (Has("master system")) return new("MASTER SYSTEM", 135, 190, 18, CaseKind.Plastic, Black, Color.FromRgb(0xE8, 0xE8, 0xE8), Black, 0.06);

        // PC (vide), Steam ou inconnu : boîtier DVD noir.
        var label = string.IsNullOrWhiteSpace(platform) ? "PC" : platform.Trim().ToUpper(CultureInfo.CurrentCulture);
        return new(label.Length > 12 ? label[..12] : label, 135, 190, 14, CaseKind.Plastic, Black, Color.FromRgb(0x2A, 0x47, 0x5E), White, 0.06);
    }
}

/// <summary>
/// Boîtier de jeu en 3D pour l'étagère du mode console : rangé, on ne voit que sa tranche (titre vertical, couleur du
/// boîtier de la plateforme) ; sélectionné, il sort de l'étagère, pivote pour montrer la jaquette et flotte doucement.
/// Au lancement, il fait un tour complet en avançant vers l'écran.
/// </summary>
public sealed class GameBox
{
    /// <summary>Taille de la vue 3D (px) : fixe, le boîtier bouge à l'intérieur (pas de déformation pendant les animations).</summary>
    public const double ViewW = 600, ViewH = 520;

    // Unités 3D : 1 unité = 100 px dans le plan z = 0 ; 190 mm (boîtier DVD) = 390 px une fois sorti.
    private const double MmToUnit = 3.9 / 190;
    private const double ShelfScale = 285.0 / 390;   // boîtier rangé : un DVD fait la hauteur des anciennes jaquettes
    private const double PickedLift = 0.3, PickedZ = 0.6, PickedAngle = 24, ShelfAngle = 90;

    private readonly Viewport3D _view;
    private readonly ScaleTransform3D _scale = new(ShelfScale, ShelfScale, ShelfScale);
    private readonly AxisAngleRotation3D _turn = new(new Vector3D(0, 1, 0), ShelfAngle);
    private readonly AxisAngleRotation3D _sway = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _tilt = new(new Vector3D(1, 0, 0), 0);
    private readonly TranslateTransform3D _move = new(0, 0, 0);
    private readonly TranslateTransform3D _bob = new(0, 0, 0);
    private readonly double _shelfY;   // décalage pour que tous les boîtiers rangés reposent sur la même ligne
    private readonly double _shelfZ;   // recul pour que toutes les tranches soient dans le même plan (bas alignés malgré la perspective)
    private bool _picked;

    public FrameworkElement View => _view;
    public CaseStyle Style { get; }

    /// <summary>Largeur de la tranche quand le boîtier est rangé (px) : sert à aligner l'étagère.</summary>
    /// <summary>Largeur occupée par le boîtier sorti (px), un peu de marge comprise pour sa rotation.</summary>
    public double PickedWidth => (Style.WidthMm + Style.DepthMm) * MmToUnit * 100 * 1.05;

    public double SpineWidth => Style.DepthMm * MmToUnit * 100 * ShelfScale;

    public GameBox(ConsoleGame game, string platform, BitmapSource? cover)
    {
        Style = CaseStyle.For(platform);
        double bw = Style.WidthMm * MmToUnit, bh = Style.HeightMm * MmToUnit, bd = Style.DepthMm * MmToUnit;
        _shelfY = -(3.9 - bh) / 2 * ShelfScale;
        _shelfZ = -bw / 2 * ShelfScale;   // rangé (tourné de 90°), la tranche est à mi-largeur devant le centre : on la ramène à z = 0
        var front = Material(FrontTexture(game.Title, cover, Style, bw / bh));
        var spine = Material(SpineTexture(game.Title, Style, bd / bh));
        var body = new DiffuseMaterial(new SolidColorBrush(Darker(Style.Body, Style.Kind == CaseKind.Cardboard ? 0.15 : 0.35)));
        var back = new DiffuseMaterial(new SolidColorBrush(Darker(Style.Body, 0.25)));
        var edge = Style.Kind == CaseKind.Plastic ? new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC))) : body;

        // Coins arrondis comme un vrai boîtier : ~4 mm (plastique), ~2 mm (CD), presque droits (carton).
        var radiusMm = RadiusMm(Style.Kind);
        var model = RoundedBox(bw / 2, bh / 2, bd / 2, radiusMm * MmToUnit, front, back, spine, edge, body);
        // Les arrondis du bas reculent dans la profondeur : la perspective les remonte un peu, d'autant plus que le rayon
        // est grand. On descend le boîtier d'autant pour que le bas VISIBLE de toutes les tranches tombe sur la même ligne.
        _shelfY -= ShelfBottomRise(radiusMm * MmToUnit * ShelfScale);

        var transform = new Transform3DGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(new RotateTransform3D(_turn));
        transform.Children.Add(new RotateTransform3D(_sway));
        transform.Children.Add(new RotateTransform3D(_tilt));
        transform.Children.Add(_move);
        transform.Children.Add(_bob);
        model.Transform = transform;
        _move.OffsetY = _shelfY;
        _move.OffsetZ = _shelfZ;

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x9A, 0x9A, 0x9A)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x7A, 0x7A, 0x7A), new Vector3D(-0.35, -0.3, -1)));

        // Caméra en perspective : la vue de ViewW px montre ViewW / 100 unités dans le plan z = 0.
        const double fov = Fov;
        var dist = CameraDistance;
        _view = new Viewport3D
        {
            Width = ViewW, Height = ViewH, ClipToBounds = false,
            Camera = new PerspectiveCamera(new Point3D(0, 0, dist), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), fov),
            Children = { new ModelVisual3D { Content = lights }, new ModelVisual3D { Content = model } },
        };
    }

    private const double Fov = 30;

    /// <summary>Rayon des coins (mm) : boîtier plastique bien arrondi, boîtier CD un peu, carton presque droit.</summary>
    private static double RadiusMm(CaseKind kind) => kind switch { CaseKind.Plastic => 8.0, CaseKind.Jewel => 3.0, _ => 2.0 };

    /// <summary>Distance de la caméra : la vue de ViewW px montre ViewW / 100 unités dans le plan z = 0.</summary>
    private static double CameraDistance => ViewW / 100 / 2 / Math.Tan(Fov / 2 * Math.PI / 180);

    /// <summary>
    /// Remontée apparente (unités) du bas d'une tranche rangée dont les coins ont un rayon <paramref name="r"/> :
    /// point le plus bas de l'arrondi, projeté en perspective, comparé à un coin droit.
    /// </summary>
    internal static double ShelfBottomRise(double r)
    {
        const double hs = 3.9 / 2 * ShelfScale;   // bas de toutes les tranches, sous l'axe de la caméra
        var d = CameraDistance;
        double lowest = hs - r;
        for (var k = 0; k <= 90; k++)
        {
            var t = k / 90.0 * Math.PI / 2;
            var y = hs - r + r * Math.Sin(t);
            var z = r * (1 - Math.Cos(t));       // l'arrondi recule vers le fond
            lowest = Math.Max(lowest, y * d / (d + z));
        }
        return hs - lowest;
    }

    // ---------- Animations ----------

    /// <summary>Sort le boîtier de l'étagère (face visible, flotte) ou l'y range (tranche visible).</summary>
    public void SetPicked(bool picked, bool animate)
    {
        if (picked == _picked && animate) return;
        _picked = picked;
        double scale = picked ? 1 : ShelfScale, angle = picked ? PickedAngle : ShelfAngle, lift = picked ? PickedLift : _shelfY, z = picked ? PickedZ : _shelfZ;
        if (!animate)
        {
            Stop();
            _scale.ScaleX = _scale.ScaleY = _scale.ScaleZ = scale;
            _turn.Angle = angle;
            _move.OffsetY = lift;
            _move.OffsetZ = z;
            if (picked) StartIdle(0);
            return;
        }
        var dur = TimeSpan.FromMilliseconds(picked ? 480 : 380);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Go(_scale, ScaleTransform3D.ScaleXProperty, scale, dur, ease);
        Go(_scale, ScaleTransform3D.ScaleYProperty, scale, dur, ease);
        Go(_scale, ScaleTransform3D.ScaleZProperty, scale, dur, ease);
        Go(_turn, AxisAngleRotation3D.AngleProperty, angle, dur, ease);
        Go(_move, TranslateTransform3D.OffsetYProperty, lift, dur, ease);
        // Le boîtier avance d'abord (on le tire de la rangée), puis se pose ; l'inverse quand on le range.
        var pull = new DoubleAnimationUsingKeyFrames { Duration = dur };
        pull.KeyFrames.Add(new EasingDoubleKeyFrame(picked ? PickedZ + 0.9 : PickedZ + 0.5, KeyTime.FromPercent(0.45), new SineEase()));
        pull.KeyFrames.Add(new EasingDoubleKeyFrame(z, KeyTime.FromPercent(1), new SineEase()));
        _move.BeginAnimation(TranslateTransform3D.OffsetZProperty, pull);
        if (picked) StartIdle(0.48);
        else
        {
            _idleGen++;
            Go(_sway, AxisAngleRotation3D.AngleProperty, 0, dur, ease);
            Go(_tilt, AxisAngleRotation3D.AngleProperty, 0, dur, ease);
            Go(_bob, TranslateTransform3D.OffsetYProperty, 0, dur, ease);
        }
    }

    /// <summary>Lancement : un tour complet en avançant vers l'écran (~0,75 s).</summary>
    public Task PlayLaunchAsync()
    {
        var dur = TimeSpan.FromMilliseconds(720);
        _idleGen++;   // le flottement ne reprend pas pendant le tour de lancement
        _turn.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(PickedAngle, PickedAngle - 360, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        _move.BeginAnimation(TranslateTransform3D.OffsetZProperty, new DoubleAnimation(PickedZ, 3.2, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        _sway.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
        return Task.Delay(dur + TimeSpan.FromMilliseconds(40));
    }

    /// <summary>Remet le boîtier en place après un lancement (retour au menu).</summary>
    public void Reset() => SetPicked(_picked, false);

    private int _idleGen;

    /// <summary>
    /// Flottement du boîtier sorti (balancement, inclinaison, montée-descente). Chaque mouvement part de la position
    /// où le boîtier s'est posé (vitesse nulle) et va doucement vers son extrême, puis oscille sans fin : aucun à-coup.
    /// </summary>
    private void StartIdle(double delay)
    {
        var gen = ++_idleGen;
        Drift(_sway, AxisAngleRotation3D.AngleProperty, 0, 5, 2.6, delay, gen);
        Drift(_tilt, AxisAngleRotation3D.AngleProperty, 0, 2, 3.4, delay, gen);
        Drift(_bob, TranslateTransform3D.OffsetYProperty, 0, 0.07, 1.9, delay, gen, from0: true);
    }

    /// <summary>
    /// De <paramref name="rest"/> vers l'extrême, puis aller-retour sans fin entre les deux extrêmes (demi-période
    /// <paramref name="half"/> s, courbe sinus : vitesse nulle aux extrêmes). <paramref name="from0"/> : oscille entre
    /// rest et rest + amplitude (pas de valeur négative).
    /// </summary>
    private void Drift(Animatable target, DependencyProperty prop, double rest, double amplitude, double half, double delay, int gen, bool from0 = false)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        double low = from0 ? rest : rest - amplitude, high = rest + amplitude;
        var first = new DoubleAnimation(rest, high, TimeSpan.FromSeconds(from0 ? half : half / 2))
        {
            BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd,
        };
        first.Completed += (_, _) =>
        {
            if (gen != _idleGen || !_picked) return;   // rangé ou relancé entre-temps
            target.BeginAnimation(prop, new DoubleAnimation(high, low, TimeSpan.FromSeconds(half))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease,
            });
        };
        target.BeginAnimation(prop, first);
    }

    private void Stop()
    {
        _idleGen++;
        foreach (var (o, p) in new (Animatable, DependencyProperty)[]
                 {
                     (_scale, ScaleTransform3D.ScaleXProperty), (_scale, ScaleTransform3D.ScaleYProperty), (_scale, ScaleTransform3D.ScaleZProperty),
                     (_turn, AxisAngleRotation3D.AngleProperty), (_sway, AxisAngleRotation3D.AngleProperty), (_tilt, AxisAngleRotation3D.AngleProperty),
                     (_move, TranslateTransform3D.OffsetYProperty), (_move, TranslateTransform3D.OffsetZProperty), (_bob, TranslateTransform3D.OffsetYProperty),
                 })
            o.BeginAnimation(p, null);
        _sway.Angle = _tilt.Angle = 0;
        _bob.OffsetY = 0;
    }

    private static void Go(Animatable o, DependencyProperty p, double to, TimeSpan dur, IEasingFunction ease) =>
        o.BeginAnimation(p, new DoubleAnimation(to, dur) { EasingFunction = ease });

    // ---------- Géométrie et textures ----------

    /// <summary>
    /// Boîtier à coins arrondis : face avant et dos en rectangle arrondi (triangles en éventail), côtés qui suivent
    /// l'arrondi. La tranche (côté gauche droit) porte sa texture, le côté droit le plastique clair, le reste le corps.
    /// </summary>
    private static Model3DGroup RoundedBox(double w, double h, double d, double r, Material front, Material back, Material spine, Material edge, Material body)
    {
        r = Math.Min(r, Math.Min(w, h) * 0.4);
        const int seg = 6;
        // Contour dans le sens direct, en partant du bas de l'arrondi bas-droit : bas-droit, haut-droit, haut-gauche, bas-gauche.
        var pts = new List<Point>();
        var corners = new[] { (w - r, -h + r, -90.0), (w - r, h - r, 0.0), (-w + r, h - r, 90.0), (-w + r, -h + r, 180.0) };
        foreach (var (cx, cy, a0) in corners)
            for (var k = 0; k <= seg; k++)
            {
                var a = (a0 + 90.0 * k / seg) * Math.PI / 180;
                pts.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
        Point Uv(Point p) => new((p.X + w) / (2 * w), (h - p.Y) / (2 * h));

        var frontMesh = new MeshGeometry3D();
        var backMesh = new MeshGeometry3D();
        frontMesh.Positions.Add(new Point3D(0, 0, d));
        frontMesh.TextureCoordinates.Add(Uv(new Point(0, 0)));
        backMesh.Positions.Add(new Point3D(0, 0, -d));
        backMesh.TextureCoordinates.Add(Uv(new Point(0, 0)));
        foreach (var p in pts)
        {
            frontMesh.Positions.Add(new Point3D(p.X, p.Y, d));
            frontMesh.TextureCoordinates.Add(Uv(p));
            backMesh.Positions.Add(new Point3D(p.X, p.Y, -d));
            backMesh.TextureCoordinates.Add(Uv(p));
        }
        for (var i = 1; i <= pts.Count; i++)
        {
            var j = i % pts.Count + 1;
            frontMesh.TriangleIndices.Add(0); frontMesh.TriangleIndices.Add(i); frontMesh.TriangleIndices.Add(j);
            backMesh.TriangleIndices.Add(0); backMesh.TriangleIndices.Add(j); backMesh.TriangleIndices.Add(i);
        }

        // Côtés : un quadrilatère par segment du contour, tourné vers l'extérieur.
        var wall = new MeshGeometry3D();
        var right = new MeshGeometry3D();
        var left = new MeshGeometry3D();
        // Segments : arrondi bas-droit + côté droit + arrondi haut-droit = plastique clair ; arrondi haut-gauche + tranche
        // + arrondi bas-gauche = texture de la tranche (elle va jusqu'au bout, sinon les coins sombres semblent la raccourcir).
        var perCorner = seg + 1;
        var topEdge = 2 * perCorner - 1;     // de la fin du haut-droit au début du haut-gauche
        var bottomEdge = 4 * perCorner - 1;  // de la fin du bas-gauche au début du bas-droit
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var mesh = i == topEdge || i == bottomEdge ? wall : i < topEdge ? right : left;
            var n = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(a.X, a.Y, d));
            mesh.Positions.Add(new Point3D(a.X, a.Y, -d));
            mesh.Positions.Add(new Point3D(b.X, b.Y, -d));
            mesh.Positions.Add(new Point3D(b.X, b.Y, d));
            // Tranche : u = profondeur (dos 0 -> avant 1), v = hauteur (haut 0 -> bas 1).
            double va = (h - a.Y) / (2 * h), vb = (h - b.Y) / (2 * h);
            mesh.TextureCoordinates.Add(new Point(1, va));
            mesh.TextureCoordinates.Add(new Point(0, va));
            mesh.TextureCoordinates.Add(new Point(0, vb));
            mesh.TextureCoordinates.Add(new Point(1, vb));
            mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 1); mesh.TriangleIndices.Add(n + 2);
            mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 2); mesh.TriangleIndices.Add(n + 3);
        }
        var model = new Model3DGroup();
        foreach (var (mesh, mat) in new[] { (frontMesh, front), (backMesh, back), (left, spine), (right, edge), (wall, body) })
        {
            mesh.Freeze();
            model.Children.Add(new GeometryModel3D(mesh, mat));
        }
        return model;
    }

    private static Material Material(BitmapSource tex)
    {
        var brush = new ImageBrush(tex) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);   // texture lissée (pas de pixels visibles)
        brush.Freeze();
        return new DiffuseMaterial(brush);
    }

    private static BitmapSource Render(FrameworkElement el, int w, int h)
    {
        el.Measure(new Size(w, h));
        el.Arrange(new Rect(0, 0, w, h));
        el.UpdateLayout();
        // Rendu en double résolution : net même sur une TV 4K (interface agrandie x2).
        const int k = 2;
        var bmp = new RenderTargetBitmap(w * k, h * k, 96 * k, 96 * k, PixelFormats.Pbgra32);
        bmp.Render(el);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Face avant : jaquette (ou titre), bandeau de la plateforme, bord et reflet selon la matière du boîtier.</summary>
    private static BitmapSource FrontTexture(string title, BitmapSource? cover, CaseStyle st, double aspect)
    {
        const int h = 640;
        var w = (int)Math.Round(h * aspect);
        var plastic = st.Kind == CaseKind.Plastic;
        var rim = plastic ? 7 : 0;
        var band = (int)Math.Round(h * st.BandRatio);
        var hinge = st.Kind == CaseKind.Jewel ? (int)(w * 0.075) : 0;   // charnière noire du boîtier CD
        // Jaquette et bandeau suivent l'arrondi du boîtier (coins arrondis, comme une vraie jaquette glissée dedans).
        var radius = RadiusMm(st.Kind) / st.HeightMm * h;
        var inner = Math.Max(0, radius - rim);
        var root = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Darker(st.Body, 0.2)) };
        var sleeve = new Grid { Margin = new Thickness(rim), Clip = new RectangleGeometry(new Rect(0, 0, w - 2 * rim, h - 2 * rim), inner, inner) };
        var art = new Grid { Margin = new Thickness(hinge, band, 0, 0), ClipToBounds = true };
        if (cover != null) art.Children.Add(new System.Windows.Controls.Image { Source = cover, Stretch = Stretch.UniformToFill });
        else
        {
            art.Background = new LinearGradientBrush(Color.FromRgb(0x24, 0x2A, 0x38), Color.FromRgb(0x0E, 0x11, 0x18), 90);
            art.Children.Add(new TextBlock
            {
                Text = title, Foreground = Brushes.White, FontSize = 44, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24),
            });
        }
        sleeve.Children.Add(art);
        if (band > 0)
            sleeve.Children.Add(new Border
            {
                Height = band, VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(Lighter(st.Band, 0.12), st.Band, 90),
                Child = new TextBlock
                {
                    Text = st.Label, Foreground = new SolidColorBrush(st.BandText), FontSize = band * 0.5, FontWeight = FontWeights.Bold,
                    Margin = new Thickness(Math.Max(12, inner * 0.6), 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                },
            });
        if (hinge > 0)
            sleeve.Children.Add(new Border
            {
                Width = hinge, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush(new GradientStopCollection { new(Darker(st.Body, 0.1), 0), new(Lighter(st.Body, 0.18), 0.5), new(Darker(st.Body, 0.1), 1) }, 0),
            });
        root.Children.Add(sleeve);
        if (st.Kind != CaseKind.Cardboard)
        {
            // Reflet du plastique transparent.
            root.Children.Add(new Border
            {
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.35),
                    new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.36), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
                }, new Point(0, 0), new Point(1, 1)),
            });
            root.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(radius) });
        }
        else
            root.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(radius) });
        return Render(root, w, h);
    }

    /// <summary>Tranche : couleur du boîtier, nom de la plateforme en haut, titre vertical.</summary>
    private static BitmapSource SpineTexture(string title, CaseStyle st, double aspect)
    {
        const int h = 640;
        var w = Math.Max(24, (int)Math.Round(h * aspect));
        var light = st.Body.R * 0.3 + st.Body.G * 0.59 + st.Body.B * 0.11 > 160;
        var text = light ? new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x2E)) : Brushes.White;
        var root = new Grid { Width = w, Height = h, Background = new LinearGradientBrush(Lighter(st.Body, 0.08), Darker(st.Body, 0.15), 0) };
        var head = Math.Max(70, (int)(h * 0.14));
        root.Children.Add(new Border
        {
            Height = head, VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(Lighter(st.Band, 0.1), st.Band, 0),
            // Nom de plateforme réduit pour tenir dans la largeur de la tranche, sans couper le mot.
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(3, 0, 3, 0),
                Child = new TextBlock { Text = st.Label, Foreground = new SolidColorBrush(st.BandText), FontSize = 16, FontWeight = FontWeights.Bold },
            },
        });
        root.Children.Add(new TextBlock
        {
            Text = title.ToUpper(CultureInfo.CurrentCulture), Foreground = text, FontSize = Math.Min(26, w * 0.5), FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Width = h - head - 24, LayoutTransform = new RotateTransform(90),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, head + 12, 0, 0),
        });
        root.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) });
        return Render(root, w, h);
    }

    private static Color Lighter(Color c, double k) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * k), (byte)(c.G + (255 - c.G) * k), (byte)(c.B + (255 - c.B) * k));

    private static Color Darker(Color c, double k) =>
        Color.FromRgb((byte)(c.R * (1 - k)), (byte)(c.G * (1 - k)), (byte)(c.B * (1 - k)));
}

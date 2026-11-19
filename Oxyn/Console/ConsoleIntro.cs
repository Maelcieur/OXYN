using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Intro du Mode console : « Orbite ». Les 4 touches du logo jaillissent du centre, tournent en spirale dans un plan
/// incliné pendant que la poussière est aspirée en tourbillon, se verrouillent en losange (onde de choc = anneau,
/// éclat horizontal), puis le logo glisse à gauche et OXYN s'écrit. Ensuite « Appuyez sur A » ; A = « Bienvenue »
/// (les particules sont soufflées). A pendant l'animation mène directement à « Bienvenue ».
/// Tout est redessiné à chaque image (<see cref="OrbitScene"/>), avec une bande-son synthétisée (<see cref="IntroSound"/>).
/// </summary>
public static class ConsoleIntro
{
    private const double W = 1920, H = 1080;
    /// <summary>Fin de l'animation (logo + nom affichés) : « Appuyez sur A » apparaît ensuite.</summary>
    private const double IntroEnd = 6.3;
    private const double WelcomeLength = 2.8;

    private static TaskCompletionSource<bool>? _confirm;

    /// <summary>Valide l'intro en cours (touche A, Entrée, clic). Faux si aucune intro n'attend : l'appelant peut alors la passer.</summary>
    public static bool TryConfirm() => _confirm?.TrySetResult(true) == true;

    /// <summary>Joue l'intro dans <paramref name="host"/> ; se termine à la fin de « Bienvenue » ou dès que <paramref name="ct"/> est annulé (passer).</summary>
    public static async Task PlayAsync(Grid host, CancellationToken ct, string? audioDeviceId = null)
    {
        var stage = new Grid { Width = W, Height = H, ClipToBounds = true };
        var view = new Viewbox { Stretch = Stretch.UniformToFill, Child = stage };
        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(view);
        host.Children.Clear();
        host.Children.Add(root);
        host.Visibility = Visibility.Visible;

        var sounds = new List<IDisposable>();
        try
        {
            // Le son est calculé AVANT de lancer l'animation : image et son partent ensemble.
            var pcm = await Task.Run(IntroSound.Orbit, ct);
            var welcomePcm = await Task.Run(IntroSound.Welcome, ct);
            var confirm = _confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var scene = new OrbitScene();
            stage.Children.Add(scene);
            if (IntroSound.Play(pcm, audioDeviceId) is { } player) sounds.Add(player);

            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(IntroEnd), ct), confirm.Task);
            ct.ThrowIfCancellationRequested();
            if (!confirm.Task.IsCompleted)
            {
                scene.ShowPrompt();
                await Task.WhenAny(confirm.Task, Task.Delay(Timeout.Infinite, ct));
                ct.ThrowIfCancellationRequested();
            }

            // La musique de l'intro s'arrête : place au son de validation.
            lock (sounds)
            {
                foreach (var s in sounds) try { s.Dispose(); } catch { }
                sounds.Clear();
                if (IntroSound.Play(welcomePcm, audioDeviceId) is { } p) sounds.Add(p);
            }
            scene.Welcome(IntroEnd);
            await Task.Delay(TimeSpan.FromSeconds(WelcomeLength), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("Intro du mode console", ex);
        }
        finally
        {
            _confirm = null;
        }

        // Sortie en fondu (aussi quand on passe l'intro).
        root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        await Task.Delay(720);
        lock (sounds)
            foreach (var s in sounds) try { s.Dispose(); } catch { }
        host.Children.Clear();
        host.Visibility = Visibility.Collapsed;
    }

    // ---------- Scène ----------

    /// <summary>La scène complète, dessinée à chaque image (même rendu que l'aperçu validé).</summary>
    internal sealed class OrbitScene : FrameworkElement
    {
        // Géométrie du logo (unités du logo : anneau de rayon 400).
        private const double KeyS = 200, KeyR = 46, KeyD = 167, RingR = 400, RingW = 26;
        private const double Lock = 3.05, FinaleAt = 4.0;
        private static readonly (double X, double Y)[] Keys = { (0, -KeyD), (KeyD, 0), (0, KeyD), (-KeyD, 0) };   // haut, droite, bas, gauche
        private static readonly double U = H / 1800;
        private static readonly Color Ink = Color.FromRgb(0xEC, 0xED, 0xF0), Grey = Color.FromRgb(0x7D, 0x81, 0x8B);
        private static readonly Color Glow = Color.FromRgb(158, 193, 255), Dust = Color.FromRgb(225, 232, 245);

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _offset;              // avance du temps quand A est appuyé pendant l'animation
        private double _promptAt = double.NaN, _welcomeAt = double.NaN;
        private readonly (double X, double Y, double Z, double Vx, double Vy, double Ph, double Tw)[] _dust = new (double, double, double, double, double, double, double)[170];
        private readonly (double X, double Y, double S, double Vx, double Vy, double Ph)[] _bokeh = new (double, double, double, double, double, double)[14];
        private readonly Brush[] _dustBrushes = new Brush[32], _inkBrushes = new Brush[32], _greyBrushes = new Brush[32];
        private static readonly Brush Background = MakeBackground();
        private readonly Typeface _word, _small;
        private bool _running;

        public OrbitScene()
        {
            Width = W; Height = H; IsHitTestVisible = false;
            var r = new Random(42);
            for (var i = 0; i < _dust.Length; i++)
                _dust[i] = (r.NextDouble(), r.NextDouble(), 0.2 + r.NextDouble() * 0.8, (r.NextDouble() - 0.5) * 0.012, -0.004 - r.NextDouble() * 0.014, r.NextDouble() * 6.3, 0.6 + r.NextDouble() * 1.6);
            for (var i = 0; i < _bokeh.Length; i++)
                _bokeh[i] = (r.NextDouble(), r.NextDouble(), 0.02 + r.NextDouble() * 0.05, (r.NextDouble() - 0.5) * 0.006, -0.002 - r.NextDouble() * 0.006, r.NextDouble() * 6.3);
            for (var a = 0; a < 32; a++)
            {
                _dustBrushes[a] = Frozen(Color.FromArgb((byte)(255 * (a + 1) / 32), Dust.R, Dust.G, Dust.B));
                _inkBrushes[a] = Frozen(Color.FromArgb((byte)(255 * (a + 1) / 32), Ink.R, Ink.G, Ink.B));
                _greyBrushes[a] = Frozen(Color.FromArgb((byte)(255 * (a + 1) / 32), Grey.R, Grey.G, Grey.B));
            }
            var bahn = new FontFamily("Bahnschrift, Segoe UI");
            _word = new Typeface(bahn, FontStyles.Normal, FontWeight.FromOpenTypeWeight(350), FontStretches.Normal);
            _small = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Loaded += (_, _) => { if (!_running) { _running = true; CompositionTarget.Rendering += OnFrame; } };
            Unloaded += (_, _) => { _running = false; CompositionTarget.Rendering -= OnFrame; };
        }

        private double T => FixedTime ?? _clock.Elapsed.TotalSeconds + _offset;

        /// <summary>Temps imposé (images fixes de vérification) ; null = horloge réelle.</summary>
        internal double? FixedTime { get; set; }
        internal void SetMarks(double prompt, double welcome) { _promptAt = prompt; _welcomeAt = welcome; }

        public void ShowPrompt() => _promptAt = T;

        /// <summary>A appuyé : si l'animation n'est pas finie, elle saute à la fin (logo + nom), puis « Bienvenue ».</summary>
        public void Welcome(double introEnd)
        {
            if (T < introEnd) _offset += introEnd - T;
            _welcomeAt = T;
        }

        private void OnFrame(object? sender, EventArgs e) => InvalidateVisual();

        // ---------- outils ----------
        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Brush Alpha(Color c, double a) => Frozen(Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B));
        private static int Step(double a) => Math.Clamp((int)(a * 32) - 1, 0, 31);
        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        private static double Seg(double t, double a, double b) => Clamp01((t - a) / (b - a));
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double EOut(double t) => 1 - Math.Pow(1 - t, 3);
        private static double EIn(double t) => t * t * t;
        private static double EInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        private static double EBack(double t) { const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2); }
        private static double Wrap(double v) => ((v % 1) + 1) % 1;
        private static double Bell(double t, double at, double width) => Math.Exp(-((t - at) * (t - at)) / width);

        private static Brush MakeBackground()
        {
            var g = new RadialGradientBrush(Color.FromRgb(0x17, 0x18, 0x1D), Color.FromRgb(0x06, 0x06, 0x08))
            {
                MappingMode = BrushMappingMode.Absolute, Center = new Point(W / 2, H / 2), GradientOrigin = new Point(W / 2, H / 2),
                RadiusX = Math.Max(W, H) * 0.7, RadiusY = Math.Max(W, H) * 0.7,
            };
            g.Freeze();
            return g;
        }

        private static Brush Radial(Color c, double a)
        {
            var g = new RadialGradientBrush(Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            g.Freeze();
            return g;
        }

        private void Key(DrawingContext dc, double x, double y, double size, double rot, bool grey, double alpha, double glow = 0)
        {
            if (alpha <= 0.01 || size <= 0.5) return;
            if (glow > 0.02)
                dc.DrawEllipse(Radial(Glow, glow * 0.55 * alpha), null, new Point(x, y), size * 0.95, size * 0.95);
            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new RotateTransform(rot * 180 / Math.PI));
            var brush = (grey ? _greyBrushes : _inkBrushes)[Step(alpha)];
            dc.DrawRoundedRectangle(brush, null, new Rect(-size / 2, -size / 2, size, size), size * KeyR / KeyS, size * KeyR / KeyS);
            dc.Pop(); dc.Pop();
        }

        private static void Ring(DrawingContext dc, double cx, double cy, double r, double w, double alpha, double glow)
        {
            if (alpha <= 0.01) return;
            if (glow > 0.02)
            {
                dc.DrawEllipse(null, new Pen(Alpha(Glow, 0.10 * glow * alpha), w * 4), new Point(cx, cy), r, r);
                dc.DrawEllipse(null, new Pen(Alpha(Glow, 0.20 * glow * alpha), w * 2.2), new Point(cx, cy), r, r);
            }
            dc.DrawEllipse(null, new Pen(Alpha(Ink, alpha), w), new Point(cx, cy), r, r);
        }

        // ---------- particules ----------
        private void Particles(DrawingContext dc, double t, double vortex)
        {
            var now = t + 37;   // dérive continue
            var m = Math.Min(W, H);
            var appear = Clamp01(t / 1.2);
            // Souffles : au verrouillage du logo, puis à l'appui sur A.
            double push = 0, bright = 0;
            foreach (var at in new[] { Lock, _welcomeAt })
            {
                if (double.IsNaN(at)) continue;
                var dt = t - at;
                if (dt <= 0) continue;
                push += Math.Exp(-dt * 1.6) * (1 - Math.Exp(-dt * 14));
                bright += Math.Exp(-dt * 2.2);
            }
            foreach (var b in _bokeh)
            {
                var x = Wrap(b.X + now * b.Vx) * W; var y = Wrap(b.Y + now * b.Vy) * H; var r = b.S * m;
                var a = (0.035 + 0.02 * Math.Sin(now * 0.7 + b.Ph)) * appear;
                dc.DrawEllipse(Radial(Color.FromRgb(190, 205, 235), a), null, new Point(x, y), r, r);
            }
            var beforeLock = t < Lock;
            foreach (var d in _dust)
            {
                var x = Wrap(d.X + now * d.Vx * d.Z) * W; var y = Wrap(d.Y + now * d.Vy * d.Z) * H;
                if (vortex > 0 && beforeLock)   // aspirées vers le centre en tournant
                {
                    double dx = x - W / 2, dy = y - H / 2, L = Math.Sqrt(dx * dx + dy * dy), ang = Math.Atan2(dy, dx) + vortex * (2.5 + d.Z * 2);
                    var k = 1 - vortex * (0.55 + 0.4 * d.Z);
                    x = W / 2 + Math.Cos(ang) * L * k; y = H / 2 + Math.Sin(ang) * L * k;
                }
                if (push > 0)
                {
                    double dx = x - W / 2, dy = y - H / 2, L = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    var k = push * m * 0.35 * d.Z * Math.Min(1, 300 / L);
                    x += dx / L * k; y += dy / L * k;
                }
                var s = (0.8 + 2.2 * d.Z) * (1 + Math.Min(bright, 1.5) * 0.8);
                var a = (0.12 + 0.5 * d.Z) * (0.55 + 0.45 * Math.Sin(now * d.Tw + d.Ph)) * appear * (1 + bright);
                if (a > 0.02) dc.DrawEllipse(_dustBrushes[Step(Math.Min(1, a))], null, new Point(x, y), s, s);
            }
        }

        // ---------- orbite (plan incliné qui se redresse) ----------
        private static (double X, double Y, double Z, double Rot, double Size, double A) Pos(double tt, int i, double cx, double cy)
        {
            var out1 = EOut(Seg(tt, 0.3, 1.0)); var spiral = EInOut(Seg(tt, 1.2, 3.0));
            var r = Lerp(0, 620, out1) * (1 - spiral) + KeyD * spiral;
            var spinA = EIn(Seg(tt, 0.3, 3.0)) * Math.PI * 8;
            var baseA = Math.Atan2(Keys[i].Y, Keys[i].X) + spinA;
            double x = Math.Cos(baseA) * r, y = Math.Sin(baseA) * r;
            var tilt = Lerp(1.15, 0, EInOut(Seg(tt, 0.6, 3.0)));
            var z = y * Math.Sin(tilt); y *= Math.Cos(tilt);
            var f = 1 / (1 - z / 1400);
            var rot = Math.PI / 4 + (1 - spiral) * Math.PI * 5 * (i % 2 == 1 ? -1 : 1);
            var size = KeyS * Lerp(0.2, 1, out1) * (tt < 3 ? Lerp(0.7, 1, spiral) : 1) * f;
            return (cx + x * f * U, cy + y * f * U, z, rot, size * U, Clamp01((tt - 0.3) * 4));
        }

        // ---------- nom + signature ----------
        private FormattedText Text(string s, Typeface face, double size, Brush brush) =>
            new(s, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private (double Cx, double Tx, double[] Ws) Lockup()
        {
            const string word = "OXYN";
            var ws = word.Select(c => Text(c.ToString(), _word, 300 * U, Brushes.White).WidthIncludingTrailingWhitespace).ToArray();
            var tw = ws.Sum() + 90 * U * 3;
            double r = RingR * U, gap = 150 * U, total = 2 * r + gap + tw;
            var cx = W / 2 - total / 2 + r;
            return (cx, cx + r + gap, ws);
        }

        /// <summary>Le logo glisse à gauche, OXYN s'écrit lettre par lettre, puis la signature. Renvoie le centre du logo.</summary>
        private double Finale(DrawingContext dc, double t, double cy, double fade)
        {
            var (lcx, tx, ws) = Lockup();
            var cx = Lerp(W / 2, lcx, EInOut(Seg(t, FinaleAt, FinaleAt + 1.0)));
            var baseLine = cy + 105 * U;
            var x = tx;
            for (var i = 0; i < 4; i++)
            {
                var q = EOut(Seg(t, FinaleAt + 0.45 + i * 0.12, FinaleAt + 0.95 + i * 0.12)) * fade;
                if (q > 0.01)
                {
                    var ft = Text("OXYN"[i].ToString(), _word, 300 * U, _inkBrushes[Step(q)]);
                    dc.DrawText(ft, new Point(x + (1 - q) * 60 * U, baseLine - ft.Baseline));
                }
                x += ws[i] + 90 * U;
            }
            var qs = EOut(Seg(t, FinaleAt + 1.25, FinaleAt + 2.0)) * fade;
            if (qs > 0.01)
            {
                var brush = Alpha(Color.FromRgb(0x6F, 0x73, 0x7C), qs);
                var sx = tx + 6 * U;
                foreach (var ch in "DASHBOARD  ·  DECK  ·  CONSOLE")
                {
                    var ft = Text(ch.ToString(), _small, 40 * U, brush);
                    dc.DrawText(ft, new Point(sx, baseLine + 120 * U - ft.Baseline));
                    sx += ft.WidthIncludingTrailingWhitespace + 14 * U;
                }
            }
            return cx;
        }

        // ---------- « Appuyez sur A » / « Bienvenue » ----------
        private void Prompt(DrawingContext dc, double t, double fade)
        {
            if (double.IsNaN(_promptAt)) return;
            var a = Seg(t, _promptAt, _promptAt + 0.6) * (0.55 + 0.45 * (0.5 + 0.5 * Math.Cos((t - _promptAt) * Math.PI / 1.1))) * fade;
            if (a <= 0.01) return;
            var label = Text("A P P U Y E Z   S U R   A", _small, 26, Alpha(Ink, a));
            const double d = 46, gap = 22;
            var total = d + gap + label.WidthIncludingTrailingWhitespace;
            var x0 = W / 2 - total / 2; var y = H - 150;
            dc.DrawEllipse(null, new Pen(Alpha(Ink, a), 2), new Point(x0 + d / 2, y), d / 2, d / 2);
            var letter = Text("A", _word, 24, Alpha(Ink, a));
            dc.DrawText(letter, new Point(x0 + d / 2 - letter.WidthIncludingTrailingWhitespace / 2, y - letter.Height / 2));
            dc.DrawText(label, new Point(x0 + d + gap, y - label.Height / 2));
        }

        private void WelcomeText(DrawingContext dc, double t)
        {
            if (double.IsNaN(_welcomeAt)) return;
            const string word = "BIENVENUE";
            var size = 92.0; var sp = 34.0;
            var fts = word.Select(c => Text(c.ToString(), _word, size, Brushes.White)).ToArray();
            var total = fts.Sum(f => f.WidthIncludingTrailingWhitespace) + sp * (word.Length - 1);
            var x = W / 2 - total / 2; var baseLine = H / 2 + size * 0.35;
            for (var i = 0; i < word.Length; i++)
            {
                var q = EOut(Seg(t, _welcomeAt + 0.35 + i * 0.06, _welcomeAt + 0.85 + i * 0.06));
                if (q > 0.01)
                {
                    var ft = Text(word[i].ToString(), _word, size, _inkBrushes[Step(q)]);
                    dc.DrawText(ft, new Point(x, baseLine - ft.Baseline + (1 - q) * 24));
                }
                x += fts[i].WidthIncludingTrailingWhitespace + sp;
            }
            // Trait fin qui s'étire sous le mot.
            var l = EInOut(Seg(t, _welcomeAt + 0.6, _welcomeAt + 1.5)) * total * 0.5;
            if (l > 1) dc.DrawRectangle(Alpha(Ink, 0.5), null, new Rect(W / 2 - l, baseLine + 44, l * 2, 2));
        }

        private static void Flare(DrawingContext dc, double f, double cy)
        {
            if (f <= 0.01) return;
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 200, 215, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * 0.75 * f), 235, 240, 255), 0.5));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 200, 215, 255), 1));
            g.Freeze();
            dc.DrawRectangle(g, null, new Rect(0, cy - 3 * U, W, 6 * U));
            dc.PushOpacity(0.25);
            dc.DrawRectangle(g, null, new Rect(0, cy - 30 * U, W, 60 * U));
            dc.Pop();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var t = T;
            const double cy = H / 2;
            var welcome = double.IsNaN(_welcomeAt) ? 0 : t - _welcomeAt;
            var logoFade = double.IsNaN(_welcomeAt) ? 1 : 1 - EInOut(Seg(t, _welcomeAt, _welcomeAt + 0.45));

            // Fond + particules (aspirées avant le verrouillage) + flash discret.
            dc.DrawRectangle(Background, null, new Rect(0, 0, W, H));
            Particles(dc, t, EIn(Seg(t, 0.5, Lock)));
            var flash = (t > 3 ? 0.045 * Bell(t, Lock, 0.012) : 0) + (welcome > 0 ? 0.05 * Bell(t, _welcomeAt, 0.01) : 0);
            if (flash > 0.003) dc.DrawRectangle(Alpha(Color.FromRgb(200, 215, 255), flash), null, new Rect(0, 0, W, H));

            // Cœur lumineux qui grossit pendant l'aspiration.
            var core = EIn(Seg(t, 0.6, Lock)) * (t < Lock ? 1 : Math.Exp(-(t - Lock) * 3));
            if (core > 0.002) dc.DrawEllipse(Radial(Color.FromRgb(230, 236, 255), 0.5 * core), null, new Point(W / 2, cy), 260 * U * (0.4 + core), 260 * U * (0.4 + core));

            dc.PushOpacity(logoFade);
            var cx = t > FinaleAt ? Finale(dc, t, cy, 1) : W / 2;

            // Touches, triées par profondeur (les plus lointaines d'abord).
            var cxk = t >= Lock ? cx : W / 2;
            var lockGlow = t > 2.9 ? Bell(t, 3.0, 0.005) : 0;
            foreach (var (p, i) in Enumerable.Range(0, 4).Select(i => (Pos(t, i, cxk, cy), i)).OrderBy(v => v.Item1.Z))
            {
                var shade = Math.Clamp(1 + p.Z / 900, 0.45, 1);
                Key(dc, p.X, p.Y, p.Size, p.Rot, i == 0, p.A * shade, lockGlow);
            }

            // Onde de choc qui devient l'anneau.
            var sw = Seg(t, 3.0, 3.7);
            if (sw > 0)
            {
                Ring(dc, cx, cy, RingR * U * Lerp(0.4, 1, EBack(sw)), RingW * U, Clamp01(sw * 3), 0.9 * (1 - Seg(t, 3.7, 4.8)));
                var w2 = Seg(t, 3.0, 3.9);
                if (w2 < 1) dc.DrawEllipse(null, new Pen(Alpha(Ink, (1 - w2) * 0.5), 6 * U), new Point(cx, cy), RingR * U * Lerp(0.4, 2.6, EOut(w2)), RingR * U * Lerp(0.4, 2.6, EOut(w2)));
            }
            dc.Pop();

            // Éclat horizontal au verrouillage, et à nouveau sur A.
            Flare(dc, t > Lock - 0.1 ? Bell(t, Lock, 0.06) : 0, cy);
            if (!double.IsNaN(_welcomeAt)) Flare(dc, Bell(t, _welcomeAt, 0.06) * 0.8, cy);

            Prompt(dc, t, logoFade);
            WelcomeText(dc, t);
        }
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMrDo.Engine;
using Effect = AVAMrDo.Engine.Effect;

namespace AVAMrDo.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMrDo/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Green = 0xFF72FF8F, Black = 0xFF000000;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private const double T = Field.Tile;

    /// <summary>A tile position (whole numbers at tile centres) on screen.</summary>
    private static Point S(double x, double y) => new(Field.Left + (x + 0.5) * T, Field.PlayTop + (y + 0.5) * T);

    /// <summary>Each level's earth: its colour and its pebbles.</summary>
    private static readonly (uint Earth, uint Dark, uint Pebble)[] Earths =
    [
        (0xFFB5702E, 0xFF8A5220, 0xFFD89A56),
        (0xFF3E8C7A, 0xFF2A6658, 0xFF6CC0AA),
        (0xFF8C3E7E, 0xFF66285A, 0xFFC070B0),
        (0xFF6E8C2E, 0xFF4E661E, 0xFFA8C860),
        (0xFF3E5E9C, 0xFF283E70, 0xFF7898D8),
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0812), new Rect(0, 0, Field.Width, Field.Height));
        DrawGarden(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (inGame || g.State == GameState.GameOver)
        {
            for (int c = 0; c < Field.Cols; c++)
                for (int r = 0; r < Field.Rows; r++)
                    if (g.Cherries[c, r] > 0) DrawCherry(dc, S(c, r), 1);
            foreach (var a in g.Apples) DrawApple(dc, a);
            foreach (var m in g.Monsters) DrawMonster(dc, m);
            if (g.Ball.State == BallState.Flying) DrawBall(dc, S(g.Ball.X, g.Ball.Y), 1);
            if (g.Clown.Alive) DrawClown(dc, S(g.Clown.X, g.Clown.Y), g.Clown.Facing, g.Clown.Walk, 1, g.Ball.State == BallState.Ready);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 22), 26, Yellow, center: true);
    }

    // ───────────────────────────── the garden ─────────────────────────────

    private void DrawGarden(DrawingContext dc)
    {
        var g = Engine;
        var (earth, dark, pebble) = Earths[(g.Level - 1) % Earths.Length];
        var area = new Rect(Field.Left, Field.PlayTop, Field.Cols * T, Field.Rows * T);
        dc.FillRectangle(Brush(earth), area);
        // Layers in the earth, and pebbles.
        for (int r = 0; r < Field.Rows * 3; r++)
            if (r % 3 == 2) dc.FillRectangle(Brush(dark), new Rect(Field.Left, Field.PlayTop + r * T / 3 + T / 6, Field.Cols * T, 3));
        for (int i = 0; i < 260; i++)
        {
            uint h = Hash(i, g.Level, 7);
            double x = Field.Left + h % (uint)(Field.Cols * T), y = Field.PlayTop + (h >> 12) % (uint)(Field.Rows * T);
            dc.DrawEllipse(Brush(i % 3 == 0 ? dark : pebble), null, new Point(x, y), 2.2, 1.6);
        }
        // The tunnels: a hollow in each dug tile and between the ones dug through to each other.
        const double w = 0.74 * T;
        var tunnel = Brush(Black);
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                if (!g.Dug[c, r]) continue;
                var p = S(c, r);
                dc.DrawRectangle(tunnel, null, new Rect(p.X - w / 2, p.Y - w / 2, w, w), 10, 10);
                if (c + 1 < Field.Cols && g.PassRight[c, r]) dc.FillRectangle(tunnel, new Rect(p.X, p.Y - w / 2, T, w));
                if (r + 1 < Field.Rows && g.PassDown[c, r]) dc.FillRectangle(tunnel, new Rect(p.X - w / 2, p.Y, w, T));
            }
        // The monsters' nest.
        var n = S(Field.NestCol, Field.NestRow);
        dc.DrawEllipse(Brush(0xFF2A1830), Pen(0xFFB070FF, 2.5), n, 0.3 * T, 0.3 * T);
        dc.DrawEllipse(null, Pen(0x80B070FF, 1.5), n, 0.2 * T, 0.2 * T);
        // A frame round the garden.
        dc.DrawRectangle(null, Pen(0xFF4A3A70, 4), area.Inflate(3));
    }

    private static void DrawCherry(DrawingContext dc, Point p, double s)
    {
        var top = new Point(p.X + 2 * s, p.Y - 15 * s);
        var a = new Point(p.X - 8 * s, p.Y + 7 * s);
        var b = new Point(p.X + 8 * s, p.Y + 9 * s);
        dc.DrawLine(Pen(0xFF3A9A3A, 2 * s), top, new Point(a.X + 1, a.Y - 6 * s));
        dc.DrawLine(Pen(0xFF3A9A3A, 2 * s), top, new Point(b.X, b.Y - 6 * s));
        Poly(dc, 0xFF4ACA4A, null, 0, top, new Point(top.X + 9 * s, top.Y - 4 * s), new Point(top.X + 4 * s, top.Y + 2 * s));
        foreach (var c in new[] { a, b })
        {
            dc.DrawEllipse(Brush(0xFFD8203A), Pen(0xFF7A0A18, 1), c, 7 * s, 7 * s);
            dc.DrawEllipse(Brush(0xC0FFFFFF), null, new Point(c.X - 2.5 * s, c.Y - 2.5 * s), 2 * s, 2 * s);
        }
    }

    private void DrawApple(DrawingContext dc, Apple a)
    {
        double wob = a.State == AppleState.Wobbling ? Math.Sin(_clock * 40) * 3 : 0;
        var p = S(a.Col, a.Y);
        p = new Point(p.X + wob, p.Y);
        double r = 0.36 * T;
        if (a.State == AppleState.Broken)
        {
            // Split in two on the ground.
            double f = Math.Max(0, 1 - a.Timer / 0.8);
            uint red = (uint)(255 * f) << 24 | 0x00E02A2A;
            dc.DrawEllipse(Brush(red), null, new Point(p.X - r * 0.6, p.Y + r * 0.5), r * 0.55, r * 0.35);
            dc.DrawEllipse(Brush(red), null, new Point(p.X + r * 0.6, p.Y + r * 0.5), r * 0.55, r * 0.35);
            dc.DrawEllipse(Brush((uint)(255 * f) << 24 | 0x00FFF0C0), null, new Point(p.X, p.Y + r * 0.45), r * 0.35, r * 0.2);
            return;
        }
        dc.DrawEllipse(Brush(0xFFE02A2A), Pen(0xFF7A0A0A, 1.5), new Point(p.X - r * 0.28, p.Y + 2), r * 0.72, r * 0.86);
        dc.DrawEllipse(Brush(0xFFE02A2A), Pen(0xFF7A0A0A, 1.5), new Point(p.X + r * 0.28, p.Y + 2), r * 0.72, r * 0.86);
        dc.DrawEllipse(Brush(0xFFF24A40), null, new Point(p.X, p.Y + 3), r * 0.62, r * 0.8);
        dc.DrawEllipse(Brush(0xB0FFFFFF), null, new Point(p.X - r * 0.38, p.Y - r * 0.25), r * 0.16, r * 0.24);
        dc.DrawLine(Pen(0xFF6A3A12, 3), new Point(p.X, p.Y - r * 0.65), new Point(p.X + 3, p.Y - r * 1.05));
        Poly(dc, 0xFF4ACA4A, 0xFF1A6A1A, 1, new Point(p.X + 2, p.Y - r * 0.9), new Point(p.X + r * 0.7, p.Y - r * 1.15), new Point(p.X + r * 0.3, p.Y - r * 0.65));
    }

    // ───────────────────────────── the clown and the monsters ─────────────────────────────

    private void DrawClown(DrawingContext dc, Point p, Dir facing, double walk, double s, bool ball)
    {
        double side = facing == Dir.Left ? -1 : 1;
        double step = Math.Sin(walk * 9);
        // Feet.
        dc.DrawEllipse(Brush(0xFFD82A2A), null, new Point(p.X - 7 * s + step * 3 * s, p.Y + 22 * s), 7 * s, 4 * s);
        dc.DrawEllipse(Brush(0xFFD82A2A), null, new Point(p.X + 7 * s - step * 3 * s, p.Y + 22 * s), 7 * s, 4 * s);
        // A baggy suit with big buttons.
        Poly(dc, 0xFF3A6AE0, 0xFF1A2A70, 1.5, new Point(p.X - 13 * s, p.Y + 19 * s), new Point(p.X + 13 * s, p.Y + 19 * s), new Point(p.X + 9 * s, p.Y + 2 * s), new Point(p.X - 9 * s, p.Y + 2 * s));
        dc.DrawEllipse(Brush(Yellow), null, new Point(p.X, p.Y + 8 * s), 2.5 * s, 2.5 * s);
        dc.DrawEllipse(Brush(Yellow), null, new Point(p.X, p.Y + 14 * s), 2.5 * s, 2.5 * s);
        // A ruff.
        for (int i = -2; i <= 2; i++) dc.DrawEllipse(Brush(White), null, new Point(p.X + i * 4.5 * s, p.Y + 2 * s), 3.5 * s, 3 * s);
        // The face, the red nose and the grin.
        var face = new Point(p.X, p.Y - 7 * s);
        dc.DrawEllipse(Brush(0xFFFFF4E8), Pen(0xFFB0A090, 1), face, 10 * s, 10 * s);
        dc.DrawEllipse(Brush(Black), null, new Point(face.X + side * 2 * s - 3.5 * s, face.Y - 3 * s), 1.6 * s, 2.2 * s);
        dc.DrawEllipse(Brush(Black), null, new Point(face.X + side * 2 * s + 3.5 * s, face.Y - 3 * s), 1.6 * s, 2.2 * s);
        dc.DrawEllipse(Brush(0xFFFF2A2A), null, new Point(face.X + side * 3 * s, face.Y + 1 * s), 3.6 * s, 3.4 * s);
        dc.DrawLine(Pen(0xFFC02A2A, 1.8 * s), new Point(face.X - 4 * s, face.Y + 5 * s), new Point(face.X + 4 * s, face.Y + 5 * s));
        // The pointed hat with its bobble.
        Poly(dc, 0xFFFFE14A, 0xFF8A6A10, 1.2, new Point(face.X - 9 * s, face.Y - 6 * s), new Point(face.X + 9 * s, face.Y - 6 * s), new Point(face.X + side * 4 * s, face.Y - 24 * s));
        dc.DrawEllipse(Brush(0xFFFF4AA0), null, new Point(face.X + side * 4 * s, face.Y - 25 * s), 3.5 * s, 3.5 * s);
        // The ball in hand, ready to throw.
        if (ball) DrawBall(dc, new Point(p.X + side * 15 * s, p.Y + 6 * s), 0.7 * s);
    }

    private void DrawBall(DrawingContext dc, Point p, double s)
    {
        double glow = 0.75 + 0.25 * Math.Sin(_clock * 18);
        dc.DrawEllipse(Brush((uint)(120 * glow) << 24 | 0x00FFF0A0), null, p, 13 * s, 13 * s);
        dc.DrawEllipse(Brush(0xFFFFFFF0), Pen(0xFFC0A040, 1), p, 7 * s, 7 * s);
    }

    private void DrawMonster(DrawingContext dc, Monster m)
    {
        var p = S(m.X, m.Y);
        uint body = m.Kind == MonsterKind.Letter ? 0xFF3AB04A : m.Digging ? 0xFF3A6AE0 : 0xFFE03A4A;
        uint dark = m.Kind == MonsterKind.Letter ? 0xFF1A6A2A : m.Digging ? 0xFF1A2A80 : 0xFF801A2A;
        double bob = Math.Sin(m.Age * 12) * 2;
        double side = m.Facing == Dir.Left ? -1 : 1;
        // Stubby legs.
        double step = Math.Sin(m.Age * 14) * 3;
        dc.FillRectangle(Brush(dark), new Rect(p.X - 11 + step, p.Y + 12, 7, 10));
        dc.FillRectangle(Brush(dark), new Rect(p.X + 4 - step, p.Y + 12, 7, 10));
        // A round body with a ridge of spikes along its back.
        var c = new Point(p.X, p.Y + bob);
        for (int i = -2; i <= 2; i++)
            Poly(dc, dark, null, 0, new Point(c.X + i * 7 - 4, c.Y - 14), new Point(c.X + i * 7 + 4, c.Y - 14), new Point(c.X + i * 7, c.Y - 24 - Math.Abs(i) * -1));
        dc.DrawEllipse(Brush(body), Pen(dark, 2), c, 19, 17);
        if (m.Digging)
        {
            // A digger's great mouth, teeth and all.
            var mouth = new Point(c.X + side * 6, c.Y + 5);
            dc.DrawEllipse(Brush(Black), null, mouth, 10, 7 + Math.Abs(Math.Sin(m.Age * 10)) * 3);
            for (int i = -1; i <= 1; i++) Poly(dc, White, null, 0, new Point(mouth.X + i * 5 - 2, mouth.Y - 6), new Point(mouth.X + i * 5 + 2, mouth.Y - 6), new Point(mouth.X + i * 5, mouth.Y - 1));
        }
        // Big eyes, looking where it goes.
        var (lx, ly) = m.Facing.Vec();
        foreach (double ex in new[] { -7.0, 7.0 })
        {
            var e = new Point(c.X + ex, c.Y - 4);
            dc.DrawEllipse(Brush(White), null, e, 6, 7);
            dc.DrawEllipse(Brush(Black), null, new Point(e.X + lx * 2.5, e.Y + ly * 2.5), 2.8, 3.4);
        }
        if (m.Kind == MonsterKind.Letter)
        {
            dc.DrawEllipse(Brush(0xFFFFF4C0), Pen(dark, 1.5), new Point(c.X, c.Y + 8), 9, 8);
            Text(dc, m.Letter.ToString(), new Point(c.X, c.Y + 8), 22, 0xFF1A6A2A, center: true, shadow: false);
        }
    }

    private static void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var p = S(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
                for (int i = 0; i < 10; i++)
                {
                    double a = i * Math.PI / 5, r0 = 6 + 34 * t, r1 = r0 + 9 * (1 - t);
                    dc.DrawLine(Pen((uint)(255 * (1 - t)) << 24 | (e.Colour & 0x00FFFFFF), 3), new Point(p.X + Math.Cos(a) * r0, p.Y + Math.Sin(a) * r0), new Point(p.X + Math.Cos(a) * r1, p.Y + Math.Sin(a) * r1));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(p.X, p.Y - 20 - t * 28), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(p.X, p.Y - t * 40), 56, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF120E1E), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFF4A3A70), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(180, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(180, 44), 28, Dim);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (!inGame) return;
        Text(dc, $"LEVEL {g.Level}", new Point(340, 16), 22, Dim);
        Text(dc, $"CHERRIES {g.CherriesLeft}", new Point(340, 44), 24, 0xFFFF6A7A);
        // The letters of EXTRA: lit as they're won.
        for (int i = 0; i < GameEngine.ExtraWord.Length; i++)
        {
            char ch = GameEngine.ExtraWord[i];
            bool won = g.Letters.Contains(ch);
            var box = new Rect(540 + i * 34, 18, 30, 34);
            dc.DrawRectangle(Brush(won ? 0xFF3AB04A : 0xFF241C38), Pen(won ? Green : 0xFF4A3A70, 2), box, 4, 4);
            Text(dc, ch.ToString(), box.Center, 28, won ? White : 0xFF5A4A80, center: true, shadow: false);
        }
        // The ball: ready, or charging.
        Text(dc, "BALL", new Point(730, 16), 22, Dim);
        if (g.Ball.State == BallState.Ready) Text(dc, "READY", new Point(730, 44), 26, Yellow);
        else
        {
            double f = g.Ball.State == BallState.Charging ? 1 - g.Ball.Timer / g.ChargeTime : 0;
            dc.DrawRectangle(null, Pen(0xFF6A5A90, 1.5), new Rect(730, 36, 90, 14));
            dc.FillRectangle(Brush(0xFFFFE14A), new Rect(731, 37, 88 * Math.Clamp(f, 0, 1), 12));
        }
        // Clowns in reserve.
        for (int i = 0; i < Math.Min(4, g.Lives - (g.Clown.Alive ? 1 : 0)); i++)
            DrawClown(dc, new Point(880 + i * 34, 36), Dir.Right, 0, 0.62, false);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB0000000), new Rect(cx - 330, 190, 660, g.SubMessage != null ? 110 : 70));
        Text(dc, msg, new Point(cx, 222), 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 272), 30, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC80A0812), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA MR. DO", new Point(cx, 140), 120, 0xFFFFE14A, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 208), 28, Dim, center: true);
        DrawClown(dc, new Point(cx - 345, 160), Dir.Right, _clock * 0.6, 1.8, true);
        DrawCherry(dc, new Point(cx + 330, 156), 2.2);
        string[] lines =
        [
            "DIG THROUGH THE GARDEN AND EAT EVERY CHERRY",
            "DROP APPLES ON THE MONSTERS - AND DON'T STAND UNDER THEM",
            "THROW THE POWER BALL: IT ROLLS DOWN THE TUNNELS TO A MONSTER",
            "WIN THE LETTERS OF EXTRA FOR ANOTHER CLOWN",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 278 + i * 34), 27, White, center: true);
        (string Label, string Points)[] table =
        [
            ("CHERRY", "50"), ("EIGHT IN A ROW", "500"), ("BALL HIT", "500"), ("APPLE DROP", "1000+"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double x = 160 + i * 235;
            Text(dc, table[i].Label, new Point(x, 448), 24, Dim, center: true);
            Text(dc, table[i].Points, new Point(x, 476), 28, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 560), 48, Yellow, center: true);
        Text(dc, "ARROWS OR W A S D: DIG AND WALK    SPACE / Z: THROW THE BALL", new Point(cx, 640), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD80A0812), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP CLOWNS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD80A0812), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

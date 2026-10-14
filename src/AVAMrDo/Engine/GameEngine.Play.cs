namespace AVAMrDo.Engine;

public sealed partial class GameEngine
{
    public const double ClownSpeed = 3.6, DigSpeed = 2.6, BallSpeed = 9;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, ClearSeconds = 3.2;
    public const double WobbleSeconds = 0.6, FallSpeed = 6.5, SpawnEvery = 3, LetterEvery = 22;
    /// <summary>Eat the cherries of a cluster this quickly one after another for the streak.</summary>
    public const double StreakGap = 0.55;
    public const int CherryPoints = 50, StreakBonus = 500, BallPoints = 500, ClusterSize = 8;
    public static readonly int[] CrushPoints = [1000, 2000, 4000, 6000, 8000];
    public const string ExtraWord = "EXTRA";
    public const int FirstExtraLife = 25000, ExtraLifeEvery = 50000;

    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Clown Clown { get; } = new();
    public Ball Ball { get; } = new();
    public List<Monster> Monsters { get; } = [];
    public List<Apple> Apples { get; } = [];
    /// <summary>Dug tiles, and the dug passages between neighbours (to the right of, and below, each tile).</summary>
    public bool[,] Dug { get; } = new bool[Field.Cols, Field.Rows];
    public bool[,] PassRight { get; } = new bool[Field.Cols, Field.Rows];
    public bool[,] PassDown { get; } = new bool[Field.Cols, Field.Rows];
    /// <summary>Which cluster a cherry belongs to (0 for none).</summary>
    public int[,] Cherries { get; } = new int[Field.Cols, Field.Rows];
    /// <summary>The letters of EXTRA won so far.</summary>
    public HashSet<char> Letters { get; } = [];
    public ClownPilot Pilot { get; } = new();
    /// <summary>What caught the last clown.</summary>
    public string Cause { get; private set; } = "";
    /// <summary>How many monsters the level sends, how many have come out, and how many are beaten.</summary>
    public int Quota { get; private set; }
    public int Released { get; private set; }
    public int Beaten { get; private set; }
    /// <summary>For tests: no monsters come out unless a test puts them there.</summary>
    internal bool Calm { get; set; }

    public double MonsterSpeed => Math.Min(4.2, 2.5 + 0.18 * (Level - 1));
    public int CherriesLeft { get { int n = 0; foreach (int c in Cherries) if (c > 0) n++; return n; } }

    private int _nextExtra, _throws, _streak, _streakCluster;
    private double _spawnIn, _letterIn, _sinceCherry;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Level = 1;
        _nextExtra = FirstExtraLife;
        Letters.Clear();
        LayOut(1);
        SetPhase(Phase.LevelStart);
    }

    private void BeginLevel(int level)
    {
        Level = level;
        LayOut(level);
        SetPhase(Phase.LevelStart);
        Say($"LEVEL {level}", level == 1 ? "EAT THE CHERRIES - MIND THE APPLES" : null, StartSeconds);
        Play(Sfx.LevelStart);
    }

    /// <summary>A fresh garden: the first tunnels, four clusters of cherries, and apples in the earth.</summary>
    private void LayOut(int level)
    {
        Array.Clear(Dug);
        Array.Clear(PassRight);
        Array.Clear(PassDown);
        Array.Clear(Cherries);
        Monsters.Clear();
        Apples.Clear();
        Effects.Clear();
        var rng = new Random(level * 31 + 7);
        // A tunnel across the middle through the nest, and one down from it to where the clown starts.
        for (int c = 3; c < 10; c++) DigLink(c, Field.NestRow, c + 1, Field.NestRow);
        for (int r = Field.NestRow; r < Field.StartRow; r++) DigLink(Field.NestCol, r, Field.NestCol, r + 1);
        // And a short one of the level's own.
        int tc = 1 + rng.Next(Field.Cols - 3), tr = 1 + rng.Next(Field.Rows - 3);
        bool across = rng.Next(2) == 0;
        for (int i = 0; i < 3; i++) DigLink(tc + (across ? i : 0), tr + (across ? 0 : i), tc + (across ? i + 1 : 0), tr + (across ? 0 : i + 1));

        // Four clusters of eight cherries, on earth that hasn't been dug.
        for (int cluster = 1; cluster <= 4; cluster++)
        {
            for (int tries = 0; tries < 2000; tries++)
            {
                bool wide = rng.Next(2) == 0;
                int w = wide ? 4 : 2, h = wide ? 2 : 4;
                int c0 = rng.Next(Field.Cols - w + 1), r0 = rng.Next(Field.Rows - h);
                bool ok = true;
                // On earth, and a tile clear of the other clusters.
                for (int c = c0 - 1; c <= c0 + w && ok; c++)
                    for (int r = r0 - 1; r <= r0 + h && ok; r++)
                    {
                        if (!Field.Inside(c, r)) continue;
                        bool inside = c >= c0 && c < c0 + w && r >= r0 && r < r0 + h;
                        if (Cherries[c, r] > 0 || (inside && (Dug[c, r] || (c == Field.NestCol && r == Field.NestRow)))) ok = false;
                    }
                if (!ok) continue;
                for (int c = c0; c < c0 + w; c++)
                    for (int r = r0; r < r0 + h; r++) Cherries[c, r] = cluster;
                break;
            }
        }
        // Apples sit in the earth, never right over a tunnel, so none falls at once.
        int apples = 5 + Math.Min(level, 3);
        for (int i = 0, tries = 0; i < apples && tries < 500; tries++)
        {
            int c = rng.Next(Field.Cols), r = rng.Next(Field.Rows - 2);
            if (Dug[c, r] || Cherries[c, r] > 0 || Dug[c, r + 1] || Apples.Any(a => a.Col == c && Math.Abs(a.Y - r) < 2)) continue;
            Apples.Add(new Apple { Col = c, Y = r });
            i++;
        }
        Quota = 6 + Math.Min(level, 4);
        Released = Beaten = 0;
        _throws = 0;
        _spawnIn = 1.5;
        _letterIn = LetterEvery;
        Reset();
    }

    /// <summary>The clown back at the start, the ball in hand, the monsters home.</summary>
    private void Reset()
    {
        Clown.X = Field.StartCol;
        Clown.Y = Field.StartRow;
        Clown.Moving = Dir.None;
        Clown.Facing = Dir.Right;
        Clown.Alive = true;
        Ball.State = BallState.Ready;
        _streak = 0;
        _streakCluster = 0;
        _sinceCherry = 99;
    }

    private void DigTile(int c, int r)
    {
        if (Field.Inside(c, r)) Dug[c, r] = true;
    }

    /// <summary>Digs two neighbouring tiles and the passage between them; true if any of it was earth.</summary>
    private bool DigLink(int c0, int r0, int c1, int r1)
    {
        if (!Field.Inside(c0, r0) || !Field.Inside(c1, r1)) return false;
        bool fresh = !Dug[c0, r0] || !Dug[c1, r1];
        DigTile(c0, r0);
        DigTile(c1, r1);
        ref bool pass = ref r0 == r1 ? ref PassRight[Math.Min(c0, c1), r0] : ref PassDown[c0, Math.Min(r0, r1)];
        fresh |= !pass;
        pass = true;
        return fresh;
    }

    /// <summary>Whether the tunnels lead from a tile one step the given way.</summary>
    public bool Open(int c, int r, Dir d)
    {
        var (dx, dy) = d.Vec();
        int c1 = c + dx, r1 = r + dy;
        if (!Field.Inside(c1, r1)) return false;
        return d switch
        {
            Dir.Right => PassRight[c, r], Dir.Left => PassRight[c1, r], Dir.Down => PassDown[c, r], _ => PassDown[c, r1],
        };
    }

    /// <summary>The apple sitting in (or falling through) a tile, if any.</summary>
    public Apple? AppleAt(int c, int r) =>
        Apples.FirstOrDefault(a => a.State != AppleState.Broken && a.Col == c && Math.Abs(a.Y - r) < 0.75);

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.LevelStart:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                _sinceCherry += dt;
                MoveClown(c, dt);
                Throw(c);
                Release(dt);
                MoveMonsters(dt);
                MoveBall(dt);
                MoveApples(dt);
                Touch();
                if (Phase == Phase.Play && !Calm && (CherriesLeft == 0 || (Released >= Quota && Beaten >= Quota)))
                    LevelDone(CherriesLeft == 0 ? "ALL THE CHERRIES!" : "ALL THE MONSTERS!");
                break;
            case Phase.Dying:
                MoveApples(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    // The monsters out on the field go home and come out again.
                    Released -= Monsters.Count(m => !m.Dead && m.Kind == MonsterKind.Creep);
                    Monsters.Clear();
                    _spawnIn = 2;
                    Reset();
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.LevelClear:
                if (PhaseTime > ClearSeconds) BeginLevel(Level + 1);
                break;
        }
        Monsters.RemoveAll(m => m.Dead);
        Apples.RemoveAll(a => a.State == AppleState.Broken && a.Timer > 0.8);
    }

    // ───────────────────────────── the clown ─────────────────────────────

    /// <summary>Whether the clown can go from its tile the given way: anywhere in the garden, but not into an apple.</summary>
    private bool ClownMay(int c, int r, Dir d)
    {
        var (dx, dy) = d.Vec();
        return Field.Inside(c + dx, r + dy) && AppleAt(c + dx, r + dy) == null;
    }

    private void MoveClown(Controls ctl, double dt)
    {
        var k = Clown;
        Dir want = Dirs.From(ctl.Dx, ctl.Dy);
        if (k.AtCentre)
        {
            k.X = Math.Round(k.X);
            k.Y = Math.Round(k.Y);
            k.Moving = want != Dir.None && ClownMay(k.Col, k.Row, want) ? want : Dir.None;
        }
        else if (want == Dir.None) return;
        else if (want == k.Moving) { }
        else if (want == k.Moving.Opposite()) { k.Moving = want; k.Digging = false; }
        else
        {
            // Asked to turn mid-tile: carry on to the nearer tile centre first.
            double frac = k.Moving.Horizontal() ? k.X - Math.Floor(k.X) : k.Y - Math.Floor(k.Y);
            var (mx, my) = k.Moving.Vec();
            bool forward = (mx + my > 0) == (frac > 0.5);
            if (!forward) { k.Moving = k.Moving.Opposite(); k.Digging = false; }
        }
        if (k.Moving == Dir.None) return;
        k.Facing = k.Moving;
        var (vx, vy) = k.Moving.Vec();
        // The tile it is heading into: into earth it digs, and slower.
        int toC = k.Col, toR = k.Row;
        if (vx != 0) toC = vx > 0 ? (int)Math.Floor(k.X + 1e-9) + 1 : (int)Math.Ceiling(k.X - 1e-9) - 1;
        else toR = vy > 0 ? (int)Math.Floor(k.Y + 1e-9) + 1 : (int)Math.Ceiling(k.Y - 1e-9) - 1;
        // Setting off from a tile centre into earth, it digs its way in, slower, until it gets there.
        if (k.AtCentre) k.Digging = Field.Inside(toC, toR) && !Dug[toC, toR];
        double step = (k.Digging ? DigSpeed : ClownSpeed) * dt;
        if (DigLink(toC - vx, toR - vy, toC, toR)) Play(Sfx.Dig);
        Advance(k, step);
        k.Walk += step;
        EatCherry();
    }

    /// <summary>Moves a mover along its way at most this far, stopping exactly on the next tile centre.</summary>
    private static bool Advance(Mover m, double dist)
    {
        var (dx, dy) = m.Moving.Vec();
        if (dx == 0 && dy == 0) return true;
        double pos = dx != 0 ? m.X : m.Y;
        int sign = dx + dy;
        double next = Math.Abs(pos - Math.Round(pos)) < 1e-6 ? Math.Round(pos) + sign : sign > 0 ? Math.Floor(pos) + 1 : Math.Ceiling(pos) - 1;
        double gap = Math.Abs(next - pos);
        double moved = Math.Min(dist, gap);
        if (dx != 0) m.X += sign * moved; else m.Y += sign * moved;
        if (moved >= gap - 1e-9)
        {
            if (dx != 0) m.X = next; else m.Y = next;
            return true;
        }
        return false;
    }

    private void EatCherry()
    {
        var k = Clown;
        int c = k.Col, r = k.Row;
        if (Math.Abs(k.X - c) > 0.35 || Math.Abs(k.Y - r) > 0.35 || Cherries[c, r] == 0) return;
        int cluster = Cherries[c, r];
        Cherries[c, r] = 0;
        // Eight from one cluster without a pause earns the streak bonus.
        _streak = cluster == _streakCluster && _sinceCherry < StreakGap ? _streak + 1 : 1;
        _streakCluster = cluster;
        _sinceCherry = 0;
        AddScore(CherryPoints, c, r);
        Play(Sfx.Cherry);
        if (_streak >= ClusterSize)
        {
            AddScore(StreakBonus, c, r - 0.6, 0xFFFFE14A);
            Play(Sfx.Streak);
            _streak = 0;
        }
    }

    // ───────────────────────────── the power ball ─────────────────────────────

    /// <summary>How long the ball takes to come back, longer each throw in a level.</summary>
    public double ChargeTime => Math.Min(7, 1.2 + 1.3 * _throws);

    private bool _firePrev;

    private void Throw(Controls c)
    {
        bool pressed = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (!pressed || Ball.State != BallState.Ready) return;
        Ball.State = BallState.Flying;
        Ball.X = Math.Round(Clown.X);
        Ball.Y = Math.Round(Clown.Y);
        Ball.Moving = Dir.None;
        Ball.Facing = Clown.Facing;
        Ball.Timer = 0;
        _throws++;
        Play(Sfx.Throw);
    }

    private void MoveBall(double dt)
    {
        var b = Ball;
        if (b.State == BallState.Charging)
        {
            b.Timer -= dt;
            if (b.Timer <= 0) b.State = BallState.Ready;
            return;
        }
        if (b.State != BallState.Flying) return;
        b.Timer += dt;
        double left = BallSpeed * dt;
        for (int guard = 0; guard < 4 && left > 1e-9 && b.State == BallState.Flying; guard++)
        {
            if (b.AtCentre)
            {
                b.X = Math.Round(b.X);
                b.Y = Math.Round(b.Y);
                // Along the tunnels towards the nearest monster it can reach; with none, wherever the tunnel goes.
                var step = StepTowards(b.Col, b.Row, (c, r) => Monsters.Any(m => !m.Dead && m.Col == c && m.Row == r), true);
                if (step == Dir.None)
                {
                    var ways = Dirs.All.Where(d => Open(b.Col, b.Row, d) && AppleAt(b.Col + d.Vec().X, b.Row + d.Vec().Y) == null).ToList();
                    step = ways.Contains(b.Facing) ? b.Facing : ways.Count > 0 ? ways.FirstOrDefault(d => d != b.Facing.Opposite(), ways[0]) : Dir.None;
                }
                if (step == Dir.None || b.Timer > 3) { Spent(); return; }
                b.Moving = b.Facing = step;
            }
            double before = b.Moving.Horizontal() ? b.X : b.Y;
            Advance(b, left);
            left -= Math.Abs((b.Moving.Horizontal() ? b.X : b.Y) - before);
            foreach (var m in Monsters)
            {
                if (m.Dead || Math.Abs(m.X - b.X) > 0.55 || Math.Abs(m.Y - b.Y) > 0.55) continue;
                Beat(m, BallPoints);
                Play(Sfx.Ball);
                Spent();
                return;
            }
        }
    }

    private void Spent()
    {
        Ball.State = BallState.Charging;
        Ball.Timer = ChargeTime;
    }

    /// <summary>
    /// The first step along the tunnels from a tile towards the nearest tile that passes the test, or none
    /// if there is no way. Apples block the way.
    /// </summary>
    public Dir StepTowards(int c0, int r0, Func<int, int, bool> goal, bool throughTunnels)
    {
        var first = new Dir[Field.Cols, Field.Rows];
        var seen = new bool[Field.Cols, Field.Rows];
        var queue = new Queue<(int C, int R)>();
        seen[c0, r0] = true;
        queue.Enqueue((c0, r0));
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            if ((c != c0 || r != r0) && goal(c, r)) return first[c, r];
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = d.Vec();
                int nc = c + dx, nr = r + dy;
                if (!Field.Inside(nc, nr) || seen[nc, nr]) continue;
                if (throughTunnels && !Open(c, r, d)) continue;
                if (AppleAt(nc, nr) != null) continue;
                seen[nc, nr] = true;
                first[nc, nr] = c == c0 && r == r0 ? d : first[c, r];
                queue.Enqueue((nc, nr));
            }
        }
        return Dir.None;
    }

    // ───────────────────────────── monsters ─────────────────────────────

    private void Release(double dt)
    {
        if (Calm) return;
        _spawnIn -= dt;
        if (_spawnIn <= 0 && Released < Quota)
        {
            _spawnIn = Math.Max(1.5, SpawnEvery - 0.15 * Level);
            Released++;
            Monsters.Add(new Monster { Kind = MonsterKind.Creep, X = Field.NestCol, Y = Field.NestRow, Moving = Dir.None });
            Play(Sfx.Hatch);
        }
        // Now and then a letter monster comes out carrying the next letter of EXTRA still to win.
        _letterIn -= dt;
        if (_letterIn <= 0 && Letters.Count < ExtraWord.Length && Monsters.All(m => m.Kind != MonsterKind.Letter))
        {
            _letterIn = LetterEvery;
            char letter = ExtraWord.First(ch => !Letters.Contains(ch));
            Monsters.Add(new Monster { Kind = MonsterKind.Letter, Letter = letter, X = Field.NestCol, Y = Field.NestRow });
            Play(Sfx.Letter);
        }
    }

    private void MoveMonsters(double dt)
    {
        var k = Clown;
        foreach (var m in Monsters)
        {
            if (m.Dead) continue;
            m.Age += dt;
            double speed = MonsterSpeed * (m.Kind == MonsterKind.Letter ? 1.1 : 1) * (m.Digging ? 0.75 : 1);
            if (m.Digging)
            {
                m.DigTime -= dt;
                if (m.DigTime <= 0 && m.AtCentre) m.Digging = false;
            }
            // Time spent with no way through the tunnels to the clown.
            m.Stuck = m.NoPath ? m.Stuck + dt : 0;
            double left = speed * dt;
            for (int guard = 0; guard < 3 && left > 1e-9; guard++)
            {
                if (m.AtCentre)
                {
                    m.X = Math.Round(m.X);
                    m.Y = Math.Round(m.Y);
                    m.Moving = Choose(m);
                    if (m.Moving == Dir.None) break;
                    m.Facing = m.Moving;
                    if (m.Digging)
                    {
                        var (dx, dy) = m.Moving.Vec();
                        DigLink(m.Col, m.Row, m.Col + dx, m.Row + dy);
                    }
                }
                double before = m.Moving.Horizontal() ? m.X : m.Y;
                Advance(m, left);
                left -= Math.Abs((m.Moving.Horizontal() ? m.X : m.Y) - before);
            }
        }
    }

    /// <summary>Which way a monster goes from a tile centre.</summary>
    private Dir Choose(Monster m)
    {
        int kc = Clown.Col, kr = Clown.Row;
        bool Free(Dir d) { var (dx, dy) = d.Vec(); return Field.Inside(m.Col + dx, m.Row + dy) && AppleAt(m.Col + dx, m.Row + dy) == null; }
        if (m.Digging)
        {
            // Straight through the earth towards the clown, the longer way first.
            int dx = Math.Sign(kc - m.Col), dy = Math.Sign(kr - m.Row);
            var first = Math.Abs(kc - m.Col) >= Math.Abs(kr - m.Row) ? Dirs.From(dx, 0) : Dirs.From(0, dy);
            var second = first.Horizontal() ? Dirs.From(0, dy) : Dirs.From(dx, 0);
            foreach (var d in new[] { first, second })
                if (d != Dir.None && Free(d)) return d;
            m.Digging = false;
        }
        var toward = StepTowards(m.Col, m.Row, (c, r) => c == kc && r == kr, true);
        m.NoPath = toward == Dir.None;
        // A creep that has had no way through for a few seconds starts to dig.
        if (m.NoPath && m.Kind == MonsterKind.Creep && m.Stuck > 3)
        {
            m.Stuck = 0;
            m.Digging = true;
            m.DigTime = 5;
            return Choose(m);
        }
        var ways = Dirs.All.Where(d => Open(m.Col, m.Row, d) && Free(d)).ToList();
        if (ways.Count == 0) return Dir.None;
        if (toward == Dir.None)
        {
            var wander = ways.Where(d => d != m.Moving.Opposite()).ToList();
            if (wander.Count == 0) wander = ways;
            return wander[_rng.Next(wander.Count)];
        }
        // Mostly the shortest way; now and then, in the early levels, a wrong turn.
        if (_rng.NextDouble() < Math.Max(0.05, 0.22 - 0.03 * Level))
        {
            var other = ways.Where(d => d != m.Moving.Opposite()).ToList();
            if (other.Count > 0) return other[_rng.Next(other.Count)];
        }
        return toward;
    }

    private void Beat(Monster m, int points)
    {
        m.Dead = true;
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = m.X, Y = m.Y, Colour = m.Kind == MonsterKind.Letter ? 0xFF72FF8F : 0xFFFF5A4A, Life = 0.6 });
        if (m.Kind == MonsterKind.Creep) Beaten++;
        AddScore(points, m.X, m.Y);
        if (m.Kind != MonsterKind.Letter) return;
        Letters.Add(m.Letter);
        Effects.Add(new Effect { Kind = EffectKind.Word, X = m.X, Y = m.Y - 0.8, Text = m.Letter.ToString(), Colour = 0xFF72FF8F, Life = 1.2 });
        if (Letters.Count == ExtraWord.Length)
        {
            Lives++;
            Letters.Clear();
            Play(Sfx.Extra);
            LevelDone("EXTRA! - ANOTHER CLOWN");
        }
    }

    // ───────────────────────────── apples ─────────────────────────────

    private void MoveApples(double dt)
    {
        foreach (var a in Apples)
        {
            switch (a.State)
            {
                case AppleState.Resting:
                {
                    // Dug out from under: it wobbles, then drops. Not while the clown is right under it still digging.
                    int below = (int)Math.Round(a.Y) + 1;
                    if (below < Field.Rows && Dug[a.Col, below] && AppleAt(a.Col, below) == null)
                    {
                        a.State = AppleState.Wobbling;
                        a.Timer = WobbleSeconds;
                        Play(Sfx.Wobble);
                    }
                    break;
                }
                case AppleState.Wobbling:
                    a.Timer -= dt;
                    if (a.Timer <= 0)
                    {
                        a.State = AppleState.Falling;
                        a.FallFrom = a.Y;
                        a.Crushed = 0;
                        Play(Sfx.Fall);
                    }
                    break;
                case AppleState.Falling:
                {
                    a.Y += FallSpeed * dt;
                    int row = (int)Math.Floor(a.Y);
                    // What it lands on or passes through.
                    foreach (var m in Monsters)
                        if (!m.Dead && Math.Abs(m.X - a.Col) < 0.6 && m.Y > a.Y - 0.3 && m.Y < a.Y + 0.9)
                        {
                            Beat(m, CrushPoints[Math.Min(a.Crushed, CrushPoints.Length - 1)]);
                            a.Crushed++;
                            Play(Sfx.Crush);
                        }
                    if (Clown.Alive && Phase == Phase.Play && Math.Abs(Clown.X - a.Col) < 0.6 && Clown.Y > a.Y - 0.3 && Clown.Y < a.Y + 0.8)
                        Caught("CRUSHED BY AN APPLE");
                    bool floor = row + 1 >= Field.Rows || !Dug[a.Col, row + 1] || Apples.Any(o => o != a && o.State != AppleState.Broken && o.Col == a.Col && Math.Abs(o.Y - (row + 1)) < 0.5);
                    if (floor && a.Y >= row)
                    {
                        a.Y = row;
                        // A drop of more than a tile breaks it.
                        if (a.Y - a.FallFrom > 1.2)
                        {
                            a.State = AppleState.Broken;
                            a.Timer = 0;
                            Play(Sfx.Break);
                        }
                        else a.State = AppleState.Resting;
                    }
                    break;
                }
                case AppleState.Broken:
                    a.Timer += dt;
                    break;
            }
        }
    }

    // ───────────────────────────── contact, scoring, the end of a level ─────────────────────────────

    private void Touch()
    {
        if (!Clown.Alive) return;
        foreach (var m in Monsters)
        {
            if (m.Dead || Math.Abs(m.X - Clown.X) > 0.6 || Math.Abs(m.Y - Clown.Y) > 0.6) continue;
            Caught(m.Kind == MonsterKind.Letter ? "CAUGHT BY A LETTER MONSTER" : m.Digging ? "CAUGHT BY A DIGGER" : "CAUGHT BY A CREEP");
            return;
        }
    }

    private void Caught(string cause)
    {
        if (!Clown.Alive || Phase != Phase.Play) return;
        Cause = cause;
        Lives--;
        Clown.Alive = false;
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = Clown.X, Y = Clown.Y, Colour = 0xFFFFFFFF, Life = 1 });
        Play(Sfx.Death);
        Music(false);
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "CLOWN" : "CLOWNS")} LEFT" : null, DyingSeconds);
    }

    private void LevelDone(string why)
    {
        int bonus = 1000 * Level;
        AddScore(bonus, Field.NestCol, Field.NestRow - 1, 0xFFFFE14A);
        Say(why, $"LEVEL {Level} BONUS {bonus}", ClearSeconds);
        Play(Sfx.LevelClear);
        Music(false);
        SetPhase(Phase.LevelClear);
    }

    private void AddScore(int points, double x, double y, uint colour = 0xFFF2F2F2)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = colour });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a level.</summary>
    internal void Go(int level = 1)
    {
        StartGame();
        BeginLevel(level);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>An empty garden: no tunnels, cherries, apples or monsters but the clown at the start.</summary>
    internal void Bare()
    {
        Array.Clear(Dug);
        Array.Clear(PassRight);
        Array.Clear(PassDown);
        Array.Clear(Cherries);
        Monsters.Clear();
        Apples.Clear();
        DigTile(Field.StartCol, Field.StartRow);
        Reset();
    }

    internal void Tunnel(int c0, int r0, int c1, int r1)
    {
        int dc = Math.Sign(c1 - c0), dr = Math.Sign(r1 - r0);
        for (int c = c0, r = r0; c != c1 || r != r1; c += dc, r += dr) DigLink(c, r, c + dc, r + dr);
        DigTile(c0, r0);
    }

    internal Monster PutMonster(int c, int r, MonsterKind kind = MonsterKind.Creep, char letter = 'E')
    {
        var m = new Monster { Kind = kind, Letter = letter, X = c, Y = r };
        Monsters.Add(m);
        if (kind == MonsterKind.Creep) Released++;
        return m;
    }

    internal Apple PutApple(int c, int r)
    {
        var a = new Apple { Col = c, Y = r };
        Apples.Add(a);
        return a;
    }

    internal void PutCherry(int c, int r, int cluster = 1) => Cherries[c, r] = cluster;

    internal void PutClown(int c, int r)
    {
        Clown.X = c;
        Clown.Y = r;
        DigTile(c, r);
    }

    internal void SetQuota(int quota) => Quota = quota;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
}

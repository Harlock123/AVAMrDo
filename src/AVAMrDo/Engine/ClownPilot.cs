namespace AVAMrDo.Engine;

/// <summary>
/// The computer at the stick, for the demonstration (and the tests). It throws the ball when a monster can
/// be reached through the tunnels, runs from one that gets close while the ball is away, keeps out from
/// under apples, and otherwise digs its way to the nearest cherry.
/// </summary>
public sealed class ClownPilot
{
    /// <summary>What it is doing, for the tests and for tuning.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var k = g.Clown;
        if (!k.Alive || g.Phase != Phase.Play) return default;
        int c = k.Col, r = k.Row;

        // The ball, at any monster the tunnels lead to.
        bool fire = g.Ball.State == BallState.Ready &&
                    g.StepTowards(c, r, (mc, mr) => g.Monsters.Any(m => !m.Dead && m.Col == mc && m.Row == mr), true) != Dir.None;

        // Too close for comfort and no ball: away from it, by whichever way puts the most room between.
        double near = g.Monsters.Where(m => !m.Dead).Select(m => Math.Abs(m.X - k.X) + Math.Abs(m.Y - k.Y)).DefaultIfEmpty(99).Min();
        if (near < 2.2 && g.Ball.State != BallState.Ready)
        {
            Doing = "flee";
            var best = Dir.None;
            double room = near;
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = d.Vec();
                int nc = c + dx, nr = r + dy;
                if (!Field.Inside(nc, nr) || g.AppleAt(nc, nr) != null || Under(g, nc, nr)) continue;
                double gap = g.Monsters.Where(m => !m.Dead).Min(m => Math.Abs(m.X - nc) + Math.Abs(m.Y - nr));
                if (gap > room) { room = gap; best = d; }
            }
            var (bx, by) = best.Vec();
            return new Controls(bx, by, fire);
        }

        // The nearest cherry, digging where need be; the way is weighed so tunnels are cheaper than earth
        // and the tiles under apples are best avoided.
        Doing = "cherries";
        var step = Cheapest(g, c, r);
        var (sx, sy) = step.Vec();
        return new Controls(sx, sy, fire);
    }

    /// <summary>Whether a tile is right under an apple, where digging brings it down.</summary>
    private static bool Under(GameEngine g, int c, int r) =>
        r > 0 && g.Apples.Any(a => a.State != AppleState.Broken && a.Col == c && a.Y < r && a.Y > r - 2.2);

    private static Dir Cheapest(GameEngine g, int c0, int r0)
    {
        var cost = new double[Field.Cols, Field.Rows];
        var first = new Dir[Field.Cols, Field.Rows];
        for (int c = 0; c < Field.Cols; c++) for (int r = 0; r < Field.Rows; r++) cost[c, r] = double.MaxValue;
        cost[c0, r0] = 0;
        var open = new PriorityQueue<(int C, int R), double>();
        open.Enqueue((c0, r0), 0);
        while (open.TryDequeue(out var at, out double sofar))
        {
            var (c, r) = at;
            if (sofar > cost[c, r]) continue;
            if ((c != c0 || r != r0) && g.Cherries[c, r] > 0) return first[c, r];
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = d.Vec();
                int nc = c + dx, nr = r + dy;
                if (!Field.Inside(nc, nr) || g.AppleAt(nc, nr) != null) continue;
                double step = g.Open(c, r, d) ? 1 : 2.2;
                if (Under(g, nc, nr)) step += 25;
                // Steer clear of monsters too.
                if (g.Monsters.Any(m => !m.Dead && Math.Abs(m.X - nc) + Math.Abs(m.Y - nr) < 1.5)) step += 15;
                double total = sofar + step;
                if (total >= cost[nc, nr]) continue;
                cost[nc, nr] = total;
                first[nc, nr] = c == c0 && r == r0 ? d : first[c, r];
                open.Enqueue((nc, nr), total);
            }
        }
        return Dir.None;
    }
}

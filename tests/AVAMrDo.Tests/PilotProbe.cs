using AVAMrDo.Engine;

namespace AVAMrDo.Tests;

/// <summary>How the computer clown gets on, level by level (set MRDO_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("MRDO_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "mrdo-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var last = g.Phase;
            int level = 1, t = 0;
            double start = 0;
            string why = "";
            for (; t < 60 * 600 && g.Level <= 5; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"L{g.Level} {g.Cause}"] = deaths.GetValueOrDefault($"L{g.Level} {g.Cause}") + 1;
                if (g.Phase == Phase.LevelClear && last != Phase.LevelClear) why = g.Message ?? "";
                if (g.Level != level) { levels.Add($"L{level}:{(t - start) / 60:0}s({why})"); level = g.Level; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: level {g.Level} after {t / 60:0}s score {g.Score} cherries left {g.CherriesLeft} | {string.Join(" ", levels)}");
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}

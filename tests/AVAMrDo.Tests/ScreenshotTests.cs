using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMrDo.Engine;
using AVAMrDo.Views;

namespace AVAMrDo.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MRDO_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamrdo-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MRDO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMrDo-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer clown play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 61250, 6);
        g.HighScores.Insert("AVA", 52800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Digging_through_the_garden()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(30);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "level-start");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Monsters.Count >= 3 && g.CherriesLeft <= 26 && g.Clown.Alive));
        Shot(w, c, "digging");
        Assert.True(PlayUntil(g, c, g => g.Ball.State == BallState.Flying && g.Ball.Timer > 0.1 && g.Monsters.Count >= 2));
        Shot(w, c, "power-ball");
        // The computer clown keeps out from under apples, so dig one out for the picture.
        var apple = g.Apples.First(a => a.State == AppleState.Resting && a.Y < Field.Rows - 4 && Math.Abs(a.Col - g.Clown.X) > 2);
        g.Tunnel(apple.Col, (int)apple.Y + 1, apple.Col, (int)apple.Y + 3);
        Assert.True(PlayUntil(g, c, g => apple.State == AppleState.Falling && apple.Y - apple.FallFrom > 0.6));
        Shot(w, c, "apple-drop");
        Assert.True(PlayUntil(g, c, g => g.Monsters.Any(m => m.Kind == MonsterKind.Letter && Math.Abs(m.X - g.Clown.X) + Math.Abs(m.Y - g.Clown.Y) < 6) && g.Letters.Count >= 1));
        Shot(w, c, "letter-monster");
        // A creep in a pocket with no way through: give it a few seconds and it digs.
        // (Losing a clown sends the monsters home, so try again if that happens first.)
        bool digging = false;
        for (int attempt = 0; attempt < 6 && !digging; attempt++)
        {
            PlayUntil(g, c, g => g.Phase == Phase.Play && g.Clown.Alive);
            int pc = g.Clown.X < Field.Cols / 2 ? Field.Cols - 3 : 0;
            g.Tunnel(pc, 1, pc + 2, 1);
            var creep = g.PutMonster(pc, 1);
            digging = PlayUntil(g, c, g => (creep.Digging && creep.Age > 4.2 && g.Clown.Alive) || creep.Dead || !g.Monsters.Contains(creep), 30) && creep.Digging && !creep.Dead;
        }
        Assert.True(digging);
        Shot(w, c, "digger");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.LevelClear && g.PhaseTime > 0.4));
        Shot(w, c, "level-clear");
        Assert.True(PlayUntil(g, c, g => g.Level >= 3 && g.Phase == Phase.Play && g.CherriesLeft <= 22 && g.Monsters.Count >= 3, 400));
        Shot(w, c, "level-3");
    }

    [AvaloniaFact]
    public void Crushed()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.Bare();
        g.PutApple(Field.StartCol, Field.StartRow - 4);
        g.Tunnel(Field.StartCol, Field.StartRow - 3, Field.StartCol, Field.StartRow);
        g.Tunnel(Field.StartCol, Field.StartRow, 2, Field.StartRow);
        for (int t = 0; t < 60 * 5 && g.Phase != Phase.Dying; t++) { g.Idle(1); c.Advance(1 / 60.0); }
        g.Idle(6);
        Shot(w, c, "crushed");
    }
}

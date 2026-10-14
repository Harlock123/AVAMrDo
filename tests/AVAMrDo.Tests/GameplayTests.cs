using AVAMrDo.Engine;
using Engine = AVAMrDo.Engine;

namespace AVAMrDo.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamrdo-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play on a bare garden: no tunnels but the clown's tile, no monsters unless a test puts them there.</summary>
    private static GameEngine Game(int level = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(level);
        g.Bare();
        return g;
    }

    private static Engine.Controls Go(int dx, int dy, bool fire = false) => new(dx, dy, fire);
    private static readonly Engine.Controls Left = new(-1, 0), Right = new(1, 0), Up = new(0, -1);

    // ───────────────────────────── digging ─────────────────────────────

    [Fact]
    public void The_clown_digs_a_tunnel_through_the_earth_more_slowly_than_he_walks_one()
    {
        var g = Game();
        Assert.False(g.Dug[5, 9]);
        g.Run(30, Left);
        Assert.True(g.Dug[5, 9]);
        Assert.True(g.PassRight[5, 9]);
        double dug = Field.StartCol - g.Clown.X;
        Assert.Equal(GameEngine.DigSpeed / 2, dug, 1);
        // Back along the tunnel just dug is quicker.
        g.Run(30, Right);
        g.Run(60, Left);
        g.PutClown(Field.StartCol, Field.StartRow);
        g.Run(15, Left);
        Assert.Equal(GameEngine.ClownSpeed / 4, Field.StartCol - g.Clown.X, 1);
    }

    [Fact]
    public void Let_go_and_he_stops_and_asked_to_turn_mid_tile_he_goes_on_to_the_nearest_centre_first()
    {
        var g = Game();
        g.Run(10, Left);
        double x = g.Clown.X;
        g.Idle(30);
        Assert.Equal(x, g.Clown.X, 6);
        g.Run(60, Up);
        Assert.Equal(Math.Round(x), g.Clown.X, 6);
        Assert.True(g.Clown.Y < Field.StartRow);
    }

    [Fact]
    public void Tunnels_side_by_side_are_walled_apart_until_dug_through()
    {
        var g = Game();
        g.Tunnel(2, 3, 8, 3);
        g.Tunnel(2, 4, 8, 4);
        Assert.False(g.Open(5, 3, Dir.Down));
        Assert.True(g.Open(5, 3, Dir.Right));
    }

    [Fact]
    public void An_apple_is_in_the_way()
    {
        var g = Game();
        g.PutApple(Field.StartCol - 1, Field.StartRow);
        g.Run(60, Left);
        Assert.Equal(Field.StartCol, g.Clown.X, 6);
    }

    // ───────────────────────────── cherries ─────────────────────────────

    [Fact]
    public void Eight_cherries_eaten_without_a_pause_earn_the_streak_bonus()
    {
        var g = Game();
        g.PutClown(1, 9);
        g.Tunnel(1, 9, 11, 9);
        for (int c = 2; c <= 9; c++) g.PutCherry(c, 9);
        g.Run(200, Right);
        Assert.Equal(0, g.CherriesLeft);
        Assert.Equal(8 * GameEngine.CherryPoints + GameEngine.StreakBonus, g.Score);
    }

    [Fact]
    public void A_pause_in_the_middle_breaks_the_streak()
    {
        var g = Game();
        g.PutClown(1, 9);
        g.Tunnel(1, 9, 11, 9);
        for (int c = 2; c <= 9; c++) g.PutCherry(c, 9);
        for (int t = 0; t < 400 && g.CherriesLeft > 0; t++)
        {
            // Stop a moment after the fourth.
            if (g.CherriesLeft == 4 && t < 400) { g.Idle(60); g.Run(1, Right); }
            g.Run(1, Right);
        }
        Assert.Equal(0, g.CherriesLeft);
        Assert.Equal(8 * GameEngine.CherryPoints, g.Score);
    }

    // ───────────────────────────── apples ─────────────────────────────

    [Fact]
    public void Dig_under_an_apple_and_it_wobbles_then_drops_crushing_what_is_below()
    {
        var g = Game();
        g.Tunnel(3, 5, 3, 9);
        var apple = g.PutApple(3, 4);
        var m1 = g.PutMonster(3, 7);
        var m2 = g.PutMonster(3, 8);
        g.Idle(1);
        Assert.Equal(AppleState.Wobbling, apple.State);
        g.Idle((int)(60 * GameEngine.WobbleSeconds) + 60);
        Assert.True(m1.Dead || !g.Monsters.Contains(m1));
        Assert.True(m2.Dead || !g.Monsters.Contains(m2));
        Assert.Equal(GameEngine.CrushPoints[0] + GameEngine.CrushPoints[1], g.Score);
        // A long drop breaks it.
        Assert.Equal(AppleState.Broken, apple.State);
    }

    [Fact]
    public void An_apple_that_drops_just_one_tile_stays_whole()
    {
        var g = Game();
        g.Tunnel(3, 5, 3, 5);
        var apple = g.PutApple(3, 4);
        g.Idle((int)(60 * GameEngine.WobbleSeconds) + 60);
        Assert.Equal(AppleState.Resting, apple.State);
        Assert.Equal(5, apple.Y, 6);
    }

    [Fact]
    public void An_apple_dropped_on_the_clown_crushes_him()
    {
        var g = Game();
        g.PutApple(Field.StartCol, Field.StartRow - 3);
        g.Tunnel(Field.StartCol, Field.StartRow - 2, Field.StartCol, Field.StartRow);
        g.Idle(120);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CRUSHED BY AN APPLE", g.Cause);
    }

    // ───────────────────────────── monsters and the ball ─────────────────────────────

    [Fact]
    public void A_creep_follows_the_tunnels_to_the_clown()
    {
        var g = Game();
        g.Tunnel(1, 9, Field.StartCol, 9);
        g.Tunnel(1, 3, 1, 9);
        g.PutMonster(1, 3);
        for (int t = 0; t < 60 * 10 && g.Phase == Phase.Play; t++) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CAUGHT BY A CREEP", g.Cause);
    }

    [Fact]
    public void A_creep_with_no_way_through_starts_to_dig()
    {
        var g = Game();
        g.Tunnel(1, 2, 3, 2);
        var m = g.PutMonster(1, 2);
        bool dug = false;
        for (int t = 0; t < 60 * 12 && g.Phase == Phase.Play; t++)
        {
            g.Idle(1);
            dug |= m.Digging;
        }
        Assert.True(dug);
        Assert.Equal(Phase.Dying, g.Phase);
    }

    [Fact]
    public void The_ball_rolls_down_the_tunnels_to_a_monster_and_takes_longer_to_come_back_each_time()
    {
        var g = Game();
        g.Tunnel(Field.StartCol, 9, 1, 9);
        g.Tunnel(1, 9, 1, 4);
        var m = g.PutMonster(1, 4);
        // Keep the monster where it is: wall it in behind an apple.
        g.Monsters.Clear();
        g.Monsters.Add(m);
        double first = g.ChargeTime;
        g.Run(1, Go(0, 0, fire: true));
        Assert.Equal(BallState.Flying, g.Ball.State);
        for (int t = 0; t < 120 && !m.Dead; t++) g.Idle(1);
        Assert.True(m.Dead || !g.Monsters.Contains(m));
        Assert.Equal(GameEngine.BallPoints, g.Score);
        Assert.Equal(BallState.Charging, g.Ball.State);
        Assert.True(g.ChargeTime > first);
        g.Idle((int)(60 * g.ChargeTime) + 2);
        Assert.Equal(BallState.Ready, g.Ball.State);
    }

    [Fact]
    public void Letter_monsters_give_their_letters_and_EXTRA_brings_another_clown_and_the_next_level()
    {
        var g = Game();
        g.Tunnel(Field.StartCol, 9, 1, 9);
        int lives = g.Lives;
        foreach (char ch in GameEngine.ExtraWord)
        {
            var m = g.PutMonster(2, 9, MonsterKind.Letter, ch);
            g.Run(1, Go(0, 0, fire: true));
            for (int t = 0; t < 120 && !m.Dead; t++) g.Idle(1);
            if (g.Phase != Phase.Play) break;
            g.Idle((int)(60 * g.ChargeTime) + 2);
        }
        Assert.Equal(Phase.LevelClear, g.Phase);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── levels and clowns ─────────────────────────────

    [Fact]
    public void Eating_every_cherry_clears_the_level_with_a_bonus()
    {
        var g = Game(2);
        g.Calm = false;
        g.PutCherry(Field.StartCol - 1, Field.StartRow);
        g.Run(30, Left);
        Assert.Equal(Phase.LevelClear, g.Phase);
        Assert.Equal(GameEngine.CherryPoints + 2000, g.Score);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 2);
        Assert.Equal(3, g.Level);
        Assert.Equal(32, g.CherriesLeft);
    }

    [Fact]
    public void Beating_every_monster_clears_the_level_too()
    {
        var g = Game();
        g.Calm = false;
        g.PutCherry(0, 0);
        g.SetQuota(1);
        g.Tunnel(Field.StartCol, 9, 2, 9);
        g.PutMonster(2, 9);
        g.Run(1, Go(0, 0, fire: true));
        g.Idle(60);
        Assert.Equal(Phase.LevelClear, g.Phase);
    }

    [Fact]
    public void A_lost_clown_starts_again_with_the_monsters_back_in_the_nest_and_the_last_ends_the_game()
    {
        var g = Game();
        g.Tunnel(Field.StartCol, 9, 4, 9);
        g.PutMonster(4, 9);
        g.Idle(60);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Empty(g.Monsters);
        Assert.Equal((double)Field.StartCol, g.Clown.X);
        g.SetLives(1);
        g.PutMonster(Field.StartCol - 1, 9);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 60);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Each_level_lays_out_four_clusters_of_eight_cherries_and_apples_that_dont_fall_at_once()
    {
        foreach (int level in new[] { 1, 2, 3, 7 })
        {
            var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
            g.Go(level);
            Assert.Equal(32, g.CherriesLeft);
            for (int k = 1; k <= 4; k++)
            {
                int n = 0;
                foreach (int c in g.Cherries) if (c == k) n++;
                Assert.Equal(8, n);
            }
            Assert.True(g.Apples.Count >= 6);
            Assert.All(g.Apples, a => Assert.False(g.Dug[a.Col, (int)a.Y + 1]));
            g.Idle(30);
            Assert.All(g.Apples, a => Assert.Equal(AppleState.Resting, a.State));
            Assert.True(g.Dug[Field.NestCol, Field.NestRow]);
            Assert.True(g.StepTowards(Field.StartCol, Field.StartRow, (c, r) => c == Field.NestCol && r == Field.NestRow, true) != Dir.None);
        }
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_clown_clears_the_first_level()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 150 && g.Level == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.Level);
        Assert.True(g.Score > 3000);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Clown.X:0.000} {g.Clown.Y:0.000} {g.Level} {g.Lives} {g.Monsters.Count} {g.CherriesLeft}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.Bare();
        g.SetScore(99999);
        g.SetLives(1);
        g.Tunnel(Field.StartCol, 9, 4, 9);
        g.PutMonster(4, 9);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "DOO") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("DOO", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}

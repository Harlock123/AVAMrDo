namespace AVAMrDo.Engine;

/// <summary>The stick (one of four ways, or none) and the button that throws the power ball.</summary>
public readonly record struct Controls(int Dx, int Dy, bool Fire = false);

/// <summary>
/// The garden: a grid of earth, <see cref="Cols"/> by <see cref="Rows"/> tiles. Positions are in tiles, a
/// tile's centre at whole numbers, (0, 0) the top left.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the score bar.</summary>
    public const double Top = 64;
    public const int Cols = 12, Rows = 10;
    /// <summary>A tile's size on screen, and where the garden sits.</summary>
    public const double Tile = 62, Left = (Width - Cols * Tile) / 2, PlayTop = Top + 46;
    /// <summary>Where the monsters' nest is, and where the clown starts.</summary>
    public const int NestCol = 6, NestRow = 4, StartCol = 6, StartRow = 9;

    public static bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;
}

public enum Dir { None, Up, Down, Left, Right }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Down, Dir.Left, Dir.Right];

    public static (int X, int Y) Vec(this Dir d) => d switch
    {
        Dir.Up => (0, -1), Dir.Down => (0, 1), Dir.Left => (-1, 0), Dir.Right => (1, 0), _ => (0, 0),
    };

    public static Dir Opposite(this Dir d) => d switch
    {
        Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None,
    };

    public static bool Horizontal(this Dir d) => d is Dir.Left or Dir.Right;

    public static Dir From(int dx, int dy) =>
        dx < 0 ? Dir.Left : dx > 0 ? Dir.Right : dy < 0 ? Dir.Up : dy > 0 ? Dir.Down : Dir.None;
}

/// <summary>Anything that walks the grid from tile centre to tile centre.</summary>
public class Mover
{
    public double X, Y;
    public Dir Moving, Facing = Dir.Right;

    public int Col => (int)Math.Round(X);
    public int Row => (int)Math.Round(Y);
    public bool AtCentre => Math.Abs(X - Math.Round(X)) < 1e-6 && Math.Abs(Y - Math.Round(Y)) < 1e-6;
}

public sealed class Clown : Mover
{
    public bool Alive = true;
    /// <summary>Whether the tile it is heading into was earth when it set off: it digs its way in, slower.</summary>
    public bool Digging;
    /// <summary>How far its feet have walked, for the step.</summary>
    public double Walk;
}

public enum MonsterKind { Creep, Letter }

public sealed class Monster : Mover
{
    public MonsterKind Kind;
    /// <summary>The letter of EXTRA a letter monster carries.</summary>
    public char Letter;
    /// <summary>A creep that couldn't reach the clown through the tunnels digs for a while.</summary>
    public bool Digging;
    public double DigTime, Stuck, Age;
    /// <summary>No way through the tunnels to the clown, at its last turn.</summary>
    public bool NoPath;
    public bool Dead;
}

public enum AppleState { Resting, Wobbling, Falling, Broken }

public sealed class Apple
{
    public int Col;
    public double Y;
    public AppleState State;
    public double Timer;
    /// <summary>Where a fall began, to tell a short drop from one that breaks it.</summary>
    public double FallFrom;
    public int Crushed;
}

public enum BallState { Ready, Flying, Charging }

public sealed class Ball : Mover
{
    public BallState State;
    public double Timer;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.9;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

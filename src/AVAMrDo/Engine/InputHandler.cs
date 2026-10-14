using Avalonia.Input;

namespace AVAMrDo.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: the stick, one of four ways (the last pressed wins,
/// so rolling from one key to the next turns at once), and the button that throws the ball.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    private static readonly Key[] LeftKeys = [Key.Left, Key.A, Key.NumPad4];
    private static readonly Key[] RightKeys = [Key.Right, Key.D, Key.NumPad6];
    private static readonly Key[] UpKeys = [Key.Up, Key.W, Key.NumPad8];
    private static readonly Key[] DownKeys = [Key.Down, Key.S, Key.NumPad2];

    public Controls Controls
    {
        get
        {
            int dx = 0, dy = 0;
            for (int i = _held.Count - 1; i >= 0 && dx == 0 && dy == 0; i--)
            {
                if (LeftKeys.Contains(_held[i])) dx = -1;
                else if (RightKeys.Contains(_held[i])) dx = 1;
                else if (UpKeys.Contains(_held[i])) dy = -1;
                else if (DownKeys.Contains(_held[i])) dy = 1;
            }
            if (dx == 0 && dy == 0)
            {
                float sx = _pad.LeftStickX, sy = _pad.LeftStickY;
                if (_pad.DPadLeft) dx = -1;
                else if (_pad.DPadRight) dx = 1;
                else if (_pad.DPadUp) dy = -1;
                else if (_pad.DPadDown) dy = 1;
                else if (Math.Abs(sx) > 0.4f || Math.Abs(sy) > 0.4f)
                {
                    if (Math.Abs(sx) > Math.Abs(sy)) dx = Math.Sign(sx); else dy = Math.Sign(sy);
                }
            }
            bool fire = Any(Key.Space, Key.Z, Key.X, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonA || _pad.ButtonB || _pad.ButtonX;
            return new Controls(dx, dy, fire);
        }
    }

    public bool Start => Any(Key.Enter, Key.Space) || _pad.ButtonStart || _pad.ButtonA;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}

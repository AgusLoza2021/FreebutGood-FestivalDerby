namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Read-only steering surface the driving controls consume. The concrete scene joystick
    /// (Game.Controls.Joystick) lives in Assembly-CSharp, which an asmdef assembly can never
    /// reference, so the adapter outside this assembly implements this interface and the
    /// runtime depends on it alone. It also lets tests drive steering with a fake instead of
    /// a live joystick.
    /// </summary>
    public interface ISteerSource
    {
        float Horizontal { get; }
        float Vertical { get; }
        bool HasInput { get; }
    }
}

namespace MazeGame.Core;

public enum ParticleKind { Smoke, Coin, Score100 }

/// <summary>Smoke puffs (skidding), coin pops and the "100" score popup.</summary>
public sealed class Particle
{
    public ParticleKind Kind;
    public double X, Y;
    public double PrevX = double.NaN, PrevY = double.NaN;
    public double Frame = 1;
    public double SpeedY;
    public double Ghost;       // 0..100 transparency effect, like Scratch's ghost effect
    public bool Dead;

    public Particle(ParticleKind kind, double x, double y)
    {
        Kind = kind; X = x; Y = y;
        if (kind == ParticleKind.Coin) SpeedY = 12;
    }

    /// <summary>
    /// Costume number in the "particles" sprite (1-3 smoke, 4-7 coin, 9 = "Score 100").
    /// </summary>
    public int Costume => Kind switch
    {
        ParticleKind.Smoke => Math.Clamp((int)Math.Floor(Frame), 1, 3),
        ParticleKind.Coin => 4 + ((int)Math.Floor(Frame) % 4),
        _ => 9,
    };

    public void Tick()
    {
        switch (Kind)
        {
            case ParticleKind.Smoke:
                if (!(Frame < 4)) { Dead = true; return; }
                Frame += 0.4;
                break;
            case ParticleKind.Coin:
                SpeedY -= 1;
                if (SpeedY < -12) { Dead = true; return; }
                Y += SpeedY;
                Frame += 0.5;
                break;
            case ParticleKind.Score100:
                Y += 1;
                Frame += 0.8;
                if (Frame > 50)
                {
                    Ghost += 10;
                    if (Frame > 60) Dead = true;
                }
                break;
        }
    }
}

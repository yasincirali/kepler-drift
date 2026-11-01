// Body (planet/moon/rock). A moving body (the Moon) circles on a "rail": there is no gravity simulation, because the game's
// thesis is "timing skill"; with gravity the orbits became chaotic curves the player could not read (tried in the prototype).
using System;

namespace KeplerDrift.Core
{
    public enum BodyKind { Earth, Moon, Venus, Rock, Mars }

    // Rail: circle of radius R centred on (Cx, Cy), W radians per frame. Velocity comes from the derivative: a locked probe
    // inherits the body's velocity, otherwise a probe released from the Moon would "forget" the Moon's motion and the
    // perfect window would be measured wrongly.
    public sealed class Rail
    {
        public double Cx, Cy, R, W, A;
        public Rail(double cx, double cy, double r, double w, double a) { Cx = cx; Cy = cy; R = r; W = w; A = a; }
    }

    public sealed class Body
    {
        // Ring = radius + 46: wide enough not to hide under the finger on a small body, narrow enough not to overflow the
        // screen on a big one.
        public const double RingMargin = 46;

        public readonly string Id, Name;
        public readonly BodyKind Kind;
        public readonly double R;
        public readonly bool Hot;     // Venus: heat builds while locked (forbids taking a brake lap)
        public readonly Rail Rail;    // null = fixed
        public double X, Y, Vx, Vy;

        public Body(string id, string name, BodyKind kind, double x, double y, double r, bool hot = false, Rail rail = null)
        {
            Id = id; Name = name; Kind = kind; X = x; Y = y; R = r; Hot = hot; Rail = rail;
        }

        public double RingR { get { return R + RingMargin; } }
        public bool Moving { get { return Rail != null; } }

        public void RailStep(int n)
        {
            if (Rail == null) return;
            Rail.A += Rail.W * n;
            X = Rail.Cx + Math.Cos(Rail.A) * Rail.R; Y = Rail.Cy + Math.Sin(Rail.A) * Rail.R;
            Vx = -Math.Sin(Rail.A) * Rail.R * Rail.W; Vy = Math.Cos(Rail.A) * Rail.R * Rail.W;
        }
    }
}

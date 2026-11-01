// Probe: position, velocity and (if any) lock. The lock fields live on the probe because the probe can be bound to only
// one body at a time; a separate "Lock" object produced null/lifetime bugs (reading the fields of a released lock).
namespace KeplerDrift.Core
{
    public sealed class ProbeShip
    {
        public double X, Y, Vx, Vy;

        public Body Lock;            // null = free flight
        public double Angle;         // relative to the body centre, radians (y down: increasing angle is clockwise on screen)
        public int Dir;              // +1 / -1: from the cross product of the velocity at lock time (the direction you "entered" the orbit)
        public double Radius;        // current; eases into TargetR (no teleporting)
        public double TargetR;
        public double Total;         // angle travelled since locking (the brake counts this)
        public int LockFrame;
        // Tentative lock: if the finger lifts before 180 ms the lock is undone and the touch becomes fuel. Saved* = the
        // straight flight at lock time; on undo the probe continues on the same line as if it had never locked.
        public bool Tentative, Confirmed;
        public double SavedX, SavedY, SavedVx, SavedVy;

        public bool Locked { get { return Lock != null; } }
        public double Speed { get { return System.Math.Sqrt(Vx * Vx + Vy * Vy); } }
    }
}

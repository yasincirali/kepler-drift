// Mission (level) data. One mission for now: MISSION 3 · MARS, with exactly the prototype's numbers (orbit-proto.js).
// Why in code and not JSON: the first missions are rule experiments; once the format settles it moves to a Resources/ data
// file (Birdie's tool/data = Resources/data single-source pattern).
// Name strings here are English defaults; the HUD shows the localized text by Id (Game/Strings.cs).
using System.Collections.Generic;

namespace KeplerDrift.Core
{
    public sealed class Station
    {
        public const double CaptureR = 40;
        public const double SafeSpeed = 2.6;   // below: soft landing; above: bounce + shield -1
        public readonly string Id = "station", Name;
        public readonly double X, Y;
        public Station(string name, double x, double y) { Name = name; X = x; Y = y; }
    }

    public sealed class SamplePod
    {
        public const double PickupR = 16;
        public readonly double X, Y; public bool Taken;
        public SamplePod(double x, double y) { X = x; Y = y; }
    }

    public sealed class Rock
    {
        public double X, Y, Vx, S, Rot, Spin;
        public double HitR { get { return S * 0.85 + 3; } }   // slightly inside the drawn polygon: grazing is forgiven
    }

    public sealed class Mission
    {
        public const int ShieldMax = 3, FuelMax = 5, SampleCount = 3;

        public string Id, Name;
        public double Width = 420, View = 880, WorldH = 2600;   // px; view = logical screen height (framing rule)
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<string> Route = new List<string>();   // the perfect window is measured against the NEXT target
        public Station Station;
        public readonly List<SamplePod> Samples = new List<SamplePod>();
        public readonly List<Rock> Rocks = new List<Rock>();
        public string StartId;

        public Body Find(string id)
        {
            foreach (var b in Bodies) if (b.Id == id) return b;
            return null;
        }

        // The body after this one on the Route: returns its position (x, y); the station can be a target too. Otherwise false.
        public bool NextTarget(Body b, out double x, out double y)
        {
            x = y = 0;
            int i = Route.IndexOf(b.Id);
            if (i < 0 || i >= Route.Count - 1) return false;
            string id = Route[i + 1];
            if (id == Station.Id) { x = Station.X; y = Station.Y; return true; }
            var n = Find(id); if (n == null) return false;
            x = n.X; y = n.Y; return true;
        }

        public static Mission Mars()
        {
            var m = new Mission { Id = "mars", Name = "MISSION 3 · MARS", StartId = "earth" };
            m.Bodies.Add(new Body("earth", "EARTH", BodyKind.Earth, 200, 2380, 34));
            // The Moon circles the Earth: the first "moving target" lesson. Speed 0.0045 rad/frame ≈ one lap per 23 s.
            m.Bodies.Add(new Body("moon", "MOON", BodyKind.Moon, 0, 0, 12, false, new Rail(200, 2380, 165, 0.0045, -1.9)));
            m.Bodies.Add(new Body("venus", "VENUS", BodyKind.Venus, 110, 1760, 26, true));
            m.Bodies.Add(new Body("ceres", "CERES", BodyKind.Rock, 300, 1430, 17));
            m.Bodies.Add(new Body("vesta", "VESTA", BodyKind.Rock, 140, 830, 15));
            m.Bodies.Add(new Body("mars", "MARS", BodyKind.Mars, 210, 330, 40));
            m.Station = new Station("MARS BASE", 340, 230);
            m.Route.AddRange(new[] { "earth", "moon", "venus", "ceres", "vesta", "mars", "station" });
            m.Samples.Add(new SamplePod(352, 2050));
            m.Samples.Add(new SamplePod(52, 1012));
            m.Samples.Add(new SamplePod(336, 628));
            AsteroidBelt(m);
            foreach (var b in m.Bodies) b.RailStep(0);
            return m;
        }

        // Asteroid belt: three lanes flowing in opposite directions. Seed 42 (prototype): the same layout on every launch, so
        // it can be memorised. The Rng call order is kept identical to the prototype (vertex count and vertex angles consume
        // numbers too) so the positions match.
        static void AsteroidBelt(Mission m)
        {
            var rr = new Rng(42);
            double[][] lanes = { new[] { 1090, 0.6, 7 }, new[] { 1140, -0.9, 8 }, new[] { 1192, 0.5, 7 } };
            foreach (var L in lanes)
            {
                int n = (int)L[2];
                for (int i = 0; i < n; i++)
                {
                    double s = 7 + rr.Next() * 8; int k = 7 + (int)(rr.Next() * 3);
                    for (int j = 0; j < k; j++) { rr.Next(); rr.Next(); }   // polygon vertices: the View generates its own
                    var rock = new Rock { S = s };
                    rock.X = -60 + (i + rr.Next() * 0.4) * (540.0 / n);
                    rock.Y = L[0] + (rr.Next() - 0.5) * 16;
                    rock.Vx = L[1] * (0.8 + rr.Next() * 0.4);
                    rock.Rot = rr.Next() * Const.Tau;
                    rock.Spin = (rr.Next() - 0.5) * 0.02;
                    m.Rocks.Add(rock);
                }
            }
        }
    }
}

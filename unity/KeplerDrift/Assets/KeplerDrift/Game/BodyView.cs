// Body view: a single sprite + planet material (Shaders/Planet.shader) and the orbit ring.
// Why one quad + shader (not a pre-drawn texture as in the prototype): the light direction (_SunDirection) can change per
// mission and the day/night boundary stays crisp at every resolution; no per-device texture size tuning.
using UnityEngine;
using KeplerDrift.Core;

namespace KeplerDrift.Game
{
    public sealed class BodyView : MonoBehaviour
    {
        public Material planetMat;   // when empty, created from KeplerDrift/Planet (CiBuild always ships the shader)

        // Same as _DiskRatio in the shader: 82% of the quad is disk, the rest is the atmosphere halo (prototype: halo 1.12 r … margin).
        const float DiskRatio = 0.82f;
        Body b; SpriteRenderer sr; LineRenderer ring;
        static Sprite square, circle;

        public void Setup(Body body)
        {
            b = body;
            sr = gameObject.AddComponent<SpriteRenderer>(); sr.sprite = Square(); sr.sortingOrder = 1;
            var sh = Shader.Find("KeplerDrift/Planet");
            if (planetMat == null && sh != null) planetMat = new Material(sh);
            if (planetMat != null) { sr.sharedMaterial = planetMat; Paint(planetMat, b); }
            else { sr.sprite = Circle(); Debug.LogWarning("Kepler Drift: Planet shader missing, drawing a flat circle"); }
            transform.localScale = Vector3.one * (float)(b.R * 2 / (planetMat != null ? DiskRatio : 1f));

            // Ring: where you can lock on. Faint, because the screen's main information is the probe and the target; it brightens on the locked body.
            var rg = new GameObject("ring"); rg.transform.SetParent(transform.parent);
            ring = rg.AddComponent<LineRenderer>();
            ring.useWorldSpace = true; ring.loop = true; ring.positionCount = 64; ring.widthMultiplier = 1f;
            ring.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            ring.sortingOrder = 0;
        }

        // Colour triple (lit, mid, night) and atmosphere are the prototype's `base`/`atmo` values.
        static void Paint(Material m, Body b)
        {
            Color lit, mid, night, atm = Color.clear; float atmStrength = 0;
            switch (b.Kind)
            {
                case BodyKind.Earth: lit = Hex(0xa9cdf0); mid = Hex(0x2d5d92); night = Hex(0x0a1a2e); atm = new Color(140 / 255f, 190 / 255f, 1f); atmStrength = .7f; break;
                case BodyKind.Moon: lit = Hex(0xdedbd4); mid = Hex(0x8d8a83); night = Hex(0x2a2927); break;
                case BodyKind.Venus: lit = Hex(0xfffbea); mid = Hex(0xe9dcae); night = Hex(0x6d603e); atm = new Color(1f, 240 / 255f, 205 / 255f); atmStrength = .8f; break;
                case BodyKind.Mars: lit = Hex(0xeda57e); mid = Hex(0xb5532f); night = Hex(0x4a1c0f); atm = new Color(230 / 255f, 150 / 255f, 110 / 255f); atmStrength = .5f; break;
                default: lit = b.Id == "vesta" ? Hex(0xcbc2b3) : Hex(0xbcb5a9); mid = b.Id == "vesta" ? Hex(0x7b7267) : Hex(0x706960); night = Hex(0x22201c); break;
            }
            m.SetColor("_Lit", lit); m.SetColor("_Mid", mid); m.SetColor("_Night", night);
            m.SetColor("_AtmosphereColor", atm); m.SetFloat("_AtmosphereStrength", atmStrength); m.SetFloat("_DiskRatio", DiskRatio);
            // Sun at the top left (prototype SUN_ANG = atan2(-1, -1)); in Unity y is up, hence (-, +).
            m.SetVector("_SunDirection", new Vector4(-0.62f, 0.62f, 0.48f, 0));
        }

        public void Sync(OrbitEngine e)
        {
            transform.position = GameRoot.World(b.X, b.Y);
            bool locked = e.Probe.Lock == b;
            float alpha = locked ? 0.45f : 0.16f;
            ring.startColor = ring.endColor = new Color(0.5f, 0.85f, 1f, alpha);
            for (int i = 0; i < 64; i++)
            {
                double a = i / 64.0 * Const.Tau;
                ring.SetPosition(i, GameRoot.World(b.X + System.Math.Cos(a) * b.RingR, b.Y + System.Math.Sin(a) * b.RingR, 0.1f));
            }
        }

        static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f); }

        public static Sprite Square()
        {
            if (square != null) return square;
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[16]; for (int i = 0; i < 16; i++) p[i] = new Color32(255, 255, 255, 255); t.SetPixels32(p); t.Apply();
            return square = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        }

        // A 1-unit-diameter circle with an anti-aliased edge (placeholder for rocks, samples, probe, station).
        public static Sprite Circle()
        {
            if (circle != null) return circle;
            const int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                p[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((1 - d) * N * 0.5f) * 255));
            }
            t.SetPixels32(p); t.Apply();
            return circle = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        }
    }
}

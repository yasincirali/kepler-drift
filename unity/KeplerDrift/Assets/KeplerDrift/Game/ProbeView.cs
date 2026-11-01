// Probe view: dot + trail. The trail is the last 18 positions (prototype S.trail): a long trail exaggerates speed, and players
// tried to read the release angle from it and got it wrong.
// Chain colour: 0 faint cyan, 1-2 white, >= 3 gold. The player reads their rhythm from the trail without looking at a number.
// Colour never carries meaning alone (colour blindness): the chain count also appears in the Hud as "PERFECT ×n".
using System.Collections.Generic;
using UnityEngine;
using KeplerDrift.Core;

namespace KeplerDrift.Game
{
    public sealed class ProbeView : MonoBehaviour
    {
        const int TrailLength = 18;
        SpriteRenderer dot; LineRenderer trail;
        readonly Queue<Vector3> path = new Queue<Vector3>();

        // Lazy setup: at AddComponent time (Awake) the parent is not assigned yet; a trail built then would land at the scene
        // root and pile up because restart would not delete it.
        void Setup()
        {
            dot = gameObject.AddComponent<SpriteRenderer>(); dot.sprite = BodyView.Circle(); dot.sortingOrder = 5;
            transform.localScale = Vector3.one * 7f;
            var tg = new GameObject("trail"); tg.transform.SetParent(transform.parent);
            trail = tg.AddComponent<LineRenderer>(); trail.useWorldSpace = true; trail.sortingOrder = 4;
            trail.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            trail.widthCurve = AnimationCurve.Linear(0, 0.3f, 1, 1f);
        }

        public void Sync(OrbitEngine e, bool reduceMotion)
        {
            if (dot == null) Setup();
            var p = e.Probe; var pos = GameRoot.World(p.X, p.Y, -0.1f);
            transform.position = pos;
            // When lost the probe is not drawn (prototype drawProbe); during post-hit invulnerability it blinks (steady with reduced motion).
            bool visible = e.Mode != Mode.Lost && (e.Invuln <= 0 || reduceMotion || (e.Frame / 4) % 2 == 1);
            dot.enabled = visible;
            if (e.Mode == Mode.Play) { path.Enqueue(pos); while (path.Count > TrailLength) path.Dequeue(); }
            trail.enabled = e.Mode != Mode.Lost;
            trail.positionCount = path.Count; int i = 0; foreach (var v in path) trail.SetPosition(i++, v);
            Color c = e.Chain >= 3 ? new Color(1f, 206 / 255f, 110 / 255f) : e.Chain >= 1 ? Color.white : new Color(170 / 255f, 222 / 255f, 1f);
            trail.startColor = new Color(c.r, c.g, c.b, 0); trail.endColor = new Color(c.r, c.g, c.b, e.Chain > 0 ? 0.6f : 0.45f);
            trail.widthMultiplier = 1.5f + Mathf.Min(e.Chain, 4) * 0.35f;
            // TODO(tangent guide): while locked, a 46 px dashed line (where releasing will go) and a "BRAKE" label.
            // TODO(flame): 12 frames of flame + 16 particles on the Burn event (prototype burn()).
        }
    }
}

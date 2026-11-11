// Timeline for the fake rope post. The ropes are the source's own components (Rope, RopeMesh, RopeWindEffect from
// Ali10555/OptimizedRopesAndCables, MIT, unchanged). This script only moves the trolley and the boat, switches
// parameters and cameras per segment, keeps the straight comparison cylinders in place, draws the debug markers
// and logs measured values. Private state of Rope is read (never written) through reflection for the debug view.
using System.Reflection;
using GogoGaga.OptimizedRopesAndCables;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-50)]
    public class RopeDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Transform trolley, boat;
        public Rope[] setL, setR, setLine;              // index 0 = hero rope, last = long soft rope (keeps its own values)
        public RopeWindEffect[] windL, windR;
        public Transform[] straight, straightA, straightB;
        public Transform straightLantern;
        public Transform mkStart, mkEnd, mkCtrl, mkTarget;
        public LineRenderer polyLine, pullLine, windLine;
        public LabelSpec live, live2;
        public Material seaMat;

        public const float TrolleyRestX = 6.6f;
        public static readonly Vector3 HeroMid = new Vector3(4.1f, 3.2f, 0f);
        static readonly FieldInfo fCur = typeof(Rope).GetField("currentValue", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo fTar = typeof(Rope).GetField("targetValue", BindingFlags.NonPublic | BindingFlags.Instance);

        Vector3 boat0;
        float lastAboveA = -1, lastAboveStiff = -1, lastAboveSoft = -1, maxOverA, windMax, windSum; int windN;
        bool logRest, logA, logB, logC;

        void Awake()
        {
            Random.InitState(77);                 // RopeWindEffect picks its seed with Random.Range in Start
            Time.fixedDeltaTime = 1f / 60f;       // two physics steps per captured frame at 30 fps
            boat0 = boat.position;
        }

        static float S(float a, float b, float t) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t)); }
        static float Pulse(float t, float t0, float tp, float t1, float amp) { return amp * S(t0, tp, t) * (1f - S(tp, t1, t)); }

        static void Params(Rope[] set, float stiffness, float damping, float weight)
        {
            for (int i = 0; i < set.Length - 1; i++) { set[i].stiffness = stiffness; set[i].damping = damping; set[i].midPointWeight = weight; }
        }
        static void Wind(RopeWindEffect[] set, float force) { foreach (var w in set) w.windForce = force; }

        static void Orbit(Camera c, float azDeg, float r, float h, Vector3 at)
        {
            float a = azDeg * Mathf.Deg2Rad;
            c.transform.position = new Vector3(at.x + Mathf.Sin(a) * r, h, at.z - Mathf.Cos(a) * r);
            c.transform.LookAt(at);
        }

        const int Common = ~((1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12));

        void Update()
        {
            float t = FrameCapturer.T;
            bool segA = t < 8f, segB = t >= 8f && t < 14f, segC = t >= 14f && t < 18.5f, segD1 = t >= 18.5f && t < 21.2f, segD2 = t >= 21.2f;

            // ---- moving ends: the trolley on its rail (hero rope) and the moored boat
            float ox = 1.4f * S(2f, 4.8f, t) - 1.7f * S(5.3f, 5.6f, t) + 0.3f * S(6.5f, 8f, t)
                     + Pulse(t, 9.0f, 9.3f, 9.6f, 1.0f) + Pulse(t, 11.8f, 12.1f, 12.4f, 1.0f)
                     + Pulse(t, 19.4f, 19.7f, 20.0f, 0.7f) + Pulse(t, 22.0f, 22.3f, 22.6f, 1.0f);
            var tp = trolley.position; tp.x = TrolleyRestX + ox; trolley.position = tp;
            float bt = Time.time;
            boat.position = boat0 + new Vector3(0, 0.06f * Mathf.Sin(1.3f * bt), 0);
            boat.rotation = Quaternion.Euler(3f * Mathf.Sin(0.9f * bt), 20f, 5f * Mathf.Sin(1.1f * bt + 1f));

            seaMat.SetTextureOffset("_BaseMap", new Vector2(0.004f, 0.016f) * bt);     // waves drift (the normal map shares this offset)

            // ---- parameters per segment (the long soft rope, last in each set, keeps its own)
            if (segA) { Params(setR, 150, 4, 1); Params(setL, 600, 20, 1); Wind(windR, 0); Wind(windL, 0); }
            else if (segB) { Params(setL, 600, 20, 1); Params(setR, 60, 2, 6); Wind(windR, 0); Wind(windL, 0); }
            else if (segC) { Params(setL, 150, 4, 1); Params(setR, 150, 4, 1); Wind(windL, 0); Wind(windR, 120); }
            else { Params(setL, 150, 4, 1); Params(setR, 150, 4, 1); Wind(windL, 0); Wind(windR, 0); }
            Params(setLine, 150, 4, 1);

            // ---- cameras
            bool split = !segD2;
            camL.enabled = camR.enabled = split; camFull.enabled = !split;
            camL.cullingMask = Common | (segA ? 1 << 10 : segD1 ? 1 << 11 : 1 << 8);
            camR.cullingMask = Common | (1 << 9);
            camFull.cullingMask = Common | (1 << 9) | (1 << 12);
            if (segA || segB)
            {
                float az = Mathf.Lerp(-7f, 7f, S(0f, 14f, t));
                var at = HeroMid + new Vector3(1.2f, -0.25f, 0);
                Orbit(camL, az, 15f, 2.1f, at); Orbit(camR, az, 15f, 2.1f, at);
            }
            else if (segC)
            {
                float az = Mathf.Lerp(30f, 42f, S(14f, 18.5f, t));
                var at = HeroMid + new Vector3(0.2f, -0.3f, 0);
                Orbit(camL, az, 12f, 2.6f, at); Orbit(camR, az, 12f, 2.6f, at);
            }
            else if (segD1)
            {
                var pos = Vector3.Lerp(new Vector3(3.5f, 4.3f, 3.1f), new Vector3(3.0f, 4.2f, 2.9f), S(18.5f, 21.2f, t));
                var at = new Vector3(2.9f, 3.45f, 0);
                camL.transform.position = pos; camL.transform.LookAt(at); camR.transform.position = pos; camR.transform.LookAt(at);
            }
            else
            {
                Orbit(camFull, Mathf.Lerp(8f, 18f, S(21.2f, 24f, t)), 8.5f, 2.6f, HeroMid + new Vector3(0.5f, -0.2f, 0));
            }

            // ---- straight comparison cylinders (unit cylinder is 2 high, 1 wide)
            for (int i = 0; i < straight.Length; i++)
            {
                Vector3 a = straightA[i].position, b = straightB[i].position, d = b - a;
                straight[i].position = (a + b) * 0.5f;
                straight[i].rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                straight[i].localScale = new Vector3(0.1f, d.magnitude * 0.5f, 0.1f);
            }
            straightLantern.position = (straightA[0].position + straightB[0].position) * 0.5f;

            // ---- debug markers on the hero rope of the right-hand set
            var hero = setR[0];
            Vector3 cur = (Vector3)fCur.GetValue(hero), tar = (Vector3)fTar.GetValue(hero);
            Vector3 s0 = hero.StartPoint.position, e0 = hero.EndPoint.position;
            mkStart.position = s0; mkEnd.position = e0; mkCtrl.position = cur; mkTarget.position = tar;
            polyLine.SetPosition(0, s0); polyLine.SetPosition(1, cur); polyLine.SetPosition(2, e0);
            pullLine.SetPosition(0, cur); pullLine.SetPosition(1, tar);
            Vector3 wind = hero.otherPhysicsFactors;
            windLine.SetPosition(0, cur); windLine.SetPosition(1, cur + wind * 4f);
            float off = Vector3.Distance(cur, tar);
            live.text = $"stiffness {hero.stiffness:0}  damping {hero.damping:0}  weight {hero.midPointWeight:0}  length {hero.ropeLength:0.0} m";
            live2.text = $"sprung midpoint is {off:0.00} m from its target";

            // ---- measurements for the notes
            Vector3 midCurve = hero.GetPointAt(0.5f);
            if (!logRest && t > 1.9f)
            {
                logRest = true;
                Debug.Log($"[gt] rope rest: span {Vector3.Distance(s0, e0):0.00} m, length {hero.ropeLength:0.0} m, control point {(s0.y + e0.y) * 0.5f - cur.y:0.00} m below the span, curve sag at t=0.5 {(s0.y + e0.y) * 0.5f - midCurve.y:0.00} m");
            }
            if (t > 5.6f && t < 8f) { if (off > 0.02f) lastAboveA = t; maxOverA = Mathf.Max(maxOverA, off); }
            if (!logA && t >= 8f) { logA = true; Debug.Log($"[gt] rope A (150/4/1): after the release at 5.6 s, offset stays under 0.02 m from {lastAboveA - 5.6f:0.00} s, largest offset {maxOverA:0.00} m"); }
            if (t > 9.6f && t < 11.7f)
            {
                var sc = (Vector3)fCur.GetValue(setL[0]); var st = (Vector3)fTar.GetValue(setL[0]);
                if (Vector3.Distance(sc, st) > 0.02f) lastAboveStiff = t;
                if (off > 0.02f) lastAboveSoft = t;
            }
            if (!logB && t >= 11.7f) { logB = true; Debug.Log($"[gt] rope B: after the tug ends at 9.6 s, stiff (600/20/1) under 0.02 m from {Mathf.Max(0, lastAboveStiff - 9.6f):0.00} s, soft (60/2/6) from {Mathf.Max(0, lastAboveSoft - 9.6f):0.00} s (soft still above at 11.7 s: {lastAboveSoft > 11.6f})"); }
            if (segC && t > 14.5f) { float z = Mathf.Abs(midCurve.z); windMax = Mathf.Max(windMax, z); windSum += z; windN++; }
            if (!logC && t >= 18.5f) { logC = true; Debug.Log($"[gt] rope C: wind force 120, hero rope middle pushed sideways up to {windMax:0.00} m, mean {windSum / Mathf.Max(1, windN):0.00} m; fixed step {Time.fixedDeltaTime:0.0000} s"); }
        }
    }
}

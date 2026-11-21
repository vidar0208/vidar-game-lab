// Drives the dynamic lighting post. Three identical rooms, three fixed lights each (lights 0-2 room A, 3-5 room B,
// 6-8 room C). No light ever moves. Room A's lights are real-time (no precomputed shadows), rooms B and C use the
// raytraced per-light shadows. In room C the warm lamp's colour is changed every frame, the cool lamp runs the source's
// "Pulse" effect and the floor ember its "Fire" effect; the shadow data stays as baked.
using AlpacaIT.DynamicLighting;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class DLDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public DynamicLight[] lights;
        public Renderer[] bulbs;
        public LabelSpec title, l1, l2, m1, m2;
        public Color warmColour = new Color(1.0f, 0.70f, 0.42f);
        MaterialPropertyBlock mpb;
        int phase = -1; float nextLog;

        static readonly Vector3 RA = new Vector3(-40f, 0f, 0f), RB = Vector3.zero, RC = new Vector3(40f, 0f, 0f);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            var cool = lights[7]; cool.lightEffect = DynamicLightEffect.Pulse; cool.lightEffectPulseSpeed = 0.6f; cool.lightEffectPulseModifier = 0.15f;
            var ember = lights[8]; ember.lightEffect = DynamicLightEffect.Fire;
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }
        static Vector3 Orbit(Vector3 target, float yawDeg, float dist, float height)
        {
            float a = yawDeg * Mathf.Deg2Rad;
            return target + new Vector3(Mathf.Sin(a) * dist, height, -Mathf.Cos(a) * dist);
        }
        void Show(Camera c, bool on, Rect r, float fov)
        {
            c.enabled = on; if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
        }
        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            // room C, warm lamp: colour changes at runtime from 12 s on (amber -> magenta -> teal -> amber)
            var warm = warmColour;
            if (t >= 12f)
            {
                float h = Mathf.Repeat(0.07f + (t - 12f) * 0.06f, 1f);
                lights[6].lightColor = Color.Lerp(warm, Color.HSVToRGB(h, 0.55f, 1f), Mathf.SmoothStep(0f, 1f, (t - 12f) / 1.5f));
            }
            else lights[6].lightColor = warm;
            for (int i = 0; i < bulbs.Length; i++)
            {
                var L = lights[i];
                mpb.SetColor("_BaseColor", L.lightColor * (1.2f + 2.4f * L.currentIntensity / Mathf.Max(0.01f, L.lightIntensity)));
                bulbs[i].SetPropertyBlock(mpb);
            }
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            int p = t < 4f ? 0 : t < 12f ? 1 : t < 19f ? 2 : t < 23f ? 3 : 4;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var over = new Vector3(0.3f, 0.4f, 0.4f);
            if (p == 0)
            {
                Show(camA, true, full, 40f); Show(camB, false, full, 0);
                Look(camA, RB + Orbit(over, Mathf.Lerp(-24f, -12f, t / 4f), 10.5f, 8.2f), RB + over);
                Labels("Precomputed per-light shadows", "3 fixed lights; each light's shadows are raytraced once and stored per surface texel", "", "", "");
            }
            else if (p == 1)
            {
                Show(camA, true, h0, 50f); Show(camB, true, h1, 50f);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 8f);
                var lp = Orbit(over, Mathf.Lerp(-16f, 8f, u), 9.6f, 8.0f);
                Look(camA, RA + lp, RA + over); Look(camB, RB + lp, RB + over);
                Labels("Same 3 lights, same room",
                    "Real-time, no precomputed shadows", "light passes through the wall and the pillars",
                    "Precomputed per-light shadows", "wall, doorway and pillars block each light");
            }
            else if (p == 2)
            {
                Show(camA, true, h0, 52f); Show(camB, true, h1, 52f);
                float u = Mathf.SmoothStep(0f, 1f, (t - 12f) / 7f);
                var lp = new Vector3(Mathf.Lerp(-5.2f, -4.4f, u), Mathf.Lerp(1.9f, 1.6f, u), -4.2f);
                var tg = new Vector3(0.6f, 0.9f, 0.9f);
                Look(camA, RB + lp, RB + tg); Look(camB, RC + lp, RC + tg);
                Labels("Same baked shadows, lights changed at runtime",
                    "Steady (as baked)", "",
                    "Colour + pulse + fire flicker at runtime", "the lights never move; the shadow shapes stay");
            }
            else if (p == 3)
            {
                Show(camA, true, full, 46f); Show(camB, false, full, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 19f) / 4f);
                var pos = Vector3.Lerp(new Vector3(-4.6f, 1.6f, -7.0f), new Vector3(-1.8f, 1.55f, -0.2f), u);
                var tg = Vector3.Lerp(new Vector3(-1.5f, 1.2f, 0.5f), new Vector3(3.2f, 1.1f, 2.0f), u);
                Look(camA, RC + pos, RC + tg);
                Labels("Walking in", "Lights stay put: colour, intensity and flicker change, shadows stay correct", "", "", "");
            }
            else
            {
                Show(camA, true, full, 40f); Show(camB, false, full, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 23f) / 3f);
                Look(camA, RC + Orbit(over, Mathf.Lerp(6f, 16f, u), 10.5f, 8.2f), RC + over);
                Labels("Precomputed per-light shadows", "Unity 6.3 URP with the Henry00IS/DynamicLighting package; scene, rooms and comparison are mine", "", "", "");
            }
            if (p == 1 || p == 2) l1.anchor = new Vector2(0.012f, 0.058f); else l1.anchor = new Vector2(0.012f, 0.03f);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                Debug.Log($"[gt] t {t:F2} room C: warm colour {lights[6].lightColor} cool intensity {lights[7].currentIntensity:F2}/{lights[7].lightIntensity} ember {lights[8].currentIntensity:F2}/{lights[8].lightIntensity} | room B warm {lights[3].currentIntensity:F2}");
            }
        }
    }
}

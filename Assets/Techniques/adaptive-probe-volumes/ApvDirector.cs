// Camera, moving sphere, labels and APV debug toggles for the probe comparison. The scene is captured twice: once with
// the old Light Probe Group (per-object SH) and once with Adaptive Probe Volumes; the split shots take the left half
// from the first run and the right half from the second, with the same camera pose. All GI is Unity's bake.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class ApvDirector : MonoBehaviour
    {
        public bool apv;                  // which capture run this is
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public Transform ball;
        public string stats = "";         // filled by the builder: bake time, probes, bricks, memory
        int phaseShown = -1;

        // 0-7 s hero (APV run), 7-15 s split floor + ball, 15-20 s split second view, 20-25 s APV bricks debug + stats
        public static int Phase(float t) => t < 7f ? 0 : t < 15f ? 1 : t < 20f ? 2 : 3;

        void Update()
        {
            float t = FrameCapturer.T;
            // the ball rolls from the blue side through the sun patch to the red wall and back
            float s = Mathf.Sin(t * 0.55f);
            ball.position = new Vector3(3.2f * s, 0.45f, 1.2f + 1.6f * Mathf.Cos(t * 0.37f));
            ball.rotation = Quaternion.Euler(0, 0, -s * 200f);

            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = Phase(t);
            var pos = new Vector3(0.6f * Mathf.Sin(t * 0.21f), 1.75f, -6.2f + 0.05f * t);
            var rot = Quaternion.Euler(16f, 4f * Mathf.Sin(t * 0.17f), 0);
            var pos3 = new Vector3(-1.0f, 2.6f, -1.0f); var rot3 = Quaternion.Euler(38f, -38f, 0);
            // phase 2 follows the ball as it rolls from the window side through the sun to the red wall
            var bp = ball.position; var pos2 = new Vector3(0.6f * bp.x - 0.4f, 1.5f, bp.z - 3.2f);
            var rot2 = Quaternion.LookRotation(bp + new Vector3(0, 0.2f, 0) - pos2);
            DebugBricks(apv && p == 3);

            if (p == 0 || p == 3)
            {
                Set(camFull, p == 0 ? pos : pos3, p == 0 ? rot : rot3);
                if (p == 0) Labels("Adaptive Probe Volumes: baked bounce light per pixel, no lightmaps",
                    "sun through the window hits the floor and the red wall; the red light bounces onto floor, ceiling and the ball",
                    "every surface here is lit by baked probes only (no lightmaps); direct sun and shadows are realtime", "", "");
                else Labels("Debug view: every sphere is one baked probe", stats + $", {ProbeReferenceVolume.instance.GetVideoMemoryCost() / 1048576f:F1} MB GPU pool",
                    "Apple M4 Mac mini, Metal, Unity 6.3 (URP 17.3) Editor. debug spheres drawn by URP's own APV probe display", "", "");
            }
            else
            {
                camBack.enabled = true;
                var c = apv ? camR : camL;
                Set(c, p == 1 ? pos : pos2, p == 1 ? rot : rot2);
                if (p == 1) Labels("Same bake inputs, same camera",
                    "Light Probe Group: one probe blend per object", "a probe sits inside the block, so the block goes dark",
                    "Adaptive Probe Volumes: probes sampled per pixel", "probes inside geometry are flagged invalid and skipped");
                else Labels("A moving object reads the same probes as the room",
                    "Light Probe Group: one probe blend at the ball's centre", "one set of SH coefficients for the whole ball",
                    "APV: the ball samples the probe grid per pixel", "its underside picks up the darker probes near the floor");
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} apv {apv} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
        }

        // URP's own APV debug view (the Rendering Debugger's Display Probes); the settings object is internal, so reflection
        static object dbg;
        static void DebugBricks(bool on)
        {
            if (dbg == null)
                dbg = typeof(ProbeReferenceVolume).GetProperty("probeVolumeDebug", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(ProbeReferenceVolume.instance);
            dbg?.GetType().GetField("drawProbes").SetValue(dbg, on);
        }

        static void Set(Camera c, Vector3 pos, Quaternion rot) { c.enabled = true; c.transform.SetPositionAndRotation(pos, rot); }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }
    }
}

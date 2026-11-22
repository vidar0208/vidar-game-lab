// Drives the strand hair post. Two identical busts (scanned head + generated groom) perform the same sudden head turns.
// Bust 0: simulated strands (Demo Team Hair solver, gravity, collision with sphere/capsule colliders).
// Bust 1: same hair asset, but global position + rotation constraints at full strength, so the strands follow the head
// rigidly (the "hair glued to the head" baseline). Cameras, lights and labels change per phase.
using Unity.DemoTeam.Hair;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class HairDirector : MonoBehaviour
    {
        public Transform[] busts;
        public HairInstance[] hair;
        public Camera camA, camB;
        public Light key, rim;
        public Renderer[] proxies;
        public LabelSpec title, l1, l2, m1, m2;
        public Vector3 headCentre = new Vector3(0f, 0.135f, 0.01f);   // head centre relative to the neck pivot (metres)
        int phase = -1; float nextLog;
        MaterialPropertyBlock mpb;

        // time, yaw, pitch, roll, transition duration
        static readonly float[,] K =
        {
            { 0.00f, -35f,  0f,   0f, 0.01f },
            { 0.55f,  45f, -3f,   4f, 0.28f },
            { 1.75f, -40f,  2f,  -3f, 0.30f },
            { 2.90f,  10f,  8f,   0f, 0.35f },
            { 4.30f,  50f, -2f,   3f, 0.28f },
            { 5.70f, -45f,  0f,  -4f, 0.28f },
            { 7.00f,   0f,  0f,   0f, 0.30f },
            { 8.80f,  35f,  0f,  12f, 0.40f },
            {10.00f, -10f,  0f,   0f, 0.30f },
            {12.60f,  55f,  0f,   0f, 0.30f },
            {14.60f, -30f,  0f,   0f, 0.30f },
            {16.30f,   0f,  0f,   0f, 0.40f },
            {18.00f,  30f, 15f,  15f, 0.50f },
            {19.80f, -30f, 10f, -15f, 0.50f },
            {21.50f,   0f,  0f,   0f, 0.40f },
            {22.80f,  40f,  0f,   0f, 0.30f },
            {24.00f, -20f,  0f,   0f, 0.30f },
        };

        public static Vector3 Pose(float t)
        {
            var p = new Vector3(K[0, 1], K[0, 2], K[0, 3]);
            for (int i = 1; i < K.GetLength(0); i++)
            {
                if (t < K[i, 0]) break;
                var q = new Vector3(K[i, 1], K[i, 2], K[i, 3]);
                float u = Mathf.Clamp01((t - K[i, 0]) / K[i, 4]);
                u = u * u * u * (u * (u * 6f - 15f) + 10f);
                p = Vector3.Lerp(p, q, u);
            }
            return p;
        }

        void Awake() { mpb = new MaterialPropertyBlock(); }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }
        void Show(Camera c, bool on, Rect r, float fov)
        {
            c.enabled = on; if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
        }
        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            var e = Pose(t);
            var rot = Quaternion.Euler(e.y, e.x, e.z);
            foreach (var b in busts) b.localRotation = rot;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            int p = t < 3.7f ? 0 : t < 11.5f ? 1 : t < 17.3f ? 2 : t < 22.3f ? 3 : 4;
            bool rigidOn = t >= 3.0f && t < 11.6f;   // the rigid copy is only needed for the split screen
            if (busts[1].gameObject.activeSelf != rigidOn) busts[1].gameObject.SetActive(rigidOn);
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            Vector3 A = busts[0].position + headCentre, B = busts[1].position + headCentre;
            float proxyAlpha = 0f;
            title.anchor = new Vector2(0.012f, 0.93f); l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f);
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            if (p == 0)
            {
                // right half only; the left half is left black for the reference clip added at encode time
                Show(camA, true, new Rect(0.5f, 0, 0.5f, 1), 30f); Show(camB, false, full, 0);
                Look(camA, A + new Vector3(0.36f, -0.02f, 0.86f), A + new Vector3(0f, -0.06f, 0f));
                key.transform.rotation = Quaternion.Euler(28f, -130f, 0f); rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);
                title.anchor = new Vector2(0.512f, 0.93f);
                Labels("My Unity build: simulated strand hair", "", "", "Sudden head turn: strands lag, swing, collide, settle", "");
            }
            else if (p == 1)
            {
                Show(camA, true, h0, 30f); Show(camB, true, h1, 30f);
                float u = (t - 3.7f) / 7.8f;
                var off = new Vector3(Mathf.Lerp(0.42f, 0.2f, u), -0.02f, 0.86f);
                Look(camA, B + off, B + new Vector3(0f, -0.06f, 0f)); Look(camB, A + off, A + new Vector3(0f, -0.06f, 0f));
                key.transform.rotation = Quaternion.Euler(28f, -130f, 0f); rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);
                Labels("Same head motion, same hair asset",
                    "Hair fixed to the head", "every strand follows the skull rigidly",
                    "Simulated strands", "lag behind the turn, overshoot, then settle");
            }
            else if (p == 2)
            {
                Show(camA, true, full, 24f); Show(camB, false, full, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 11.5f) / 5.8f);
                var off = Quaternion.Euler(0f, Mathf.Lerp(-78f, -58f, u), 0f) * new Vector3(0f, 0.05f, 0.9f);
                Look(camA, A + off, A + new Vector3(0f, -0.05f, 0f));
                key.transform.rotation = Quaternion.Euler(20f, Mathf.Lerp(120f, 140f, u), 0f); rim.transform.rotation = Quaternion.Euler(10f, 30f, 0f);
                Labels("Side light", "Each strand is drawn as its own lit ribbon; the highlights travel as the strands swing and settle", "", "", "");
            }
            else if (p == 3)
            {
                Show(camA, true, full, 32f); Show(camB, false, full, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 17.3f) / 5f);
                var off = Quaternion.Euler(0f, Mathf.Lerp(28f, 40f, u), 0f) * new Vector3(0f, -0.06f, 1.0f);
                Look(camA, A + off, A + new Vector3(0f, -0.1f, 0f));
                key.transform.rotation = Quaternion.Euler(28f, -130f, 0f); rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);
                proxyAlpha = Mathf.Clamp01((t - 17.6f) / 0.6f) * Mathf.Clamp01((22.0f - t) / 0.6f);
                Labels("Collision", "Strands are pushed out of the head, neck and shoulder colliders (orange)", "", "", "");
            }
            else
            {
                Show(camA, true, full, 30f); Show(camB, false, full, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22.3f) / 2.7f);
                var off = Quaternion.Euler(0f, Mathf.Lerp(-30f, -8f, u), 0f) * new Vector3(0f, 0f, 1.0f);
                Look(camA, A + off, A + new Vector3(0f, -0.07f, 0f));
                key.transform.rotation = Quaternion.Euler(28f, -130f, 0f); rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);
                Labels("Strand hair simulation", "Unity 6.3 URP with Unity's Demo Team Hair package; groom, head setup and comparison are mine", "", "", "");
            }
            Shader.SetGlobalVector("_GTKeyDir", -key.transform.forward); Shader.SetGlobalVector("_GTKeyCol", key.color.linear * key.intensity);
            Shader.SetGlobalVector("_GTRimDir", -rim.transform.forward); Shader.SetGlobalVector("_GTRimCol", rim.color.linear * rim.intensity);
            Shader.SetGlobalVector("_GTAmbient", new Vector4(0.42f, 0.44f, 0.5f, 1f));
            camA.cullingMask = proxyAlpha > 0.001f ? ~0 : ~(1 << 26);
            foreach (var r in proxies)
            {
                r.enabled = proxyAlpha > 0.001f;
                mpb.SetColor("_BaseColor", new Color(1f, 0.5f, 0.05f, 0.18f * proxyAlpha));
                r.SetPropertyBlock(mpb);
            }
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                var h = hair[0];
                int strands = 0, particles = 0;
                if (h.strandGroupInstances != null)
                    foreach (var g in h.strandGroupInstances) { ref readonly var sg = ref g.groupAssetReference.Resolve(); strands += sg.strandCount; particles = sg.strandParticleCount; }
                Debug.Log($"[gt] t {t:F2} pose {Pose(t)} strands {strands} x {particles} particles, steps last frame {h.execState.lastStepCount}");
            }
        }
    }
}

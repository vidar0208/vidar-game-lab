// Drives the CoR skinning post: poses both figures the same way (arms lowered, elbow bend, hand twist about the
// forearm axis), moves the cameras, and after the skinning kernels ran (CorMeasure, order 100) reads the skinned
// vertices back to measure the left wrist and elbow cross-sections and the left arm's volume against the rest pose.
// Every number on screen comes from those measurements or from the pose values.
using CoR;
using GameTech;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class CorDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public Transform[] lbsBones, corBones;
        public CorGpuSkinner lbs, cor;
        public int wristRing, elbowRing, ringSize;
        public int[] armTris;
        public LabelSpec title, l1, l2, m1, m2;

        public const int LLbs = 20, LCor = 21;
        int phase = -1, logged = -1; float nextLog;
        float down, bend, twist, sweep = 1f;
        Vector3[] rest, buf;
        float restWrist, restElbow, restVol;

        static float Area(Vector3[] v, int start, int count)
        {
            var c = Vector3.zero; for (int i = 0; i < count - 1; i++) c += v[start + i]; c /= count - 1;
            var a = Vector3.zero;
            for (int i = 0; i < count - 1; i++) a += Vector3.Cross(v[start + i] - c, v[start + i + 1] - c);
            return a.magnitude * 0.5f;
        }

        float Volume(Vector3[] v)
        {
            double s = 0;
            for (int i = 0; i < armTris.Length; i += 3)
                s += Vector3.Dot(v[armTris[i]], Vector3.Cross(v[armTris[i + 1]], v[armTris[i + 2]]));
            return Mathf.Abs((float)(s / 6.0));
        }

        void Start()
        {
            rest = lbs.corAsset.vertices; buf = new Vector3[rest.Length];
            restWrist = Area(rest, wristRing, ringSize); restElbow = Area(rest, elbowRing, ringSize); restVol = Volume(rest);
            Debug.Log($"[gt] rest: wrist area {restWrist * 1e4f:F2} cm2, elbow area {restElbow * 1e4f:F2} cm2, left arm volume {restVol * 1e6f:F0} cm3");
            gameObject.AddComponent<CorMeasure>().director = this;
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        static void Show(Camera c, bool on, Rect r, float fov, int layer)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.cullingMask = (1 << 0) | (1 << layer); c.aspect = r.width * 1920f / (r.height * 1080f);
        }

        static void Pose(Transform[] b, float down, float bend, float twist, float t)
        {
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? 1f : -1f; int o = s == 0 ? 5 : 8;
                b[o].localRotation = Quaternion.AngleAxis(sx * down, Vector3.forward) * Quaternion.AngleAxis(sx * 8f, Vector3.up);
                b[o + 1].localRotation = Quaternion.AngleAxis(sx * bend, Vector3.up);
                b[o + 2].localRotation = Quaternion.AngleAxis(twist, Vector3.right);
            }
            b[1].localRotation = Quaternion.Euler(1.5f * Mathf.Sin(t * 0.9f), 2f * Mathf.Sin(t * 0.6f), 0);
            b[4].localRotation = Quaternion.Euler(4f + 2f * Mathf.Sin(t * 0.7f), -10f * Mathf.Sin(t * 0.4f), 0);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            sweep = 1f;
            if (t < 4f)
            {
                phase = 0;
                down = 52f; bend = 70f + 25f * Mathf.Sin(t * 1.3f); twist = 110f * Mathf.Sin(t * 1.5f);
                Show(camA, true, full, 32f, LCor); Show(camB, false, full, 32f, LCor);
                float u = Mathf.SmoothStep(0f, 1f, t / 4f);
                Look(camA, new Vector3(Mathf.Lerp(-1.6f, -1.1f, u), 1.05f, Mathf.Lerp(2.6f, 2.1f, u)), new Vector3(-0.12f, 0.85f, 0f));
            }
            else if (t < 11f)
            {
                phase = 1;
                down = 18f; bend = 12f; twist = 175f * Mathf.SmoothStep(0f, 1f, (t - 4.6f) / 4.6f);
                Show(camA, true, h0, 30f, LLbs); Show(camB, true, h1, 30f, LCor);
            }
            else if (t < 17f)
            {
                phase = 2;
                down = 48f; twist = 0f; bend = 150f * Mathf.SmoothStep(0f, 1f, (t - 11.6f) / 4.4f);
                Show(camA, true, h0, 30f, LLbs); Show(camB, true, h1, 30f, LCor);
            }
            else if (t < 22f)
            {
                phase = 3;
                down = 40f; bend = 115f; twist = 170f;
                sweep = 0.5f + 0.5f * Mathf.Cos((t - 17.4f) / 4.2f * Mathf.PI * 2f);
                if (t < 17.4f) sweep = 1f;
                Show(camA, true, full, 30f, LCor); Show(camB, false, full, 30f, LCor);
            }
            else
            {
                phase = 4;
                down = 60f + 8f * Mathf.Sin(t * 1.1f); bend = 95f + 30f * Mathf.Sin(t * 1.4f); twist = 140f * Mathf.Sin(t * 1.2f);
                Show(camA, true, h0, 34f, LLbs); Show(camB, true, h1, 34f, LCor);
            }
            Pose(lbsBones, down, bend, twist, t); Pose(corBones, down, bend, twist, t);
            lbs.corWeight = 0f; cor.corWeight = sweep;

            // cameras that follow the left arm (bone positions after posing)
            var elbow = corBones[6].position; var wrist = corBones[7].position;
            if (phase == 1)
            {
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 7f);
                var target = Vector3.Lerp(elbow, wrist, 0.75f);
                foreach (var c in new[] { camA, camB }) Look(c, target + new Vector3(Mathf.Lerp(0.05f, -0.06f, u), 0.06f, 0.42f), target);
            }
            else if (phase == 2)
            {
                // look along the normal of the bend plane (the upper arm's own up axis), so the fold is seen from the side
                float u = Mathf.SmoothStep(0f, 1f, (t - 11f) / 6f);
                var nrm = corBones[5].rotation * Vector3.up;
                var target = elbow + (wrist - elbow) * 0.15f;
                foreach (var c in new[] { camA, camB }) Look(c, target + nrm * Mathf.Lerp(0.62f, 0.5f, u) + new Vector3(0, 0, 0.12f), target);
            }
            else if (phase == 3)
            {
                var nrm = corBones[5].rotation * Vector3.up;
                var target = Vector3.Lerp(elbow, wrist, 0.5f);
                Look(camA, target + nrm * 0.62f + new Vector3(0, 0, 0.18f), target);
            }
            else if (phase == 4)
            {
                float u = (t - 22f) / 4f;
                foreach (var c in new[] { camA, camB }) Look(c, new Vector3(Mathf.Lerp(-0.7f, -0.3f, u), 1.0f, 2.3f), new Vector3(-0.08f, 0.82f, 0f));
            }
        }

        // called by CorMeasure after both skinning kernels were dispatched this frame
        public void AfterSkinning()
        {
            float wL, wC, eL, eC, vL, vC;
            lbs.VerticesOut.GetData(buf); wL = Area(buf, wristRing, ringSize) / restWrist; eL = Area(buf, elbowRing, ringSize) / restElbow; vL = Volume(buf) / restVol;
            cor.VerticesOut.GetData(buf); wC = Area(buf, wristRing, ringSize) / restWrist; eC = Area(buf, elbowRing, ringSize) / restElbow; vC = Volume(buf) / restVol;
            float t = FrameCapturer.T;
            title.text = l1.text = l2.text = m1.text = m2.text = "";
            switch (phase)
            {
                case 0:
                    title.text = "Center of rotation skinning";
                    l1.text = "Each vertex turns about its own precomputed centre: blended joints keep their volume";
                    break;
                case 1:
                    title.text = $"Hand twisted {twist:0}° about the forearm";
                    l1.text = "Linear blend skinning"; l2.text = $"wrist cross-section {wL * 100f:0}% of rest";
                    m1.text = "Center of rotation skinning"; m2.text = $"wrist cross-section {wC * 100f:0}% of rest";
                    break;
                case 2:
                    title.text = $"Elbow bent {bend:0}°";
                    l1.text = "Linear blend skinning"; l2.text = $"elbow cross-section {eL * 100f:0}%, arm volume {vL * 100f:0}% of rest";
                    m1.text = "Center of rotation skinning"; m2.text = $"elbow cross-section {eC * 100f:0}%, arm volume {vC * 100f:0}% of rest";
                    break;
                case 3:
                    title.text = $"Same pose, CoR weight {sweep:0.00} (0 = linear blend)";
                    l1.text = $"Elbow {bend:0}°, hand twisted {twist:0}°";
                    l2.text = $"wrist cross-section {wC * 100f:0}%, arm volume {vC * 100f:0}% of rest";
                    break;
                default:
                    title.text = "Center of rotation skinning";
                    l1.text = "Linear blend"; m1.text = "Center of rotation";
                    l2.text = "Unity 6.3 URP, ported from Milk-Drinker01's CoR-Skinning";
                    m2.text = "centres precomputed in the editor, skinned in a compute shader";
                    break;
            }
            if (phase == 0) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            if (phase == 3) { l1.anchor = new Vector2(0.012f, 0.058f); }
            if (phase != logged || t >= nextLog)
            {
                if (phase != logged) Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}'");
                logged = phase; nextLog = t + 0.5f;
                Debug.Log($"[gt] t {t:F2} down {down:F0} bend {bend:F0} twist {twist:F0} sweep {sweep:F2} | LBS wrist {wL:F3} elbow {eL:F3} vol {vL:F3} | CoR wrist {wC:F3} elbow {eC:F3} vol {vC:F3}");
            }
        }
    }

    [DefaultExecutionOrder(100)]
    public class CorMeasure : MonoBehaviour
    {
        public CorDirector director;
        void LateUpdate() { if (director != null) director.AfterSkinning(); }
    }
}

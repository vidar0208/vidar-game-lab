// Drives the cavity post: a stone hall built from code (tiled floor, steps, fluted pillars, panelled wall, a hanging
// cloth, urns, the stand-in mannequin), lit by one low sun and a flat ambient. Each frame draws the normal + depth
// buffer once, then per band runs the cavity pass with that band's settings and renders the scene camera with it.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class CavityDirector : MonoBehaviour
    {
        public Camera src;
        public Material compose;
        public Shader cavityShader;
        public Renderer[] renderers;
        public Transform character;
        public LabelSpec title;
        public LabelSpec[] l1, l2;
        public float flipY = 0f;

        readonly RenderTexture[] rt = new RenderTexture[3];
        readonly System.Collections.Generic.Dictionary<string, Transform> bones = new System.Collections.Generic.Dictionary<string, Transform>();
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> bind = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
        CavityPass pass;
        Material[] mats;
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            for (int i = 0; i < 3; i++) { rt[i] = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt[i].Create(); }
            pass = new CavityPass(cavityShader, W, H);
            var set = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in renderers) foreach (var m in r.sharedMaterials) set.Add(m);
            mats = new Material[set.Count]; set.CopyTo(mats);
            src.enabled = false;
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                if (t != character) bind[t] = t.localRotation;
            }
            // a scene built from code has no ambient probe: a flat ambient as the fill
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(0.30f, 0.32f, 0.38f));
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        void Idle(float t)
        {
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            Turn("Upperarm_L", 77f, Vector3.forward); Turn("Upperarm_R", -77f, Vector3.forward);
            Turn("Upperarm_L", 5f, Vector3.right); Turn("Upperarm_R", 5f, Vector3.right);
            Turn("Lowerarm_L", -10f, Vector3.right); Turn("Lowerarm_R", -10f, Vector3.right);
            Turn("Chest", 1.2f * Mathf.Sin(t * 1.6f), Vector3.right);
            Turn("Head", 8f * Mathf.Sin(t * 0.3f), Vector3.up);
            Turn("Upperleg_L", 3f, Vector3.forward); Turn("Upperleg_R", -3f, Vector3.forward);
        }

        // the settings used in the video: source defaults except curvature scale 2 px, curvature ridge / valley 0.5
        // and cavity distance 0.3 (the defaults draw 1 px lines that are hard to see at 1080p)
        static CavitySettings S(CavitySettings.CavityType type = CavitySettings.CavityType.Both, float dist = 0.3f)
        {
            return new CavitySettings
            {
                cavityType = type, curvatureScale = 2f, curvatureRidge = 0.5f, curvatureValley = 0.5f,
                cavityDistance = dist, cavityRidge = 1.25f, cavityValley = 1.25f, cavitySamples = 4
            };
        }

        // lit band: s == null renders without the cavity term
        void Lit(int i, CavitySettings s)
        {
            var tex = s != null ? pass.Cavity(i, src, s) : null;
            foreach (var m in mats)
            {
                if (tex != null) m.SetTexture("_ScreenSpaceCavityTexture", tex);
                m.SetVector("_SSCScreen", new Vector4(W, H, s != null ? 1 : 0, flipY));
            }
            src.targetTexture = rt[i]; src.Render();
            compose.SetTexture("_B" + i, rt[i]);
        }

        // buffer band: the raw cavity buffer
        void Buf(int i, CavitySettings s) { compose.SetTexture("_B" + i, pass.Cavity(i, src, s)); }

        void Labels(string ttl, float s1, float s2, string[] a, string[] b)
        {
            title.text = ttl;
            float[] start = { 0f, s1, s2 };
            for (int i = 0; i < 3; i++)
            {
                l1[i].text = a != null && i < a.Length ? a[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = b != null && i < b.Length ? b[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
            compose.SetFloat("_S1", s1); compose.SetFloat("_S2", s2);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Idle(t);

            // slow orbit around the hall, then a push in towards the urn, the figure and the cloth
            float a = Mathf.Lerp(-0.36f, 0.16f, Mathf.Clamp01(t / 21f));
            float push = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21f, 26f, t));
            float r = Mathf.Lerp(4.5f, 2.9f, push);
            Vector3 c = Vector3.Lerp(new Vector3(0f, 1.15f, 1.9f), new Vector3(0.1f, 1.0f, 1.5f), push);
            src.transform.position = c + new Vector3(r * Mathf.Sin(a), 0.45f - 0.15f * push, -r * Mathf.Cos(a));
            src.transform.LookAt(c);

            pass.NormalDepth(src, renderers);

            float third = 1f / 3f; int phase;
            var V = Vector4.zero;
            const string dflt = "curvature 2 px, ridge / valley 0.5; cavity distance 0.3, ridge / valley 1.25, 4 samples";
            if (t < 2.5f)
            {
                phase = 0; Lit(0, S());
                compose.SetVector("_Off", Vector4.zero);
                Labels("Screen-space cavity: from depth and normals, darken creases, brighten edges", 1f, 1f,
                    new[] { "Cavity on (type Both)" }, new[] { dflt });
            }
            else if (t < 8.5f)
            {
                phase = 1; Lit(0, null); Lit(1, S());
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Same frame without and with the cavity term (centre of the frame)", 0.5f, 1f,
                    new[] { "Off", "On (type Both)" }, new[] { "plain URP lighting, flat colours", "light x cavity buffer x 4" });
            }
            else if (t < 13f)
            {
                phase = 2; Lit(0, S(CavitySettings.CavityType.Curvature)); Lit(1, S(CavitySettings.CavityType.Cavity)); Lit(2, S());
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Type: curvature (normal differences) vs cavity (sampled depth around the point)", third, 2 * third,
                    new[] { "Curvature", "Cavity", "Both" }, new[] { "4 normal taps, 2 px out", "4 samples within 0.15 m", "product of the two" });
            }
            else if (t < 16.5f)
            {
                phase = 3; Buf(0, S(CavitySettings.CavityType.Curvature)); Buf(1, S(CavitySettings.CavityType.Cavity)); Buf(2, S());
                V = new Vector4(1, 1, 1, 0);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("The buffer itself, shown x 2: mid grey = light unchanged, dark = darkened, light = brightened", third, 2 * third,
                    new[] { "Curvature buffer", "Cavity buffer", "Both buffer" }, new[] { "thin lines on edges and folds", "wider, grainy: 4 random samples", "what the lit pass multiplies by" });
            }
            else if (t < 21f)
            {
                phase = 4; Buf(0, S(CavitySettings.CavityType.Cavity, 0.1f)); Buf(1, S(CavitySettings.CavityType.Cavity, 0.3f)); Buf(2, S(CavitySettings.CavityType.Cavity, 0.8f));
                V = new Vector4(1, 1, 1, 0);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Cavity distance (cavity buffer only): how far the samples reach, radius = distance / 2", third, 2 * third,
                    new[] { "Distance 0.1", "Distance 0.3", "Distance 0.8" }, new[] { "4 samples, reach 0.05 m", "4 samples, reach 0.15 m", "4 samples, reach 0.4 m" });
            }
            else
            {
                phase = 5; Lit(0, S());
                compose.SetVector("_Off", Vector4.zero);
                Labels("Edges and creases from depth and normals, multiplied into sun and ambient alike", 1f, 1f,
                    new[] { "Cavity on (type Both)" }, new[] { dflt });
            }
            compose.SetVector("_View", V);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}

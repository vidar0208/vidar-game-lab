// Drives the rim light post: the stand-in mannequin stands on a pedestal in a dark space, a warm light from behind
// and to the side, and the camera orbits slowly. Each band is a separate render of the same frame with different rim
// settings on the character's two materials; bands show the centre of their frame.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class RimDirector : MonoBehaviour
    {
        public Camera src;
        public Material compose;
        public Material[] mats;   // the character's costume and skin materials (GameTech/Rimlight)
        public Transform character;
        public LabelSpec title;
        public LabelSpec[] l1, l2;
        public Color rimColour = new Color(0f, 0.8344827f, 1f, 1f);
        public float rimIntensity = 1.5f;

        readonly RenderTexture[] rt = new RenderTexture[3];
        readonly System.Collections.Generic.Dictionary<string, Transform> bones = new System.Collections.Generic.Dictionary<string, Transform>();
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> bind = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            for (int i = 0; i < 3; i++) { rt[i] = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt[i].Create(); }
            src.enabled = false;
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                if (t != character) bind[t] = t.localRotation;
            }
            // a scene built from code has no ambient probe: a dim flat ambient as the only fill
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(0.2f, 0.22f, 0.29f));
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // standing idle on the stand-in mannequin (T-pose bind): arms down, slow breathing and a small head turn
        void Idle(float t)
        {
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            Turn("Upperarm_L", 76f, Vector3.forward); Turn("Upperarm_R", -76f, Vector3.forward);
            Turn("Upperarm_L", 6f, Vector3.right); Turn("Upperarm_R", 6f, Vector3.right);
            Turn("Lowerarm_L", -12f, Vector3.right); Turn("Lowerarm_R", -12f, Vector3.right);
            Turn("Chest", 1.5f * Mathf.Sin(t * 1.6f), Vector3.right);
            Turn("Head", 10f * Mathf.Sin(t * 0.35f), Vector3.up);
            Turn("Upperleg_L", 4f, Vector3.forward); Turn("Upperleg_R", -4f, Vector3.forward);
        }

        void Set(bool on = true, float power = 3.5f, float scale = 1f, float bias = 0f, float opacity = 1f,
                 bool pulse = false, float freq = 2f, bool noise = false, float nscale = 1f, Vector3 nspeed = default)
        {
            foreach (var m in mats)
            {
                m.SetFloat("_Enablerimlight", on ? 1 : 0);
                m.SetColor("_Rimlightcolor", rimColour * rimIntensity);
                m.SetFloat("_Rimlightpower", power); m.SetFloat("_Rimlightscale", scale); m.SetFloat("_Rimlightbias", bias);
                m.SetFloat("_Rimlightopacity", opacity);
                m.SetFloat("_Pulsate", pulse ? 1 : 0); m.SetFloat("_Frequency", freq);
                m.SetFloat("_Addnoise", noise ? 1 : 0); m.SetFloat("_Noisescale", nscale); m.SetVector("_Noisespeed", nspeed);
            }
        }

        void Shot(int i) { src.targetTexture = rt[i]; src.Render(); }

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
            float a = (-35f + 8f * t) * Mathf.Deg2Rad;
            src.transform.position = new Vector3(2.7f * Mathf.Sin(a), 1.05f, 2.7f * Mathf.Cos(a));
            src.transform.LookAt(new Vector3(0, 0.74f, 0));

            const string dflt = "power 3.5, scale 1, bias 0, opacity 1";
            const string col = "colour (0, 0.83, 1) x 1.5";
            float third = 1f / 3f; int phase;
            if (t < 3f)
            {
                phase = 0; Set(); Shot(0);
                compose.SetVector("_Off", Vector4.zero);
                Labels("Fresnel rim light: bias + scale x (1 - N.V)^power, added on top of the lit colour", 1f, 1f,
                    new[] { "Rim on" }, new[] { dflt + ", " + col });
            }
            else if (t < 9f)
            {
                phase = 1; Set(on: false); Shot(0); Set(); Shot(1);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Same frame without and with the rim term (centre of the frame)", 0.5f, 1f,
                    new[] { "Rim off", "Rim on" }, new[] { "lit colour only", dflt });
            }
            else if (t < 14f)
            {
                phase = 2; float[] p = { 1.5f, 3.5f, 6f };
                for (int i = 0; i < 3; i++) { Set(power: p[i]); Shot(i); }
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Power: how fast the rim falls off from the silhouette towards the middle", third, 2 * third,
                    new[] { "Power 1.5", "Power 3.5 (default)", "Power 6" }, new[] { "scale 1, opacity 1", "scale 1, opacity 1", "scale 1, opacity 1" });
            }
            else if (t < 19.5f)
            {
                phase = 3; Set(); Shot(0); Set(pulse: true, freq: 4f); Shot(1);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Pulsate: scale x (sin(frequency x time) + 1) / 2", 0.5f, 1f,
                    new[] { "Pulsate off", "Pulsate on, frequency 4" }, new[] { dflt, "4 rad/s = one breath every 1.57 s" });
            }
            else
            {
                phase = 4; Set(power: 2f); Shot(0); Set(power: 2f, noise: true, nscale: 10f, nspeed: new Vector3(0, 2.5f, 0)); Shot(1);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Noise: 3D simplex noise in world space multiplies the rim (0 to 2x)", 0.5f, 1f,
                    new[] { "Noise off (power 2)", "Noise on (power 2)" }, new[] { "scale 1, bias 0, opacity 1", "noise scale 10 (world pos x 10), speed (0, 2.5, 0): drifts down 0.25 m/s" });
            }
            compose.SetTexture("_B0", rt[0]); compose.SetTexture("_B1", rt[1]); compose.SetTexture("_B2", rt[2]);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}

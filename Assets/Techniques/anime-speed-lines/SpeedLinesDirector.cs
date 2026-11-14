// Drives the speed lines post: a stand-in mannequin runs on the spot while the road and the posts stream past, the
// camera pushes in, the scene camera renders into a texture, and the speed lines shader runs over it as a full-screen
// blit (the source's render pass does Blit(camera colour, temp, material) then copies back; here it is one
// Graphics.Blit per band from the director).
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class SpeedLinesDirector : MonoBehaviour
    {
        public Camera src;
        public Material[] lines;   // three instances of the speed lines material, one per band
        public Material compose, road;
        public Transform character;
        public Transform[] posts;
        public LabelSpec title;
        public LabelSpec[] l1, l2;

        public const float Speed = 14f, PostSpacing = 4f, RoadLength = 72f;

        RenderTexture srcRT;
        readonly RenderTexture[] outRT = new RenderTexture[3];
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        float[] postZ;
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); srcRT.Create();
            for (int i = 0; i < 3; i++) { outRT[i] = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); outRT[i].Create(); }
            src.targetTexture = srcRT; src.enabled = false;
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                if (t != character) bind[t] = t.localRotation;
            }
            postZ = new float[posts.Length];
            for (int i = 0; i < posts.Length; i++) postZ[i] = posts[i].position.z;
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // a simple run cycle on the stand-in mannequin (T-pose bind): lean, leg and arm swing, knee and elbow bend
        void Run(float t)
        {
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = t * Mathf.PI * 2f * 1.6f, s = Mathf.Sin(ph);
            character.localPosition = new Vector3(0, 0.05f * Mathf.Abs(Mathf.Cos(ph)), 0);
            Turn("Spine", 14f, Vector3.right); Turn("Chest", 6f, Vector3.right); Turn("Head", -12f, Vector3.right);
            Turn("Upperleg_L", -45f * s, Vector3.right); Turn("Upperleg_R", 45f * s, Vector3.right);
            Turn("Lowerleg_L", 25f + 45f * Mathf.Max(0, Mathf.Sin(ph + 1.4f)), Vector3.right);
            Turn("Lowerleg_R", 25f + 45f * Mathf.Max(0, Mathf.Sin(ph + 1.4f + Mathf.PI)), Vector3.right);
            Turn("Upperarm_L", 75f, Vector3.forward); Turn("Upperarm_R", -75f, Vector3.forward);
            Turn("Upperarm_L", 50f * s, Vector3.right); Turn("Upperarm_R", -50f * s, Vector3.right);
            Turn("Lowerarm_L", -75f, Vector3.right); Turn("Lowerarm_R", -75f, Vector3.right);
        }

        void Set(Material m, float tiling = 200f, float remap = 0.8f, float scroll = 3f, float alpha = 1f, float mask = 1f)
        {
            // shader defaults: tiling 200, radial scale 0.1, power 1, remap 0.8, animation 3, mask scale 1, hardness 0, power 5
            m.SetFloat("_SpeedLinesTiling", tiling); m.SetFloat("_SpeedLinesRemap", remap); m.SetFloat("_SpeedLinesAnimation", scroll);
            m.SetColor("_Colour", new Color(1, 1, 1, alpha)); m.SetFloat("_MaskScale", mask);
        }

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
            // the world streams towards -z past the runner, who stays at the origin facing +z (towards the camera)
            float d = Speed * t;
            for (int i = 0; i < posts.Length; i++)
            {
                float z = Mathf.Repeat(postZ[i] - d + RoadLength - 8f, RoadLength) - (RoadLength - 8f);
                var p = posts[i].position; posts[i].position = new Vector3(p.x, p.y, z);
            }
            road.SetTextureOffset("_BaseMap", new Vector2(0, d / 4f));
            Run(t);
            float push = Mathf.SmoothStep(0f, 1f, Mathf.Repeat(t, 26f) / 26f);
            src.transform.position = new Vector3(0.35f * Mathf.Sin(t * 0.4f), 1.05f + 0.02f * Mathf.Sin(t * 9f), Mathf.Lerp(3.8f, 2.3f, push));
            src.transform.LookAt(new Vector3(0, 0.85f, 0));
            src.Render();

            float third = 1f / 3f; int phase;
            var tex = new Texture[] { outRT[0], outRT[1], outRT[2] };
            string dflt = "remap 0.8, scroll 3, mask 1.0";
            if (t < 3f)
            {
                phase = 0; Set(lines[0]); Graphics.Blit(srcRT, outRT[0], lines[0]);
                Labels("Speed lines: scrolling noise in polar coordinates, kept out of a centre mask", 1f, 1f,
                    new[] { "Shader defaults" }, new[] { "tiling 200, remap 0.8, scroll speed 3, mask scale 1.0, colour white" });
            }
            else if (t < 8f)
            {
                phase = 1; Set(lines[1]); Graphics.Blit(srcRT, outRT[1], lines[1]); tex[0] = srcRT;
                Labels("Same frame without and with the speed lines pass", 0.5f, 1f,
                    new[] { "Off", "On" }, new[] { "scene only", "tiling 200, " + dflt });
            }
            else if (t < 12.5f)
            {
                phase = 2; float[] v = { 80f, 200f, 500f };
                for (int i = 0; i < 3; i++) { Set(lines[i], tiling: v[i]); Graphics.Blit(srcRT, outRT[i], lines[i]); }
                Labels("Tiling: noise cells around the circle (more = denser, thinner lines)", third, 2 * third,
                    new[] { "Tiling 80", "Tiling 200 (default)", "Tiling 500" }, new[] { dflt, dflt, dflt });
            }
            else if (t < 17f)
            {
                phase = 3; float[] v = { 0f, 3f, 10f };
                for (int i = 0; i < 3; i++) { Set(lines[i], scroll: v[i]); Graphics.Blit(srcRT, outRT[i], lines[i]); }
                Labels("Scroll speed: how fast the noise moves along the radius", third, 2 * third,
                    new[] { "Scroll 0 (frozen)", "Scroll 3 (default)", "Scroll 10" }, new[] { "tiling 200, remap 0.8", "tiling 200, remap 0.8", "tiling 200, remap 0.8" });
            }
            else if (t < 21.5f)
            {
                phase = 4; float[] v = { 0.35f, 0.7f, 1f };
                for (int i = 0; i < 3; i++) { Set(lines[i], alpha: v[i]); Graphics.Blit(srcRT, outRT[i], lines[i]); }
                Labels("Opacity: the colour's alpha scales the blend over the scene", third, 2 * third,
                    new[] { "Alpha 0.35", "Alpha 0.7", "Alpha 1.0 (default)" }, new[] { "tiling 200, " + dflt, "tiling 200, " + dflt, "tiling 200, " + dflt });
            }
            else
            {
                phase = 5; float[] v = { 0.6f, 1f, 1.4f };
                for (int i = 0; i < 3; i++) { Set(lines[i], mask: v[i]); Graphics.Blit(srcRT, outRT[i], lines[i]); }
                Labels("Centre mask scale: lines fade in from the centre, full strength at this radius (1 = screen edge)", third, 2 * third,
                    new[] { "Mask 0.6", "Mask 1.0 (default)", "Mask 1.4" }, new[] { "hardness 0, power 5", "hardness 0, power 5", "hardness 0, power 5" });
            }
            compose.SetTexture("_B0", tex[0]); compose.SetTexture("_B1", tex[1]); compose.SetTexture("_B2", tex[2]);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}

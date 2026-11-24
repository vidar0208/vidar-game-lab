// Drives the dual paraboloid post. At start it bakes the HDRI cubemap into a dual paraboloid atlas (one tile per
// hemisphere per mip, tiles the size of a cube face). Camera A renders the props with the cubemap, camera B with the
// atlas (global _GMode). A full-screen quad shows the split. Runs before FrameCapturer (-100).
using UnityEngine;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class DPDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, generate; public Material[] reflMats; public Cubemap cube;
        public LabelSpec title, l1, l2, m1, m2;
        RenderTexture rtA, rtB, atlas; int phase = -1, face;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; r.Create(); return r; }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            camA.allowMSAA = camB.allowMSAA = true;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            face = cube.width;
            atlas = new RenderTexture(face * 4, face * 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            atlas.Create();
            generate.SetTexture("_Cube", cube);
            Graphics.Blit(null, atlas, generate);
            foreach (var m in reflMats) { m.SetTexture("_Cube", cube); m.SetTexture("_DP", atlas); }
            Debug.Log($"[gt] cube face {face} px, mips {cube.mipmapCount}; dual paraboloid atlas {atlas.width} px, tile {face} px");
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            // orbit around the props, then move in on the big sphere, then pull back
            float ang = Mathf.Lerp(35f, -62f, S(t / 15f)) + Mathf.Lerp(0f, -18f, S((t - 15f) / 10f));
            float close = S((t - 14.5f) / 2f) - S((t - 22f) / 2f);
            float rad = Mathf.Lerp(3.1f, 1.45f, close), hgt = Mathf.Lerp(1.15f, 0.95f, close);
            var target = Vector3.Lerp(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 0.75f, 0f), close);
            var pos = target + Quaternion.Euler(0, ang, 0) * new Vector3(0, hgt - target.y, -rad);
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 15f ? 0 : t < 19f ? 1 : t < 22.5f ? 2 : 3;
            Shader.SetGlobalFloat("_GDebug", p == 2 ? 1 : 0);
            Shader.SetGlobalFloat("_GMode", 0); camA.Render();
            Shader.SetGlobalFloat("_GMode", 1); camB.Render();
            float split = 0.5f, line = 1;
            int cubeTexels = 6 * face * face, dpTexels = 2 * face * face;
            switch (p)
            {
                case 0:
                    Labels("Same HDRI, same camera, two environment maps", "Cubemap", $"6 faces x {face} x {face} ({cubeTexels / 1000}k texels per mip)", "Dual paraboloid", $"2 halves x {face} x {face} ({dpTexels / 1000}k texels per mip)");
                    break;
                case 1:
                    Labels("Close-up: where the two halves meet", "Cubemap", "the GPU filters across face edges", "Dual paraboloid", "front and back half meet at the rim, stretched");
                    break;
                case 2:
                    Labels("Which part of the map each pixel reads", "Cubemap: one colour per face", "6 renders to update", "Dual paraboloid: front / back half", "2 renders to update");
                    break;
                default:
                    Labels("Dual paraboloid reflections", "Unity 6.3 URP, after 526077247/Dual-Paraboloid-Reflection-Mapping (MIT)", "HDRI: Studio Small 09, Poly Haven (CC0)", "", "");
                    split = 0f; line = 0;
                    break;
            }
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { foreach (var r in new[] { rtA, rtB, atlas }) if (r) r.Release(); }
    }
}

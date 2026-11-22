// Drives the grass drawing-methods post. The same field (same random positions, sizes and sway) is drawn two ways
// with Graphics.RenderMeshPrimitives from one instance buffer each: billboard cards on layer 8 (seen only by camera A)
// and blade meshes on layer 9 (seen only by camera B). Both cameras render the same view into textures and a
// full-screen quad shows them side by side. Runs before FrameCapturer (execution order -100).
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class GrassDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, cardMat, bladeMat; public Mesh cardMesh, bladeMesh;
        public int cards = 14000, blades = 200000; public Vector2 fieldX = new Vector2(-16f, 16f), fieldZ = new Vector2(-7f, 26f);
        public LabelSpec title, l1, l2, m1, m2;
        GraphicsBuffer cardBuf, bladeBuf; RenderTexture rtA, rtB; int phase = -1;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 }; r.Create(); return r; }

        GraphicsBuffer Fill(int n, int seed, float hMin, float hMax)
        {
            var rnd = new System.Random(seed); var d = new Vector4[n * 2];
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(fieldX.x, fieldX.y, (float)rnd.NextDouble()), z = Mathf.Lerp(fieldZ.x, fieldZ.y, (float)rnd.NextDouble());
                // patches: taller grass in soft clumps
                float clump = Mathf.PerlinNoise(x * 0.18f + 3.1f, z * 0.18f + 7.7f);
                float s = Mathf.Lerp(hMin, hMax, Mathf.Clamp01(clump * 0.8f + (float)rnd.NextDouble() * 0.45f));
                d[i * 2] = new Vector4(x, 0f, z, s);
                d[i * 2 + 1] = new Vector4((float)rnd.NextDouble() * 6.2832f, 0.15f + (float)rnd.NextDouble() * 0.35f, (float)rnd.NextDouble(), (float)rnd.NextDouble() * 6.2832f);
            }
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n * 2, 16); b.SetData(d); return b;
        }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            // same seed: both methods start from the same positions; blades get many more instances than cards
            cardBuf = Fill(cards, 7, 0.32f, 0.5f); bladeBuf = Fill(blades, 7, 0.22f, 0.5f);
            cardMat.SetBuffer("_Inst", cardBuf); bladeMat.SetBuffer("_Inst", bladeBuf);
            Shader.SetGlobalVector("_GWind", new Vector4(0.8f, 0f, 0.6f, 0f));
            Debug.Log($"[gt] grass: {cards} cards x {cardMesh.triangles.Length / 3} tris, {blades} blades x {bladeMesh.triangles.Length / 3} tris");
        }

        void Draw(Material m, Mesh mesh, int count, int layer)
        {
            var rp = new RenderParams(m)
            {
                worldBounds = new Bounds(new Vector3(0, 0.5f, 9.5f), new Vector3(40f, 4f, 40f)),
                layer = layer, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true
            };
            Graphics.RenderMeshPrimitives(rp, mesh, 0, count);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            // low glide through the field, then rise to look down, then settle back to a mid height
            float up = S((t - 14f) / 2.5f) - S((t - 18.8f) / 2.0f) * 0.55f - S((t - 22.3f) / 1.2f) * 0.45f;
            var pos = new Vector3(Mathf.Lerp(-1.2f, 1.0f, S(t / 25f)), Mathf.Lerp(1.05f, 4.6f, up), Mathf.Lerp(-4.2f, 0.8f, t / 25f));
            float pitch = Mathf.Lerp(9f, 62f, up);
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, Mathf.Lerp(-8f, 10f, S(t / 25f)), 0));
            Shader.SetGlobalVector("_GCamFwd", camA.transform.forward); Shader.SetGlobalFloat("_GTime", t);
        }

        void LateUpdate()
        {
            if (cardBuf == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 14f ? 1 : t < 19f ? 2 : t < 22.5f ? 3 : 4;
            float dbg = p == 3 ? 1 : 0; cardMat.SetFloat("_DebugInst", dbg); bladeMat.SetFloat("_DebugInst", dbg);
            Draw(cardMat, cardMesh, cards, 8); Draw(bladeMat, bladeMesh, blades, 9);
            camA.Render(); camB.Render();
            float split = 0.5f, line = 1;
            int bt = bladeMesh.triangles.Length / 3;
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("Two ways to draw grass", $"Blade meshes, {blades:N0} instances", "same field, same light", "", "");
                    break;
                case 1:
                    split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same field, same light, same camera", "Billboard cards", $"{cards:N0} alpha-tested quads facing the camera plane", "Blade meshes", $"{blades:N0} instances, {bt} triangles each");
                    break;
                case 2:
                    Labels("From above", "Cards", "upright planes: from above they go thin", "Blades", "real geometry keeps its shape");
                    break;
                case 3:
                    Labels("One colour per instance", $"{cards:N0} cards", "the texture draws the blades", $"{blades:N0} blades", "the geometry draws the blades");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Billboard cards vs blade meshes", "Unity 6.3 URP, after daniel-ilett/shaders-6grass (MIT)", "no geometry shader version: Metal does not support them", "", "");
                    break;
            }
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { cardBuf?.Release(); bladeBuf?.Release(); foreach (var r in new[] { rtA, rtB }) if (r) r.Release(); }
    }
}

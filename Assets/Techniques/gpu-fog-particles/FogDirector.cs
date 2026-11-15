// Drives the GPU fog particles post. The fog is ordinary Unity particle systems (camera-facing quads, no texture)
// drawn with the source's URP fog shader, which builds each quad's alpha from noise computed in the fragment shader.
// Ground fog sits on layer 9, falling fog on layer 10. Up to three band cameras render the same view each frame; a
// band hides fog by leaving its layer out of the culling mask, and noise / soft-fade settings are changed on the two
// fog materials between band renders, so every band shows the same particles.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class FogDirector : MonoBehaviour
    {
        public Camera[] cams;
        public Material compose, groundMat, fallMat;
        public ParticleSystem ground;
        public ParticleSystem[] falling;
        public LabelSpec title;
        public LabelSpec[] l1, l2;
        public Color ambient;
        public Transform character;
        public float startZ = 14f, endZ = -2f;
        public string groundSpec = "", fallSpec = "";

        public const int GroundLayer = 9, FallLayer = 10;
        const int Off = 0, GroundOnly = 1 << GroundLayer, FallOnly = 1 << FallLayer, Both = GroundOnly | FallOnly;
        readonly System.Collections.Generic.Dictionary<string, Transform> bones = new();
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> bind = new();
        readonly RenderTexture[] rt = new RenderTexture[3];
        float gSimple, gSimplex, gVor, fSimple, fSimplex, fVor, gSoft, fSoft;
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            for (int i = 0; i < 3; i++) { rt[i] = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt[i].Create(); }
            foreach (var c in cams) c.enabled = false;
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(ambient);
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
            foreach (var tr in character.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(tr.name)) bones[tr.name] = tr;
                if (tr != character) bind[tr] = tr.localRotation;
            }
            gSimple = groundMat.GetFloat("_SimpleNoiseAmount"); gSimplex = groundMat.GetFloat("_SimplexNoiseAmount"); gVor = groundMat.GetFloat("_VoronoiNoiseAmount");
            fSimple = fallMat.GetFloat("_SimpleNoiseAmount"); fSimplex = fallMat.GetFloat("_SimplexNoiseAmount"); fVor = fallMat.GetFloat("_VoronoiNoiseAmount");
            gSoft = groundMat.GetFloat("_SurfaceDepthFade"); fSoft = fallMat.GetFloat("_SurfaceDepthFade");
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // stand-in mannequin walking slowly along the path towards the camera
        void Walk(float t)
        {
            float z = Mathf.Lerp(startZ, endZ, t / 26f);
            float p = t * 2f * Mathf.PI * 0.85f;
            character.position = new Vector3(0f, 0.012f * Mathf.Cos(2f * p), z);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            Turn("Upperarm_L", 77f, Vector3.forward); Turn("Upperarm_R", -77f, Vector3.forward);
            Turn("Upperarm_L", 14f * Mathf.Sin(p), Vector3.right); Turn("Upperarm_R", -14f * Mathf.Sin(p), Vector3.right);
            Turn("Lowerarm_L", -12f, Vector3.right); Turn("Lowerarm_R", -12f, Vector3.right);
            Turn("Upperleg_L", -20f * Mathf.Sin(p), Vector3.right); Turn("Upperleg_R", 20f * Mathf.Sin(p), Vector3.right);
            Turn("Lowerleg_L", 6f + 26f * Mathf.Max(0f, Mathf.Cos(p)), Vector3.right);
            Turn("Lowerleg_R", 6f + 26f * Mathf.Max(0f, -Mathf.Cos(p)), Vector3.right);
            Turn("Chest", 3f * Mathf.Sin(p), Vector3.up);
            Turn("Head", 4f, Vector3.right);
        }

        // noise: 0 none (radial mask only), 1 Voronoi only, 2 all three layers as tuned
        void Noise(int n)
        {
            groundMat.SetFloat("_SimpleNoiseAmount", n == 2 ? gSimple : 0f); groundMat.SetFloat("_SimplexNoiseAmount", n == 2 ? gSimplex : 0f);
            groundMat.SetFloat("_VoronoiNoiseAmount", n >= 1 ? gVor : 0f);
            fallMat.SetFloat("_SimpleNoiseAmount", n == 2 ? fSimple : 0f); fallMat.SetFloat("_SimplexNoiseAmount", n == 2 ? fSimplex : 0f);
            fallMat.SetFloat("_VoronoiNoiseAmount", n >= 1 ? fVor : 0f);
        }

        void Shot(int i, int fogMask, int noise = 2, bool soft = true)
        {
            Noise(noise);
            groundMat.SetFloat("_SurfaceDepthFade", soft ? gSoft : 0f); fallMat.SetFloat("_SurfaceDepthFade", soft ? fSoft : 0f);
            var c = cams[i];
            c.cullingMask = ~((1 << 8) | Both) | fogMask;
            c.targetTexture = rt[i]; c.Render();
            compose.SetTexture("_B" + i, rt[i]);
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
            Walk(t);
            int nG = ground.particleCount, nF = 0;
            foreach (var f in falling) nF += f.particleCount;

            // camera walks backwards ahead of the character, low, looking back along the path
            float z = character.position.z;
            Vector3 pos = new Vector3(0.45f, 1.05f + 0.1f * Mathf.Sin(t * 0.25f), z - 5.4f);
            Vector3 look = new Vector3(-0.15f, 0.95f, z + 1.2f);
            foreach (var c in cams) { c.transform.position = pos; c.transform.LookAt(look); }

            float third = 1f / 3f; int phase;
            string gN = $"{nG} quads", fN = $"{nF} quads";
            if (t < 3f)
            {
                phase = 0; Shot(0, Both);
                compose.SetVector("_Off", Vector4.zero);
                Labels("Textureless fog: particle quads whose shape is noise computed in the shader", 1f, 1f,
                    new[] { "Fog on" }, new[] { $"ground {gN} + falling {fN}, no textures" });
            }
            else if (t < 10f)
            {
                phase = 1; Shot(0, Off); Shot(1, Both);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Same frame, fog particles off and on (centre of the frame)", 0.5f, 1f,
                    new[] { "Fog off", "Fog on" }, new[] { "particle systems not rendered", $"{nG + nF} quads, shader noise" });
            }
            else if (t < 16f)
            {
                phase = 2; Shot(0, GroundOnly); Shot(1, FallOnly); Shot(2, Both);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Two emitters, one shader", third, 2 * third,
                    new[] { $"Ground fog: {gN}", $"Falling fog: {fN}", "Both" }, new[] { groundSpec, fallSpec, $"{nG + nF} quads" });
            }
            else if (t < 21f)
            {
                phase = 3; Shot(0, Both, 0); Shot(1, Both, 1); Shot(2, Both, 2);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Same quads, noise layers in the shader", third, 2 * third,
                    new[] { "No noise", "1 layer", "3 layers" },
                    new[] { "radial mask only", $"Voronoi {gVor:0.##}", $"value {gSimple:0.##} x simplex {gSimplex:0.##} x Voronoi {gVor:0.##}" });
            }
            else
            {
                phase = 4; Shot(0, Both);
                compose.SetVector("_Off", Vector4.zero);
                Labels("Walking out of the bank: the fog is only where the quads are", 1f, 1f,
                    new[] { "Fog on" }, new[] { $"ground {gN} + falling {fN}, depth fade {gSoft:0.##} m" });
            }
            Noise(2);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}

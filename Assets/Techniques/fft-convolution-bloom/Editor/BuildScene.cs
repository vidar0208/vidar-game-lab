// 87 FFT convolution bloom: a dark room with strong light sources (a row of bulbs, a neon ring, two neon tubes, a moving
// lantern with a point light) plus chrome and glossy props that catch highlights. URP's Bloom + ACES sit in a volume
// that only the comparison render uses; the FFT path renders without post-processing into HDR.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class FFTBloomScene
    {
        const string Dir = "Assets/Techniques/fft-convolution-bloom/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Mat(string name, Color c, float metal = 0f, float smooth = 0.4f) => SceneKit.Mat(Dir, name, Lit, c, metal, smooth);

        static Material Glow(string name, Color c, float power)
        {
            // keyword and colour are set before the asset is created, otherwise the saved material loses _EMISSION
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", c * 0.2f); m.SetFloat("_Smoothness", 0.5f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * power);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = Quaternion.Euler(euler);
            return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.03f, 0.035f, 0.05f), new Color(0.02f, 0.02f, 0.025f), new Color(0.01f, 0.01f, 0.01f));
            SceneKit.Shadows(25f);
            RenderSettings.skybox = null; RenderSettings.reflectionIntensity = 0.15f; // dark room: little environment reflection
            var moon = SceneKit.Sun("Moon", new Vector3(35, 150, 0), new Color(0.55f, 0.65f, 1f), 0.05f, true);
            RenderSettings.sun = moon;

            Prim(PrimitiveType.Plane, "Floor", Mat("Floor", new Color(0.04f, 0.04f, 0.045f), 0f, 0.82f), Vector3.zero, new Vector3(3, 1, 3));
            var wall = Mat("Wall", new Color(0.06f, 0.06f, 0.07f), 0f, 0.3f);
            Prim(PrimitiveType.Cube, "Back wall", wall, new Vector3(0, 3f, 5.5f), new Vector3(18f, 6f, 0.4f));
            Prim(PrimitiveType.Cube, "Left wall", wall, new Vector3(-7f, 3f, 0f), new Vector3(0.4f, 6f, 12f));
            Prim(PrimitiveType.Cube, "Right wall", wall, new Vector3(7f, 3f, 0f), new Vector3(0.4f, 6f, 12f));

            // a row of small bulbs on a wire
            var bulb = Glow("Bulb", new Color(1f, 0.72f, 0.42f), 10f);
            for (int i = 0; i < 9; i++)
            {
                float x = -4f + i;
                Prim(PrimitiveType.Sphere, "Bulb", bulb, new Vector3(x, 3.1f - 0.12f * Mathf.Cos(x * 0.6f), 3.8f), Vector3.one * 0.12f);
            }
            Prim(PrimitiveType.Cube, "Wire", Mat("Wire", new Color(0.02f, 0.02f, 0.02f)), new Vector3(0, 3.18f, 3.8f), new Vector3(9f, 0.015f, 0.015f));

            // neon ring (lathe torus) facing the camera, and two neon tubes
            var prof = new Vector2[24];
            for (int i = 0; i < prof.Length; i++) { float a = i * Mathf.PI * 2f / prof.Length; prof[i] = new Vector2(0.035f * Mathf.Sin(a), 0.75f + 0.035f * Mathf.Cos(a)); }
            var ringMesh = new MeshKit().Lathe(prof, 72).Build("Ring", 60f); AssetDatabase.CreateAsset(ringMesh, Dir + "Ring.asset");
            var ring = SceneKit.Part("Neon ring", ringMesh, Glow("Neon cyan", new Color(0.25f, 0.85f, 1f), 5f), new Vector3(0, 1.9f, 4.2f), Quaternion.Euler(90, 0, 0));
            var ringPivot = new GameObject("Ring pivot"); ringPivot.transform.position = ring.transform.position; ring.transform.SetParent(ringPivot.transform, true);
            Prim(PrimitiveType.Cube, "Neon tube", Glow("Neon magenta", new Color(1f, 0.25f, 0.7f), 5f), new Vector3(-3.6f, 1.6f, 4.6f), new Vector3(0.05f, 2.4f, 0.05f));
            Prim(PrimitiveType.Cube, "Neon tube", Glow("Neon amber", new Color(1f, 0.55f, 0.15f), 5f), new Vector3(3.6f, 1.6f, 4.6f), new Vector3(0.05f, 2.4f, 0.05f));

            // props that catch highlights
            var chrome = Mat("Chrome", new Color(0.9f, 0.9f, 0.92f), 1f, 0.92f);
            Prim(PrimitiveType.Sphere, "Chrome sphere", chrome, new Vector3(-1.6f, 0.45f, 1.8f), Vector3.one * 0.9f);
            Prim(PrimitiveType.Sphere, "Chrome sphere", chrome, new Vector3(1.9f, 0.3f, 2.4f), Vector3.one * 0.6f);
            var block = Mat("Block", new Color(0.35f, 0.32f, 0.3f), 0f, 0.6f);
            Prim(PrimitiveType.Cube, "Block", block, new Vector3(-3.2f, 0.4f, 2.8f), new Vector3(0.8f, 0.8f, 0.8f), new Vector3(0, 20, 0));
            Prim(PrimitiveType.Cube, "Block", block, new Vector3(3.3f, 0.6f, 1.6f), new Vector3(0.6f, 1.2f, 0.6f), new Vector3(0, -15, 0));
            Prim(PrimitiveType.Cylinder, "Column", Mat("Column", new Color(0.5f, 0.12f, 0.1f), 0f, 0.7f), new Vector3(0.5f, 0.5f, 2.9f), new Vector3(0.5f, 0.5f, 0.5f));

            // moving lantern with a point light
            var lantern = Prim(PrimitiveType.Sphere, "Lantern", Glow("Lantern", new Color(1f, 0.85f, 0.6f), 16f), new Vector3(0, 1f, 0.6f), Vector3.one * 0.16f);
            var pl = new GameObject("Lantern light").AddComponent<Light>();
            pl.type = LightType.Point; pl.color = new Color(1f, 0.8f, 0.55f); pl.intensity = 3f; pl.range = 5f; pl.shadows = LightShadows.None;
            pl.transform.SetParent(lantern.transform, false);
            var rl = new GameObject("Ring light").AddComponent<Light>();
            rl.type = LightType.Point; rl.color = new Color(0.3f, 0.8f, 1f); rl.intensity = 2f; rl.range = 5f; rl.shadows = LightShadows.None;
            rl.transform.position = new Vector3(0, 1.9f, 3.9f);

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 42, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.SolidColor; srcCam.backgroundColor = Color.black; srcCam.cullingMask = ~(1 << 8); srcCam.allowHDR = true;

            // URP Bloom + ACES only, for the comparison render
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, Dir + "Post.asset");
            var b = SceneKit.Add<Bloom>(profile); b.intensity.Override(1f); b.threshold.Override(1f); b.scatter.Override(0.7f);
            var tone = SceneKit.Add<Tonemapping>(profile); tone.mode.Override(TonemappingMode.ACES);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            var vol = new GameObject("Post volume").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;

            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/FFTBloomCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            Material M(string shader, string file) { var m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, Dir + file); return m; }
            var dir = new GameObject("Director").AddComponent<FFTBloomDirector>();
            dir.src = srcCam; dir.compose = compose;
            dir.fft = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "FFTCS.compute");
            dir.kernelGenerate = M("Hidden/GT/FFTBloomKernel", "KernelGenerate.mat");
            dir.sourceGenerate = M("Hidden/GT/FFTBloomSource", "SourceGenerate.mat");
            dir.finalBlit = M("Hidden/GT/FFTBloomFinal", "FinalBlit.mat");
            dir.lantern = lantern.transform; dir.ring = ringPivot.transform;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
        }
    }
}

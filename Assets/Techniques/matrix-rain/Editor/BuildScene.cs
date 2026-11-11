// 75 digital rain: IA on a plinth in a dark hall with an arch, slabs, a sphere and a tilted cube.
// Every surface has a second renderer with the additive rain material, as the source scene overlays its mesh.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class MatrixScene
    {
        const string Dir = "Assets/Techniques/matrix-rain/";
        const string Base = "GameTech/MatrixBaseLit";
        static readonly Dictionary<Texture, Material> rainMats = new Dictionary<Texture, Material>();
        static Material rainPlain;
        static int overlays;

        static Material RainFor(Material src)
        {
            var tex = src != null && src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
            if (tex == null) return rainPlain;
            if (!rainMats.TryGetValue(tex, out var m))
            {
                m = new Material(Shader.Find("GameTech/MatrixRain")); m.SetTexture("_BaseMap", tex);
                AssetDatabase.CreateAsset(m, Dir + "Rain_" + tex.name + ".mat");
                rainMats[tex] = m;
            }
            return m;
        }

        // second renderer on the same mesh (and the same bones for a skinned mesh) with the rain material
        static void AddRain(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = RainFor(src[i]);
                var go = new GameObject(r.name + " rain"); go.transform.SetParent(r.transform, false);
                Renderer d;
                if (r is SkinnedMeshRenderer s)
                {
                    var k = go.AddComponent<SkinnedMeshRenderer>();
                    k.sharedMesh = s.sharedMesh; k.bones = s.bones; k.rootBone = s.rootBone; k.localBounds = s.localBounds; k.updateWhenOffscreen = true;
                    d = k;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    d = go.AddComponent<MeshRenderer>();
                }
                d.sharedMaterials = dst;
                d.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; d.receiveShadows = false;
                overlays++;
            }
        }

        static GameObject Prop(string name, Mesh mesh, Material m, Vector3 pos, Quaternion rot)
        {
            var go = SceneKit.Part(name, mesh, m, pos, rot);
            AddRain(go);
            return go;
        }

        public static void Build()
        {
            rainMats.Clear(); overlays = 0;
            rainPlain = new Material(Shader.Find("GameTech/MatrixRain"));
            AssetDatabase.CreateAsset(rainPlain, Dir + "Rain_plain.mat");

            SceneKit.Ambient(new Color(0.10f, 0.12f, 0.13f), new Color(0.07f, 0.08f, 0.09f), new Color(0.03f, 0.035f, 0.035f));
            SceneKit.Shadows(18f);
            SceneKit.Sun("Key light", new Vector3(38, 205, 0), new Color(0.95f, 0.97f, 1f), 1.35f, true);
            SceneKit.Sun("Rim light", new Vector3(18, 20, 0), new Color(0.55f, 0.8f, 1f), 0.55f, false);
            SceneKit.Sun("Fill light", new Vector3(15, 130, 0), new Color(0.6f, 0.7f, 0.75f), 0.3f, false);

            var stone = SceneKit.Mat(Dir, "Stone", Base, new Color(0.30f, 0.31f, 0.33f), 0f, 0.35f);
            var dark = SceneKit.Mat(Dir, "Dark", Base, new Color(0.11f, 0.115f, 0.125f), 0f, 0.45f);
            var metal = SceneKit.Mat(Dir, "Metal", Base, new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.6f);

            // ---- hall
            Prop("Floor", new MeshKit().Box(new Vector3(0, -0.1f, 0), new Vector3(26, 0.2f, 26)).Build("Floor"), dark, Vector3.zero, Quaternion.identity);
            Prop("Back wall", new MeshKit().Box(new Vector3(0, 3.5f, -5.2f), new Vector3(20, 7, 0.4f)).Build("BackWall"), dark, Vector3.zero, Quaternion.identity);
            var plinth = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 1.0f), new Vector2(0.10f, 1.0f), new Vector2(0.14f, 0.9f), new Vector2(0.14f, 0) }, 72).Build("Plinth");
            Prop("Plinth", plinth, stone, Vector3.zero, Quaternion.identity);

            // ---- arch behind IA
            var arch = new MeshKit();
            arch.Box(new Vector3(-1.45f, 1.5f, -2.3f), new Vector3(0.5f, 3.0f, 0.5f));
            arch.Box(new Vector3(1.45f, 1.5f, -2.3f), new Vector3(0.5f, 3.0f, 0.5f));
            arch.Box(new Vector3(0, 3.25f, -2.3f), new Vector3(3.9f, 0.5f, 0.6f));
            Prop("Arch", arch.Build("Arch"), stone, Vector3.zero, Quaternion.identity);

            // ---- slabs, sphere, cube, low steps
            Prop("Slab left", new MeshKit().Box(Vector3.zero, new Vector3(0.95f, 2.3f, 0.35f)).Build("SlabL"), stone, new Vector3(-2.7f, 1.15f, -0.9f), Quaternion.Euler(0, 28, 0));
            Prop("Slab right", new MeshKit().Box(Vector3.zero, new Vector3(0.85f, 1.9f, 0.32f)).Build("SlabR"), stone, new Vector3(2.8f, 0.95f, -0.5f), Quaternion.Euler(0, -32, 0));
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.name = "Sphere"; sphere.transform.position = new Vector3(2.35f, 0.45f, 0.35f); sphere.transform.localScale = Vector3.one * 0.9f;
            sphere.GetComponent<Renderer>().sharedMaterial = metal; AddRain(sphere);
            Prop("Cube", new MeshKit().Box(Vector3.zero, Vector3.one * 0.7f).Build("Cube"), stone, new Vector3(-1.8f, 0.52f, 1.0f), Quaternion.Euler(22, 35, 14));
            var steps = new MeshKit();
            for (int i = 0; i < 3; i++) steps.Box(new Vector3(0, 0.09f + i * 0.18f, -3.4f - i * 0.45f), new Vector3(6.5f - i * 0.5f, 0.18f, 0.45f));
            Prop("Steps", steps.Build("Steps"), stone, Vector3.zero, Quaternion.identity);

            // ---- IA, normal standing pose on the plinth
            var ia = IAKit.Spawn(Base, Dir, (m, n) => { m.SetFloat("_Smoothness", 0.2f); });
            ia.transform.position = new Vector3(0, 0.14f, 0);
            AddRain(ia);

            // ---- screen-space version: one full-viewport quad, only the right camera of the split sees its layer
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Screen rain quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 9;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "ScreenQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "ScreenQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm;
            var sm = new Material(Shader.Find("GameTech/MatrixScreen")); AssetDatabase.CreateAsset(sm, Dir + "ScreenRain.mat");
            var qr = quad.GetComponent<Renderer>(); qr.sharedMaterial = sm; qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.SetActive(false);

            var dr = new GameObject("Director").AddComponent<MatrixDirector>();
            dr.whiteNoiseGenerator = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "GenerateWhiteNoise.compute");
            if (dr.whiteNoiseGenerator == null) Debug.LogError("[gt] compute shader not found");
            dr.screenQuad = quad;
            var bg = new Color(0.004f, 0.007f, 0.006f);
            dr.camL = SceneKit.Cam("Camera left", new Rect(0, 0, 0.5f, 1), 34, bg, 0); dr.camL.cullingMask = ~(1 << 9);
            dr.camR = SceneKit.Cam("Camera right", new Rect(0.5f, 0, 0.5f, 1), 34, bg, 1);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 34, bg, 2); dr.camFull.cullingMask = ~(1 << 9); dr.camFull.tag = "MainCamera";

            SceneKit.Grade(Dir, 0.55f, 0.1f, 12f, 6f, 0.3f, 0f);

            LabelSpec.Create("OFF: plain lit material", 0.03f, 0.05f).During(2.0f, 6.5f);
            LabelSpec.Create("ON: triplanar rain, 6.9 cm glyph cells", 0.53f, 0.05f).During(2.0f, 6.5f);
            LabelSpec.Create("Step 1: rain overlay opens from the screen centre, 0.4 per second", 0.03f, 0.05f).During(6.8f, 9.6f);
            LabelSpec.Create("Step 2: the surface is discarded, additive rain stays", 0.03f, 0.05f).During(9.6f, 13f);
            LabelSpec.Create("One XY projection: smears off-axis", 0.03f, 0.05f).During(13.2f, 15.5f);
            LabelSpec.Create("Three projections, weights normal^10", 0.53f, 0.05f).During(13.2f, 15.5f);
            LabelSpec.Create("Triplanar: grid from world position", 0.03f, 0.05f).During(15.7f, 18f);
            LabelSpec.Create("Screen space: grid in pixels (26.7 px)", 0.53f, 0.05f).During(15.7f, 18f);
            LabelSpec.Create("Coloured variant: one Lab colour per drop", 0.03f, 0.05f).During(18.1f, 20.1f);
            Debug.Log($"[gt] matrix scene: {overlays} rain overlay renderers, {rainMats.Count + 1} rain materials");
        }
    }
}

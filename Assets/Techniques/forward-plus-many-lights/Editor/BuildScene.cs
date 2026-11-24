// Builds the many-lights plaza from code: 1,000 point lights with small glowing markers, one floor mesh plus a
// tiled copy of the floor (2 m tiles, on its own layer), pillars, fog, cameras and labels.
// It also adds a Forward copy of the project's Forward+ renderer to the active URP asset (renderer list) and sets
// the asset's per-object light limit to 8, URP's maximum, so the Forward side gets its best case.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class ManyLightsScene
    {
        const string Dir = "Assets/Techniques/forward-plus-many-lights/";
        const int TileLayer = 28, WholeLayer = 29, LightCount = 1000;

        static int AddForwardRenderer(UniversalRenderPipelineAsset rp)
        {
            var so = new SerializedObject(rp);
            var list = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData d && d.renderingMode == RenderingMode.Forward) { Limit(so); return i; }
            var srcPath = AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(0).objectReferenceValue);
            AssetDatabase.CopyAsset(srcPath, Dir + "ForwardRenderer.asset");
            var fwd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Dir + "ForwardRenderer.asset");
            fwd.renderingMode = RenderingMode.Forward; EditorUtility.SetDirty(fwd);
            list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fwd;
            Limit(so);
            return list.arraySize - 1;
        }

        static void Limit(SerializedObject so)
        {
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 8;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(so.targetObject);
        }

        static GameObject Prim(PrimitiveType t, string name, Material m, Vector3 pos, Vector3 scale, int layer = 0)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.position = pos; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = m; g.layer = layer;
            return g;
        }

        public static void Build()
        {
            var rp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            int fwdIndex = AddForwardRenderer(rp);
            int fplusIndex = 0;
            var rdl = rp.rendererDataList;
            for (int i = 0; i < rdl.Length; i++) if (rdl[i] is UniversalRendererData d && d.renderingMode == RenderingMode.ForwardPlus) { fplusIndex = i; break; }
            Debug.Log($"[gt] URP asset {rp.name}: Forward renderer {fwdIndex}, Forward+ renderer {fplusIndex}, per-object limit {rp.maxAdditionalLightsCount}");

            RenderSettings.skybox = null;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 11f; RenderSettings.fogEndDistance = 29f;
            RenderSettings.fogColor = new Color(0.004f, 0.004f, 0.008f);
            SceneKit.Ambient(new Color(0.012f, 0.012f, 0.02f), new Color(0.008f, 0.008f, 0.012f), new Color(0.004f, 0.004f, 0.006f));
            SceneKit.Grade(Dir, 0.6f, 0f, 12f, 10f, 0.3f, 0.03f);

            var floorMat = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.24f, 0.24f, 0.26f), 0f, 0.5f);
            var pillarMat = SceneKit.Mat(Dir, "Pillar", "Universal Render Pipeline/Lit", new Color(0.32f, 0.31f, 0.3f), 0f, 0.3f);
            Prim(PrimitiveType.Plane, "Floor (one mesh)", floorMat, Vector3.zero, new Vector3(8f, 1f, 8f), WholeLayer);
            var tiles = new GameObject("Floor tiles"); int tileCount = 0;
            for (int x = 0; x < 40; x++)
                for (int z = 0; z < 40; z++)
                {
                    var q = Prim(PrimitiveType.Quad, "Tile", floorMat, new Vector3(-39f + x * 2f, 0f, -39f + z * 2f), new Vector3(2f, 2f, 1f), TileLayer);
                    q.transform.rotation = Quaternion.Euler(90f, 0, 0); q.transform.SetParent(tiles.transform, true); tileCount++;
                }
            var pillars = new GameObject("Pillars");
            for (int x = -6; x <= 6; x++)
                for (int z = -6; z <= 6; z++)
                    Prim(PrimitiveType.Cube, "Pillar", pillarMat, new Vector3(x * 6f + 3f, 1.6f, z * 6f + 3f), new Vector3(0.5f, 3.2f, 0.5f)).transform.SetParent(pillars.transform, true);

            var dir = new GameObject("Director").AddComponent<ManyLightsDirector>();
            dir.forwardIndex = fwdIndex; dir.forwardPlusIndex = fplusIndex; dir.tileLayer = TileLayer; dir.wholeLayer = WholeLayer; dir.tileCount = tileCount;
            var rng = new System.Random(138);
            float R() => (float)rng.NextDouble();
            var markerMats = new Material[12];
            var colors = new Color[12];
            for (int k = 0; k < 12; k++)
            {
                colors[k] = Color.HSVToRGB(k / 12f, 0.8f, 1f);
                markerMats[k] = SceneKit.Mat(Dir, "Marker" + k, "Universal Render Pipeline/Unlit", colors[k] * 2.2f, 0f, 0f);
            }
            dir.lights = new Light[LightCount]; dir.orbit = new Vector4[LightCount]; dir.heights = new float[LightCount]; dir.phases = new float[LightCount];
            var root = new GameObject("Lights");
            for (int i = 0; i < LightCount; i++)
            {
                int k = rng.Next(12);
                var g = new GameObject("Point light " + i); g.transform.SetParent(root.transform, false);
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 3.5f; l.intensity = 3f; l.color = colors[k]; l.shadows = LightShadows.None;
                var mk = Prim(PrimitiveType.Sphere, "Marker", markerMats[k], Vector3.zero, Vector3.one * 0.12f);
                mk.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; mk.transform.SetParent(g.transform, false);
                dir.lights[i] = l;
                dir.orbit[i] = new Vector4(-38f + 76f * R(), -38f + 76f * R(), 0.4f + 1.2f * R(), (R() < 0.5f ? -1f : 1f) * (0.3f + 0.6f * R()));
                dir.heights[i] = 0.3f + 1.5f * R(); dir.phases[i] = R() * 6.2832f;
                g.transform.position = new Vector3(dir.orbit[i].x, dir.heights[i], dir.orbit[i].y);
            }

            var bg = new Color(0.004f, 0.004f, 0.008f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 45f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 45f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 45f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 45f, bg, 2, true);
            foreach (var cam in new[] { dir.camFull, dir.camL, dir.camR }) { cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 32); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 32); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built many-lights scene: {LightCount} lights, {tileCount} tiles");
        }
    }
}

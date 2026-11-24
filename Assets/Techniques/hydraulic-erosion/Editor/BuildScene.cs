// Builds the erosion scene from code: one 256 x 256 grid mesh (uv = grid coordinates; the terrain shader moves the
// vertices to the height the erosion buffers hold), a gradient sky with a low warm sun, shadows, the two scene
// cameras and the display camera with the composite quad.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class ErosionScene
    {
        const string Dir = "Assets/Techniques/hydraulic-erosion/";

        static Mesh Grid(int n, float scale, float elev)
        {
            var v = new Vector3[n * n]; var uv = new Vector2[n * n]; var tri = new int[(n - 1) * (n - 1) * 6]; int t = 0;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int i = y * n + x; uv[i] = new Vector2(x, y);
                v[i] = new Vector3((x / (n - 1f) * 2 - 1) * scale, 0, (y / (n - 1f) * 2 - 1) * scale);
                if (x < n - 1 && y < n - 1) { tri[t++] = i + n; tri[t++] = i + n + 1; tri[t++] = i; tri[t++] = i + n + 1; tri[t++] = i + 1; tri[t++] = i; }
            }
            var m = new Mesh { name = "Terrain grid", indexFormat = IndexFormat.UInt32 };
            m.vertices = v; m.uv = uv; m.triangles = tri;
            var nn = new Vector3[n * n]; for (int i = 0; i < nn.Length; i++) nn[i] = Vector3.up; m.normals = nn;
            m.bounds = new Bounds(new Vector3(0, elev * 0.5f, 0), new Vector3(scale * 2, elev * 1.2f, scale * 2));
            AssetDatabase.CreateAsset(m, Dir + "TerrainGrid.asset"); return m;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            var skyM = new Material(Shader.Find("GameTech/ErosionSky")); AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere); sky.name = "Sky"; Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.transform.localScale = Vector3.one * 800f; var smr = sky.GetComponent<MeshRenderer>(); smr.sharedMaterial = skyM; smr.shadowCastingMode = ShadowCastingMode.Off;

            var terrainM = new Material(Shader.Find("GameTech/ErosionTerrain")); AssetDatabase.CreateAsset(terrainM, Dir + "Terrain.mat");
            var go = new GameObject("Terrain"); go.AddComponent<MeshFilter>().sharedMesh = Grid(256, 20f, 12f);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = terrainM; mr.shadowCastingMode = ShadowCastingMode.TwoSided;

            var sun = SceneKit.Sun("Sun", new Vector3(22f, 198f, 0f), new Color(1f, 0.78f, 0.55f), 3.2f, true);
            sun.shadowBias = 0.2f; sun.shadowNormalBias = 0.6f;
            SceneKit.Shadows(90f);
            SceneKit.Grade(Dir, 0.3f, 0f, 14f, 8f, 0.3f, 0.05f);

            var dir = new GameObject("Director").AddComponent<ErosionDirector>();
            dir.terrain = terrainM; dir.erosion = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/Erosion.compute");
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 50f, Color.black, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 50f, Color.black, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.1f; c.farClipPlane = 1000f; c.cullingMask = ~(1 << 10); }

            var disp = new Material(Shader.Find("GameTech/ErosionComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad); dq.name = "Display quad"; dq.layer = 10; Object.DestroyImmediate(dq.GetComponent<Collider>());
            dq.transform.position = new Vector3(500f, 0f, 0f); dq.transform.localScale = new Vector3(16f / 9f, 1f, 1f);
            var dmr = dq.GetComponent<MeshRenderer>(); dmr.sharedMaterial = disp; dmr.shadowCastingMode = ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: terrain {terrainM.shader.isSupported}, sky {skyM.shader.isSupported}, erosion {(dir.erosion != null)}, composite {disp.shader.isSupported}");
        }
    }
}

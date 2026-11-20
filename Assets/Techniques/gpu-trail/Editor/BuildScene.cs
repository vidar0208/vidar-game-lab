// Builds the GPU trails scene from code: a dark floor ringed by pillars, the stand-in mannequin walking through
// the middle, and three trail systems that get the same input (1,536 swarm points + 5 body points):
// main (culling + 3 LODs), noCull (culling off, only for the culling shot), lodDebug (flat colour per LOD).
using System.Linq;
using GameTech.GpuTrails;
using GpuTrailSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class TrailScene
    {
        const string Dir = "Assets/Techniques/gpu-trail/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material M(string name, Color c, float metal, float smooth)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Material Unlit(string name, Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c); m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Material TrailMat(string name, bool flat, Color c, float intensity)
        {
            var m = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Dir + "Shaders/GpuTrailURP.shader"));
            m.SetFloat("_Intensity", intensity); m.SetFloat("_FadePower", 1.5f);
            m.SetFloat("_UseFlat", flat ? 1f : 0f); m.SetColor("_FlatColor", c);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static ComputeShader CS(string n) => AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "GpuTrail/Shaders/" + n + ".compute");

        static GpuTrailRenderer TrailSystem(string name, int layer, Transform[] body, bool culling, Material mat, Material[] lodMats,
            float[] starts, int[] steps, Camera target)
        {
            var go = new GameObject(name);
            var sw = go.AddComponent<TrailSwarm>();
            sw.bodyPoints = body; sw.appendNodeCS = CS("GpuTrailAppendNode");
            var r = go.AddComponent<GpuTrailRenderer>();
            r.trailIndexDispatcherCS = CS("GpuTrailIndexDispatcher"); r.calcLodCS = CS("GpuTrailCalcLod");
            r.cullingCS = CS("GpuTrailCulling"); r.updateVertexCS = CS("GpuTrailUpdateVertex");
            r.defaultMaterial = mat; r.startWidth = 0.022f; r.endWidth = 0f; r.targetCamera = target;
            r.cullingEnable = culling; r.renderLayer = layer;
            var so = new SerializedObject(r);
            var list = so.FindProperty("lodSettings"); list.ClearArray();
            for (int i = 0; i < starts.Length; i++)
            {
                list.InsertArrayElementAtIndex(i);
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("enable").boolValue = true;
                e.FindPropertyRelative("startDistance").floatValue = starts[i];
                e.FindPropertyRelative("lodNodeStep").intValue = steps[i];
                e.FindPropertyRelative("material").objectReferenceValue = lodMats != null ? lodMats[i] : null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[gt] trail system {name}: layer {layer}, culling {culling}, lods {starts.Length}, compute {(r.cullingCS != null)}/{(r.updateVertexCS != null)}/{(r.calcLodCS != null)}/{(sw.appendNodeCS != null)}");
            return r;
        }

        static LineRenderer Line(string name, Material m)
        {
            var go = new GameObject(name) { layer = TrailDirector.LayerLines };
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = m; lr.positionCount = 2; lr.useWorldSpace = true; lr.widthMultiplier = 0.07f;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            return lr;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.05f, 0.055f, 0.07f), new Color(0.03f, 0.03f, 0.04f), new Color(0.02f, 0.02f, 0.02f));
            var dir = new GameObject("Director").AddComponent<TrailDirector>();
            var C = new Vector3(0f, 0f, 3f);

            var floorM = M("Floor", new Color(0.022f, 0.024f, 0.03f), 0f, 0.3f);
            var pillarM = M("Pillar", new Color(0.09f, 0.09f, 0.1f), 0f, 0.4f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.position = new Vector3(0, -0.1f, 3f); floor.transform.localScale = new Vector3(40f, 0.2f, 40f);
            floor.GetComponent<Renderer>().sharedMaterial = floorM;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f + 0.2f;
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder); p.name = "Pillar " + i;
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.transform.position = C + new Vector3(11f * Mathf.Cos(a), 2.5f, 11f * Mathf.Sin(a));
                p.transform.localScale = new Vector3(0.6f, 2.5f, 0.6f);
                p.GetComponent<Renderer>().sharedMaterial = pillarM;
            }
            SceneKit.Sun("Key", new Vector3(38f, 150f, 0f), new Color(0.75f, 0.82f, 1f), 0.4f, true);
            SceneKit.Sun("Rim", new Vector3(20f, 20f, 0f), new Color(0.7f, 0.6f, 1f), 0.9f, false);

            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.7f, 0.58f, 0.5f) : new Color(0.42f, 0.44f, 0.5f));
                m.SetFloat("_Smoothness", part == "skin" ? 0.4f : 0.35f);
            });
            man.transform.position = C;
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.shadowCastingMode = ShadowCastingMode.On; smr.updateWhenOffscreen = true; }
            dir.character = man.transform;
            var all = man.GetComponentsInChildren<Transform>(true);
            var body = new[] { "Hand_L", "Hand_R", "Foot_L", "Foot_R", "Head" }.Select(n => all.First(t => t.name == n)).ToArray();

            var bg = new Color(0.008f, 0.01f, 0.016f);
            dir.camMain = SceneKit.Cam("Main camera (cull, LOD and facing target)", new Rect(0, 0, 1, 1), 42f, bg, -20, false);
            dir.camMain.enabled = false;
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 42f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 42f, bg, 1, true);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.05f; c.farClipPlane = 80f; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            var trailMat = TrailMat("Trail", false, Color.white, 1.1f); trailMat.SetFloat("_FadePower", 2f);
            var lodMats = new[] { TrailMat("Trail LOD0", true, new Color(0.25f, 1f, 0.35f), 2.5f), TrailMat("Trail LOD1", true, new Color(1f, 0.85f, 0.15f), 2.5f), TrailMat("Trail LOD2", true, new Color(1f, 0.2f, 0.15f), 2.5f) };
            dir.main = TrailSystem("Trails main", TrailDirector.LayerMain, body, true, trailMat, null, dir.lodStart, dir.lodStep, dir.camMain);
            dir.noCull = TrailSystem("Trails no culling", TrailDirector.LayerNoCull, body, false, trailMat, null, new[] { 0f }, new[] { 1 }, dir.camMain);
            dir.lodDebug = TrailSystem("Trails LOD colours", TrailDirector.LayerLod, body, true, trailMat, lodMats, dir.lodStart, dir.lodStep, dir.camMain);
            dir.swarmMain = dir.main.GetComponent<TrailSwarm>();

            var lineM = Unlit("Line", new Color(1f, 1f, 1f));
            dir.edgeL = Line("Frustum edge L", lineM); dir.edgeR = Line("Frustum edge R", lineM);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.name = "Camera marker"; marker.layer = TrailDirector.LayerLines;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.localScale = new Vector3(0.35f, 0.2f, 0.5f); marker.GetComponent<Renderer>().sharedMaterial = lineM;
            dir.camMarker = marker.transform;

            var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dir.dotMesh = sph.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(sph);
            dir.dotMat = Unlit("Dot", new Color(0.75f, 0.8f, 0.95f));

            SceneKit.Grade(Dir, 0.5f, 0.1f, 14f, 8f, 0.35f, 0.08f);
            AssetDatabase.SaveAssets();

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            Debug.Log("[gt] built: floor, 12 pillars, mannequin, 3 trail systems x " + dir.swarmMain.TrailCount + " trails");
        }
    }
}

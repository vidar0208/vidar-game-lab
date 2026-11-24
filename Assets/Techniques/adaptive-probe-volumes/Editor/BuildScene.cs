// Builds the probe-lighting room from code and bakes it with Unity's own bakers, in one of two modes:
// BuildLegacy: URP light probe system = Light Probe Groups, a 1.5 m probe grid, per-object SH.
// Build:       URP light probe system = Adaptive Probe Volumes, one global Probe Volume and a baking set.
// Everything static only contributes GI and receives it from probes (no lightmaps). The sun is Mixed (Baked Indirect):
// direct light and shadows stay realtime, the bounce comes from the bake.
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class ApvScene
    {
        const string Dir = "Assets/Techniques/adaptive-probe-volumes/";

        public static void BuildLegacy() => Build(false);
        public static void BuildApv() => Build(true);

        static GameObject Box(string name, Material m, Vector3 center, Vector3 size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.position = center; g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.receiveGI = ReceiveGI.LightProbes; r.lightProbeUsage = LightProbeUsage.BlendProbes;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return g;
        }

        static void Build(bool apv)
        {
            var rp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var so = new SerializedObject(rp);
            so.FindProperty("m_LightProbeSystem").intValue = apv ? 1 : 0;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();

            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.09f);
            SceneKit.Grade(Dir, 0.35f, 0.9f, 12f, 8f, 0.25f, 0.02f);
            SceneKit.Shadows(25f);

            var white = SceneKit.Mat(Dir, "White", "Universal Render Pipeline/Lit", new Color(0.78f, 0.76f, 0.72f), 0f, 0.25f);
            var red = SceneKit.Mat(Dir, "Red", "Universal Render Pipeline/Lit", new Color(0.85f, 0.07f, 0.05f), 0f, 0.2f);
            var blue = SceneKit.Mat(Dir, "Blue", "Universal Render Pipeline/Lit", new Color(0.06f, 0.2f, 0.85f), 0f, 0.2f);
            var ballMat = SceneKit.Mat(Dir, "Ball", "Universal Render Pipeline/Lit", new Color(0.85f, 0.85f, 0.85f), 0f, 0.45f);

            // room 10 x 4 x 14 m, walls 0.2 m thick
            Box("Floor", white, new Vector3(0, -0.1f, 0), new Vector3(10.4f, 0.2f, 14.4f));
            Box("Ceiling", white, new Vector3(0, 4.1f, 0), new Vector3(10.4f, 0.2f, 14.4f));
            Box("Red wall", red, new Vector3(-5.1f, 2f, 0), new Vector3(0.2f, 4f, 14f));
            Box("Back wall", white, new Vector3(0, 2f, 7.1f), new Vector3(10f, 4f, 0.2f));
            Box("Front wall", white, new Vector3(0, 2f, -7.1f), new Vector3(10f, 4f, 0.2f));
            // blue wall with a window: hole y 0.9-3.3, z -3..3
            Box("Blue wall low", blue, new Vector3(5.1f, 0.45f, 0), new Vector3(0.2f, 0.9f, 14f));
            Box("Blue wall high", blue, new Vector3(5.1f, 3.65f, 0), new Vector3(0.2f, 0.7f, 14f));
            Box("Blue wall back", blue, new Vector3(5.1f, 2.1f, 5f), new Vector3(0.2f, 2.4f, 4f));
            Box("Blue wall front", blue, new Vector3(5.1f, 2.1f, -5f), new Vector3(0.2f, 2.4f, 4f));
            // furniture
            Box("Pillar", white, new Vector3(-2.6f, 2f, 4.2f), new Vector3(0.6f, 4f, 0.6f));
            Box("Block", white, new Vector3(1.8f, 0.4f, 4.6f), new Vector3(2.2f, 0.8f, 1.4f));
            Box("Shelf", white, new Vector3(-4.6f, 1.1f, -2.8f), new Vector3(0.8f, 0.1f, 2.4f));

            var sun = SceneKit.Sun("Sun", new Vector3(9f, -98f, 0f), new Color(1f, 0.86f, 0.66f), 5f, true);
            sun.lightmapBakeType = LightmapBakeType.Mixed; sun.shadows = LightShadows.Soft;

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere); ballGo.name = "Ball (dynamic)"; Object.DestroyImmediate(ballGo.GetComponent<Collider>());
            ballGo.transform.localScale = Vector3.one * 0.9f; var br = ballGo.GetComponent<MeshRenderer>(); br.sharedMaterial = ballMat; br.lightProbeUsage = LightProbeUsage.BlendProbes;

            var ls = new LightingSettings
            {
                name = "Lighting", bakedGI = true, realtimeGI = false, lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                mixedBakeMode = MixedLightingMode.IndirectOnly, lightmapResolution = 10, lightmapMaxSize = 512,
                directSampleCount = 32, indirectSampleCount = 512, environmentSampleCount = 128, maxBounces = 3, ao = false
            };
            AssetDatabase.CreateAsset(ls, Dir + "Lighting.lighting");
            Lightmapping.lightingSettings = ls;

            ProbeVolumeBakingSet set = null;
            if (apv)
            {
                var pv = new GameObject("Adaptive Probe Volume (global)").AddComponent<ProbeVolume>(); pv.mode = ProbeVolume.Mode.Global;
            }
            else
            {
                var lpg = new GameObject("Light Probe Group (1.5 m grid)").AddComponent<LightProbeGroup>();
                var pts = new System.Collections.Generic.List<Vector3>();
                for (float x = -4.5f; x <= 4.51f; x += 1.5f) for (float y = 0.3f; y <= 3.71f; y += 1.7f) for (float z = -6.5f; z <= 6.51f; z += 1.625f) pts.Add(new Vector3(x, y, z));
                lpg.probePositions = pts.ToArray();
            }

            // cameras and labels
            var dir = new GameObject("Director").AddComponent<ApvDirector>(); dir.apv = apv; dir.ball = ballGo.transform;
            var bg = new Color(0.02f, 0.02f, 0.025f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 60f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 60f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 60f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 60f, bg, 2, true);
            foreach (var cam in new[] { dir.camFull, dir.camL, dir.camR }) { cam.nearClipPlane = 0.05f; cam.farClipPlane = 40f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 30); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 28);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 30); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 28);

            // save first: the baking set needs the scene GUID
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Dir + "Main.unity");
            if (apv)
            {
                set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>(); set.name = "APV_BakingSet";
                set.TryAddScene(AssetDatabase.AssetPathToGUID(Dir + "Main.unity"));
                AssetDatabase.CreateAsset(set, Dir + "APV_BakingSet.asset"); AssetDatabase.SaveAssets();
                typeof(ProbeVolumeBakingSet).GetMethod("SyncBakingSets", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
                ProbeReferenceVolume.instance.SetActiveBakingSet(set);
            }
            var t0 = System.DateTime.Now;
            bool baked = Lightmapping.Bake();
            float secs = (float)(System.DateTime.Now - t0).TotalSeconds;
            string info;
            if (apv)
            {
                int probes = 0, bricks = 0, cells = 0;
                var descs = typeof(ProbeVolumeBakingSet).GetField("cellDescs", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(set) as IDictionary;
                if (descs != null) foreach (var v in descs.Values)
                    {
                        cells++;
                        probes += (int)v.GetType().GetField("probeCount").GetValue(v);
                        bricks += (int)v.GetType().GetField("bricksCount").GetValue(v);
                    }
                info = $"{probes:N0} probes in {bricks:N0} bricks ({cells} cells), min spacing {set.minDistanceBetweenProbes:0.#} m, bake {secs:F0} s";
            }
            else info = $"{LightmapSettings.lightProbes?.count ?? 0} legacy probes, bake {secs:F0} s";
            dir.stats = info;
            Debug.Log($"[gt] bake ok {baked} apv {apv}: {info}");
            EditorSceneManager.SaveScene(scene, Dir + "Main.unity");
        }
    }
}

// Builds the cloud scene: the repo's Clouds Test container box (camera below the layer, lower sun), a procedural sky, and a copy of
// the project's URP renderer with a Full Screen Pass Renderer Feature that runs GameTech/Clouds before post-processing.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class CloudsScene
    {
        const string Dir = "Assets/Techniques/raymarched-clouds/";

        public static void Build()
        {
            // renderer with the full-screen pass
            AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset", Dir + "CloudsRenderer.asset");
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Dir + "CloudsRenderer.asset");
            var mat = new Material(Shader.Find("GameTech/Clouds")); AssetDatabase.CreateAsset(mat, Dir + "Clouds.mat");
            var feat = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feat.name = "Clouds (Full Screen Pass)";
            feat.passMaterial = mat; feat.passIndex = 0; feat.fetchColorBuffer = true;
            feat.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feat.requirements = ScriptableRenderPassInput.Depth;
            AssetDatabase.AddObjectToAsset(feat, rd);
            rd.rendererFeatures.Add(feat);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long fid);
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap"); map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = fid;
            so.ApplyModifiedPropertiesWithoutUndo(); rd.SetDirty(); EditorUtility.SetDirty(rd);
            var rp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var rso = new SerializedObject(rp); var list = rso.FindProperty("m_RendererDataList");
            list.arraySize++; int rendererIndex = list.arraySize - 1; list.GetArrayElementAtIndex(rendererIndex).objectReferenceValue = rd;
            rso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();

            // sky and sun (sun rotation from the Clouds Test scene)
            var sun = SceneKit.Sun("Sun", Vector3.zero, Color.white, 1f, false);
            sun.transform.rotation = Quaternion.Euler(30f, 317f, 0f);   // the Clouds Test sun is 77 deg high; lowered to 30 deg for side light
            var skyMat = new Material(Shader.Find("Skybox/Procedural"));
            skyMat.SetFloat("_SunSize", 0.03f); skyMat.SetFloat("_AtmosphereThickness", 0.8f); skyMat.SetFloat("_Exposure", 1.1f);
            skyMat.SetColor("_GroundColor", new Color(0.32f, 0.36f, 0.42f));
            AssetDatabase.CreateAsset(skyMat, Dir + "Sky.mat");
            RenderSettings.skybox = skyMat; RenderSettings.sun = sun; RenderSettings.fog = false;
            SceneKit.Grade(Dir, 0.2f, 0f, 8f, 6f, 0.2f, 0.02f);

            var container = new GameObject("Container").transform;
            container.position = new Vector3(0, 0, 109f); container.localScale = new Vector3(1996.8999f, 572.57196f, 1838.9454f);

            var dir = new GameObject("Director").AddComponent<CloudsDirector>();
            dir.mat = mat; dir.container = container; dir.sun = sun;
            dir.noiseCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "NoiseGenCompute.compute");
            dir.blueNoise = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "BlueNoise.png");
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 60f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 60f, Color.black, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 60f, Color.black, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR })
            {
                c.clearFlags = CameraClearFlags.Skybox; c.nearClipPlane = 0.3f; c.farClipPlane = 10000f;
                c.GetComponent<UniversalAdditionalCameraData>().SetRenderer(rendererIndex);
                c.transform.SetPositionAndRotation(new Vector3(-150f, -240f, -650f), Quaternion.Euler(-21f, 50f, 0f));   // below the cloud layer, looking up
            }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 30); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 28);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 30); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 28);
            Debug.Log($"[gt] clouds renderer index {rendererIndex}, feature id {fid}");
        }
    }
}

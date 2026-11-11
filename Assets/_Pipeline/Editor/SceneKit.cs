// Editor-side helpers shared by the per-post BuildScene scripts: materials, lights, cameras, post volume.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech
{
    public static class SceneKit
    {
        public static Material Mat(string dir, string name, string shader, Color c, float metallic = 0, float smooth = 0.5f)
        {
            var sh = Shader.Find(shader);
            if (sh == null) Debug.LogError("[gt] shader not found: " + shader);
            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, dir + name + ".mat");
            return m;
        }

        public static GameObject Part(string name, Mesh mesh, Material m, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot;
            return go;
        }

        public static Light Sun(string name, Vector3 euler, Color c, float intensity, bool shadows)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Directional; l.color = c; l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.transform.rotation = Quaternion.Euler(euler);
            return l;
        }

        public static Camera Cam(string name, Rect rect, float fov, Color bg, int depth, bool post = true)
        {
            var go = new GameObject(name);
            var cam = go.AddComponent<Camera>();
            cam.rect = rect; cam.depth = depth; cam.fieldOfView = fov; cam.allowHDR = true;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = post; data.renderShadows = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            return cam;
        }

        // Overrides must be stored as sub-assets of the profile, or they are lost on the play-mode reload.
        public static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        public static VolumeProfile Grade(string dir, float bloom = 0.5f, float exposure = 0f, float contrast = 10f, float saturation = 5f, float vignette = 0.3f, float grain = 0.2f)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, dir + "Post.asset");
            var b = Add<Bloom>(profile); b.intensity.Override(bloom); b.threshold.Override(1.0f); b.scatter.Override(0.7f);
            var tone = Add<Tonemapping>(profile); tone.mode.Override(TonemappingMode.ACES);
            var vig = Add<Vignette>(profile); vig.intensity.Override(vignette); vig.smoothness.Override(0.45f);
            var ca = Add<ColorAdjustments>(profile); ca.postExposure.Override(exposure); ca.contrast.Override(contrast); ca.saturation.Override(saturation);
            var fg = Add<FilmGrain>(profile); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(grain); fg.response.Override(0.8f);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            var vol = new GameObject("Post volume").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;
            return profile;
        }

        public static void Ambient(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
        }

        public static void Shadows(float distance)
        {
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp != null) { rp.shadowDistance = distance; EditorUtility.SetDirty(rp); }
        }
    }
}

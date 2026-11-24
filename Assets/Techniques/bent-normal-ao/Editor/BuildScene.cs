// Builds the bent normal scene from code: the Poly Haven studio HDRI as a cubemap (specular convolution for its mips),
// a blurred studio backdrop, four empty prop objects (floor, stone base, chrome parts, copper parts) whose meshes the
// director generates at start, the two scene cameras and the display camera with the composite quad.
// The HDRI is not part of this folder: download studio_small_09_1k.hdr from polyhaven.com (CC0) into
// <project>/../assets/ before building.
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class BNScene
    {
        const string Dir = "Assets/Techniques/bent-normal-ao/";

        static Material Lit(string name, Color c, float metallic, float smooth)
        {
            var m = new Material(Shader.Find("GameTech/BNLit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        public static void Build()
        {
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets/studio_small_09_1k.hdr"));
            string dst = Dir + "studio_small_09_1k.hdr";
            File.Copy(src, dst, true); AssetDatabase.ImportAsset(dst);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureShape = TextureImporterShape.TextureCube; imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            imp.mipmapEnabled = true; imp.sRGBTexture = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 256;
            var st = new TextureImporterSettings(); imp.ReadTextureSettings(st); st.cubemapConvolution = TextureImporterCubemapConvolution.Specular; imp.SetTextureSettings(st);
            imp.SaveAndReimport();
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(dst);
            Debug.Log($"[gt] hdri cubemap {(cube ? cube.width + " px, mips " + cube.mipmapCount : "missing")}");

            RenderSettings.skybox = null; RenderSettings.fog = false;
            var skyM = new Material(Shader.Find("GameTech/BNSky")); skyM.SetTexture("_Cube", cube); AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere); sky.name = "Studio"; Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.transform.localScale = Vector3.one * 40f; var smr = sky.GetComponent<MeshRenderer>(); smr.sharedMaterial = skyM; smr.shadowCastingMode = ShadowCastingMode.Off;

            var mats = new[]
            {
                Lit("Floor", new Color(0.05f, 0.05f, 0.055f), 0f, 0.8f),
                Lit("Stone", new Color(0.42f, 0.40f, 0.37f), 0f, 0.55f),
                Lit("Chrome", new Color(0.96f, 0.96f, 0.97f), 1f, 0.95f),
                Lit("Copper", new Color(0.95f, 0.64f, 0.54f), 1f, 0.9f),
            };
            string[] names = { "Floor", "Stone base", "Chrome", "Copper" };
            var groups = new MeshFilter[4];
            for (int g = 0; g < 4; g++)
            {
                var go = new GameObject(names[g]); groups[g] = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[g]; mr.shadowCastingMode = ShadowCastingMode.Off;
            }

            SceneKit.Grade(Dir, 0.5f, 0.0f, 12f, 4f, 0.3f, 0.05f);

            var dir = new GameObject("Director").AddComponent<BNDirector>();
            dir.cube = cube; dir.litMats = mats; dir.groups = groups;
            dir.bake = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/BNBake.compute");
            dir.depthMat = new Material(Shader.Find("GameTech/BNDepth")); AssetDatabase.CreateAsset(dir.depthMat, Dir + "Depth.mat");
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 38f, Color.black, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 38f, Color.black, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.03f; c.farClipPlane = 60f; c.cullingMask = ~(1 << 10); }

            var disp = new Material(Shader.Find("GameTech/BNComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
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
            Debug.Log($"[gt] built: lit {mats[0].shader.isSupported}, depth {dir.depthMat.shader.isSupported}, bake {(dir.bake != null)}, composite {disp.shader.isSupported}");
        }
    }
}

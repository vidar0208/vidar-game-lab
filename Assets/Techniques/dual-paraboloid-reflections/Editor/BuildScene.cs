// Builds the dual paraboloid scene from code: the Poly Haven studio HDRI as a cubemap (imported with specular
// convolution for its mips), a blurred studio backdrop, a chrome sphere on a dark plinth, a chrome torus, a brushed
// (rougher) sphere, a glossy dark floor, the two scene cameras and the display camera with the composite quad.
// The HDRI is not part of this folder: download studio_small_09_1k.hdr from polyhaven.com (CC0) into
// <project>/../assets/ before building.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class DPScene
    {
        const string Dir = "Assets/Techniques/dual-paraboloid-reflections/";

        static Mesh Torus(float R, float r, int nu, int nv)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= nu; i++) for (int j = 0; j <= nv; j++)
            {
                float a = i * 2f * Mathf.PI / nu, b = j * 2f * Mathf.PI / nv;
                var c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * R; var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                v.Add(c + d * r); n.Add(d);
            }
            for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++)
            {
                int a = i * (nv + 1) + j, b = a + nv + 1;
                tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            var m = new Mesh { name = "Torus" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "Torus.asset"); return m;
        }

        static Mesh Sphere(int nu, int nv)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                float th = j * Mathf.PI / nv, ph = i * 2f * Mathf.PI / nu;
                v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * 0.5f);
            }
            for (int j = 0; j < nv; j++) for (int i = 0; i < nu; i++)
            {
                int a = j * (nu + 1) + i, b = a + nu + 1;
                tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            var m = new Mesh { name = "Sphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(v);
            var n = new Vector3[v.Count]; for (int k = 0; k < n.Length; k++) n[k] = v[k].normalized; m.normals = n;
            m.SetTriangles(tri, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "SphereHi.asset"); return m;
        }

        static Material Refl(string name, Color c, float metallic, float smooth)
        {
            var m = new Material(Shader.Find("GameTech/DPReflect"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(t); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity); go.transform.localScale = scale; go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        public static void Build()
        {
            // HDRI -> cubemap with convolved mips
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
            var skyM = new Material(Shader.Find("GameTech/DPSky")); skyM.SetTexture("_Cube", cube); AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var sky = Prim(PrimitiveType.Sphere, "Studio", Vector3.zero, Vector3.one * 40f, skyM);
            sky.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var chrome = Refl("Chrome", new Color(0.96f, 0.96f, 0.97f), 1f, 0.97f);
            var brushed = Refl("Brushed", new Color(0.92f, 0.78f, 0.55f), 1f, 0.72f);
            var gold = Refl("Gold", new Color(1f, 0.77f, 0.45f), 1f, 0.94f);
            var plinth = Refl("Plinth", new Color(0.03f, 0.03f, 0.035f), 0f, 0.88f);
            var floorM = Refl("Floor", new Color(0.06f, 0.06f, 0.065f), 0f, 0.8f);

            Prim(PrimitiveType.Cylinder, "Floor", new Vector3(0, -0.01f, 0), new Vector3(9f, 0.01f, 9f), floorM);
            Prim(PrimitiveType.Cylinder, "Plinth", new Vector3(0, 0.13f, 0), new Vector3(0.9f, 0.13f, 0.9f), plinth);
            var sm = Sphere(192, 96);
            SceneKit.Part("Chrome sphere", sm, chrome, new Vector3(0, 0.76f, 0), Quaternion.identity).transform.localScale = Vector3.one * 1.0f;
            SceneKit.Part("Brushed sphere", sm, brushed, new Vector3(1.05f, 0.32f, 0.35f), Quaternion.identity).transform.localScale = Vector3.one * 0.64f;
            var torus = SceneKit.Part("Gold torus", Torus(0.34f, 0.09f, 96, 48), gold, new Vector3(-1.1f, 0.43f, 0.2f), Quaternion.Euler(0, 35f, 0));

            SceneKit.Grade(Dir, 0.5f, 0.0f, 12f, 4f, 0.3f, 0.05f);

            var dir = new GameObject("Director").AddComponent<DPDirector>();
            dir.cube = cube; dir.reflMats = new[] { chrome, brushed, gold, plinth, floorM };
            dir.generate = new Material(Shader.Find("GameTech/DPGenerate")); AssetDatabase.CreateAsset(dir.generate, Dir + "Generate.mat");
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 38f, Color.black, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 38f, Color.black, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.03f; c.farClipPlane = 60f; c.cullingMask = ~(1 << 10); }

            var disp = new Material(Shader.Find("GameTech/DPComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
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
            Debug.Log($"[gt] built: reflect {chrome.shader.isSupported}, generate {dir.generate.shader.isSupported}, composite {disp.shader.isSupported}");
        }
    }
}

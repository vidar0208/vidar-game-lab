// Builds the path tracing scene from code: a closed room (open at the front) with coloured side walls and a ceiling
// light, a glass sphere, a mirror sphere, a tall box and a small glossy cube, plus an orange lamp standing outside
// the camera's view. Every object has a URP material (raster side) and a PTObject (path tracer side). The raster
// side gets a point light, a spot light at the outside lamp and a realtime reflection probe.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class PTScene
    {
        const string Dir = "Assets/Techniques/compute-path-tracer/";
        static int meshN;

        static Mesh Quad(Vector3 normal, Vector3 up, float w, float h)
        {
            var r = Vector3.Cross(normal, up).normalized * (w * 0.5f); var u = up.normalized * (h * 0.5f);
            var m = new Mesh { name = "Quad" + meshN };
            m.vertices = new[] { -r - u, r - u, -r + u, r + u };
            m.normals = new[] { normal, normal, normal, normal };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "Mesh" + (meshN++) + ".asset"); return m;
        }

        static Material Lit(string name, Color c, float metallic, float smooth, Color emission = default)
        {
            var m = SceneKit.Mat(Dir, name, "Universal Render Pipeline/Lit", c, metallic, smooth);
            if (emission.maxColorComponent > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            return m;
        }

        static PTObject Obj(string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            return go.AddComponent<PTObject>();
        }

        static PTObject Wall(string name, Vector3 pos, Vector3 normal, Vector3 up, float w, float h, Color c)
        {
            var o = Obj(name, Quad(normal, up, w, h), Lit(name, c, 0, 0.1f), pos, Quaternion.identity, Vector3.one);
            o.diffuse = c; return o;
        }

        public static void Build()
        {
            meshN = 0;
            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.08f, 0.08f, 0.085f);
            QualitySettings.realtimeReflectionProbes = true;
            SceneKit.Shadows(20f);
            SceneKit.Grade(Dir, 0.4f, 0f, 8f, 6f, 0.25f, 0f);

            Color white = new Color(0.75f, 0.74f, 0.72f), red = new Color(0.72f, 0.12f, 0.06f), teal = new Color(0.07f, 0.42f, 0.48f);
            Wall("Floor", new Vector3(0, 0, -1.25f), Vector3.up, Vector3.forward, 9f, 7.5f, white);
            Wall("Ceiling", new Vector3(0, 3, 0), Vector3.down, Vector3.forward, 5f, 5f, white);
            Wall("Back wall", new Vector3(0, 1.5f, 2.5f), Vector3.back, Vector3.up, 5f, 3f, white);
            Wall("Left wall", new Vector3(-2.5f, 1.5f, 0), Vector3.right, Vector3.up, 5f, 3f, red);
            Wall("Right wall", new Vector3(2.5f, 1.5f, 0), Vector3.left, Vector3.up, 5f, 3f, teal);
            Wall("Right wall outside", new Vector3(2.52f, 1.5f, 0), Vector3.right, Vector3.up, 5f, 3f, new Color(0.3f, 0.3f, 0.3f));
            Wall("Ceiling outside", new Vector3(0, 3.02f, 0), Vector3.up, Vector3.forward, 5f, 5f, new Color(0.3f, 0.3f, 0.3f));

            var lightCol = new Color(1f, 0.93f, 0.82f);
            var ceil = Obj("Ceiling light", Quad(Vector3.down, Vector3.forward, 1.6f, 1.6f), Lit("CeilingLight", Color.white, 0, 0, lightCol * 6f), new Vector3(0, 2.99f, 0.3f), Quaternion.identity, Vector3.one);
            ceil.diffuse = Color.white; ceil.emission = lightCol; ceil.emissionStrength = 8f;

            var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            var glassM = Lit("Glass", new Color(0.9f, 0.95f, 1f, 0.15f), 0, 0.97f);
            glassM.SetFloat("_Surface", 1); glassM.SetFloat("_Blend", 1); glassM.SetOverrideTag("RenderType", "Transparent");
            glassM.SetFloat("_SrcBlend", (float)BlendMode.One); glassM.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); glassM.SetFloat("_ZWrite", 0);
            glassM.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glassM.EnableKeyword("_ALPHAPREMULTIPLY_ON"); glassM.renderQueue = 3000;
            var glass = Obj("Glass sphere", sphere, glassM, new Vector3(-1.05f, 0.55f, 0.2f), Quaternion.identity, Vector3.one * 1.1f);
            glass.flag = 2; glass.ior = 1.5f; glass.smoothness = 1f; glass.specularProbability = 1f; glass.diffuse = Color.white;

            var mirror = Obj("Mirror sphere", sphere, Lit("Mirror", new Color(0.95f, 0.93f, 0.9f), 1f, 0.96f), new Vector3(0.95f, 0.6f, 0.9f), Quaternion.identity, Vector3.one * 1.2f);
            mirror.diffuse = new Color(0.8f, 0.8f, 0.8f); mirror.specular = new Color(0.95f, 0.93f, 0.9f); mirror.smoothness = 1f; mirror.specularProbability = 1f;

            var boxC = new Color(0.78f, 0.76f, 0.72f);
            var box = Obj("Tall box", cube, Lit("TallBox", boxC, 0, 0.2f), new Vector3(-0.35f, 0.85f, 1.5f), Quaternion.Euler(0, 25f, 0), new Vector3(0.8f, 1.7f, 0.8f));
            box.diffuse = boxC;

            var blue = new Color(0.12f, 0.25f, 0.75f);
            var small = Obj("Glossy cube", cube, Lit("GlossyCube", blue, 0, 0.85f), new Vector3(1.55f, 0.3f, -0.9f), Quaternion.Euler(0, 15f, 0), Vector3.one * 0.6f);
            small.diffuse = blue; small.specular = Color.white; small.smoothness = 0.85f; small.specularProbability = 0.15f;

            var lampCol = new Color(1f, 0.5f, 0.18f);
            var lampPos = new Vector3(3.25f, 1.0f, -3.0f);
            var lamp = Obj("Lamp outside the frame", cube, Lit("Lamp", lampCol, 0, 0, lampCol * 4f), lampPos, Quaternion.LookRotation(new Vector3(0, 1f, 0) - lampPos), new Vector3(1.4f, 2.0f, 0.25f));
            lamp.diffuse = lampCol; lamp.emission = lampCol; lamp.emissionStrength = 20f; lamp.offscreen = true;

            // raster lights
            var pl = new GameObject("Ceiling point light").AddComponent<Light>(); pl.type = LightType.Point; pl.transform.position = new Vector3(0, 2.75f, 0.3f);
            pl.color = lightCol; pl.intensity = 9f; pl.range = 12f; pl.shadows = LightShadows.Soft;
            var sl = new GameObject("Lamp spot light").AddComponent<Light>(); sl.type = LightType.Spot; sl.transform.position = lampPos + (new Vector3(0, 1f, 0) - lampPos).normalized * 0.4f;
            sl.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1f, 0) - lampPos); sl.color = lampCol; sl.intensity = 30f; sl.range = 20f; sl.spotAngle = 90f; sl.shadows = LightShadows.Soft;

            var probe = new GameObject("Reflection probe").AddComponent<ReflectionProbe>(); probe.transform.position = new Vector3(0, 1.5f, 0);
            probe.size = new Vector3(5f, 3f, 5f); probe.boxProjection = true; probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting; probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; probe.resolution = 256;

            var dir = new GameObject("Director").AddComponent<PTDirector>();
            dir.pt = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/PathTrace.compute"); dir.probe = probe;
            dir.cam = SceneKit.Cam("Scene cam", new Rect(0, 0, 1, 1), 40f, Color.black, -5, false);
            dir.cam.transform.SetPositionAndRotation(new Vector3(0, 1.5f, -6.9f), Quaternion.identity);
            dir.cam.nearClipPlane = 0.05f; dir.cam.farClipPlane = 50f; dir.cam.cullingMask = ~(1 << 10);

            var disp = new Material(Shader.Find("GameTech/PTComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
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
            Debug.Log($"[gt] built: tracer {(dir.pt != null)}, composite {disp.shader.isSupported}, sphere {(sphere != null)}, cube {(cube != null)}");
        }
    }
}

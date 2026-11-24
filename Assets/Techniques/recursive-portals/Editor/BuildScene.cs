// Builds the portal hall: an open-top courtyard with coloured wall segments, an orange portal at the north end and a
// teal portal at the south end, both facing north, so looking into the orange one shows the hall again from the south.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class PortalScene
    {
        const string Dir = "Assets/Techniques/recursive-portals/";
        static Mesh cube;

        public static void Build()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = tmp.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(tmp);
            var sky = new Color(0.52f, 0.68f, 0.88f);
            RenderSettings.skybox = null;
            SceneKit.Ambient(new Color(0.55f, 0.65f, 0.8f), new Color(0.45f, 0.45f, 0.45f), new Color(0.25f, 0.22f, 0.2f));
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.006f; RenderSettings.fogColor = sky;
            SceneKit.Grade(Dir, 0.3f, 0f, 10f, 10f, 0.25f, 0.02f);
            SceneKit.Shadows(60f);
            SceneKit.Sun("Sun", new Vector3(48f, 35f, 0f), new Color(1f, 0.93f, 0.82f), 1.6f, true);

            string lit = "Universal Render Pipeline/Lit";
            var cream = SceneKit.Mat(Dir, "Cream", lit, new Color(0.9f, 0.85f, 0.75f), 0f, 0.2f);
            var white = SceneKit.Mat(Dir, "White", lit, new Color(0.92f, 0.92f, 0.9f), 0f, 0.3f);
            var tileA = SceneKit.Mat(Dir, "TileA", lit, new Color(0.82f, 0.8f, 0.76f), 0f, 0.35f);
            var tileB = SceneKit.Mat(Dir, "TileB", lit, new Color(0.55f, 0.53f, 0.52f), 0f, 0.35f);
            var orange = SceneKit.Mat(Dir, "Orange", lit, new Color(1f, 0.42f, 0.08f), 0f, 0.4f);
            var teal = SceneKit.Mat(Dir, "Teal", lit, new Color(0.05f, 0.72f, 0.66f), 0f, 0.4f);
            var magenta = SceneKit.Mat(Dir, "Magenta", lit, new Color(0.85f, 0.1f, 0.55f), 0f, 0.5f);
            var yellow = SceneKit.Mat(Dir, "Yellow", lit, new Color(1f, 0.82f, 0.1f), 0f, 0.4f);
            var blue = SceneKit.Mat(Dir, "Blue", lit, new Color(0.15f, 0.3f, 0.95f), 0f, 0.4f);

            var hall = new GameObject("Hall").transform;
            // floor: 2 m checker tiles
            for (int ix = 0; ix < 5; ix++) for (int iz = 0; iz < 13; iz++)
                Box("Tile", (ix + iz) % 2 == 0 ? tileA : tileB, new Vector3(-4f + 2f * ix, -0.1f, -12f + 2f * iz), new Vector3(2f, 0.2f, 2f), hall);
            // side walls: 2 m segments with a hue ramp along the hall
            for (int i = 0; i < 13; i++)
            {
                float z = -12f + 2f * i;
                var c = Color.HSVToRGB(Mathf.Repeat(0.47f + 0.55f * i / 12f, 1f), 0.55f, 0.88f);
                var m = SceneKit.Mat(Dir, "Wall" + i, lit, c, 0f, 0.25f);
                Box("Wall W", m, new Vector3(-5.2f, 2.5f, z), new Vector3(0.4f, 5f, 2f), hall);
                Box("Wall E", m, new Vector3(5.2f, 2.5f, z), new Vector3(0.4f, 5f, 2f), hall);
            }
            Box("Wall N", cream, new Vector3(0, 2.5f, 13.2f), new Vector3(10.8f, 5f, 0.4f), hall);
            Box("Wall S", cream, new Vector3(0, 2.5f, -13.2f), new Vector3(10.8f, 5f, 0.4f), hall);
            for (int i = 0; i < 7; i++) { float z = -12f + 4f * i; Box("Pillar", white, new Vector3(-4.65f, 2.25f, z), new Vector3(0.5f, 4.5f, 0.5f), hall); Box("Pillar", white, new Vector3(4.65f, 2.25f, z), new Vector3(0.5f, 4.5f, 0.5f), hall); }
            // props so every repeat of the hall is easy to read
            var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(sph.GetComponent<Collider>()); sph.name = "Sphere"; sph.transform.SetParent(hall);
            sph.transform.position = new Vector3(-2.6f, 0.8f, -5f); sph.transform.localScale = Vector3.one * 1.6f; sph.GetComponent<MeshRenderer>().sharedMaterial = magenta;
            Box("Crate", yellow, new Vector3(3.2f, 0.6f, 6.0f), new Vector3(1.2f, 1.2f, 1.2f), hall);
            Box("Column", blue, new Vector3(-2.9f, 1.25f, 5f), new Vector3(0.8f, 2.5f, 0.8f), hall);
            Box("Obelisk behind the teal portal", magenta, new Vector3(0f, 1.6f, -11.2f), new Vector3(1f, 3.2f, 1f), hall);

            // the two portals (forward +z = into the portal); opening 3 x 4 m
            var A = Portal("Portal orange", orange, new Vector3(0, 0, 9f));
            var B = Portal("Portal teal", teal, new Vector3(0, 0, -9f));
            var portals = new[] { A, B };
            var screenMat = new Material(Shader.Find("GameTech/PortalScreen"));
            screenMat.SetColor("_InactiveColour", new Color(0.06f, 0.06f, 0.07f));
            AssetDatabase.CreateAsset(screenMat, Dir + "PortalScreen.mat");

            var dir = new GameObject("Director").AddComponent<PortalDirector>();
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 60f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 60f, sky, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 60f, sky, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 60f, sky, 2, true);
            dir.rigFull = Rig("Rig full", dir.camFull, 20, portals, screenMat, 1920);
            dir.rigL = Rig("Rig left", dir.camL, 21, portals, screenMat, 957);
            dir.rigR = Rig("Rig right", dir.camR, 22, portals, screenMat, 957);
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 30); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 28);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 30); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 28);
        }

        static Transform Portal(string name, Material frame, Vector3 pos)
        {
            var t = new GameObject(name).transform; t.position = pos;
            Box("Post L", frame, pos + new Vector3(-1.68f, 2.15f, 0), new Vector3(0.36f, 4.3f, 0.36f), t);
            Box("Post R", frame, pos + new Vector3(1.68f, 2.15f, 0), new Vector3(0.36f, 4.3f, 0.36f), t);
            Box("Beam", frame, pos + new Vector3(0, 4.18f, 0), new Vector3(3.72f, 0.36f, 0.36f), t);
            return t;
        }

        static PortalRig Rig(string name, Camera player, int layer, Transform[] portals, Material screenMat, int w)
        {
            int others = (1 << 20) | (1 << 21) | (1 << 22);
            player.cullingMask = ~others | (1 << layer);
            player.nearClipPlane = 0.05f; player.farClipPlane = 400f;
            var rig = new GameObject(name).AddComponent<PortalRig>();
            rig.player = player; rig.portals = portals; rig.width = w; rig.height = 1080; rig.recursionLimit = 8;
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = name + " screen " + i; q.layer = layer;
                q.transform.SetParent(portals[i], false); q.transform.localPosition = new Vector3(0, 2f, 0); q.transform.localScale = new Vector3(3f, 4f, 1f);
                var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = screenMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                rig.screens[i] = mr;
                var c = new GameObject(name + " portal cam " + i).AddComponent<Camera>();
                c.fieldOfView = player.fieldOfView; c.nearClipPlane = player.nearClipPlane; c.farClipPlane = player.farClipPlane;
                c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = player.backgroundColor; c.allowHDR = true;
                c.cullingMask = player.cullingMask; c.enabled = false;
                var data = c.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false; data.renderShadows = true; data.antialiasing = AntialiasingMode.None;
                rig.cams[i] = c;
            }
            return rig;
        }

        static void Box(string name, Material m, Vector3 pos, Vector3 size, Transform parent)
        {
            var go = SceneKit.Part(name, cube, m, pos, Quaternion.identity, parent);
            go.transform.position = pos; go.transform.localScale = size;
        }
    }
}

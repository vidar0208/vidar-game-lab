// Builds the strand hair scene: two copies of a scanned head (Lee Perry-Smith head scan, CC BY 3.0), a generated groom
// stored as a Demo Team Hair "Custom" hair asset, one simulated and one rigid hair instance, sphere/capsule colliders
// with orange debug copies, a warm key + cool rim light, cameras and labels.
using System.Collections.Generic;
using Unity.DemoTeam.Hair;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class HairScene
    {
        const string Dir = "Assets/Techniques/strand-hair-sim/";
        const float S = 0.045f;                                   // metres per head-scan unit
        static readonly Vector3 Neck = new Vector3(0f, -1.6f, -0.3f);   // pivot, scan units
        static readonly Vector3 C = new Vector3(0f, 1.4f, -0.1f);       // scalp sampling centre, scan units
        static readonly Vector3 SkullC = new Vector3(0f, 1.35f, -0.05f), SkullR = new Vector3(1.95f, 2.65f, 2.2f);
        public static int Strands = 30000, Verts = 40;

        static float EMin(float az)
        {
            float[] a = { 0, 40, 70, 100, 130, 180 }, e = { 22, 22, 20, 18, -15, -38 };
            for (int i = 1; i < a.Length; i++) if (az <= a[i]) return Mathf.Lerp(e[i - 1], e[i], (az - a[i - 1]) / (a[i] - a[i - 1]));
            return e[e.Length - 1];
        }

        static Vector3 Constrain(Vector3 x, float o)
        {
            // skull shell (inflated ellipsoid)
            var q = x - SkullC; var R = SkullR + Vector3.one * o;
            var n = new Vector3(q.x / R.x, q.y / R.y, q.z / R.z);
            if (n.sqrMagnitude < 1f) { n = n.normalized; x = SkullC + new Vector3(n.x * R.x, n.y * R.y, n.z * R.z); }
            // neck cylinder
            if (x.y < SkullC.y - 0.5f)
            {
                var r = new Vector2(x.x, x.z + 0.35f); float rr = 1.35f + o;
                if (r.magnitude < rr) { r = r.normalized * rr; x.x = r.x; x.z = r.y - 0.35f; }
            }
            // upper torso: widening ellipse below the neck
            if (x.y < -2.4f)
            {
                float s = Mathf.Clamp01((-2.4f - x.y) / 1.2f);
                float rx = 1.3f + 1.9f * s + o, rz = 1.6f + 0.5f * s + o;
                var r = new Vector2(x.x / rx, (x.z + 0.4f) / rz);
                if (r.sqrMagnitude < 1f) { r = r.normalized; x.x = r.x * rx; x.z = r.y * rz - 0.4f; }
            }
            // keep side strands behind the face
            if (x.z > 0.4f && Mathf.Abs(x.x) < 2.3f && x.y > -1.5f && x.y < 2.2f) x.z = 0.4f;
            return x;
        }

        static HairGroom Groom(Mesh head)
        {
            var go = new GameObject("~groom collider"); go.layer = 31;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = head;
            Physics.SyncTransforms();
            var rnd = new System.Random(111);
            float R01() => (float)rnd.NextDouble();
            var counts = new List<int>(); var pos = new List<Vector3>();
            int tries = 0;
            while (counts.Count < Strands && tries < Strands * 20)
            {
                tries++;
                var d = new Vector3(R01() * 2 - 1, R01() * 2 - 1, R01() * 2 - 1);
                if (d.sqrMagnitude > 1f || d.sqrMagnitude < 1e-4f) continue;
                d.Normalize();
                float e = Mathf.Asin(d.y) * Mathf.Rad2Deg, az = Mathf.Abs(Mathf.Atan2(d.x, d.z)) * Mathf.Rad2Deg;
                if (e < EMin(az) + (R01() - 0.5f) * 6f) continue;
                if (!Physics.Raycast(C + d * 10f, -d, out var hit, 20f, 1 << 31)) continue;
                var p = hit.point; var nrm = hit.normal;
                float L = 5.6f + 0.8f * R01(), seg = L / (Verts - 1);
                float o = 0.03f + 0.2f * Mathf.Pow(R01(), 0.7f);
                var rad = new Vector3(p.x - C.x, 0f, p.z - C.z).normalized;
                float front = Mathf.Max(0f, rad.z);
                // comb: away from a centre part and backwards (strongly for the front hairline), then gravity
                var away = new Vector3(Mathf.Sign(p.x + 1e-4f) * (0.3f + 0.7f * Mathf.Abs(rad.x)), 0f, -(0.25f + 1.8f * front));
                var x = p; pos.Add(x);
                Vector3 t = Vector3.zero;
                for (int k = 1; k < Verts; k++)
                {
                    var q = x - SkullC; var nS = new Vector3(q.x / (SkullR.x * SkullR.x), q.y / (SkullR.y * SkullR.y), q.z / (SkullR.z * SkullR.z)).normalized;
                    Vector3 dir;
                    if (nS.y < 0.15f && x.y < SkullC.y) dir = Vector3.down;              // past the widest point: hang
                    else
                    {
                        dir = Vector3.ProjectOnPlane(Vector3.down + away * 0.9f, nS);   // slide over the scalp
                        dir = dir.sqrMagnitude < 1e-6f ? Vector3.down : dir.normalized;
                    }
                    t = k == 1 ? (dir + nrm * 0.25f).normalized : Vector3.Slerp(t, dir, 0.5f).normalized;
                    x = Constrain(x + t * seg, o * Mathf.Clamp01(k / 3f));
                    pos.Add(x);
                }
                counts.Add(Verts);
            }
            Object.DestroyImmediate(go);
            // scan units -> metres in the hair instance's local space (same origin as the head mesh)
            for (int i = 0; i < pos.Count; i++) pos[i] = pos[i] * S;
            var groom = ScriptableObject.CreateInstance<HairGroom>();
            groom.counts = counts.ToArray(); groom.positions = pos.ToArray();
            groom.diameterMM = 0.5f; groom.tipScale = 0.15f; groom.tipOffset = 0.6f;
            AssetDatabase.CreateAsset(groom, Dir + "Groom.asset");
            Debug.Log($"[gt] groom: {counts.Count} strands x {Verts} vertices from {tries} samples");
            return groom;
        }

        static bool Scalp(Vector3 p, float margin)
        {
            var d = (p - C).normalized;
            float e = Mathf.Asin(d.y) * Mathf.Rad2Deg, az = Mathf.Abs(Mathf.Atan2(d.x, d.z)) * Mathf.Rad2Deg;
            return e > EMin(az) + margin;
        }

        // hair cap: the head triangles inside the hairline, pushed out 0.03 units, drawn dark under the strands
        static Mesh Cap(Mesh head)
        {
            var v = head.vertices; var n = head.normals; var t = head.triangles;
            var tris = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
                if (Scalp(v[t[i]], 10f) && Scalp(v[t[i + 1]], 10f) && Scalp(v[t[i + 2]], 10f)) tris.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
            var vv = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) vv[i] = v[i] + n[i] * 0.03f;
            var m = new Mesh { name = "ScalpCap", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = vv; m.normals = n; m.triangles = tris.ToArray(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "ScalpCap.asset");
            Debug.Log($"[gt] scalp cap {tris.Count / 3} triangles");
            return m;
        }

        static Material TransparentProxy()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.SetColor("_BaseColor", new Color(1f, 0.5f, 0.05f, 0.35f)); m.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(m, Dir + "ColliderProxy.mat");
            return m;
        }

        static void Collider(Transform parent, string name, int kind, Vector3 c, float r, float h, int axis, Material proxyM, List<Renderer> proxies)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            if (kind == 0) { var sc = go.AddComponent<SphereCollider>(); sc.center = c; sc.radius = r; }
            else { var cc = go.AddComponent<CapsuleCollider>(); cc.center = c; cc.radius = r; cc.height = h; cc.direction = axis; }
            var p = GameObject.CreatePrimitive(kind == 0 ? PrimitiveType.Sphere : PrimitiveType.Capsule);
            Object.DestroyImmediate(p.GetComponent<Collider>()); p.name = name + " proxy"; p.layer = 26;
            p.transform.SetParent(parent, false); p.transform.localPosition = c;
            if (kind == 0) p.transform.localScale = Vector3.one * 2f * r;
            else
            {
                p.transform.localRotation = axis == 0 ? Quaternion.Euler(0, 0, 90f) : axis == 2 ? Quaternion.Euler(90f, 0, 0) : Quaternion.identity;
                p.transform.localScale = new Vector3(2f * r, h / 2f, 2f * r);
            }
            var pr = p.GetComponent<Renderer>(); pr.sharedMaterial = proxyM; pr.shadowCastingMode = ShadowCastingMode.Off; pr.enabled = false;
            proxies.Add(pr);
        }

        static Mesh capMesh; static Material capM;
        static HairInstance Bust(string name, Vector3 at, Mesh head, Material skin, HairAsset asset, Material hairM, bool rigid, Material proxyM, List<Renderer> proxies, out Transform root)
        {
            root = new GameObject(name).transform; root.position = at;
            var mesh = new GameObject("Head scan"); mesh.transform.SetParent(root, false);
            mesh.transform.localPosition = -Neck * S; mesh.transform.localScale = Vector3.one * S;
            mesh.AddComponent<MeshFilter>().sharedMesh = head;
            var mr = mesh.AddComponent<MeshRenderer>(); mr.sharedMaterial = skin; mr.shadowCastingMode = ShadowCastingMode.On;
            var cols = new GameObject("Colliders").transform; cols.SetParent(root, false);
            cols.localPosition = -Neck * S; cols.localScale = Vector3.one * S;
            Collider(cols, "Skull", 0, new Vector3(0f, 1.5f, -0.3f), 2.0f, 0f, 0, proxyM, proxies);
            Collider(cols, "Face", 0, new Vector3(0f, 0.9f, 0.3f), 2.25f, 0f, 0, proxyM, proxies);
            Collider(cols, "Neck", 1, new Vector3(0f, -1.6f, -0.3f), 1.15f, 3.6f, 1, proxyM, proxies);
            Collider(cols, "Shoulders", 1, new Vector3(0f, -3.6f, -0.5f), 0.95f, 7.6f, 0, proxyM, proxies);

            var cap = new GameObject("Scalp cap"); cap.transform.SetParent(root, false);
            cap.transform.localPosition = -Neck * S; cap.transform.localScale = Vector3.one * S;
            cap.AddComponent<MeshFilter>().sharedMesh = capMesh; cap.AddComponent<MeshRenderer>().sharedMaterial = capM;
            var hgo = new GameObject("Hair"); hgo.transform.SetParent(root, false); hgo.transform.localPosition = -Neck * S;
            var hi = hgo.AddComponent<HairInstance>();
            hi.strandGroupProviders = new[] { new HairInstance.GroupProvider { hairAsset = asset } };
            var ex = hi.settingsExecutive; ex.updateSimulationRate = HairInstance.SettingsExecutive.UpdateRate.Fixed90Hz; ex.updateStepsMax = true; ex.updateStepsMaxValue = 4; hi.settingsExecutive = ex;
            var gs = hi.strandGroupDefaults;
            var ph = gs.settingsPhysics;
            ph.constraintIterations = 4; ph.boundaryCollision = true; ph.boundaryCollisionFriction = 0.3f;
            ph.localShape = true; ph.localShapeInfluence = 0.15f;
            ph.globalRotation = true; ph.globalRotationInfluence = 0.3f; ph.globalFade = true; ph.globalFadeOffset = 0.15f; ph.globalFadeExtent = 0.25f;
            ph.dampingLinear = false; ph.dampingAngular = false;
            if (rigid) { ph.globalPosition = true; ph.globalPositionInfluence = 1f; ph.globalPositionInterval = HairSim.SettingsPhysics.TimeInterval.Per1ms; ph.globalRotation = true; ph.globalRotationInfluence = 1f; }
            gs.settingsPhysics = ph;
            var rs = gs.settingsRendering; rs.material = true; rs.materialAsset = hairM; rs.renderer = HairSim.SettingsRendering.Renderer.BuiltinStrips; rs.allowIndirect = false; rs.clipThreshold = 0f; rs.kLODSelection = HairSim.RenderLODSelection.Manual; rs.kLODSelectionValue = 1f; rs.rendererShadows = ShadowCastingMode.On; gs.settingsRendering = rs;
            hi.strandGroupDefaults = gs;
            HairInstanceBuilder.BuildHairInstance(hi, hi.strandGroupProviders, HideFlags.NotEditable);
            return hi;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.16f, 0.17f, 0.2f), new Color(0.1f, 0.1f, 0.11f), new Color(0.05f, 0.045f, 0.04f));
            SceneKit.Shadows(4f);
            var dir = new GameObject("Director").AddComponent<HairDirector>();

            var nrmPath = Dir + "Head/Infinite-Level_02_Tangent_SmoothUV.jpg";
            var ti = (TextureImporter)AssetImporter.GetAtPath(nrmPath); ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport();
            var mi = (ModelImporter)AssetImporter.GetAtPath(Dir + "Head/Head.obj"); mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk; mi.isReadable = true; mi.SaveAndReimport();
            Mesh head = null; foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Dir + "Head/Head.obj")) if (o is Mesh m) head = m;
            if (head == null) { Debug.LogError("[gt] no head mesh"); return; }
            Debug.Log($"[gt] head mesh {head.vertexCount} verts, bounds {head.bounds}");

            var skin = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            skin.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Head/Map-COL.jpg")); skin.SetColor("_BaseColor", new Color(0.82f, 0.76f, 0.74f));
            skin.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath)); skin.SetFloat("_BumpScale", 1f); skin.EnableKeyword("_NORMALMAP");
            skin.SetFloat("_Smoothness", 0.38f);
            AssetDatabase.CreateAsset(skin, Dir + "Skin.mat");

            var hairM = new Material(Shader.Find("GameTech/HairStrandKK")); hairM.SetColor("_BaseColor", new Color(0.24f, 0.16f, 0.1f));
            Debug.Log($"[gt] hair shader supported {hairM.shader.isSupported}");
            AssetDatabase.CreateAsset(hairM, Dir + "Hair.mat");

            capMesh = Cap(head);
            capM = new Material(Shader.Find("Universal Render Pipeline/Lit")); capM.SetColor("_BaseColor", new Color(0.14f, 0.095f, 0.06f)); capM.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(capM, Dir + "ScalpCap.mat");
            var groom = Groom(head);
            var asset = ScriptableObject.CreateInstance<HairAsset>();
            asset.settingsBasic.type = HairAsset.Type.Custom; asset.settingsBasic.kLODClusters = true;
            asset.settingsCustom.dataProvider = groom;
            var res = asset.settingsCustom.settingsResolve; res.resampleCurves = true; res.resampleResolution = 24; res.resampleQuality = 3;
            res.rootUV = HairAsset.SettingsResolve.RootUV.UseFallback; asset.settingsCustom.settingsResolve = res;
            AssetDatabase.CreateAsset(asset, Dir + "Hair.asset");
            HairAssetBuilder.BuildHairAsset(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] hair asset: checksum {asset.checksum}, groups {asset.strandGroups?.Length ?? 0}, lods {(asset.strandGroups != null ? asset.strandGroups[0].lodCount : 0)} strands {(asset.strandGroups != null ? asset.strandGroups[0].strandCount : 0)} x {(asset.strandGroups != null ? asset.strandGroups[0].strandParticleCount : 0)}");

            var proxyM = TransparentProxy();
            var proxies = new List<Renderer>();
            var hs = Bust("Bust simulated", new Vector3(0f, 1.4f, 0f), head, skin, asset, hairM, false, proxyM, proxies, out var b0);
            var proxies0 = new List<Renderer>(proxies);
            var hr = Bust("Bust rigid", new Vector3(5f, 1.4f, 0f), head, skin, asset, hairM, true, proxyM, proxies, out var b1);
            foreach (var r in proxies) if (!proxies0.Contains(r)) Object.DestroyImmediate(r.gameObject);
            dir.busts = new[] { b0, b1 };
            var dbg = dir.gameObject.AddComponent<HairDebug>(); dbg.sim = hs; dbg.rigid = hr; dbg.simRoot = b0; dbg.rigidRoot = b1; dbg.from = 3.7f; dbg.to = 11.5f; dbg.every = 0.1f; dir.hair = new[] { hs, hr }; dir.proxies = proxies0.ToArray();

            dir.key = SceneKit.Sun("Key", new Vector3(28f, -130f, 0f), new Color(1f, 0.86f, 0.72f), 2.2f, true);
            dir.key.shadowBias = 0.02f; dir.key.shadowNormalBias = 0.1f;
            dir.rim = SceneKit.Sun("Rim", new Vector3(18f, 150f, 0f), new Color(0.7f, 0.82f, 1f), 3.0f, false);
            SceneKit.Grade(Dir, 0.2f, 0.0f, 10f, 0f, 0.32f, 0.06f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.03f, 0.032f, 0.036f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 30f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 30f, bg, 1, true);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.02f; c.farClipPlane = 20f; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            Debug.Log("[gt] built: 2 busts (simulated + rigid), groom, colliders, key + rim light");
        }
    }
}

// Builds the capsule occlusion scene from code: a room corner (floor, back wall, side wall), a pillar, a bench,
// the stand-in mannequin with one OcclusionCapsule per body capsule (18, same table as its mesh) parented to its
// bones, orange debug copies of the capsules on their own layer, a soft sky ambient and a weak sun. Every surface
// uses GameTech/CapsuleLit, which multiplies the ambient term by the capsule occlusion.
using CapsuleOcclusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class CapsuleScene
    {
        const string Dir = "Assets/Techniques/capsule-occlusion/";
        const string Shd = "GameTech/CapsuleLit";

        static Material M(string name, Color c, float smooth)
        {
            var m = new Material(Shader.Find(Shd)); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static void Capsule(Transform root, Transform bone, Vector3 a, Vector3 b, float r, Material proxyM, int i)
        {
            var go = new GameObject("Occlusion capsule " + i);
            Vector3 wa = root.TransformPoint(a), wb = root.TransformPoint(b);
            float len = Vector3.Distance(wa, wb);
            go.transform.position = (wa + wb) * 0.5f;
            go.transform.rotation = len > 1e-4f ? Quaternion.LookRotation(wb - wa) : Quaternion.identity;
            go.transform.SetParent(bone, true);
            var oc = go.AddComponent<OcclusionCapsule>(); oc.size = len; oc.radius = r;
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule); p.name = "Debug proxy"; p.layer = 25;
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(go.transform, false);
            p.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            p.transform.localScale = new Vector3(2 * r, (len + 2 * r) / 2f, 2 * r);
            var pr = p.GetComponent<Renderer>(); pr.sharedMaterial = proxyM; pr.shadowCastingMode = ShadowCastingMode.Off;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.55f, 0.62f, 0.75f), new Color(0.3f, 0.3f, 0.32f), new Color(0.16f, 0.15f, 0.14f));
            SceneKit.Shadows(15f);
            var dir = new GameObject("Director").AddComponent<CapsuleDirector>();

            var floorM = M("Floor", new Color(0.62f, 0.6f, 0.57f), 0.25f);
            var wallM = M("Wall", new Color(0.7f, 0.68f, 0.64f), 0.1f);
            var pillarM = M("Pillar", new Color(0.55f, 0.5f, 0.46f), 0.15f);
            var woodM = M("Wood", new Color(0.45f, 0.3f, 0.2f), 0.3f);
            Box("Floor", floorM, new Vector3(0, -0.05f, 0), new Vector3(10f, 0.1f, 10f));
            Box("Back wall", wallM, new Vector3(0, 1.5f, 1.7f), new Vector3(10f, 3f, 0.2f));
            Box("Side wall", wallM, new Vector3(2.1f, 1.5f, -1f), new Vector3(0.2f, 3f, 5.6f));
            Box("Skirting", woodM, new Vector3(0, 0.04f, 1.59f), new Vector3(10f, 0.08f, 0.02f));
            Box("Pillar", pillarM, new Vector3(1.37f, 1.5f, 1.42f), new Vector3(0.3f, 3f, 0.3f));
            Box("Bench", woodM, new Vector3(-1.1f, 0.22f, 1.2f), new Vector3(0.9f, 0.06f, 0.35f));
            Box("Bench leg 1", woodM, new Vector3(-1.45f, 0.1f, 1.2f), new Vector3(0.06f, 0.2f, 0.3f));
            Box("Bench leg 2", woodM, new Vector3(-0.75f, 0.1f, 1.2f), new Vector3(0.06f, 0.2f, 0.3f));

            var man = Mannequin.Build(Shd, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.8f, 0.66f, 0.56f) : new Color(0.62f, 0.64f, 0.68f));
                m.SetFloat("_Smoothness", 0.3f); m.SetFloat("_Character", 1f);
            });
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.shadowCastingMode = ShadowCastingMode.On; smr.updateWhenOffscreen = true; smr.gameObject.layer = 24; }
            dir.character = man.transform;

            // occlusion capsules: the mannequin's own capsule table, bound to the same bones
            var proxyM = new Material(Shader.Find("Universal Render Pipeline/Lit")); proxyM.SetColor("_BaseColor", new Color(1f, 0.5f, 0.05f));
            proxyM.EnableKeyword("_EMISSION"); proxyM.SetColor("_EmissionColor", new Color(0.5f, 0.22f, 0.02f));
            proxyM.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(proxyM, Dir + "CapsuleProxy.mat");
            Transform B(string n) { foreach (var t in man.GetComponentsInChildren<Transform>()) if (t.name == n) return t; Debug.LogError("[gt] no bone " + n); return null; }
            var root = man.transform; int i = 0;
            Capsule(root, B("Hips"), new Vector3(-0.05f, 0.75f, 0), new Vector3(0.05f, 0.75f, 0), 0.085f, proxyM, i++);
            Capsule(root, B("Spine"), new Vector3(0, 0.80f, 0), new Vector3(0, 0.94f, 0), 0.08f, proxyM, i++);
            Capsule(root, B("Chest"), new Vector3(0, 0.98f, 0), new Vector3(0, 1.07f, 0), 0.105f, proxyM, i++);
            Capsule(root, B("Chest"), new Vector3(-0.12f, 1.09f, 0), new Vector3(0.12f, 1.09f, 0), 0.055f, proxyM, i++);
            Capsule(root, B("Neck"), new Vector3(0, 1.11f, 0), new Vector3(0, 1.17f, 0), 0.035f, proxyM, i++);
            Capsule(root, B("Head"), new Vector3(0, 1.265f, 0.01f), new Vector3(0, 1.275f, 0.01f), 0.11f, proxyM, i++);
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1 : 1; string n = side == 0 ? "_L" : "_R";
                Capsule(root, B("Upperarm" + n), new Vector3(0.15f * sx, 1.10f, 0), new Vector3(0.36f * sx, 1.10f, 0), 0.038f, proxyM, i++);
                Capsule(root, B("Lowerarm" + n), new Vector3(0.38f * sx, 1.10f, 0), new Vector3(0.56f * sx, 1.10f, 0), 0.032f, proxyM, i++);
                Capsule(root, B("Hand" + n), new Vector3(0.61f * sx, 1.10f, 0), new Vector3(0.63f * sx, 1.10f, 0), 0.04f, proxyM, i++);
                Capsule(root, B("Upperleg" + n), new Vector3(0.085f * sx, 0.69f, 0), new Vector3(0.085f * sx, 0.41f, 0), 0.062f, proxyM, i++);
                Capsule(root, B("Lowerleg" + n), new Vector3(0.085f * sx, 0.38f, 0), new Vector3(0.085f * sx, 0.10f, 0), 0.046f, proxyM, i++);
                Capsule(root, B("Foot" + n), new Vector3(0.085f * sx, 0.04f, -0.03f), new Vector3(0.085f * sx, 0.04f, 0.10f), 0.04f, proxyM, i++);
            }
            man.transform.position = new Vector3(-0.75f, 0f, 1.42f);

            SceneKit.Sun("Sun", new Vector3(38f, 28f, 0f), new Color(1f, 0.9f, 0.78f), 0.35f, true);
            SceneKit.Grade(Dir, 0.15f, 0.0f, 8f, 4f, 0.3f, 0.08f);
            AssetDatabase.SaveAssets();

            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "CapsuleOcclusion/CapsuleOcclusion.compute");
            if (compute == null) Debug.LogError("[gt] missing compute shader");
            var bg = new Color(0.04f, 0.045f, 0.05f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 40f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 40f, bg, 1, true);
            dir.camC = SceneKit.Cam("Cam C", new Rect(0, 0, 1, 1), 40f, bg, 2, true);
            foreach (var c in new[] { dir.camA, dir.camB, dir.camC })
            {
                c.nearClipPlane = 0.05f; c.farClipPlane = 40f;
                var co = c.gameObject.AddComponent<CapsuleOcclusionURP>(); co.compute = compute; co.maxCameraRange = 12f;
            }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            dir.r1 = LabelSpec.Create("", 0.68f, 0.058f, 34); dir.r2 = LabelSpec.Create("", 0.68f, 0.018f, 30);
            Debug.Log($"[gt] built: room corner, pillar, bench, mannequin with {i} occlusion capsules");
        }
    }
}

// Stand-in character built from code: one skinned mesh of capsules, each part bound rigidly to one bone.
// 1.39 m tall, T-pose, facing +z, its left side at -x. Bone names match CharacterRig's names.
// Two materials: "costume" (torso, upper arms, legs, feet) and "skin" (head, neck, forearms, hands).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech
{
    public static class Mannequin
    {
        static readonly string[] Names = { "Hips", "Spine", "Chest", "Neck", "Head",
            "Upperarm_L", "Lowerarm_L", "Hand_L", "Upperarm_R", "Lowerarm_R", "Hand_R",
            "Upperleg_L", "Lowerleg_L", "Foot_L", "Upperleg_R", "Lowerleg_R", "Foot_R" };
        static readonly int[] Parent = { -1, 0, 1, 2, 3, 2, 5, 6, 2, 8, 9, 0, 11, 12, 0, 14, 15 };
        static readonly Vector3[] Pos = {
            new Vector3(0, 0.74f, 0), new Vector3(0, 0.84f, 0), new Vector3(0, 0.98f, 0), new Vector3(0, 1.13f, 0), new Vector3(0, 1.17f, 0),
            new Vector3(-0.15f, 1.10f, 0), new Vector3(-0.38f, 1.10f, 0), new Vector3(-0.58f, 1.10f, 0),
            new Vector3(0.15f, 1.10f, 0), new Vector3(0.38f, 1.10f, 0), new Vector3(0.58f, 1.10f, 0),
            new Vector3(-0.085f, 0.70f, 0), new Vector3(-0.085f, 0.39f, 0), new Vector3(-0.085f, 0.07f, 0),
            new Vector3(0.085f, 0.70f, 0), new Vector3(0.085f, 0.39f, 0), new Vector3(0.085f, 0.07f, 0) };

        static readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        static readonly List<Vector2> UV = new List<Vector2>();
        static readonly List<BoneWeight> W = new List<BoneWeight>();
        static readonly List<int>[] Tri = { new List<int>(), new List<int>() };

        // capsule from a to b with radius r (a == b gives a sphere), bound to one bone, into submesh sub (0 costume, 1 skin)
        static void Capsule(Vector3 a, Vector3 b, float r, int bone, int sub)
        {
            const int S = 18, H = 6;
            var d = b - a; var q = d.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.FromToRotation(Vector3.up, d.normalized);
            int start = V.Count, rings = 2 * (H + 1);
            for (int j = 0; j < rings; j++)
            {
                bool top = j > H; float phi = top ? (j - H - 1) / (float)H * Mathf.PI * 0.5f : (j / (float)H - 1) * Mathf.PI * 0.5f;
                var c = top ? b : a;
                for (int s = 0; s <= S; s++)
                {
                    float th = s / (float)S * Mathf.PI * 2;
                    var n = q * new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                    V.Add(c + n * r); N.Add(n); UV.Add(new Vector2(s / (float)S, j / (float)(rings - 1)));
                    W.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 });
                }
            }
            var t = Tri[sub];
            for (int j = 0; j < rings - 1; j++)
                for (int s = 0; s < S; s++)
                {
                    int v0 = start + j * (S + 1) + s, v1 = v0 + 1, v2 = v0 + S + 1, v3 = v2 + 1;
                    t.Add(v0); t.Add(v2); t.Add(v1); t.Add(v1); t.Add(v2); t.Add(v3);
                }
        }

        static int B(string name) => System.Array.IndexOf(Names, name);

        public static GameObject Build(string shader, string matDir, System.Action<Material, string> setup)
        {
            V.Clear(); N.Clear(); UV.Clear(); W.Clear(); Tri[0].Clear(); Tri[1].Clear();
            var root = new GameObject("Character");
            var bones = new Transform[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                bones[i] = new GameObject(Names[i]).transform;
                bones[i].SetParent(Parent[i] < 0 ? root.transform : bones[Parent[i]], false);
                bones[i].position = Pos[i];
            }
            Capsule(new Vector3(-0.05f, 0.75f, 0), new Vector3(0.05f, 0.75f, 0), 0.085f, B("Hips"), 0);
            Capsule(new Vector3(0, 0.80f, 0), new Vector3(0, 0.94f, 0), 0.08f, B("Spine"), 0);
            Capsule(new Vector3(0, 0.98f, 0), new Vector3(0, 1.07f, 0), 0.105f, B("Chest"), 0);
            Capsule(new Vector3(-0.12f, 1.09f, 0), new Vector3(0.12f, 1.09f, 0), 0.055f, B("Chest"), 0);
            Capsule(new Vector3(0, 1.11f, 0), new Vector3(0, 1.17f, 0), 0.035f, B("Neck"), 1);
            Capsule(new Vector3(0, 1.265f, 0.01f), new Vector3(0, 1.275f, 0.01f), 0.11f, B("Head"), 1);
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1 : 1; string n = side == 0 ? "_L" : "_R";
                Capsule(new Vector3(0.15f * sx, 1.10f, 0), new Vector3(0.36f * sx, 1.10f, 0), 0.038f, B("Upperarm" + n), 0);
                Capsule(new Vector3(0.38f * sx, 1.10f, 0), new Vector3(0.56f * sx, 1.10f, 0), 0.032f, B("Lowerarm" + n), 1);
                Capsule(new Vector3(0.61f * sx, 1.10f, 0), new Vector3(0.63f * sx, 1.10f, 0), 0.04f, B("Hand" + n), 1);
                Capsule(new Vector3(0.085f * sx, 0.69f, 0), new Vector3(0.085f * sx, 0.41f, 0), 0.062f, B("Upperleg" + n), 0);
                Capsule(new Vector3(0.085f * sx, 0.38f, 0), new Vector3(0.085f * sx, 0.10f, 0), 0.046f, B("Lowerleg" + n), 0);
                Capsule(new Vector3(0.085f * sx, 0.04f, -0.03f), new Vector3(0.085f * sx, 0.04f, 0.10f), 0.04f, B("Foot" + n), 0);
            }
            var mesh = new Mesh { name = "Mannequin", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV); mesh.boneWeights = W.ToArray();
            mesh.subMeshCount = 2; mesh.SetTriangles(Tri[0], 0); mesh.SetTriangles(Tri[1], 1);
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * root.transform.localToWorldMatrix;
            mesh.bindposes = bind; mesh.RecalculateBounds(); mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, matDir + "Mannequin.asset");

            var mats = new Material[2];
            string[] mname = { "costume", "skin" };
            Color[] col = { new Color(0.42f, 0.46f, 0.55f), new Color(0.92f, 0.82f, 0.74f) };
            for (int k = 0; k < 2; k++)
            {
                var m = new Material(Shader.Find(shader)); m.SetColor("_BaseColor", col[k]);
                setup?.Invoke(m, mname[k]);
                AssetDatabase.CreateAsset(m, matDir + "Char_" + mname[k] + ".mat");
                mats[k] = m;
            }
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = bones; smr.rootBone = bones[0]; smr.sharedMaterials = mats; smr.updateWhenOffscreen = true;
            return root;
        }
    }
}

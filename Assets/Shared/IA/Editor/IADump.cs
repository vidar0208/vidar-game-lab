// One-off: logs the IA model's renderers, materials and bones so post scripts can pose and shade it.
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameTech
{
    public static class IADump
    {
        public static void Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Shared/IA/ia.fbx");
            var go = Object.Instantiate(prefab);
            var sb = new StringBuilder("[gt] IA dump\n");
            sb.AppendLine("root scale " + go.transform.localScale + " rot " + go.transform.localEulerAngles);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                sb.Append($"R {r.GetType().Name} '{r.name}' parent '{r.transform.parent?.name}' bounds c{b.center} s{b.size} mats:");
                foreach (var m in r.sharedMaterials) sb.Append(" [" + (m ? m.name : "null") + "]");
                if (r is SkinnedMeshRenderer s) sb.Append($" bones {s.bones.Length} verts {s.sharedMesh.vertexCount} tris {s.sharedMesh.triangles.Length / 3} blend {s.sharedMesh.blendShapeCount} uv {s.sharedMesh.uv.Length}");
                sb.AppendLine();
            }
            void Walk(Transform t, int d)
            {
                if (t.GetComponent<Renderer>() == null)
                    sb.AppendLine(new string(' ', d) + t.name + " p" + t.position.ToString("F3") + " e" + t.localEulerAngles.ToString("F0"));
                if (d < 14) foreach (Transform c in t) Walk(c, d + 1);
            }
            Walk(go.transform, 0);
            Debug.Log(sb.ToString());
            new GameObject("cam").AddComponent<Camera>();
        }
    }
}

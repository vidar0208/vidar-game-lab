// Spawns the scene character with one new material per source material name, so each technique can apply its own shader.
// Uses the first model (FBX or prefab) found in Assets/Shared/Character/Model/. That folder is not part of the repository:
// put your own rigged character there (Humanoid rig recommended, about 1.4 m tall, T-pose). Without a model it builds
// the stand-in mannequin below, so every scene still builds and renders.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech
{
    public static class CharacterKit
    {
        public const string ModelDir = "Assets/Shared/Character/Model";

        public static GameObject FindModel()
        {
            if (!AssetDatabase.IsValidFolder(ModelDir)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ModelDir }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null) return go;
            }
            return null;
        }

        // setup(material, sourceMaterialName) lets the technique set its own shader properties
        public static GameObject Spawn(string shader, string matDir, System.Action<Material, string> setup = null)
        {
            var prefab = FindModel();
            if (prefab == null)
            {
                Debug.Log("[gt] no character model in " + ModelDir + ", building the stand-in mannequin");
                var man = Mannequin.Build(shader, matDir, setup);
                man.AddComponent<CharacterRig>();
                return man;
            }
            var go = Object.Instantiate(prefab);
            go.name = "Character";
            var cache = new Dictionary<string, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string name = src[i] != null ? src[i].name : "none";
                    if (!cache.TryGetValue(name, out var m))
                    {
                        m = new Material(Shader.Find(shader));
                        Texture tex = null;
                        if (src[i] != null) tex = src[i].HasProperty("_BaseMap") ? src[i].GetTexture("_BaseMap") : src[i].mainTexture;
                        if (tex != null) m.SetTexture("_BaseMap", tex);
                        m.SetColor("_BaseColor", Color.white);
                        setup?.Invoke(m, name);
                        AssetDatabase.CreateAsset(m, matDir + "Char_" + name.Replace('.', '_').Replace('/', '_') + ".mat");
                        cache[name] = m;
                    }
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            }
            go.AddComponent<CharacterRig>();
            return go;
        }
    }
}

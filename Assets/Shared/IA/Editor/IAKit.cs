// Spawns the IA model with one material per source material name, textures assigned by name
// (the FBX has no embedded textures), underwear helper meshes hidden, IARig attached.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech
{
    public static class IAKit
    {
        const string Dir = "Assets/Shared/IA/";

        public static string TextureFor(string materialName)
        {
            string n = materialName.ToLowerInvariant();
            if (n.StartsWith("face")) return "iaface";
            if (n.Contains("hair") || n.Contains("halo")) return "iahair";
            if (n.StartsWith("skin")) return "iaSkin";
            return "ia_costume";
        }

        // setup(material, sourceMaterialName) lets the post set its own shader properties
        public static GameObject Spawn(string shader, string matDir, System.Action<Material, string> setup = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "ia.fbx");
            var go = Object.Instantiate(prefab);
            go.name = "IA";
            var cache = new Dictionary<string, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "calbra" || r.name == "Calpants") { r.gameObject.SetActive(false); continue; }
                var src = r.sharedMaterials; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string name = src[i] != null ? src[i].name : "none";
                    if (!cache.TryGetValue(name, out var m))
                    {
                        m = new Material(Shader.Find(shader));
                        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + TextureFor(name) + ".png");
                        m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
                        setup?.Invoke(m, name);
                        AssetDatabase.CreateAsset(m, matDir + "IA_" + name.Replace('.', '_') + ".mat");
                        cache[name] = m;
                    }
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            }
            go.AddComponent<IARig>();
            return go;
        }
    }
}

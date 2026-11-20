using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    // GameTech change: the source sets its shader inputs as globals (Shader.SetGlobal*). Two virtual textures run
    // side by side here, so each one writes its inputs to its own materials instead.
    public class VTBinding : MonoBehaviour
    {
        public List<Material> materials = new List<Material>();
        readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>();

        public void SetTexture(string name, Texture t) { textures[name] = t; foreach (var m in materials) if (m != null) m.SetTexture(name, t); }
        public void SetVector(string name, Vector4 v) { vectors[name] = v; foreach (var m in materials) if (m != null) m.SetVector(name, v); }
        public Texture GetTexture(string name) => textures.TryGetValue(name, out var t) ? t : null;

        public void Add(Material m)
        {
            materials.Add(m);
            foreach (var kv in textures) m.SetTexture(kv.Key, kv.Value);
            foreach (var kv in vectors) m.SetVector(kv.Key, kv.Value);
        }
    }
}

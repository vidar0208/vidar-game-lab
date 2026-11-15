// Drives the specular probe post. Every reflection the camera sees comes from a box-projected reflection probe in
// Custom mode holding one of the baked cubemaps. Each band renders its own copy of the room (see Shot), so each band
// shows the same view lit by the same lightmap with a different baked probe. No realtime lights are in the scene.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class SpecProbeDirector : MonoBehaviour
    {
        public Camera src;
        public Material compose;
        public ReflectionProbe probe;
        public Texture plain, specular, dim, big;   // baked cubemaps
        public LabelSpec title;
        public LabelSpec[] l1, l2;
        public ReflectionProbe[] probes;
        public string tuned = "", tunedShort = "", dimText = "", bigText = "";

        readonly RenderTexture[] rt = new RenderTexture[3];
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            for (int i = 0; i < 3; i++) { rt[i] = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt[i].Create(); }
            src.enabled = false;
            var originals = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            for (int k = 1; k <= probes.Length; k++)
                foreach (var r in originals)
                {
                    if (r.gameObject.layer == 8 || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null) continue;
                    var g = new GameObject(r.name + " copy " + k);
                    g.transform.SetPositionAndRotation(r.transform.position + Offset(k), r.transform.rotation); g.transform.localScale = r.transform.lossyScale;
                    g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var nr = g.AddComponent<MeshRenderer>(); nr.sharedMaterials = r.sharedMaterials; nr.shadowCastingMode = r.shadowCastingMode;
                    nr.lightmapIndex = r.lightmapIndex; nr.lightmapScaleOffset = r.lightmapScaleOffset;
                    nr.reflectionProbeUsage = r.reflectionProbeUsage;
                }
            for (int k = 0; k < probes.Length; k++) { probes[k].transform.position += Offset(k); probes[k].enabled = true; }
            Debug.Log($"[gt] room copies {probes.Length}, renderers per room {originals.Length}, mesh sample {originals[0].GetComponent<MeshFilter>()?.sharedMesh?.name}");
        }

        // cube == null: probe disabled (lightmap only, no reflections)
        void Shot(int i, Texture cube)
        {
            // Enabling a probe or swapping its cubemap only takes effect in the next frame, so each band renders its own
            // copy of the room, 100 m apart: copy k has probe k (plain, specular, dim, big), the last copy has none.
            // All copies use the same lightmap texels as the original room.
            int k = cube == null ? probes.Length : System.Array.FindIndex(probes, p => p.customBakedTexture == cube);
            Vector3 home = src.transform.position;
            src.transform.position = home + Offset(k);
            src.targetTexture = rt[i]; src.Render();
            src.transform.position = home;
            compose.SetTexture("_B" + i, rt[i]);
        }

        static Vector3 Offset(int k) => new Vector3(100f * k, 0f, 0f);

        void Labels(string ttl, float s1, float s2, string[] a, string[] b)
        {
            title.text = ttl;
            float[] start = { 0f, s1, s2 };
            for (int i = 0; i < 3; i++)
            {
                l1[i].text = a != null && i < a.Length ? a[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = b != null && i < b.Length ? b[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
            compose.SetFloat("_S1", s1); compose.SetFloat("_S2", s2);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            // slow lateral dolly along the glossy floor so the highlights slide over the surfaces
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 21f));
            float push = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21f, 26f, t));
            Vector3 pos = Vector3.Lerp(new Vector3(-2.3f, 1.25f, -3.3f), new Vector3(1.2f, 1.15f, -3.4f), u);
            pos = Vector3.Lerp(pos, new Vector3(-0.3f, 0.78f, -0.4f), push);
            src.transform.position = pos;
            src.transform.LookAt(Vector3.Lerp(new Vector3(0f, 0.75f, 0.6f), new Vector3(-0.55f, 0.56f, 0.55f), push));

            float third = 1f / 3f; int phase;
            if (t < 3f)
            {
                phase = 0; Shot(0, specular);
                compose.SetVector("_Off", Vector4.zero);
                Labels("Baked lights leave no highlight in a reflection probe. Bake a bright sphere at each light into it.", 1f, 1f,
                    new[] { "Specular probe" }, new[] { tuned });
            }
            else if (t < 10f)
            {
                phase = 1; Shot(0, plain); Shot(1, specular);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Same frame, same lightmap, two baked probes (centre of the frame)", 0.5f, 1f,
                    new[] { "Plain baked probe", "Specular probe" }, new[] { "baked lights are not in the cubemap", tuned });
            }
            else if (t < 16f)
            {
                phase = 2; Shot(0, dim); Shot(1, specular); Shot(2, big);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Sphere brightness and size, baked into three probes", third, 2 * third,
                    new[] { "Too dim", "Tuned", "Too big" }, new[] { dimText, tunedShort, bigText });
            }
            else if (t < 21f)
            {
                phase = 3; Shot(0, null); Shot(1, plain); Shot(2, specular);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("What the probe adds on top of the lightmap", third, 2 * third,
                    new[] { "Probe off", "Plain probe", "Specular probe" }, new[] { "lightmap only, no reflections", "room reflected, lights missing", "room plus light highlights" });
            }
            else
            {
                phase = 4; Shot(0, specular);
                compose.SetVector("_Off", Vector4.zero);
                Labels("The chrome ball shows the baked cubemap itself: the highlights are in the texture", 1f, 1f,
                    new[] { "Specular probe" }, new[] { tuned });
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}

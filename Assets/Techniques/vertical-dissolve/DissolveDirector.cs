// Timeline for the vertical dissolve post: writes the dissolve parameters into the materials every frame
// (the source ships a DissolveControl example script for the same job), moves cameras, the character and the two test pods.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class DissolveDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Material[] chrMats;
        public Material podMatL, podMatR;          // L / R = left / right on screen
        public Transform chr, podL, podR;

        static readonly Color Blue = new Color(0.05f, 0.5f, 0.9f), Orange = new Color(1.3f, 0.38f, 0.05f);

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }
        // left half of the split screen: border, waves and inside tint switched off in the shader
        void Begin(ScriptableRenderContext ctx, Camera cam) { Shader.SetGlobalFloat("_VD_Simple", cam == camL ? 1f : 0f); }

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }
        static float Key(float t, params float[] kv)
        {
            if (t <= kv[0]) return kv[1];
            for (int i = 0; i + 3 < kv.Length; i += 2)
                if (t < kv[i + 2]) return Mathf.Lerp(kv[i + 1], kv[i + 3], S(kv[i], kv[i + 2], t));
            return kv[kv.Length - 1];
        }

        static void Set(Material m, float fill, float invert, float border, float noise, float wave, Color tint, float world)
        {
            m.SetFloat("_Fill", fill); m.SetFloat("_Invertmask", invert); m.SetFloat("_Borderwidth", border);
            m.SetFloat("_Noisescale", noise); m.SetFloat("_Wave1amplitude", wave); m.SetFloat("_Wave2amplitude", wave);
            m.SetColor("_Fillcolor", tint); m.SetFloat("_Worldcoordinates", world);
        }

        void Look(Camera c, Vector3 pos, Vector3 at) { c.transform.position = pos; c.transform.LookAt(at); }

        void Update()
        {
            float t = FrameCapturer.T;
            bool A = t < 9f, pods = t >= 13f;
            camL.enabled = camR.enabled = A; camFull.enabled = !A;
            chr.gameObject.SetActive(!pods); podL.gameObject.SetActive(pods); podR.gameObject.SetActive(pods);
            Shader.SetGlobalFloat("_VD_Time", t); Shader.SetGlobalFloat("_VD_Simple", 0f);

            float fill = 0.87f, invert = 0f;
            if (A)
            {
                fill = Key(t, 0.3f, 0.87f, 4.0f, 0.20f, 4.6f, 0.20f, 8.6f, 0.87f);
                chr.rotation = Quaternion.Euler(0, -18f + t * 5f, 0);
                Vector3 p = Vector3.Lerp(new Vector3(0f, 0.14f, 3.25f), new Vector3(0f, 0.10f, 2.95f), S(0f, 9f, t));
                Look(camL, p, new Vector3(0, 0.02f, 0)); Look(camR, p, new Vector3(0, 0.02f, 0));
            }
            else if (!pods)
            {
                if (t < 11f) fill = Mathf.Lerp(0.87f, 0.14f, S(9.2f, 10.8f, t));
                else { invert = 1f; fill = Mathf.Lerp(0.87f, 0.13f, S(11.2f, 12.8f, t)); }
                chr.rotation = Quaternion.Euler(0, 27f - (t - 9f) * 6f, 0);
                float a = Mathf.Lerp(24f, 8f, S(9f, 13f, t)) * Mathf.Deg2Rad;
                Look(camFull, new Vector3(Mathf.Sin(a) * 3.7f, 0.2f, Mathf.Cos(a) * 3.7f), new Vector3(0, 0.0f, 0));
            }
            foreach (var m in chrMats) Set(m, fill, invert, 0.35f, 4f, 0.04f, Blue, 1f);

            if (pods)
            {
                Look(camFull, new Vector3(0f, 1.0f + 0.05f * S(13f, 24f, t), 3.35f), new Vector3(0, 0.06f, 0));
                if (t < 17f)
                {
                    float bob = 0.05f + 0.30f * Mathf.Sin((t - 13f) * 2.4f), tilt = 13f * Mathf.Sin((t - 13f) * 1.7f);
                    podL.position = new Vector3(0.85f, bob, 0); podR.position = new Vector3(-0.85f, bob, 0);
                    podL.rotation = podR.rotation = Quaternion.Euler(0, 0, tilt);
                    Set(podMatL, 0.5f, 0f, 0.35f, 4f, 0.04f, Blue, 0f);       // vertex (object) coordinates
                    Set(podMatR, 0.5f, 0f, 0.35f, 4f, 0.04f, Blue, 1f);       // world coordinates
                }
                else
                {
                    podL.position = new Vector3(0.85f, 0.05f, 0); podR.position = new Vector3(-0.85f, 0.05f, 0);
                    podL.rotation = podR.rotation = Quaternion.Euler(0, (t - 17f) * 20f, 0);
                    int step = Mathf.Min(3, (int)((t - 17f) / 1.75f));
                    Set(podMatL, 0.56f, 0f, 0.35f, 4f, 0.04f, Blue, 1f);
                    Set(podMatR, 0.56f, 0f, step == 0 ? 1.0f : 0.35f, step == 1 ? 10f : 4f, step == 2 ? 0.12f : 0.04f, step == 3 ? Orange : Blue, 1f);
                }
            }
        }
    }
}

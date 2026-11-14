// Drives the planar reflection post: moves the objects and the light, renders the mirrored camera(s) through
// PlanarReflectionPort, then renders the scene camera into a texture that the display quad shows.
// The "without planar" side of the comparison shows only the skybox reflection from the environment probe.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PlanarDirector : MonoBehaviour
    {
        public Camera src, probe;
        public Material compose;
        public Transform ball, torus, slab, orb;
        public GameObject[] movers;
        public LabelSpec title;
        public LabelSpec[] l1, l2;

        RenderTexture srcRT;
        PlanarReflectionPort[] ports;
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear };
            srcRT.Create();
            src.targetTexture = srcRT; src.enabled = false;
            compose.SetTexture("_A", srcRT);

            var floorGo = GameObject.Find("Floor"); if (floorGo) floorGo.layer = 9; // the floor is not drawn into its own reflection
            if (probe) probe.enabled = false;

            ports = new PlanarReflectionPort[3];
            for (int i = 0; i < 3; i++) ports[i] = new PlanarReflectionPort { shaderPropertyName = "_PlanarTex" + i, enableHdr = true, reflectLayers = ~((1 << 8) | (1 << 9)) };
        }

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
            Shader.SetGlobalFloat("_PlanarS1", s1); Shader.SetGlobalFloat("_PlanarS2", s2);
        }

        static string Res(PlanarReflectionPort p) => p.Texture ? $"{p.Texture.width} x {p.Texture.height}" : "?";

        void LateUpdate()
        {
            float t = FrameCapturer.T;

            // moving objects: a rolling ball, a floating torus, a sliding slab, a glowing orb on a figure-eight
            float bx = 3.2f * Mathf.Sin(t * 0.55f);
            ball.position = new Vector3(bx, 0.45f, -0.6f);
            ball.rotation = Quaternion.Euler(0, 0, -bx / 0.45f * Mathf.Rad2Deg);
            torus.position = new Vector3(-1.6f + 0.8f * Mathf.Sin(t * 0.4f), 1.1f + 0.35f * Mathf.Sin(t * 1.3f), 1.4f);
            torus.rotation = Quaternion.Euler(70f, t * 40f, 0);
            slab.position = new Vector3(2.2f - 1.2f * Mathf.Cos(t * 0.3f), 0.9f, 1.8f);
            orb.position = new Vector3(2.6f * Mathf.Sin(t * 0.7f), 1.5f + 0.4f * Mathf.Sin(t * 1.4f), 0.4f + 1.2f * Mathf.Sin(t * 1.4f));

            // camera: a slow arc that gets lower and closer for the resolution comparison
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15f, 17f, t)) - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20.5f, 23f, t));
            float a = (-12f + 8f * Mathf.Sin(t * 0.12f)) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(7.5f, 4.6f, k), h = Mathf.Lerp(1.15f, 0.55f, k);
            var target = new Vector3(0, Mathf.Lerp(0.75f, 0.45f, k), 0.6f);
            src.transform.position = target + new Vector3(Mathf.Sin(a) * r, h - target.y, -Mathf.Cos(a) * r);
            src.transform.LookAt(target);

            int phase;
            if (t < 3f || t >= 21f)
            {
                phase = t < 3f ? 0 : 4;
                Shader.SetGlobalFloat("_PlanarMode", 0);
                ports[0].clipPlaneOffset = 0.07f; ports[0].resolutionMultiplier = PlanarReflectionPort.ResolutionMultipliers.Full;
                ports[0].Render(src);
                Labels("Planar reflection: a mirrored camera renders the scene into a texture every frame", 1f, 1f,
                    new[] { $"Reflection texture {Res(ports[0])}, clip plane offset 0.07 m" }, null);
            }
            else if (t < 10f)
            {
                phase = 1;
                Shader.SetGlobalFloat("_PlanarMode", 1);
                ports[0].clipPlaneOffset = 0.07f; ports[0].resolutionMultiplier = PlanarReflectionPort.ResolutionMultipliers.Full;
                ports[0].Render(src);
                Labels("Same floor, two reflection sources", 0.5f, 1f,
                    new[] { "Skybox reflection only", $"Planar reflection, {Res(ports[0])}, every frame" },
                    new[] { "environment probe, no scene objects", "every object, moving ones included" });
            }
            else if (t < 16f)
            {
                phase = 2;
                Shader.SetGlobalFloat("_PlanarMode", 2);
                float[] off = { 0f, 0.07f, 0.4f };
                for (int i = 0; i < 3; i++) { ports[i].clipPlaneOffset = off[i]; ports[i].resolutionMultiplier = PlanarReflectionPort.ResolutionMultipliers.Full; ports[i].Render(src); }
                Labels("Clip plane offset: the source moves the mirror plane and the clip plane together", 1f / 3f, 2f / 3f,
                    new[] { "Offset 0.00 m", "Offset 0.07 m (source default)", "Offset 0.40 m" },
                    new[] { "mirror on the floor", "mirror 7 cm above the floor", "mirror 40 cm above the floor" });
            }
            else
            {
                phase = 3;
                Shader.SetGlobalFloat("_PlanarMode", 2);
                var m = new[] { PlanarReflectionPort.ResolutionMultipliers.Full, PlanarReflectionPort.ResolutionMultipliers.Half, PlanarReflectionPort.ResolutionMultipliers.Quarter };
                for (int i = 0; i < 3; i++) { ports[i].clipPlaneOffset = 0.07f; ports[i].resolutionMultiplier = m[i]; ports[i].Render(src); }
                Labels("Reflection texture resolution (multiplier of the 1920 x 1080 view)", 1f / 3f, 2f / 3f,
                    new[] { "Full", "Half", "Quarter" },
                    new[] { $"{Res(ports[0])} = {ports[0].Texture.width * ports[0].Texture.height / 1000}k pixels", $"{Res(ports[1])} = {ports[1].Texture.width * ports[1].Texture.height / 1000}k pixels", $"{Res(ports[2])} = {ports[2].Texture.width * ports[2].Texture.height / 1000}k pixels" });
            }
            src.Render();
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}

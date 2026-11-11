// Timeline for the post-processing scan post: pushes the scan parameters to the shader every frame (the source's
// PostProcessingScanOrigin script does the same for the origin), moves the cameras and the origin beacon.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class ScanDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Transform beacon;
        public Color colour = new Color(0.12f, 0.95f, 1.6f, 0.85f);

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }

        void Look(Camera c, Vector3 pos, Vector3 at) { c.transform.position = pos; c.transform.LookAt(at); }

        void Update()
        {
            float t = FrameCapturer.T;
            bool A = t < 8f;
            camL.enabled = camR.enabled = A; camFull.enabled = !A;

            float tiling = 0.2f, speed = 0.5f, power = 5f, radius = 12f, hard = 0.5f, mpow = 2f, dbg = 0f;
            Vector3 bpos = new Vector3(0.45f, 1.95f + 0.05f * Mathf.Sin(t * 1.7f), -0.45f);

            if (A)
            {
                float k = S(0f, 8f, t);
                Vector3 p = Vector3.Lerp(new Vector3(1.3f, 1.6f, 6.4f), new Vector3(0.5f, 1.4f, 4.3f), k);
                Vector3 at = new Vector3(0f, 0.95f, -1.0f); tiling = 0.3f; power = 8f;
                Look(camL, p, at); Look(camR, p, at);
            }
            else
            {
                float k = S(8f, 24f, t);
                Look(camFull, Vector3.Lerp(new Vector3(-5.2f, 4.3f, 7.4f), new Vector3(4.6f, 3.5f, 7.0f), k), new Vector3(0f, 0.5f, -3.0f));
                if (t < 10f) { }
                else if (t < 12f) tiling = 0.8f;
                else if (t < 13.7f) { tiling = 0.8f; radius = 6f; hard = 1f; mpow = 1f; }
                else if (t < 15.4f) { tiling = 0.8f; radius = 6f; hard = 0.2f; mpow = 1f; }
                else if (t < 19f)
                {
                    tiling = 0.4f;
                    float u = S(15.4f, 18.8f, t);
                    bpos = new Vector3(0.45f + 4.6f * Mathf.Sin(u * 3.6f) * (1f - 0.35f * u), 1.9f, -0.45f - 7.5f * Mathf.Sin(u * Mathf.PI * 0.62f));
                }
                else
                {
                    tiling = 0.4f;
                    dbg = 1 + Mathf.Min(2, (int)((t - 19f) / 1.6667f));
                    float u = 1f;
                    bpos = new Vector3(0.45f + 4.6f * Mathf.Sin(u * 3.6f) * (1f - 0.35f * u), 1.9f, -0.45f - 7.5f * Mathf.Sin(u * Mathf.PI * 0.62f));
                }
            }

            beacon.position = bpos;
            Shader.SetGlobalVector("_Scan_Origin", new Vector3(bpos.x, 0f, bpos.z));   // floor point under the beacon
            Shader.SetGlobalColor("_Scan_Colour", colour);
            Shader.SetGlobalFloat("_Scan_Tiling", tiling); Shader.SetGlobalFloat("_Scan_Speed", speed); Shader.SetGlobalFloat("_Scan_Power", power);
            Shader.SetGlobalFloat("_Scan_MaskRadius", radius); Shader.SetGlobalFloat("_Scan_MaskHardness", hard); Shader.SetGlobalFloat("_Scan_MaskPower", mpow);
            Shader.SetGlobalFloat("_Scan_MultiplyBlend", 0f);
            Shader.SetGlobalFloat("_Scan_Time", t); Shader.SetGlobalFloat("_Scan_Debug", dbg);
        }
    }
}

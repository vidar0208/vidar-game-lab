// Timeline for the toon shading post: split-screen comparison, layer-by-layer build-up, then the sun orbiting.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class ToonDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Transform sun;
        public Vector3 look = new Vector3(0, 0.78f, 0);
        float stage = 6;
        static readonly int StageId = Shader.PropertyToID("_Toon_Stage"), EdgeId = Shader.PropertyToID("_Toon_LitEdge"), BackId = Shader.PropertyToID("_Toon_Backlight");

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }
        void Begin(ScriptableRenderContext ctx, Camera cam) { Shader.SetGlobalFloat(StageId, cam == camL ? 0f : stage); }

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }
        static Vector3 Dir(float az, float el)
        {
            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }
        void Aim(Camera c, float az, float el, float dist, Vector3 target) { c.transform.position = target + dist * Dir(az, el); c.transform.LookAt(target); }

        void Update()
        {
            float t = FrameCapturer.T;
            bool A = t < 9f, B = t >= 9f && t < 17f;
            camL.enabled = camR.enabled = A; camFull.enabled = !A;
            float sunAz = 40f, sunEl = 27f, litEdge = 0.653f, backlight = 2.3f;
            stage = 6;
            if (A)
            {
                float az = Mathf.Lerp(-40f, 34f, S(0f, 9f, t));
                Aim(camL, az, 5f, 2.85f, look); Aim(camR, az, 5f, 2.85f, look);
            }
            else if (B)
            {
                stage = 1 + Mathf.Min(5, (int)((t - 9f) / (8f / 6f)));
                Aim(camFull, Mathf.Lerp(58f, 34f, S(9f, 17f, t)), 6f, 2.2f, look + new Vector3(0, 0.12f, 0));
            }
            else
            {
                // sun goes behind her, holds there while the backlight amount changes, then comes back round
                sunAz = t < 19.6f ? Mathf.Lerp(40f, 200f, (t - 17f) / 2.6f) : t < 21.4f ? 200f : Mathf.Lerp(200f, 400f, (t - 21.4f) / 2.6f);
                if (t >= 19.6f && t < 21.4f) backlight = 3.0f;
                if (t >= 21.4f) litEdge = 0.5f;
                Aim(camFull, Mathf.Lerp(20f, 4f, S(17f, 24f, t)), 6f, 2.45f, look + new Vector3(0, 0.08f, 0));
            }
            sun.rotation = Quaternion.LookRotation(-Dir(sunAz, sunEl));
            Shader.SetGlobalFloat(StageId, stage); Shader.SetGlobalFloat(EdgeId, litEdge); Shader.SetGlobalFloat(BackId, backlight);
        }
    }
}

// Timeline for the cross-section post. Does the job of the source's *CuttingController scripts
// (push plane position + normal to the shader every frame), plus cameras and the debug steps.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class CrossSectionDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Transform cap0, cap1, cap2, ghost;
        public GameObject overlay;
        public Vector3 look = new Vector3(0, 1.0f, 0);

        readonly Vector4[] pos = new Vector4[3], nrm = new Vector4[3];
        float count = 1, backMode = 1;
        static readonly int PosId = Shader.PropertyToID("_CS_PlanePos"), NrmId = Shader.PropertyToID("_CS_PlaneNrm"),
            CountId = Shader.PropertyToID("_CS_Count"), BackId = Shader.PropertyToID("_CS_BackMode");

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }
        // the left half of the split screen is "clip only": back faces lit, no cap (its camera does not see the cap layer)
        void Begin(ScriptableRenderContext ctx, Camera cam) { Shader.SetGlobalFloat(BackId, cam == camL ? 0f : backMode); }

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }

        void Aim(Camera c, float az, float el, float dist)
        {
            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
            c.transform.position = look + dist * new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            c.transform.LookAt(look);
        }

        void Plane(int i, Vector3 p, Vector3 n, Transform quad)
        {
            pos[i] = p; nrm[i] = n;
            if (quad == null) return;
            // Unity's quad faces its local -Z, so -Z must point to the removed side (same convention as the source controller)
            quad.position = p;
            quad.rotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
        }

        void Update()
        {
            float t = FrameCapturer.T;
            bool A = t < 9f, B = t >= 9f && t < 17f;
            camL.enabled = camR.enabled = A; camFull.enabled = !A;
            bool c0 = true, c12 = false, ov = false, gh = false;
            backMode = 1; count = 1;
            Vector3 mid = new Vector3(0, look.y, 0);

            if (A)
            {
                float z = Mathf.Lerp(0.47f, 0f, S(0.5f, 3.8f, t)) - 0.16f * S(5.6f, 8.8f, t);
                Plane(0, new Vector3(0, mid.y, z), Vector3.forward, cap0);
                float az = Mathf.Lerp(-27f, 27f, S(0f, 9f, t));
                Aim(camL, az, 11f, 3.9f); Aim(camR, az, 11f, 3.9f);
            }
            else if (B)
            {
                if (t < 13.4f)
                {
                    float x = Mathf.Lerp(1.12f, -0.45f, S(9.3f, 13.0f, t));
                    Plane(0, new Vector3(x, mid.y, 0), Vector3.right, cap0);
                }
                else
                {
                    count = 3; c12 = true;
                    float k = S(13.6f, 16.3f, t);
                    Plane(0, new Vector3(Mathf.Lerp(-0.45f, 0.02f, k), mid.y, 0), Vector3.right, cap0);
                    Plane(1, new Vector3(0, Mathf.Lerp(-0.06f, 1.05f, k), 0), Vector3.up, cap1);
                    Plane(2, new Vector3(0, mid.y, Mathf.Lerp(-0.56f, 0.0f, k)), Vector3.forward, cap2);
                }
                Aim(camFull, Mathf.Lerp(52f, 36f, S(9f, 17f, t)), Mathf.Lerp(14f, 24f, S(12.5f, 17f, t)), 3.5f);
            }
            else
            {
                Plane(0, new Vector3(0, mid.y, 0), Vector3.forward, cap0);
                ghost.position = cap0.position; ghost.rotation = cap0.rotation;
                int step = Mathf.Min(3, (int)((t - 17f) / 1.75f));
                if (step == 0) { backMode = 0; c0 = false; }
                else if (step == 1) { c0 = false; ov = true; }
                else if (step == 2) { c0 = false; gh = true; }
                Aim(camFull, Mathf.Lerp(-20f, 16f, S(17f, 24f, t)), 12f, 3.6f);
            }

            cap0.gameObject.SetActive(c0); cap1.gameObject.SetActive(c12); cap2.gameObject.SetActive(c12);
            overlay.SetActive(ov); ghost.gameObject.SetActive(gh);
            Shader.SetGlobalVectorArray(PosId, pos); Shader.SetGlobalVectorArray(NrmId, nrm);
            Shader.SetGlobalFloat(CountId, count); Shader.SetGlobalFloat(BackId, backMode);
        }
    }
}

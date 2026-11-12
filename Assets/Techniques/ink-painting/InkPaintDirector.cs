// Timeline for the ink painting post: split screens, the three steps one by one (global _InkStage),
// FeiBai on/off and the distance remap view on the left-hand ink set.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class InkPaintDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public GameObject litSet, inkLeftSet;
        public Material[] inkLeftMats;

        static float S(float a, float b, float t) { float x = Mathf.Clamp01((t - a) / (b - a)); return x * x * (3 - 2 * x); }
        static void Look(Camera c, Vector3 from, Vector3 at) { c.transform.position = from; c.transform.LookAt(at); }
        void Both(Vector3 from, Vector3 at) { Look(camL, from, at); Look(camR, from, at); }

        void Update()
        {
            float t = FrameCapturer.T;
            bool split = t < 7f || (t >= 15f && t < 23f);
            camL.enabled = camR.enabled = split; camFull.enabled = !split;
            litSet.SetActive(t < 7f); inkLeftSet.SetActive(t >= 15f && t < 23f);
            camL.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = t < 7f;   // post only for the Lit half
            float stage = 2;
            if (t >= 7f && t < 15f) stage = t < 9.7f ? 0 : (t < 12.3f ? 1 : 2);
            Shader.SetGlobalFloat("_InkStage", stage);
            bool debug = t >= 19.5f;
            foreach (var m in inkLeftMats) { m.SetFloat("_FeiBai", debug ? 1 : 0); m.SetFloat("_Debug", debug ? 1 : 0); }

            if (t < 7f)
            {
                float k = S(0, 7, t);
                Both(Vector3.Lerp(new Vector3(-1.6f, 2.3f, -6.2f), new Vector3(0.4f, 2.1f, -4.4f), k), new Vector3(0.2f, 1.9f, 6f));
            }
            else if (t < 15f)
            {
                float k = S(7, 15, t);
                Look(camFull, Vector3.Lerp(new Vector3(2.6f, 2.5f, -5.2f), new Vector3(-1.4f, 2.1f, -3.6f), k), new Vector3(0.6f, 1.7f, 4.6f));
            }
            else if (t < 19.5f)
            {
                float k = S(15, 19.5f, t);
                Both(Vector3.Lerp(new Vector3(2.4f, 2.7f, -0.9f), new Vector3(3.0f, 2.5f, -0.2f), k), new Vector3(5.4f, 2.3f, 4.6f));
            }
            else if (t < 23f)
            {
                float k = S(19.5f, 23, t);
                Both(Vector3.Lerp(new Vector3(0.5f, 4.4f, -9f), new Vector3(-0.5f, 4.0f, -8.2f), k), new Vector3(0f, 3.2f, 30f));
            }
            else
            {
                float k = S(23, 27.5f, t);
                Look(camFull, Vector3.Lerp(new Vector3(0.4f, 2.4f, 2.3f), new Vector3(-1.6f, 3.0f, -3.2f), k), new Vector3(0.7f, 2.2f, 6f));
            }
        }
    }
}

// Timeline for the impossible-geometry post: camera orbits, debug tint quads, switching one layered set on and off.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class PortalDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public GameObject set1;            // root of the ON copy of set 1
        public GameObject[] tints;         // one full-viewport tint quad per stencil ID
        public Transform crystalOn, crystalOff;

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }

        void Orbit(Camera c, float azDeg, float r, float h)
        {
            float a = azDeg * Mathf.Deg2Rad;
            c.transform.position = new Vector3(Mathf.Sin(a) * r, h, -Mathf.Cos(a) * r);
            c.transform.LookAt(new Vector3(0, 1.35f, 0));
        }

        void Update()
        {
            float t = FrameCapturer.T;
            bool A = t < 8f;
            camL.enabled = camR.enabled = A; camFull.enabled = !A;
            Shader.SetGlobalVector("_Room_LightPos", new Vector3(0, 2.85f, 0));
            bool tint = false, s1 = true;
            if (A)
            {
                float az = Mathf.Lerp(-24f, 16f, S(0f, 8f, t));
                Orbit(camL, az, 6.9f, 1.6f); Orbit(camR, az, 6.9f, 1.6f);
            }
            else if (t < 17f)
            {
                Orbit(camFull, Mathf.Lerp(-96f, 96f, S(8f, 17f, t)), 7.4f, 1.7f);
            }
            else
            {
                Orbit(camFull, Mathf.Lerp(-52f, -36f, S(17f, 24f, t)), 7.8f, 1.75f);
                tint = t < 19.5f;
                s1 = !(t >= 19.5f && t < 21.7f);
            }
            foreach (var q in tints) q.SetActive(tint);
            set1.SetActive(s1);
            var spin = Quaternion.Euler(0, t * 40f, 0);
            if (crystalOn != null) crystalOn.localRotation = spin;
            if (crystalOff != null) crystalOff.localRotation = spin;
        }
    }
}

// Drives the subsurface/transmission post: two identical object sets (wax pillar, jade disc, leaf sprig) on two layers,
// one camera per half of the frame, the same back light. Per phase it switches the _SUBSURFACE / _TRANSMISSION keywords
// on each set's materials, frames the cameras and sets the labels. The last shot sweeps _Thickness on the right set.
using UnityEngine;
using GameTech;

namespace GameTech.Posts
{
    public class SSSDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public Material[] matsL, matsR;        // wax, jade, leaf
        public float[] baseThickness;
        public LabelSpec title, l1, l2, m1, m2;
        public Vector3 target = new Vector3(0f, 0.095f, 0.02f);
        int phase = -1;

        static void Keys(Material[] ms, bool sss, bool trans)
        {
            foreach (var m in ms)
            {
                if (sss) m.EnableKeyword("_SUBSURFACE"); else m.DisableKeyword("_SUBSURFACE");
                if (trans) m.EnableKeyword("_TRANSMISSION"); else m.DisableKeyword("_TRANSMISSION");
            }
        }

        void Frame(Camera c, Rect r, float fov, float yaw, float dist, float elev)
        {
            c.enabled = true; c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            var q = Quaternion.Euler(elev, yaw, 0f);
            c.transform.position = target + q * new Vector3(0f, 0f, -dist);
            c.transform.LookAt(target);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            float yaw = Mathf.Lerp(-16f, 16f, S(t / 25f));
            Rect full = new Rect(0, 0, 1, 1), left = new Rect(0, 0, 0.5f, 1), right = new Rect(0.5f, 0, 0.5f, 1);
            int p = t < 3.5f ? 0 : t < 12f ? 1 : t < 18.5f ? 2 : t < 23f ? 3 : 4;
            for (int i = 0; i < matsR.Length; i++) { matsR[i].SetFloat("_Thickness", baseThickness[i]); matsL[i].SetFloat("_Thickness", baseThickness[i]); }
            switch (p)
            {
                case 0:
                    camA.enabled = false; Frame(camB, full, Mathf.Lerp(24f, 21f, S(t / 3.5f)), yaw, 1.15f, 10f);
                    Keys(matsR, true, true);
                    Labels("Back light through wax, jade and a leaf", "Subsurface scattering + transmission on", "light enters the material and comes out the other side", "", "");
                    break;
                case 1:
                    Frame(camA, left, 30f, yaw, 1.12f, 11f); Frame(camB, right, 30f, yaw, 1.12f, 11f);
                    Keys(matsL, false, false); Keys(matsR, true, true);
                    Labels("Same light, same albedo, same camera", "Both off", "lit only where N.L > 0: a dark silhouette", "Subsurface + transmission", "wrap past the edge, glow through thin parts");
                    break;
                case 2:
                    Frame(camA, left, 30f, yaw, 1.12f, 11f); Frame(camB, right, 30f, yaw, 1.12f, 11f);
                    Keys(matsL, true, false); Keys(matsR, true, true);
                    Labels("Two separate terms", "Subsurface wrap only", "light bends around the normal, tinted", "Wrap + transmission", "plus light passing along the view ray");
                    break;
                case 3:
                {
                    camA.enabled = false; Frame(camB, full, Mathf.Lerp(21f, 18f, S((t - 18.5f) / 4.5f)), yaw, 1.15f, 9f);
                    Keys(matsR, true, true);
                    float k = Mathf.Lerp(0.95f, 0.05f, S((t - 19f) / 3.5f));
                    for (int i = 0; i < matsR.Length; i++) matsR[i].SetFloat("_Thickness", k);
                    Labels("Thickness", $"Thickness {k:0.00} on all three", "transmission scales with (1 - thickness)", "", "");
                    break;
                }
                default:
                    camA.enabled = false; Frame(camB, full, 21f, yaw, 1.15f, 9f);
                    Keys(matsR, true, true);
                    Labels("Subsurface scattering + transmission", "Unity 6.3 URP, shading ported from kShading (MIT)", "thickness: wax 0.72, jade 0.55, leaf 0.40", "", "");
                    break;
            }
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }
    }
}

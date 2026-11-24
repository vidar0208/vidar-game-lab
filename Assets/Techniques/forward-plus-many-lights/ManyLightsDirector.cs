// 1,000 moving point lights in a plaza, rendered by URP's own paths: Forward (a per-object light list, at most
// 8 lights per object) and Forward+ (lights binned into screen tiles and depth slices, no per-object limit).
// My code: the plaza, light motion, cameras, labels and the visible-light count. The rendering is URP's.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public class ManyLightsDirector : MonoBehaviour
    {
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public Light[] lights;
        public Vector4[] orbit;          // centre x, centre z, radius, angular speed
        public float[] heights, phases;
        public int forwardIndex, forwardPlusIndex, tileLayer, wholeLayer, tileCount;
        int phaseShown = -1;
        readonly Plane[] planes = new Plane[6];

        void Update()
        {
            float t = FrameCapturer.T;
            for (int i = 0; i < lights.Length; i++)
            {
                var o = orbit[i]; float a = phases[i] + t * o.w;
                lights[i].transform.position = new Vector3(o.x + Mathf.Cos(a) * o.z, heights[i] + 0.15f * Mathf.Sin(a * 2.3f), o.y + Mathf.Sin(a) * o.z);
            }
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 6f ? 0 : t < 15f ? 1 : t < 21f ? 2 : 3;
            // slow push along the plaza
            var pos = new Vector3(-6f + t * 0.35f, 2.6f, -24f + t * 0.5f);
            var rot = Quaternion.Euler(14f, 18f - t * 0.6f, 0);
            if (p == 0 || p == 3)
            {
                Set(camFull, pos, rot, forwardPlusIndex, false);
                int n = Visible(camFull);
                if (p == 0) Labels("1,000 moving point lights, one floor mesh: URP Forward+",
                    $"{n} of them inside the camera view this frame (URP keeps at most 256 per camera on desktop)",
                    "Forward+ bins the lights into screen tiles and depth slices; each pixel reads only its own list", "", "");
                else Labels("URP Forward vs Forward+, Unity 6.3: same scene, only the renderer's rendering path changes",
                    $"{n} lights in view; Forward caps each object at 8 (URP's maximum), Forward+ has no per-object cap",
                    "Apple M4 Mac mini, Metal, Unity 6.3 Editor, 1080p. Frame times were not measured for this video", "", "");
            }
            else
            {
                camBack.enabled = true;
                Set(camL, pos, rot, forwardIndex, p == 2); Set(camR, pos, rot, forwardPlusIndex, false);
                int n = Visible(camL);
                if (p == 1) Labels($"Same lights, same camera, {n} lights in view",
                    "Forward: each object gets its 8 most important lights", "the whole floor is one object, so it shows 8 pools of light",
                    "Forward+: lights are picked per screen tile", "every light in view lands on the floor");
                else Labels("The old fix: cut big objects into small ones",
                    $"Forward, floor cut into {tileCount:N0} tiles of 2 m", $"each tile gets its own 8 lights: {tileCount:N0} floor objects instead of 1",
                    "Forward+, still one floor mesh", "same result without touching the content");
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
            if (Time.frameCount % 60 == 0) Debug.Log($"[gt] t {t:F2} visible full {Visible(camFull)} split {Visible(camL)}");
        }

        void Set(Camera c, Vector3 pos, Quaternion rot, int renderer, bool tiles)
        {
            c.enabled = true; c.transform.SetPositionAndRotation(pos, rot);
            c.GetUniversalAdditionalCameraData().SetRenderer(renderer);
            int mask = ~((1 << tileLayer) | (1 << wholeLayer));
            c.cullingMask = mask | (1 << (tiles ? tileLayer : wholeLayer));
        }

        // lights whose range sphere touches the view frustum (what URP's culling hands to the renderer)
        int Visible(Camera c)
        {
            GeometryUtility.CalculateFrustumPlanes(c, planes);
            int n = 0;
            foreach (var l in lights) if (GeometryUtility.TestPlanesAABB(planes, new Bounds(l.transform.position, Vector3.one * l.range * 2f))) n++;
            return n;
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }
    }
}

// Camera paths, rig settings and labels for the recursive-portal comparison.
using UnityEngine;

namespace GameTech.Posts
{
    public class PortalDirector : MonoBehaviour
    {
        public Camera camFull, camL, camR, camBack;
        public PortalRig rigFull, rigL, rigR;
        public LabelSpec title, l1, l2, m1, m2;
        int phaseShown = -1;
        float msAvg;

        // 0-7 s hero, 7-14 s split oblique clip off/on, 14-21 s split recursion 1/8, 21-25 s side view + cost
        public static int Phase(float t) => t < 7f ? 0 : t < 14f ? 1 : t < 21f ? 2 : 3;

        void Update()
        {
            float t = FrameCapturer.T;
            int p = Phase(t);
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            rigFull.enabled = p == 0 || p == 3; rigL.enabled = rigR.enabled = p == 1 || p == 2;

            if (p == 0)
            {
                Set(camFull, new Vector3(1.2f * Mathf.Sin(t * 0.25f), 1.8f, -2f + 0.45f * t), Quaternion.Euler(2f, -4f * Mathf.Sin(t * 0.25f), 0));
                rigFull.recursionLimit = 8; rigFull.oblique = true;
                Labels("Recursive portals: the portal camera renders inside its own view",
                    "through the orange frame you see the hall from the teal frame, and the orange frame again",
                    "8 levels, drawn deepest first. URP port of SebLague/Portals", "", "");
            }
            else if (p == 1)
            {
                camBack.enabled = true;
                float u = t - 7f;
                var pos = new Vector3(0.4f, 1.8f, 2f + 0.3f * u); var rot = Quaternion.Euler(2f, -2f, 0);
                Set(camL, pos, rot); Set(camR, pos, rot);
                rigL.oblique = false; rigR.oblique = true; rigL.recursionLimit = rigR.recursionLimit = 8;
                Labels("Oblique near plane: clip everything behind the exit portal",
                    "OFF: normal projection", "the portal camera stands behind the far wall and sees its back",
                    "ON: near plane moved onto the portal surface", "only what lies beyond the portal is drawn");
            }
            else if (p == 2)
            {
                camBack.enabled = true;
                float u = t - 14f;
                var pos = new Vector3(0.8f, 1.8f, -1f + 0.3f * u); var rot = Quaternion.Euler(2f, -3f, 0);
                Set(camL, pos, rot); Set(camR, pos, rot);
                rigL.oblique = rigR.oblique = true; rigL.recursionLimit = 1; rigR.recursionLimit = 8;
                Labels("Recursion limit: how many times the portal camera renders",
                    "limit 1", $"the inner frame gets a flat colour. {rigL.rendersLastFrame} portal render per frame",
                    "limit 8 (the source default is 5)", $"{rigR.rendersLastFrame} portal renders per frame");
            }
            else
            {
                float u = t - 21f;
                var pos = new Vector3(1.6f - 0.2f * u, 2.3f, -4f - 0.4f * u);
                var look = Quaternion.LookRotation(new Vector3(0, 2f, 9f) - pos);
                Set(camFull, pos, look);
                rigFull.recursionLimit = 8; rigFull.oblique = true;
                msAvg = msAvg == 0 ? rigFull.msLastFrame : Mathf.Lerp(msAvg, rigFull.msLastFrame, 0.1f);
                Labels("Every level is one more full camera render",
                    $"this frame: {rigFull.rendersLastFrame} portal renders, {msAvg:F1} ms CPU to submit them",
                    "Apple M4 Mac mini, Metal, Unity 6.3 URP Editor, 1920x1080 portal textures", "", "");
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
            if (Time.frameCount % 60 == 0) Debug.Log($"[gt] t {t:F2} renders full {rigFull.rendersLastFrame} L {rigL.rendersLastFrame} R {rigR.rendersLastFrame} ms {rigFull.msLastFrame:F2}/{rigL.msLastFrame:F2}/{rigR.msLastFrame:F2}");
        }

        static void Set(Camera c, Vector3 pos, Quaternion rot) { c.enabled = true; c.transform.SetPositionAndRotation(pos, rot); }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }
    }
}

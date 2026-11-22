// Drives the pixel sort post. The scene camera renders into a texture; the unmodified compute shader from
// ruccho/BitonicPixelSorter (Compute/BitonicPixelSorter.compute) sorts it into one or two output textures, dispatched
// the same way as the source's BitonicPixelSorter.Execute (one thread group per row or column, BPS_SIZE_4096 above
// 2048 px). A full-screen quad then shows unsorted / sorted halves. Runs before FrameCapturer (execution order -100).
using UnityEngine;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PixelSortDirector : MonoBehaviour
    {
        public Camera sceneCam; public Material display; public ComputeShader sorter;
        public Transform character;
        public LabelSpec title, l1, l2, m1, m2;
        public float tMin = 0.06f, tMax = 0.6f;
        RenderTexture orig, sortA, sortB; int kernel = -1, phase = -1;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }

        RenderTexture RT(int w, int h, int depth, bool rw)
        {
            var r = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { enableRandomWrite = rw, antiAliasing = 1 };
            r.Create(); return r;
        }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            orig = RT(w, h, 24, false); sortA = RT(w, h, 0, true); sortB = RT(w, h, 0, true);
            sceneCam.targetTexture = orig; sceneCam.enabled = false;
            kernel = sorter.FindKernel("SortPass");
            display.SetTexture("_Orig", orig); display.SetTexture("_SortA", sortA); display.SetTexture("_SortB", sortB);
        }

        // same calls as the source's Execute(): direction true = sort along rows, false = along columns
        void Sort(RenderTexture dst, bool horizontal, bool ascending, float min, float max)
        {
            int size = horizontal ? orig.width : orig.height, lines = horizontal ? orig.height : orig.width;
            if (size > 2048) sorter.EnableKeyword("BPS_SIZE_4096"); else sorter.DisableKeyword("BPS_SIZE_4096");
            sorter.SetBool("direction", horizontal);
            sorter.SetFloat("thresholdMin", min); sorter.SetFloat("thresholdMax", max);
            sorter.SetTexture(kernel, "srcTex", orig); sorter.SetTexture(kernel, "sortTex", dst);
            sorter.SetBool("ordering", ascending);
            sorter.Dispatch(kernel, lines, 1, 1);
        }


        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            // slow dolly down the colonnade toward the low sun
            sceneCam.transform.position = new Vector3(Mathf.Lerp(-0.35f, 0.25f, S(t / 25f)), 1.05f + 0.1f * Mathf.Sin(t * 0.25f), Mathf.Lerp(-7.2f, -5.4f, S(t / 25f)));
            sceneCam.transform.rotation = Quaternion.Euler(-3.5f, Mathf.Lerp(3f, -3f, S(t / 25f)), 0f);
            if (character) character.rotation = Quaternion.Euler(0f, 180f + 25f * Mathf.Sin(t * 0.35f), 0f);
        }

        void LateUpdate()
        {
            if (kernel < 0) return;
            float t = FrameCapturer.T;
            sceneCam.Render();
            int p = t < 3f ? 0 : t < 11f ? 1 : t < 17f ? 2 : t < 22f ? 3 : 4;
            float split = 1f, left = 0, right = 0, line = 0;
            switch (p)
            {
                case 0:
                    Labels("Late sun across a colonnade", "Pixel sort off", "the image as rendered", "", "");
                    break;
                case 1:
                    Sort(sortA, true, true, tMin, tMax);
                    split = Mathf.Lerp(1f, 0.5f, S((t - 3f) / 0.8f)); right = 1; line = 1;
                    Labels("Same frame, same camera", "Off", "", "Rows sorted by brightness", $"only pixels with brightness {tMin:0.00}-{tMax:0.00} move");
                    break;
                case 2:
                {
                    float mx = Mathf.Lerp(0.1f, 1.0f, S((t - 11.5f) / 5f));
                    Sort(sortA, true, true, tMin, mx); left = 1; right = 1;
                    Labels("Threshold range", $"Brightness {tMin:0.00} to {mx:0.00} is sorted", "darker and brighter pixels stay where they are", "", "");
                    break;
                }
                case 3:
                    Sort(sortA, true, true, tMin, tMax); Sort(sortB, false, true, tMin, tMax);
                    left = 1; right = 2; line = 1;
                    Labels("Sort direction", "Along rows", "horizontal streaks", "Along columns", "vertical streaks, same range");
                    break;
                default:
                    Sort(sortA, true, true, tMin, tMax); left = 1; right = 1;
                    Labels("Bitonic pixel sort", "Unity 6.3 URP, compute shader from ruccho/BitonicPixelSorter (MIT)", "one thread group per row, sorted in shared memory", "", "");
                    break;
            }
            display.SetFloat("_Split", split); display.SetFloat("_LeftSel", left); display.SetFloat("_RightSel", right); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { foreach (var r in new[] { orig, sortA, sortB }) if (r) r.Release(); }
    }
}

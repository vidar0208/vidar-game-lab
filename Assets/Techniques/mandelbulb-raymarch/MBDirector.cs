// Drives the Mandelbulb post. Each frame the compute shader ray-marches the fractal into two images (2x the output
// size) from two cameras; the composite shows image A left of the split and image B right of it. Only the visible
// columns of each image are marched. At Start it times one 1920x1080 frame (GPU, waited) and finds, on the CPU with
// the same distance estimate, where the zoom ray hits the 3-iteration and 15-iteration surfaces.
// Runs before FrameCapturer (execution order -100).
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class MBDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display; public ComputeShader mb;
        public int ss = 2, iterLow = 3, iterHigh = 15;
        public float power = 8f, fov = 40f, zoomMax = 100f, darkness = 75f;
        public Vector3 aim = new Vector3(-0.61f, 0.12f, -0.66f);
        public Vector3 lightTo = new Vector3(0.1f, 0.75f, -0.65f);
        public LabelSpec title, l1, l2, m1, m2;

        RenderTexture rtA, rtB; int W, H, phase = -1; float msFrame, yaw = 43f;
        Vector3 P0, dir0, hitLo, hitHi; float dLo, dHi;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h)
        {
            var r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { enableRandomWrite = true, filterMode = FilterMode.Bilinear };
            r.Create(); return r;
        }

        // the same distance estimate as the compute shader, in double precision
        static double DE(double px, double py, double pz, int iter, double pw)
        {
            double qx = px, qy = pz, qz = py; // pole axis to +Y, as in the shader (p.xzy)
            double zx = qx, zy = qy, zz = qz, dr = 1, r = 0;
            for (int i = 0; i < iter; i++)
            {
                r = System.Math.Sqrt(zx * zx + zy * zy + zz * zz); if (r > 2) break;
                double th = System.Math.Acos(zz / r), ph = System.Math.Atan2(zy, zx);
                dr = System.Math.Pow(r, pw - 1) * pw * dr + 1; double zr = System.Math.Pow(r, pw); th *= pw; ph *= pw;
                zx = zr * System.Math.Sin(th) * System.Math.Cos(ph) + qx; zy = zr * System.Math.Sin(ph) * System.Math.Sin(th) + qy; zz = zr * System.Math.Cos(th) + qz;
            }
            return 0.5 * System.Math.Log(r) * r / dr;
        }

        static Vector3 Hit(Vector3 o, Vector3 d, int iter, double pw, out float dist)
        {
            double t = 0;
            for (int i = 0; i < 5000; i++)
            {
                double h = DE(o.x + d.x * t, o.y + d.y * t, o.z + d.z * t, iter, pw);
                if (h < 1e-7) break; t += h * 0.9; if (t > 20) break;
            }
            dist = (float)t; return o + d * (float)t;
        }

        void Draw(RenderTexture rt, Camera cam, int mode, int iter, float pw, int x0, int x1)
        {
            if (x1 <= x0) return;
            mb.SetTexture(0, "Dest", rt);
            mb.SetMatrix("_CamToWorld", cam.cameraToWorldMatrix);
            mb.SetMatrix("_CamInvProj", cam.projectionMatrix.inverse);
            mb.SetVector("_LightDir", -(Quaternion.Euler(0, yaw - 43f, 0) * lightTo).normalized); // the sun turns with the orbit
            mb.SetFloat("_Power", pw); mb.SetInt("_Iter", iter); mb.SetInt("_Mode", mode);
            mb.SetFloat("_PixAng", 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / rt.height);
            mb.SetFloat("_Darkness", darkness);
            mb.SetVector("_ColA", new Vector3(0, 0, 1)); mb.SetVector("_ColB", new Vector3(1, 0, 0)); // source scene colours
            mb.SetInt("_X0", x0); mb.SetInt("_W", rt.width); mb.SetInt("_H", rt.height);
            mb.Dispatch(0, (x1 - x0 + 7) / 8, (rt.height + 7) / 8, 1);
        }

        Vector3 Orbit(float yaw, float pitch, float r) => Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -r);

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            W = w * ss; H = h * ss; rtA = RT(W, H); rtB = RT(W, H);
            foreach (var c in new[] { camA, camB }) { c.enabled = false; c.aspect = 16f / 9f; c.fieldOfView = fov; }
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);

            // zoom ray: from the hero camera position at t = 3 s toward the aim point
            P0 = Orbit(25f + 6f * 3f, 16f, 3.5f); dir0 = (aim - P0).normalized;
            hitLo = Hit(P0, dir0, iterLow, power, out dLo); hitHi = Hit(P0, dir0, iterHigh, power, out dHi);

            // time one 1920x1080 frame (no supersampling), hero view
            var tr = RT(1920, 1080);
            camB.transform.SetPositionAndRotation(P0, Quaternion.LookRotation(dir0));
            Draw(tr, camB, 1, iterHigh, power, 0, 1920); AsyncGPUReadback.Request(tr, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Draw(tr, camB, 1, iterHigh, power, 0, 1920);
            AsyncGPUReadback.Request(tr, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            msFrame = (float)sw.Elapsed.TotalMilliseconds / 10f; tr.Release();
            Debug.Log($"[gt] mandelbulb: zoom ray hits 3 it at {dLo:F4}, 15 it at {dHi:F4}; 1920x1080 power {power} {iterHigh} it: {msFrame:F1} ms per frame (GPU, waited, avg of 10)");
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 13f ? 1 : t < 19f ? 2 : t < 22.5f ? 3 : 4;
            float split = 0.5f, line = 1; int modeA = 1, modeB = 1, itA = iterHigh, itB = iterHigh; float pw = power;
            Vector3 up = Vector3.up;

            switch (p)
            {
                case 0:
                {
                    yaw = 25f + 6f * t; var cp = Orbit(yaw, 16f, 3.5f);
                    var look = Vector3.Lerp(Vector3.zero, aim, S(t / 3f));
                    foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look - cp));
                    split = 0; line = 0;
                    Labels("Mandelbulb: ray marched, no triangles", "Each pixel steps along its ray by a distance estimate", $"power {power:0}, {iterHigh} iterations, sun with soft shadows and AO", "", "");
                    break;
                }
                case 1:
                {
                    float z = Mathf.Exp(Mathf.Log(zoomMax) * S((t - 3.6f) / 9.0f));
                    yaw = 43f; var rot = Quaternion.LookRotation(dir0) * Quaternion.Euler(0, 0, 22f * (t - 3f));
                    camA.transform.SetPositionAndRotation(hitLo - dir0 * (dLo / z), rot);
                    camB.transform.SetPositionAndRotation(hitHi - dir0 * (dHi / z), rot);
                    itA = iterLow; split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same view, moving toward the surface", $"{iterLow} iterations", $"{z:F0}x closer", $"{iterHigh} iterations (source loop limit)", $"{z:F0}x closer");
                    break;
                }
                case 2:
                {
                    pw = Mathf.Lerp(2f, power, S((t - 13.3f) / 5.4f));
                    yaw = 140f + 8f * (t - 13f); var cp = Orbit(yaw, 20f, Mathf.Lerp(3.9f, 3.4f, S((t - 13.3f) / 5.4f)));
                    foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(-cp));
                    split = 0; line = 0;
                    Labels("Changing the power in z -> z^power + c", $"power {pw:F1}", $"{iterHigh} iterations, same lighting", "", "");
                    break;
                }
                default:
                {
                    yaw = 215f + 7f * (t - 19f); var cp = Orbit(yaw, 14f, 3.4f);
                    foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(-cp));
                    if (p == 3)
                    {
                        modeA = 0;
                        Labels("Same fractal, two ways to shade it", "Source shading", "normal + escape count colours, step-count rim", "Added lighting", "sun with soft shadows, distance AO, rim light");
                    }
                    else
                    {
                        split = 0; line = 0;
                        Labels("Ray-marched Mandelbulb", "Unity 6.3 URP, after SebLague/Ray-Marching (MIT)", $"1920x1080, power {power:0}, {iterHigh} iterations: {msFrame:F0} ms per frame on an Apple M4 Mac mini (Metal)", "", "");
                    }
                    break;
                }
            }
            int xs = Mathf.Clamp(Mathf.RoundToInt(split * W), 0, W);
            Draw(rtA, camA, modeA, itA, pw, 0, Mathf.Min(W, xs + 2));
            Draw(rtB, camB, modeB, itB, pw, Mathf.Max(0, xs - 2), W);
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { foreach (var r in new[] { rtA, rtB }) if (r) r.Release(); }
    }
}

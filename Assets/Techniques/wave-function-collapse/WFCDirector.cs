// Runs the solver at start, records one run step by step and plays it back for the video.
// 100 seeds are solved first to count contradictions and time a run. The town shown is the first seed (from 1)
// whose result has 15-30 % water, at least 3 bridge cells and at least 60 houses (picked for the picture, said in notes).
// "Random tiles" uses the same tiles and weights, revealed in the same cell order as the WFC run.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class WFCDirector : MonoBehaviour
    {
        public WFCTown townW, townR, townH;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public int MX = 24, MY = 24;

        List<WFCTiles.Tile> tiles; WFCModel model;
        int[] gridW, gridR; int[] decidedAt; int S; int fails, runs = 100, seed, edgesTotal; double msPerRun;
        bool[] shown; float[] glow; int phase = -1;

        void Start()
        {
            tiles = WFCTiles.Build();
            var prop = WFCTiles.Propagator(tiles);
            var w = new double[tiles.Count]; for (int t = 0; t < w.Length; t++) w[t] = tiles[t].w;
            model = new WFCModel(MX, MY, w, prop);

            model.Run(999, -1, false);   // warm-up (JIT)
            var sw = new Stopwatch(); double okMs = 0; int ok = 0;
            for (int s = 1; s <= runs; s++)
            {
                sw.Restart(); bool good = model.Run(s, -1, false); sw.Stop();
                if (good) { ok++; okMs += sw.Elapsed.TotalMilliseconds; } else fails++;
            }
            msPerRun = okMs / Mathf.Max(1, ok);

            for (seed = 1; seed < 500; seed++)
            {
                if (!model.Run(seed, -1, false)) continue;
                int water = 0, bridge = 0, house = 0;
                foreach (int t in model.observed)
                {
                    foreach (char ch in tiles[t].p) { if (ch == 'W' || ch == 'B') water++; if (ch == 'H') house++; }
                    if (tiles[t].name == "bridge" || tiles[t].name == "landing") bridge++;
                }
                float wf = water / (9f * MX * MY);
                if (wf >= 0.15f && wf <= 0.3f && bridge >= 3 && house >= 60) { Debug.Log($"[gt] seed {seed}: water {wf:P0}, bridge cells {bridge}, houses {house}"); break; }
            }
            model.Run(seed, -1, true);
            gridW = (int[])model.observed.Clone();
            S = model.history.Count - 1;
            int N = MX * MY;
            decidedAt = new int[N];
            for (int i = 0; i < N; i++) { decidedAt[i] = S; for (int k = 0; k <= S; k++) if (model.history[k][i] <= 1) { decidedAt[i] = k; break; } }

            var rnd = new System.Random(seed + 1000);
            gridR = new int[N];
            for (int i = 0; i < N; i++) gridR[i] = WFCModel.WeightedPick(w, rnd.NextDouble());

            var all = new bool[N]; for (int i = 0; i < N; i++) all[i] = true;
            int breaksR = WFCTiles.Breaks(tiles, gridR, all, MX, MY, out edgesTotal);
            int breaksW = WFCTiles.Breaks(tiles, gridW, all, MX, MY, out _);
            Debug.Log($"[gt] tiles {WFCTiles.Base.Length} types, {tiles.Count} variants; {fails} contradictions in {runs} seeds; {msPerRun:F2} ms per solved run; seed {seed}, {S} observations; breaks random {breaksR}/{edgesTotal}, wfc {breaksW}");

            townW.BuildTiles(tiles, gridW, MX, MY, seed);
            townR.BuildTiles(tiles, gridR, MX, MY, seed);
            townH.BuildBars(MX, MY);
            shown = new bool[N]; glow = new float[N];
            Update();
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        void Orbit(Camera c, Vector3 center, float yaw, float pitch, float dist, float fov)
        {
            c.fieldOfView = fov;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            c.transform.SetPositionAndRotation(center - rot * Vector3.forward * dist, rot);
        }

        // u 0..1 along the run: step k; cells decided at or before k are shown
        int Reveal(float u, WFCTown town)
        {
            int k = Mathf.Clamp(Mathf.FloorToInt(u * S + 0.0001f), 0, S);
            for (int i = 0; i < shown.Length; i++)
            {
                shown[i] = decidedAt[i] <= k;
                glow[i] = shown[i] ? Mathf.Clamp01(1f - (k - decidedAt[i]) / 14f) : 0;
            }
            if (k >= S) for (int i = 0; i < glow.Length; i++) glow[i] = 0;
            town.ShowTiles(shown, glow);
            return k;
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            if (model == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 12f ? 1 : t < 19.5f ? 2 : 3;
            bool split = p == 1 || p == 2;
            camFull.enabled = !split; camL.enabled = split; camR.enabled = split; camBack.enabled = split;
            float yaw = 28f + t * 3.2f;
            int N = MX * MY;

            if (p == 0 || p == 3)
            {
                for (int i = 0; i < N; i++) { shown[i] = true; glow[i] = 0; }
                townW.ShowTiles(shown, null);
                Orbit(camFull, townW.transform.position, yaw, 50f, 44f, 30f);
                if (p == 0)
                    Labels("Wave function collapse: a town from tile rules", $"{MX} x {MY} cells, {WFCTiles.Base.Length} tiles, {tiles.Count} with rotations", "every edge matches its neighbour", "", "");
                else
                    Labels("Wave function collapse", "Unity 6.3 URP, C# port of mxgmn/WaveFunctionCollapse (MIT)",
                        $"{fails} contradictions in {runs} seeds, {msPerRun:F1} ms per town on an Apple M4 (CPU)", "", "");
            }
            else if (p == 1)
            {
                float u = Smooth((t - 3.4f) / 7.6f);
                int k = Reveal(u, townW);
                var shownCopy = (bool[])shown.Clone();
                townR.ShowTiles(shownCopy, glow);
                int bR = WFCTiles.Breaks(tiles, gridR, shownCopy, MX, MY, out _);
                int bW = WFCTiles.Breaks(tiles, gridW, shownCopy, MX, MY, out _);
                Orbit(camL, townR.transform.position, yaw, 50f, 57f, 31f);
                Orbit(camR, townW.transform.position, yaw, 50f, 57f, 31f);
                Labels("Same tiles, same order, different rule",
                    "Random tiles (same weights)", $"{bR:N0} of {edgesTotal:N0} edges don't match",
                    "Wave function collapse", $"{bW:N0} of {edgesTotal:N0} edges don't match");
            }
            else
            {
                float u = Smooth((t - 12.3f) / 6.6f);
                int k = Reveal(u, townW);
                townH.ShowBars(model.history[k], tiles.Count, k < S ? model.picks[Mathf.Min(k + 1, S)] : -1);
                int dec = 0; for (int i = 0; i < N; i++) if (shown[i]) dec++;
                Orbit(camL, townH.transform.position, yaw, 46f, 55f, 33f);
                Orbit(camR, townW.transform.position, yaw, 46f, 55f, 33f);
                Labels("Lowest entropy first, then propagate",
                    $"Options left per cell (bar height, {tiles.Count} at start)", $"white bar: the next cell to decide, step {k:N0} of {S:N0}",
                    "Decided cells", $"{dec:N0} of {N:N0} cells decided");
            }
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l2.text} | {m2.text}"); }
        }
    }
}

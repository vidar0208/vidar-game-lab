// Wave function collapse solver, ported from Model.cs of mxgmn/WaveFunctionCollapse
// (Copyright (C) 2016 Maxim Gumin, The MIT License (MIT)). Same data layout and steps as the source:
// the wave (which tiles are still possible in each cell), the compatible counters, the entropy heuristic
// with a tiny random noise, Observe (weighted pick), Ban and Propagate. Non-periodic grid, N = 1 (tiled model).
// Added for the video: after every observation the number of options left in every cell is recorded.
using System;
using System.Collections.Generic;

namespace GameTech.Posts
{
    public class WFCModel
    {
        bool[][] wave;
        readonly int[][][] propagator;
        int[][][] compatible;
        public int[] observed;

        (int, int)[] stack;
        int stacksize;

        public readonly int MX, MY, T;
        readonly double[] weights;
        double[] weightLogWeights, distribution;

        public int[] sumsOfOnes;
        double sumOfWeights, sumOfWeightLogWeights, startingEntropy;
        double[] sumsOfWeights, sumsOfWeightLogWeights, entropies;

        public List<byte[]> history;   // options left per cell: [0] after Clear, then one entry per observation
        public List<int> picks;        // observed cell per history entry (-1 for the first)

        public WFCModel(int width, int height, double[] weights, int[][][] propagator)
        {
            MX = width; MY = height; this.weights = weights; T = weights.Length; this.propagator = propagator;
            Init();
        }

        void Init()
        {
            wave = new bool[MX * MY][];
            compatible = new int[wave.Length][][];
            for (int i = 0; i < wave.Length; i++)
            {
                wave[i] = new bool[T];
                compatible[i] = new int[T][];
                for (int t = 0; t < T; t++) compatible[i][t] = new int[4];
            }
            distribution = new double[T];
            observed = new int[MX * MY];

            weightLogWeights = new double[T];
            sumOfWeights = 0;
            sumOfWeightLogWeights = 0;
            for (int t = 0; t < T; t++)
            {
                weightLogWeights[t] = weights[t] * Math.Log(weights[t]);
                sumOfWeights += weights[t];
                sumOfWeightLogWeights += weightLogWeights[t];
            }
            startingEntropy = Math.Log(sumOfWeights) - sumOfWeightLogWeights / sumOfWeights;

            sumsOfOnes = new int[MX * MY];
            sumsOfWeights = new double[MX * MY];
            sumsOfWeightLogWeights = new double[MX * MY];
            entropies = new double[MX * MY];

            stack = new (int, int)[wave.Length * T];
            stacksize = 0;
        }

        public bool Run(int seed, int limit, bool record)
        {
            Clear();
            var random = new Random(seed);
            if (record) { history = new List<byte[]>(); picks = new List<int>(); Snap(-1); }

            for (int l = 0; l < limit || limit < 0; l++)
            {
                int node = NextUnobservedNode(random);
                if (node >= 0)
                {
                    Observe(node, random);
                    bool success = Propagate();
                    if (record) Snap(node);
                    if (!success) return false;
                }
                else
                {
                    for (int i = 0; i < wave.Length; i++) for (int t = 0; t < T; t++) if (wave[i][t]) { observed[i] = t; break; }
                    return true;
                }
            }
            return true;
        }

        void Snap(int node)
        {
            var s = new byte[wave.Length];
            for (int i = 0; i < s.Length; i++) s[i] = (byte)Math.Max(0, sumsOfOnes[i]);
            history.Add(s); picks.Add(node);
        }

        int NextUnobservedNode(Random random)
        {
            double min = 1E+4;
            int argmin = -1;
            for (int i = 0; i < wave.Length; i++)
            {
                int remainingValues = sumsOfOnes[i];
                double entropy = entropies[i];
                if (remainingValues > 1 && entropy <= min)
                {
                    double noise = 1E-6 * random.NextDouble();
                    if (entropy + noise < min)
                    {
                        min = entropy + noise;
                        argmin = i;
                    }
                }
            }
            return argmin;
        }

        void Observe(int node, Random random)
        {
            bool[] w = wave[node];
            for (int t = 0; t < T; t++) distribution[t] = w[t] ? weights[t] : 0.0;
            int r = WeightedPick(distribution, random.NextDouble());
            for (int t = 0; t < T; t++) if (w[t] != (t == r)) Ban(node, t);
        }

        // Helper.Random from the source
        public static int WeightedPick(double[] weights, double r)
        {
            double sum = 0;
            for (int i = 0; i < weights.Length; i++) sum += weights[i];
            double threshold = r * sum;
            double partialSum = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                partialSum += weights[i];
                if (partialSum >= threshold) return i;
            }
            return 0;
        }

        bool Propagate()
        {
            while (stacksize > 0)
            {
                (int i1, int t1) = stack[stacksize - 1];
                stacksize--;

                int x1 = i1 % MX;
                int y1 = i1 / MX;

                for (int d = 0; d < 4; d++)
                {
                    int x2 = x1 + dx[d];
                    int y2 = y1 + dy[d];
                    if (x2 < 0 || y2 < 0 || x2 + 1 > MX || y2 + 1 > MY) continue;

                    int i2 = x2 + y2 * MX;
                    int[] p = propagator[d][t1];
                    int[][] compat = compatible[i2];

                    for (int l = 0; l < p.Length; l++)
                    {
                        int t2 = p[l];
                        int[] comp = compat[t2];
                        comp[d]--;
                        if (comp[d] == 0) Ban(i2, t2);
                    }
                }
            }
            // As in the source: an empty cell empties its neighbours in turn, so a contradiction reaches cell 0.
            return sumsOfOnes[0] > 0;
        }

        void Ban(int i, int t)
        {
            wave[i][t] = false;

            int[] comp = compatible[i][t];
            for (int d = 0; d < 4; d++) comp[d] = 0;
            stack[stacksize] = (i, t);
            stacksize++;

            sumsOfOnes[i] -= 1;
            sumsOfWeights[i] -= weights[t];
            sumsOfWeightLogWeights[i] -= weightLogWeights[t];

            double sum = sumsOfWeights[i];
            entropies[i] = Math.Log(sum) - sumsOfWeightLogWeights[i] / sum;
        }

        void Clear()
        {
            for (int i = 0; i < wave.Length; i++)
            {
                for (int t = 0; t < T; t++)
                {
                    wave[i][t] = true;
                    for (int d = 0; d < 4; d++) compatible[i][t][d] = propagator[opposite[d]][t].Length;
                }
                sumsOfOnes[i] = weights.Length;
                sumsOfWeights[i] = sumOfWeights;
                sumsOfWeightLogWeights[i] = sumOfWeightLogWeights;
                entropies[i] = startingEntropy;
                observed[i] = -1;
            }
            stacksize = 0;

            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
            {
                int i = x + y * MX;
                for (int t = 0; t < T; t++)
                {
                    bool noNeighborsRight = x < MX - 1 && propagator[2][t].Length == 0;
                    bool noNeighborsTop = y > 0 && propagator[3][t].Length == 0;
                    bool noNeighborsLeft = x > 0 && propagator[0][t].Length == 0;
                    bool noNeighborsBottom = y < MY - 1 && propagator[1][t].Length == 0;
                    if (noNeighborsRight || noNeighborsTop || noNeighborsLeft || noNeighborsBottom) Ban(i, t);
                }
            }
            if (stacksize > 0) Propagate();
        }

        public static readonly int[] dx = { -1, 0, 1, 0 };
        public static readonly int[] dy = { 0, 1, 0, -1 };
        static readonly int[] opposite = { 2, 3, 0, 1 };
    }
}

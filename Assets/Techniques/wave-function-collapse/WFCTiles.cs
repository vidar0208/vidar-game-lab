// Procedural tile set for the WFC town. Each tile is a 3 x 3 pattern of ground types, read row by row:
// G grass, R road, W water, B bridge deck over water, H house on grass.
// The source reads tiles and a hand-written neighbour list from XML. Here the neighbour rule is computed:
// two tiles may touch when their touching edges (3 cells each) are identical. Rotated copies are made with the
// source's rotate() formula; a rotation that gives an already listed pattern is dropped. Every copy keeps the
// weight of its tile, as in SimpleTiledModel.
using System.Collections.Generic;

namespace GameTech.Posts
{
    public static class WFCTiles
    {
        public struct Tile { public string name, p; public double w; }

        public static readonly (string name, string p, double w)[] Base =
        {
            ("grass",    "GGGGGGGGG", 4.0),
            ("house",    "GGGGHGGGG", 1.2),
            ("driveway", "GGGGHGGRG", 1.2),
            ("road",     "GRGGRGGRG", 1.5),
            ("corner",   "GGGGRRGRG", 1.0),
            ("T-junction","GGGRRRGRG", 0.6),
            ("crossing", "GRGRRRGRG", 0.3),
            ("water",    "WWWWWWWWW", 1.5),
            ("shore",    "GGGGGGWWW", 1.0),
            ("shore corner", "GGGGGGGGW", 0.6),
            ("bay",      "GGWGGWWWW", 0.6),
            ("bridge",   "WBWWBWWBW", 0.15),
            ("landing",  "GRGGRGWBW", 0.15),
        };

        public static string Rotate(string a)
        {
            var r = new char[9];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) r[x + y * 3] = a[2 - y + x * 3];
            return new string(r);
        }

        public static List<Tile> Build()
        {
            var list = new List<Tile>();
            foreach (var b in Base)
            {
                var seen = new List<string>();
                string p = b.p;
                for (int r = 0; r < 4; r++)
                {
                    if (!seen.Contains(p)) { seen.Add(p); list.Add(new Tile { name = b.name, p = p, w = b.w }); }
                    p = Rotate(p);
                }
            }
            return list;
        }

        static string Col(string p, int x) => new string(new[] { p[x], p[x + 3], p[x + 6] });
        static string Row(string p, int y) => p.Substring(y * 3, 3);

        // d follows the source: 0 = left (x-1), 1 = down (y+1), 2 = right (x+1), 3 = up (y-1)
        public static bool Fits(string a, string b, int d)
        {
            switch (d)
            {
                case 0: return Col(a, 0) == Col(b, 2);
                case 1: return Row(a, 2) == Row(b, 0);
                case 2: return Col(a, 2) == Col(b, 0);
                default: return Row(a, 0) == Row(b, 2);
            }
        }

        // propagator[d][t1] = tiles allowed in the neighbour cell in direction d (same layout as the source)
        public static int[][][] Propagator(List<Tile> tiles)
        {
            int T = tiles.Count;
            var prop = new int[4][][];
            for (int d = 0; d < 4; d++)
            {
                prop[d] = new int[T][];
                for (int t1 = 0; t1 < T; t1++)
                {
                    var l = new List<int>();
                    for (int t2 = 0; t2 < T; t2++) if (Fits(tiles[t1].p, tiles[t2].p, d)) l.Add(t2);
                    prop[d][t1] = l.ToArray();
                }
            }
            return prop;
        }

        // Internal edges whose two sides do not match. shown[i] == false cells are skipped.
        public static int Breaks(List<Tile> tiles, int[] grid, bool[] shown, int MX, int MY, out int edges)
        {
            int bad = 0; edges = 0;
            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
            {
                int i = x + y * MX;
                if (x + 1 < MX) { edges++; int j = i + 1; if (shown[i] && shown[j] && !Fits(tiles[grid[i]].p, tiles[grid[j]].p, 2)) bad++; }
                if (y + 1 < MY) { edges++; int j = i + MX; if (shown[i] && shown[j] && !Fits(tiles[grid[i]].p, tiles[grid[j]].p, 1)) bad++; }
            }
            return bad;
        }
    }
}

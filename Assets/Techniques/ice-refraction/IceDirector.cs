// Timeline for the ice refraction post. Builds the ice height map from code (cracks + frost noise), writes the
// source's material properties every frame (two materials: left half of a split screen, right half / full frame).
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class IceDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Material matL, matR;      // L = left half (layer 8), R = right half and full frame (layer 9)
        Texture2D height;

        // values of the source's Ice material
        public const float Refract = -0.02f, Fresnel = 0.2f;
        public static readonly Color IceColor = new Color(0.115f, 1.315f, 2.0f, 1f);

        void Start()
        {
            height = MakeIceHeight();
            matL.SetTexture("_IceNormals", height); matR.SetTexture("_IceNormals", height);
            Debug.Log($"[gt] ice: height map {height.width}x{height.height}");
        }

        // ---- tileable height map: two layers of crack lines (cell borders) on top of value noise
        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
        static int Mod(int a, int n) { return ((a % n) + n) % n; }
        static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }

        // distance to the nearest cell border, in cell units (second nearest feature point minus nearest)
        static float Border(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float f1 = 9f, f2 = 9f;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                float px = gx + Hash(Mod(gx, cells), Mod(gy, cells), seed), py = gy + Hash(Mod(gx, cells), Mod(gy, cells), seed + 7);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
            return f2 - f1;
        }

        static float Value(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period; int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(Mod(ix, period), Mod(iy, period), seed), b = Hash(Mod(ix + 1, period), Mod(iy, period), seed);
            float c = Hash(Mod(ix, period), Mod(iy + 1, period), seed), d = Hash(Mod(ix + 1, period), Mod(iy + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static Texture2D MakeIceHeight()
        {
            const int S = 1024;
            var px = new byte[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float n = 0.5f * Value(u, v, 4, 1) + 0.25f * Value(u, v, 8, 2) + 0.14f * Value(u, v, 16, 3) + 0.08f * Value(u, v, 32, 4) + 0.03f * Value(u, v, 64, 5);
                float big = 1f - Smooth(0f, 0.035f, Border(u, v, 3, 11));       // 1 on a crack line
                float small = 1f - Smooth(0f, 0.04f, Border(u, v, 8, 29));
                float h = 0.62f + 0.55f * (n - 0.5f) - 0.34f * big - 0.10f * small;
                px[y * S + x] = (byte)(Mathf.Clamp01(h) * 255f);
            }
            var tex = new Texture2D(S, S, TextureFormat.R8, true, true) { name = "ice_height", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixelData(px, 0);
            tex.Apply(true);
            return tex;
        }

        static void Set(Material m, float refract, Color ice, float tiling, float fresnel, float fresnelOn)
        {
            m.SetFloat("_RefractStrength", refract); m.SetColor("_IceColor", ice);
            m.SetVector("_IceTiling", new Vector4(tiling, tiling, 0, 0));
            m.SetFloat("_FresnelStrength", fresnel); m.SetFloat("_FresnelOn", fresnelOn);
        }

        static float S(float a, float b, float t) { return Smooth(a, b, t); }

        void Orbit(Camera c, float azDeg, float r, float h, Vector3 at)
        {
            float a = azDeg * Mathf.Deg2Rad;
            c.transform.position = new Vector3(at.x + Mathf.Sin(a) * r, h, at.z + Mathf.Cos(a) * r);
            c.transform.LookAt(at);
        }

        void Update()
        {
            float t = FrameCapturer.T;
            bool split = t < 8f || (t >= 14f && t < 19f);
            camL.enabled = camR.enabled = split; camFull.enabled = !split;
            Set(matL, Refract, IceColor, 1f, Fresnel, 1f);
            Set(matR, Refract, IceColor, 1f, Fresnel, 1f);

            if (t < 8f)
            {   // A: scene colour without offset | full ice
                Set(matL, 0f, Color.white, 1f, Fresnel, 0f);
                var at = new Vector3(0.2f, 0.95f, 0);
                float az = Mathf.Lerp(-13f, 13f, S(0f, 8f, t));
                Orbit(camL, az, 5.4f, 1.3f, at); Orbit(camR, az, 5.4f, 1.3f, at);
            }
            else if (t < 14f)
            {   // B: the layers of the graph, one after another, on the slab
                if (t < 9.5f) Set(matR, 0f, Color.white, 1f, Fresnel, 0f);
                else if (t < 11f) Set(matR, Refract, Color.white, 1f, Fresnel, 0f);
                else if (t < 12.5f) Set(matR, Refract, IceColor, 1f, Fresnel, 0f);
                camFull.transform.position = Vector3.Lerp(new Vector3(1.0f, 1.2f, 4.4f), new Vector3(0.8f, 1.15f, 4.0f), S(8f, 14f, t));
                camFull.transform.LookAt(new Vector3(0.35f, 0.82f, 0.2f));
            }
            else if (t < 19f)
            {   // C: parameter pairs on the crystals
                if (t < 16.5f) Set(matR, Refract, IceColor, 3f, Fresnel, 1f);
                else Set(matR, Refract, IceColor, 1f, 1.0f, 1f);
                var at = new Vector3(2.5f, 0.95f, 0.2f);
                float az = Mathf.Lerp(-6f, 12f, S(14f, 19f, t));
                Orbit(camL, az, 4.3f, 1.25f, at); Orbit(camR, az, 4.3f, 1.25f, at);
            }
            else
            {   // D: from beside the lantern to behind the slab
                float k = S(19.3f, 23.6f, t);
                camFull.transform.position = Vector3.Lerp(new Vector3(-3.3f, 1.3f, 3.3f), new Vector3(0.7f, 1.3f, 4.1f), k);
                camFull.transform.LookAt(new Vector3(-0.6f, 1.25f, -1.8f));
            }
        }
    }
}

// Deformable ground lane: a heightfield kept on the CPU and drawn as a grid mesh.
// Plays the role of the Unity Terrain heightmap in edualvarado/unity-footprints (TerrainMaster / DeformTerrainMaster):
// h is the live heightmap, h0 the "constant" copy the deformation limits are measured against.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class SoftGround : MonoBehaviour
    {
        public int nx = 144, nz = 400;       // cells; samples are (nx+1) x (nz+1)
        public float cell = 0.0125f;         // m
        public string presetName = "snow";
        public double young = 200000;        // Young's modulus E, Pa
        public float tau = 0.2f;             // characteristic (contact) time, s
        public float poisson = 0.1f;         // Poisson ratio
        public int filterIterations = 0;     // 3x3 Gaussian passes per footprint
        public bool bump = false;            // raise the ring of cells around the contact area
        public float noise = 0.002f;         // m, initial surface relief
        public Material material;

        [System.NonSerialized] public float[] h, h0;
        Vector3[] verts, normals; Mesh mesh;
        int dx0, dx1, dz0, dz1; bool dirty;
        public int W => nx + 1;
        public Vector3 Origin => transform.position;
        public float Z0 => transform.position.z;
        public float Z1 => transform.position.z + nz * cell;

        void Awake()
        {
            int w = nx + 1, d = nz + 1;
            h = new float[w * d]; h0 = new float[w * d]; verts = new Vector3[w * d]; normals = new Vector3[w * d];
            var uv = new Vector2[w * d];
            Vector3 o = transform.position;
            for (int iz = 0; iz < d; iz++) for (int ix = 0; ix < w; ix++)
            {
                float x = o.x + ix * cell, z = o.z + iz * cell;
                // relief fades to zero at the lane edges so lanes and the static surround meet without a step
                float fade = Mathf.Clamp01(Mathf.Min(Mathf.Min(ix, nx - ix), Mathf.Min(iz, nz - iz)) / 10f);
                float v = noise * fade * ((Mathf.PerlinNoise(x * 6f + 31f, z * 6f + 17f) - 0.5f) * 2f + 0.4f * (Mathf.PerlinNoise(x * 23f + 5f, z * 23f + 9f) - 0.5f) * 2f);
                int i = iz * w + ix;
                h[i] = h0[i] = v; verts[i] = new Vector3(ix * cell, v, iz * cell); uv[i] = new Vector2(x, z);
            }
            var tri = new int[nx * nz * 6];
            for (int iz = 0, t = 0; iz < nz; iz++) for (int ix = 0; ix < nx; ix++)
            {
                int a = iz * w + ix, b = a + 1, c = a + w, e = c + 1;
                tri[t++] = a; tri[t++] = c; tri[t++] = b; tri[t++] = b; tri[t++] = c; tri[t++] = e;
            }
            mesh = new Mesh { name = "SoftGround", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tri;
            Normals(0, nx, 0, nz);
            mesh.normals = normals;
            mesh.bounds = new Bounds(new Vector3(nx * cell * 0.5f, 0, nz * cell * 0.5f), new Vector3(nx * cell, 0.6f, nz * cell));
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
        }

        void Normals(int x0, int x1, int z0, int z1)
        {
            int w = nx + 1; float inv = 1f / (2f * cell);
            x0 = Mathf.Max(0, x0); x1 = Mathf.Min(nx, x1); z0 = Mathf.Max(0, z0); z1 = Mathf.Min(nz, z1);
            for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
            {
                float l = h[iz * w + Mathf.Max(0, ix - 1)], r = h[iz * w + Mathf.Min(nx, ix + 1)];
                float b = h[Mathf.Max(0, iz - 1) * w + ix], f = h[Mathf.Min(nz, iz + 1) * w + ix];
                normals[iz * w + ix] = new Vector3((l - r) * inv, 1f, (b - f) * inv).normalized;
            }
        }

        public bool InRange(int ix, int iz) => ix >= 0 && ix <= nx && iz >= 0 && iz <= nz;
        public int Ix(float x) => Mathf.RoundToInt((x - transform.position.x) / cell);
        public int Iz(float z) => Mathf.RoundToInt((z - transform.position.z) / cell);
        public Vector3 World(int i) { int w = nx + 1; return transform.position + new Vector3((i % w) * cell, h[i], (i / w) * cell); }
        public float HeightAt(float x, float z)
        {
            int ix = Mathf.Clamp(Ix(x), 0, nx), iz = Mathf.Clamp(Iz(z), 0, nz);
            return transform.position.y + h[iz * (nx + 1) + ix];
        }

        public void MarkDirty(int x0, int x1, int z0, int z1)
        {
            if (!dirty) { dx0 = x0; dx1 = x1; dz0 = z0; dz1 = z1; dirty = true; }
            else { dx0 = Mathf.Min(dx0, x0); dx1 = Mathf.Max(dx1, x1); dz0 = Mathf.Min(dz0, z0); dz1 = Mathf.Max(dz1, z1); }
        }

        // 3x3 Gaussian (1 2 1 / 2 4 2 / 1 2 1) / 16 over a window, as NewFilterHeightMapReturn in the source
        public void Blur(int x0, int x1, int z0, int z1)
        {
            int w = nx + 1;
            x0 = Mathf.Max(1, x0); x1 = Mathf.Min(nx - 1, x1); z0 = Mathf.Max(1, z0); z1 = Mathf.Min(nz - 1, z1);
            int bw = x1 - x0 + 1, bh = z1 - z0 + 1; if (bw <= 0 || bh <= 0) return;
            var tmp = new float[bw * bh];
            for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
            {
                int i = iz * w + ix;
                tmp[(iz - z0) * bw + (ix - x0)] = (h[i - w - 1] + 2 * h[i - w] + h[i - w + 1] + 2 * h[i - 1] + 4 * h[i] + 2 * h[i + 1] + h[i + w - 1] + 2 * h[i + w] + h[i + w + 1]) / 16f;
            }
            for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++) h[iz * w + ix] = tmp[(iz - z0) * bw + (ix - x0)];
            MarkDirty(x0, x1, z0, z1);
        }

        void LateUpdate()
        {
            if (!dirty) return;
            int w = nx + 1;
            int x0 = Mathf.Max(0, dx0), x1 = Mathf.Min(nx, dx1), z0 = Mathf.Max(0, dz0), z1 = Mathf.Min(nz, dz1);
            for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++) verts[iz * w + ix].y = h[iz * w + ix];
            Normals(x0 - 1, x1 + 1, z0 - 1, z1 + 1);
            mesh.SetVertices(verts); mesh.SetNormals(normals);
            dirty = false;
        }
    }
}

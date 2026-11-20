// Input side of the GPU trails (my code, plugs into the source's GpuTrailAppendNode): every frame it uploads
// 4 sub-frame positions per trail (input-major layout the AppendNode kernel expects) for a few body points of
// the walking mannequin plus a swarm of points circling it. Node append, vertex update, culling and LOD all
// run in the source's compute shaders.
using GpuTrailSystem;
using UnityEngine;

namespace GameTech.GpuTrails
{
    public class TrailSwarm : GpuTrailAppendNode
    {
        public Transform[] bodyPoints = new Transform[0];
        public int swarmCount = 1024;
        public float life = 1.6f;
        public float inputPerSec = 120f;
        public int subSteps = 4;
        public Vector3 centre = new Vector3(0f, 0f, 3f);
        public Color bodyColor = new Color(6f, 3f, 0.8f);

        Vector3[] prevBody;
        Vector3[] posArray;
        bool colorsSet;

        public int TrailCount => bodyPoints.Length + swarmCount;
        public int NodesPerTrail => gpuTrail != null && gpuTrail.IsInitialized ? gpuTrail.NodeNumPerTrail : Mathf.CeilToInt(life * inputPerSec);

        void Awake() { if (gpuTrail == null) gpuTrail = new GpuTrail(); }

        protected override void PreInitGpuTrail()
        {
            gpuTrail.trailNum = TrailCount;
            gpuTrail.life = life;
            gpuTrail.inputPerSec = inputPerSec;
            gpuTrail.minNodeDistance = 0.004f;
            inputCountMax = subSteps;
            colorEnable = true;
            ignoreOriginInput = true;
        }

        static float Hash(int i, int k)
        {
            uint h = (uint)(i * 747796405 + k * 2891336453u + 12345);
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (h & 0xffffff) / 16777216f;
        }

        // swarm point s at time t: circles the centre at its own radius, height and speed, with slow wobble
        public static Vector3 SwarmPos(int s, float t, Vector3 centre)
        {
            float r = 1.3f + 3.2f * Hash(s, 1);
            float y = 0.25f + 2.6f * Hash(s, 2);
            float dirSign = Hash(s, 3) < 0.8f ? 1f : -1f;
            float w = dirSign * (0.45f + 0.45f * Hash(s, 4)) * 1.6f / Mathf.Sqrt(r);
            float a = 6.2832f * Hash(s, 5) + w * t + 0.3f * Mathf.Sin(0.7f * t + 6.2832f * Hash(s, 6));
            float rr = r + 0.35f * Mathf.Sin((0.6f + Hash(s, 7)) * t + 6.2832f * Hash(s, 8));
            float yy = y + 0.45f * Mathf.Sin((0.5f + 0.8f * Hash(s, 9)) * t + 6.2832f * Hash(s, 10));
            return centre + new Vector3(rr * Mathf.Cos(a), yy, rr * Mathf.Sin(a));
        }

        public static Color SwarmColor(int s)
        {
            float h = Mathf.Lerp(0.5f, 0.8f, Hash(s, 11));
            return Color.HSVToRGB(h, 0.75f, 1f);
        }

        protected override int UpdateInputBuffer()
        {
            int n = gpuTrail.trailNum, k = inputCountMax, nb = bodyPoints.Length;
            if (posArray == null || posArray.Length != n * k) posArray = new Vector3[n * k];
            if (prevBody == null || prevBody.Length != nb)
            {
                prevBody = new Vector3[nb];
                for (int i = 0; i < nb; i++) prevBody[i] = bodyPoints[i].position;
            }
            float t = Time.time, dt = Time.deltaTime;
            for (int j = 0; j < k; j++)
            {
                float f = (j + 1f) / k;            // oldest first, the last one is this frame
                float tj = t - dt * (1f - f);
                for (int i = 0; i < nb; i++) posArray[n * j + i] = Vector3.Lerp(prevBody[i], bodyPoints[i].position, f);
                for (int s = 0; s < swarmCount; s++) posArray[n * j + nb + s] = SwarmPos(s, tj, centre);
            }
            for (int i = 0; i < nb; i++) prevBody[i] = bodyPoints[i].position;
            InputBufferPos.SetData(posArray);

            if (!colorsSet)
            {
                var cols = new Color[n * k];
                for (int j = 0; j < k; j++)
                {
                    for (int i = 0; i < nb; i++) cols[n * j + i] = bodyColor;
                    for (int s = 0; s < swarmCount; s++) cols[n * j + nb + s] = SwarmColor(s);
                }
                InputBufferColor.SetData(cols);
                colorsSet = true;
            }
            return k;
        }
    }
}

// Measurement only (writes to the log, draws nothing): reads back the strand tip particles of the simulated and the
// rigid instance, expresses both in their own bust's local frame, and logs how far the simulated tips are from the
// rigid ones (same strand, same asset). This is the "lag" number quoted in the post.
using Unity.DemoTeam.Hair;
using UnityEngine;

namespace GameTech.Posts
{
    public class HairDebug : MonoBehaviour
    {
        public HairInstance sim, rigid;
        public Transform simRoot, rigidRoot;
        public float every = 0.1f, from = 0f, to = 1e9f;
        float next; Vector3[] a, b; float maxSeen;

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            if (t < from || t > to || t < next) return;
            next = t + every;
            if (sim.solverData == null || rigid.solverData == null || sim.solverData.Length == 0) return;
            var sd = sim.solverData[0]; var rd = rigid.solverData[0];
            int n = sd.buffers._ParticlePosition.count;
            if (a == null || a.Length != n) { a = new Vector3[n]; b = new Vector3[n]; }
            sd.buffers._ParticlePosition.GetData(a); rd.buffers._ParticlePosition.GetData(b);
            ref readonly var g = ref sim.strandGroupInstances[0].groupAssetReference.Resolve();
            int sc = g.strandCount, pc = g.strandParticleCount;
            bool inter = g.particleMemoryLayout == HairAsset.MemoryLayout.Interleaved;
            var d = new float[sc]; double sum = 0;
            for (int s = 0; s < sc; s++)
            {
                int i = inter ? (pc - 1) * sc + s : s * pc + pc - 1;
                var pa = simRoot.InverseTransformPoint(a[i]); var pb = rigidRoot.InverseTransformPoint(b[i]);
                d[s] = (pa - pb).magnitude; sum += d[s];
            }
            System.Array.Sort(d);
            float p95 = d[(int)(sc * 0.95f)], mx = d[sc - 1];
            maxSeen = Mathf.Max(maxSeen, p95);
            Debug.Log($"[gt] lag t {t:F2} tips sim-vs-rigid mean {sum / sc * 100:F1} cm p95 {p95 * 100:F1} cm max {mx * 100:F1} cm (p95 peak so far {maxSeen * 100:F1} cm)");
        }
    }
}

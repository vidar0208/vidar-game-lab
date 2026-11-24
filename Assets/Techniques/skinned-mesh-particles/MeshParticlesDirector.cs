// Particles born on an animated character, fed by the Smrvfx SkinnedMeshBaker (Smrvfx/, from keijiro/Smrvfx, Unlicense).
// My code: the dance, the hidden bind-pose copy, the compute particle system (Shaders/MeshParticles.compute), the
// particle renderer, cameras, labels and timing.
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(100)]   // after the bakers' LateUpdate, so the maps hold this frame's mesh
    public class MeshParticlesDirector : MonoBehaviour
    {
        public ComputeShader sim;
        public Material drawMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        // A: dances, particles from the hidden bind-pose copy A0. C: dances, inherit 0. B: dances, inherit 1.
        public CharacterRig rigA, rigB, rigC;
        public Transform rootA0;
        public Smrvfx.SkinnedMeshBaker bakerA, bakerB, bakerC;
        public Vector3 baseA, baseB, baseC;

        public const int Count = 131072, EmitPerFrame = 1600;
        const float Dt = 1f / 30f;

        class Sys
        {
            public ComputeBuffer pos, vel; public Material mat; public Smrvfx.SkinnedMeshBaker baker; public float inherit; public int head;
        }
        Sys sA, sB, sC;
        int kEmit, kStep, phaseShown = -1, verts; float bakeMs;

        Sys Make(string name, Smrvfx.SkinnedMeshBaker baker, float inherit)
        {
            var s = new Sys { baker = baker, inherit = inherit };
            s.pos = new ComputeBuffer(Count, 16); s.vel = new ComputeBuffer(Count, 16);
            s.pos.SetData(new Vector4[Count]); s.vel.SetData(new Vector4[Count]);
            s.mat = new Material(drawMat);
            s.mat.SetBuffer("_Pos", s.pos); s.mat.SetBuffer("_Vel", s.vel);
            s.mat.SetFloat("_Size", 0.0065f); s.mat.SetFloat("_Gain", 0.2f);
            s.mat.SetVector("_Hot", new Vector4(2.4f, 1.6f, 0.8f, 1)); s.mat.SetVector("_Mid", new Vector4(2.2f, 0.75f, 0.12f, 1));
            s.mat.SetVector("_Cold", new Vector4(0.55f, 0.07f, 0.03f, 1));
            var go = new GameObject(name + " particles");
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = new Vector3[Count * 6];
            var idx = new int[Count * 6]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = s.mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            return s;
        }

        void Start()
        {
            kEmit = sim.FindKernel("Emit"); kStep = sim.FindKernel("Step");
            sA = Make("Bind pose", bakerA, 1f); sC = Make("Inherit 0", bakerC, 0f); sB = Make("Inherit 1", bakerB, 1f);
            foreach (var r in new[] { rigA, rigB, rigC }) { r.idle = false; r.blink = false; }
            // CPU cost of the baker's main step: one SkinnedMeshRenderer.BakeMesh of the mannequin
            Pose(rigB, baseB, 1.3f);
            var smr = rigB.GetComponentInChildren<SkinnedMeshRenderer>(); var tmp = new Mesh(); verts = smr.sharedMesh.vertexCount;
            smr.BakeMesh(tmp); var sw = Stopwatch.StartNew(); for (int i = 0; i < 200; i++) smr.BakeMesh(tmp); bakeMs = (float)sw.Elapsed.TotalMilliseconds / 200f;
            Destroy(tmp);
            Debug.Log($"[gt] mannequin {verts} verts, BakeMesh {bakeMs:F3} ms, deltaTime {Time.deltaTime:F4}, captureDeltaTime {Time.captureDeltaTime:F4}");
        }

        // a procedural dance on top of the bind pose (all rotations are world-space deltas, see CharacterRig.Turn)
        static void Pose(CharacterRig r, Vector3 basePos, float t)
        {
            float w = 2f * Mathf.PI * 0.85f, a = Mathf.Sin(w * t), b = Mathf.Sin(w * t + 2.1f), k = Mathf.Sin(w * t * 0.5f);
            r.transform.SetPositionAndRotation(basePos + new Vector3(Mathf.Sin(t * 1.1f) * 0.3f, 0.04f * Mathf.Abs(a), Mathf.Sin(t * 0.7f) * 0.15f),
                Quaternion.Euler(0, 180f + Mathf.Sin(t * 0.55f) * 55f, 0));
            r.ResetPose();
            r.Turn("Spine", 28f * Mathf.Sin(w * t * 0.5f + 0.6f), Vector3.up);
            r.Turn("Chest", 8f * a, Vector3.forward);
            r.Turn("Upperarm_L", 18f + 62f * a, Vector3.forward); r.Turn("Upperarm_R", -(18f + 62f * b), Vector3.forward);
            r.Turn("Upperarm_L", 45f * Mathf.Cos(w * t), Vector3.right); r.Turn("Upperarm_R", 45f * Mathf.Cos(w * t + 2.1f), Vector3.right);
            r.Turn("Lowerarm_L", -35f - 35f * a, Vector3.up); r.Turn("Lowerarm_R", 35f + 35f * b, Vector3.up);
            float kl = Mathf.Max(0, k), kr = Mathf.Max(0, -k);
            r.Turn("Upperleg_L", -55f * kl, Vector3.right); r.Turn("Lowerleg_L", 50f * kl, Vector3.right);
            r.Turn("Upperleg_R", -55f * kr, Vector3.right); r.Turn("Lowerleg_R", 50f * kr, Vector3.right);
            r.Turn("Head", 15f * Mathf.Sin(w * t * 0.5f), Vector3.up);
        }

        void Update()
        {
            float t = FrameCapturer.T;
            Pose(rigA, baseA, t); Pose(rigB, baseB, t); Pose(rigC, baseC, t);
            rootA0.SetPositionAndRotation(rigA.transform.position, rigA.transform.rotation);   // bind-pose copy: root only

            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 6f ? 0 : t < 14f ? 1 : t < 21f ? 2 : 3;
            if (p == 0 || p == 3)
            {
                Look(camFull, baseB, t * 14f - 20f, 4.0f);
                if (p == 0) Labels("Particles born on an animated mesh, every frame",
                    "the baker turns the skinned mesh into position + velocity textures (8,192 surface points)",
                    $"my compute particles read them: {EmitPerFrame:N0} born per frame, each inherits the surface velocity", "", "");
                else Labels("Baker from keijiro/Smrvfx (Unlicense); the particles are my compute shader",
                    $"BakeMesh of the {verts:N0}-vertex stand-in mannequin: {bakeMs:F2} ms on the CPU per frame",
                    "Apple M4 Mac mini, Metal, Unity 6.3 Editor, 1080p. No VFX Graph: compute particles fed by the same baked textures", "", "");
            }
            else
            {
                camBack.enabled = true;
                if (p == 1)
                {
                    Look(camL, baseA, 15f, 4.4f); Look(camR, baseB, 15f, 4.4f);
                    Labels("Same dance, same particles: only the spawn points differ",
                        "Bind pose: the mesh before skinning", "particles keep the T-pose, only the root moves",
                        "Skinned mesh, baked this frame", "particles follow the arms and legs");
                }
                else
                {
                    Look(camL, baseC, 15f, 4.4f); Look(camR, baseB, 15f, 4.4f);
                    Labels("Same skinned spawn points: does a particle keep the surface velocity?",
                        "Inherit 0: born at rest", "the body moves on and leaves them behind as a smear",
                        "Inherit 1: born with the surface velocity", "they travel with the body; fast arms fling them out");
                }
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            foreach (var s in new[] { sA, sB, sC }) Run(s, t);
        }

        void Run(Sys s, float t)
        {
            if (s.baker.PositionMap == null) return;
            var pm = s.baker.PositionMap;
            sim.SetInt("_Count", Count); sim.SetInt("_Head", s.head); sim.SetInt("_EmitCount", EmitPerFrame);
            sim.SetInt("_Frame", Time.frameCount); sim.SetInt("_MapW", pm.width); sim.SetInt("_MapH", pm.height);
            sim.SetFloat("_Inherit", s.inherit); sim.SetFloat("_Dt", Dt); sim.SetFloat("_Time", t);
            foreach (var k in new[] { kEmit, kStep }) { sim.SetBuffer(k, "_Pos", s.pos); sim.SetBuffer(k, "_Vel", s.vel); }
            sim.SetTexture(kEmit, "_PosMap", pm); sim.SetTexture(kEmit, "_VelMap", s.baker.VelocityMap); sim.SetTexture(kEmit, "_NrmMap", s.baker.NormalMap);
            sim.Dispatch(kStep, (Count + 255) / 256, 1, 1);
            sim.Dispatch(kEmit, (EmitPerFrame + 63) / 64, 1, 1);
            s.head = (s.head + EmitPerFrame) % Count;
        }

        static void Look(Camera c, Vector3 at, float yaw, float dist)
        {
            c.enabled = true;
            var target = at + new Vector3(0, 0.78f, 0);
            var rot = Quaternion.Euler(8f, yaw, 0);
            c.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void OnDestroy()
        {
            foreach (var s in new[] { sA, sB, sC }) if (s != null) { s.pos.Release(); s.vel.Release(); }
        }
    }
}

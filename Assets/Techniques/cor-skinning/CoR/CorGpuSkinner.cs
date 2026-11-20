using System.Runtime.InteropServices;
using UnityEngine;

namespace CoR
{
    // Port of the source's runtime path (CoRData buffers from CoRManager + CorGPUSkinning) for URP.
    // Same buffers, same kernel (CorSkinning), same per-bone data: bone localToWorld matrices and rotations.
    // GameTech changes: one component per skinned figure instead of SkinnedCor + CoRManager; bones gathered
    // with the source's non-job fallback (skinningNoJobs) and SetData; the CoR weight is per figure (the source has
    // one global weight), so one figure can show plain linear blend skinning (weight 0) with the same kernel;
    // output buffers are handed to a URP material that reads them by vertex id.
    [DefaultExecutionOrder(0)]
    public class CorGpuSkinner : MonoBehaviour
    {
        public CorAsset corAsset;
        public ComputeShader skinningShader;
        public Transform[] allBones;            // the skinned mesh's bones (indices as in the bone weights)
        public Material material;
        [Range(0, 1)] public float corWeight = 1f;

        public ComputeBuffer VerticesOut { get; private set; }
        Transform[] bones;                      // used bones only, as in the source
        ComputeBuffer realIndices, vertices, normals, tangents, boneWeights, bindPoses, bindRotations, corWeights, tBuffer;
        ComputeBuffer normalsOut, tangentsOut, boneBuffer, qBuffer;
        Matrix4x4[] boneMatrices; Vector4[] q;
        int kernel;
        Material instanceMat;

        void Start()
        {
            var a = corAsset;
            bones = new Transform[a.usedBones.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = allBones[a.usedBones[i]];
            int n = a.vertices.Length;

            // CoRData.setupBuffers
            realIndices = new ComputeBuffer(a.usedBoneIndices.Length, sizeof(int)); realIndices.SetData(a.usedBoneIndices);
            vertices = new ComputeBuffer(n, 12); vertices.SetData(a.vertices);
            normals = new ComputeBuffer(n, 12); normals.SetData(a.normals);
            tangents = new ComputeBuffer(n, 16); tangents.SetData(a.tangents);
            tBuffer = new ComputeBuffer(n, 12); tBuffer.SetData(a.pStar);
            boneWeights = new ComputeBuffer(n, Marshal.SizeOf(typeof(BoneWeight))); boneWeights.SetData(a.boneWeights);
            bindPoses = new ComputeBuffer(a.bindposes.Length, 64); bindPoses.SetData(a.bindposes);
            var rot = new Vector4[a.bindposes.Length];
            for (int i = 0; i < rot.Length; i++) { var r = a.bindposes[i].rotation; rot[i] = new Vector4(r.x, r.y, r.z, r.w); }
            bindRotations = new ComputeBuffer(rot.Length, 16); bindRotations.SetData(rot);
            corWeights = new ComputeBuffer(n, 4); corWeights.SetData(a.corWeight);

            // CorGPUSkinning.OnSetup
            VerticesOut = new ComputeBuffer(n, 12);
            normalsOut = new ComputeBuffer(n, 12);
            tangentsOut = new ComputeBuffer(n, 16);
            boneBuffer = new ComputeBuffer(bones.Length, 64);
            qBuffer = new ComputeBuffer(bones.Length, 16);
            boneMatrices = new Matrix4x4[bones.Length]; q = new Vector4[bones.Length];
            kernel = skinningShader.FindKernel("CSMain");

            instanceMat = new Material(material) { name = material.name + " (" + name + ")" };
            instanceMat.SetBuffer("verticesOutBuffer", VerticesOut);
            instanceMat.SetBuffer("normalsOutBuffer", normalsOut);
            GetComponent<MeshRenderer>().sharedMaterial = instanceMat;
        }

        void LateUpdate()
        {
            // skinningNoJobs
            for (int i = 0; i < bones.Length; i++)
            {
                boneMatrices[i] = bones[i].localToWorldMatrix;
                var r = bones[i].rotation; q[i] = new Vector4(r.x, r.y, r.z, r.w);
            }
            boneBuffer.SetData(boneMatrices); qBuffer.SetData(q);

            var cs = skinningShader;
            cs.SetFloat("g_corWeight", corWeight);
            cs.SetBuffer(kernel, "realIndices", realIndices);
            cs.SetBuffer(kernel, "verticesBuffer", vertices);
            cs.SetBuffer(kernel, "normalsBuffer", normals);
            cs.SetBuffer(kernel, "tangentsBuffer", tangents);
            cs.SetBuffer(kernel, "corWeight", corWeights);
            cs.SetBuffer(kernel, "boneWeightBuffer", boneWeights);
            cs.SetBuffer(kernel, "bindPoseRotations", bindRotations);
            cs.SetBuffer(kernel, "bindBuffer", bindPoses);
            cs.SetBuffer(kernel, "tBuffer", tBuffer);
            cs.SetInt("vertCount", corAsset.vertices.Length);
            var inv = Quaternion.Inverse(transform.rotation);
            cs.SetVector("inverseBaseRotation", new Vector4(inv.x, inv.y, inv.z, inv.w));
            cs.SetMatrix("worldToLocalMatrix", transform.worldToLocalMatrix);
            cs.SetBuffer(kernel, "qBuffer", qBuffer);
            cs.SetBuffer(kernel, "boneBuffer", boneBuffer);
            cs.SetBuffer(kernel, "verticesOutBuffer", VerticesOut);
            cs.SetBuffer(kernel, "normalsOutBuffer", normalsOut);
            cs.SetBuffer(kernel, "tangentsOutBuffer", tangentsOut);
            cs.Dispatch(kernel, corAsset.vertices.Length / 128 + 1, 1, 1);
        }

        void OnDestroy()
        {
            foreach (var b in new[] { realIndices, vertices, normals, tangents, boneWeights, bindPoses, bindRotations, corWeights, tBuffer, VerticesOut, normalsOut, tangentsOut, boneBuffer, qBuffer })
                b?.Dispose();
        }
    }
}

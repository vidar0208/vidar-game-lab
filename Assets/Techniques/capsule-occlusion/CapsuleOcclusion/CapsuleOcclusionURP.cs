// URP version of the source's CapsuleOcclusionCamera (GameTech port). The source hooks OnPreRender, which URP does
// not call, and clusters with a rasterization pass that writes UAVs from fragment shaders. Here the same work runs
// in RenderPipelineManager.beginCameraRendering for this camera only, with the source's compute clustering path:
// gather capsules (OcclusionCapsule, unchanged logic), build the froxel lists, publish the buffers as globals.
// Per-camera switches (on/off, apply to the character, view mode) let split screens compare on the same frame.
using UnityEngine;
using UnityEngine.Rendering;

namespace CapsuleOcclusion
{
	[RequireComponent(typeof(Camera))]
	public class CapsuleOcclusionURP : MonoBehaviour
	{
		// source parameters and defaults
		public Vector3Int clusterSize = new Vector3Int(64, 64, 32);
		[Range(4, 32)] public int clusterDataHeadroom = 16;
		[Min(0)] public float maxCameraRange = 50;
		[Range(16, OcclusionCapsule.MAX_CAPSULE_COUNT)] public int maxCapsuleCount = 256;
		[Range(1, 8)] public float capsuleRangeMultiplier = 4;
		[Range(0, 100)] public float intensity = 10;
		public bool cullCapsules = true;
		public bool sortCapsules = true;
		public ComputeShader compute;

		// GameTech: comparison switches
		public bool occlusion = true;      // false: occlusion factor is 1 everywhere
		public bool onCharacter = true;    // false: materials flagged as character skip it
		public int view = 0;               // 0 shaded, 1 ambient only, 2 debug (cluster heat x occlusion)

		public static int LastVisible;     // capsules sent by the last camera
		public static uint LastEntries;    // capsule-in-froxel entries (async readback, a few frames old)

		Camera m_camera;
		CommandBuffer m_cmd;
		ComputeBuffer m_clusters, m_clusterData, m_counter;
		readonly Vector4[] m_frustumRays = new Vector4[4];
		readonly uint[] m_zero = { 0 };
		int m_frame;

		static readonly int ClustersOut = Shader.PropertyToID("_ClustersOut"), MaxData = Shader.PropertyToID("_MaxData");
		static readonly int View = Shader.PropertyToID("_GT_COView"), OnChar = Shader.PropertyToID("_GT_COChar");
		static readonly int ViewProj = Shader.PropertyToID("_GT_COViewProj"), Far = Shader.PropertyToID("_GT_COFar");

		void OnEnable()
		{
			m_camera = GetComponent<Camera>();
			m_cmd = new CommandBuffer { name = "Capsule Occlusion" };
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			Utils.Release(ref m_clusters); Utils.Release(ref m_clusterData); Utils.Release(ref m_counter);
		}

		void OnBeginCamera(ScriptableRenderContext context, Camera camera)
		{
			if (camera != m_camera || compute == null) return;

			OcclusionCapsule.UpdateCapsuleDataForCamera(camera, maxCapsuleCount, capsuleRangeMultiplier, cullCapsules, sortCapsules);
			LastVisible = OcclusionCapsule.visibleCapsuleCount;

			int voxels = clusterSize.x * clusterSize.y * clusterSize.z;
			Utils.LazyCreate(ref m_clusters, "Clusters", voxels, sizeof(uint) * 2);
			Utils.LazyCreate(ref m_clusterData, "Cluster Data", voxels * clusterDataHeadroom, sizeof(uint) * 2);
			Utils.LazyCreate(ref m_counter, "Counter", 1, sizeof(uint));

			m_cmd.Clear();
			m_cmd.SetBufferData(m_counter, m_zero);

			Utils.GetFrustumRays(camera.worldToCameraMatrix, camera.projectionMatrix, m_frustumRays);
			float range = Mathf.Min(maxCameraRange, camera.farClipPlane);
			const int k = 0;
			m_cmd.SetComputeVectorParam(compute, ShaderIDs.CameraPosition, camera.transform.position);
			m_cmd.SetComputeVectorParam(compute, ShaderIDs.CameraForward, camera.transform.forward);
			m_cmd.SetComputeFloatParam(compute, ShaderIDs.Range, range);
			m_cmd.SetComputeVectorArrayParam(compute, ShaderIDs.FrustumRays, m_frustumRays);
			m_cmd.SetComputeVectorParam(compute, ShaderIDs.ClusterSize, (Vector3)clusterSize);
			m_cmd.SetComputeVectorArrayParam(compute, ShaderIDs.CapsuleParams1, OcclusionCapsule.capsuleParams1);
			m_cmd.SetComputeVectorArrayParam(compute, ShaderIDs.CapsuleParams2, OcclusionCapsule.capsuleParams2);
			m_cmd.SetComputeIntParam(compute, ShaderIDs.CapsuleCount, OcclusionCapsule.visibleCapsuleCount);
			m_cmd.SetComputeIntParam(compute, MaxData, voxels * clusterDataHeadroom);
			m_cmd.SetComputeBufferParam(compute, k, ShaderIDs.ClusterData, m_clusterData);
			m_cmd.SetComputeBufferParam(compute, k, ShaderIDs.Counter, m_counter);
			m_cmd.SetComputeBufferParam(compute, k, ClustersOut, m_clusters);
			m_cmd.DispatchCompute(compute, k, Utils.GetThreadGroupCount(8, clusterSize));

			// Upload (same globals as the source, plus the per-camera switches)
			m_cmd.SetGlobalVectorArray(ShaderIDs.CapsuleParams1, OcclusionCapsule.capsuleParams1);
			m_cmd.SetGlobalVectorArray(ShaderIDs.CapsuleParams2, OcclusionCapsule.capsuleParams2);
			m_cmd.SetGlobalVector(ShaderIDs.ClusterSize, (Vector3)clusterSize);
			m_cmd.SetGlobalBuffer(ShaderIDs.CapsuleClusters, m_clusters);
			m_cmd.SetGlobalBuffer(ShaderIDs.CapsuleClusterData, m_clusterData);
			m_cmd.SetGlobalFloat(ShaderIDs.CapsuleOcclusionIntensity, occlusion ? intensity : 0f);
			m_cmd.SetGlobalFloat(ShaderIDs.DebugClusters, view == 2 ? 1 : 0);
			m_cmd.SetGlobalFloat(ShaderIDs.DepthToRange, Mathf.Max(1, camera.farClipPlane / maxCameraRange));
			m_cmd.SetGlobalFloat(View, view);
			m_cmd.SetGlobalFloat(OnChar, onCharacter ? 1 : 0);
			// cluster lookup in the material uses the same (non-GPU) matrices the froxel rays were built from
			m_cmd.SetGlobalMatrix(ViewProj, camera.projectionMatrix * camera.worldToCameraMatrix);
			m_cmd.SetGlobalFloat(Far, camera.farClipPlane);
			context.ExecuteCommandBuffer(m_cmd);

			if (++m_frame % 15 == 0)
				AsyncGPUReadback.Request(m_counter, r => { if (!r.hasError) LastEntries = r.GetData<uint>()[0]; });
		}

		public int GetByteCount()
		{
			int voxelCount = clusterSize.x * clusterSize.y * clusterSize.z;
			return voxelCount * sizeof(uint) * 2 + voxelCount * clusterDataHeadroom * sizeof(uint) * 2 + sizeof(uint);
		}
	}
}

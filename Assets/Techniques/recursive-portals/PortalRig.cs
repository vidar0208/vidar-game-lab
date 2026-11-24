// URP port of the portal rendering in SebLague/Portals (MIT, Copyright (c) 2020 Sebastian Lague):
// Portal.Render, Portal.SetNearClipPlane and CameraUtility.VisibleFromCamera / BoundsOverlap.
// Changes for URP: the built-in pipeline's MainCamera.OnPreCull hook does not exist, so the portal cameras are
// rendered from LateUpdate with Camera.Render(); each portal ping-pongs two render textures instead of reading and
// writing one; one rig per player camera so split-screen halves can use different settings.
// Not ported: travellers (teleporting and slicing objects) and the screen-thickness trick for walking through.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(1000)]
    public class PortalRig : MonoBehaviour
    {
        public Camera player;
        public bool oblique = true;                         // source: always on (with nearClipLimit)
        public int recursionLimit = 5;                      // source default 5
        public Transform[] portals = new Transform[2];      // forward (+z) points into the portal
        public MeshRenderer[] screens = new MeshRenderer[2];
        public Camera[] cams = new Camera[2];
        public float nearClipOffset = 0.05f, nearClipLimit = 0.2f;
        public int width = 1920, height = 1080;
        [System.NonSerialized] public int rendersLastFrame;
        [System.NonSerialized] public float msLastFrame;

        RenderTexture[,] rts;
        MeshFilter[] mfs;
        Material[] mats;
        static readonly int MainTex = Shader.PropertyToID("_MainTex"), Mask = Shader.PropertyToID("displayMask");
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        void Start()
        {
            rts = new RenderTexture[2, 2]; mfs = new MeshFilter[2]; mats = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                mfs[i] = screens[i].GetComponent<MeshFilter>();
                mats[i] = screens[i].material;           // per-rig instance
                mats[i].SetFloat(Mask, 1);
                for (int j = 0; j < 2; j++)
                {
                    rts[i, j] = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR) { name = $"{name} portal{i} rt{j}" };
                    rts[i, j].Create();
                }
                cams[i].enabled = false;
            }
        }

        void LateUpdate()
        {
            rendersLastFrame = 0; msLastFrame = 0;
            if (!player.enabled) return;
            player.aspect = width / (float)height;
            sw.Restart();
            for (int i = 0; i < 2; i++) Render(i);
            msLastFrame = (float)sw.Elapsed.TotalMilliseconds;
        }

        // Portal i's camera renders the view shown on the linked portal's screen (Portal.Render in the source)
        void Render(int i)
        {
            Transform P = portals[i], L = portals[1 - i];
            Camera portalCam = cams[i];
            MeshRenderer linkedScreen = screens[1 - i], screen = screens[i];
            Material linkedMat = mats[1 - i];

            // Skip rendering the view from this portal if player is not looking at the linked portal
            if (!VisibleFromCamera(linkedScreen, player)) return;

            var localToWorldMatrix = player.transform.localToWorldMatrix;
            var renderPositions = new Vector3[recursionLimit];
            var renderRotations = new Quaternion[recursionLimit];
            int startIndex = 0;
            portalCam.projectionMatrix = player.projectionMatrix;
            for (int k = 0; k < recursionLimit; k++)
            {
                // No need for recursive rendering if linked portal is not visible through this portal
                if (k > 0 && !BoundsOverlap(mfs[i], mfs[1 - i], portalCam)) break;
                localToWorldMatrix = P.localToWorldMatrix * L.worldToLocalMatrix * localToWorldMatrix;
                int renderOrderIndex = recursionLimit - k - 1;
                renderPositions[renderOrderIndex] = localToWorldMatrix.GetColumn(3);
                renderRotations[renderOrderIndex] = localToWorldMatrix.rotation;
                portalCam.transform.SetPositionAndRotation(renderPositions[renderOrderIndex], renderRotations[renderOrderIndex]);
                startIndex = renderOrderIndex;
            }

            // Hide screen so that camera can see through portal (this shader has no shadow pass, so ShadowsOnly hides it)
            screen.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            linkedMat.SetFloat(Mask, 0);   // deepest level: the innermost portal shows a flat colour

            int cur = 0;
            for (int k = startIndex; k < recursionLimit; k++)
            {
                portalCam.transform.SetPositionAndRotation(renderPositions[k], renderRotations[k]);
                SetNearClipPlane(P, portalCam);
                portalCam.targetTexture = rts[i, cur];
                portalCam.Render();
                rendersLastFrame++;
                linkedMat.SetTexture(MainTex, rts[i, cur]);   // the next (shallower) level sees this result
                cur ^= 1;
                if (k == startIndex) linkedMat.SetFloat(Mask, 1);
            }
            screen.shadowCastingMode = ShadowCastingMode.Off;
        }

        // Oblique projection: the portal camera's near plane lies on the portal surface (Lengyel's method)
        void SetNearClipPlane(Transform clipPlane, Camera portalCam)
        {
            if (!oblique) { portalCam.projectionMatrix = player.projectionMatrix; return; }
            int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, clipPlane.position - portalCam.transform.position));
            Vector3 camSpacePos = portalCam.worldToCameraMatrix.MultiplyPoint(clipPlane.position);
            Vector3 camSpaceNormal = portalCam.worldToCameraMatrix.MultiplyVector(clipPlane.forward) * dot;
            float camSpaceDst = -Vector3.Dot(camSpacePos, camSpaceNormal) + nearClipOffset;
            // Don't use oblique clip plane if very close to portal as it seems this can cause some visual artifacts
            if (Mathf.Abs(camSpaceDst) > nearClipLimit)
                portalCam.projectionMatrix = player.CalculateObliqueMatrix(new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst));
            else
                portalCam.projectionMatrix = player.projectionMatrix;
        }

        // ---- CameraUtility ----
        static readonly Vector3[] cubeCornerOffsets = {
            new Vector3(1, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
            new Vector3(-1, 1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(1, -1, 1) };
        static readonly Plane[] planes = new Plane[6];

        static bool VisibleFromCamera(Renderer renderer, Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }

        static bool BoundsOverlap(MeshFilter nearObject, MeshFilter farObject, Camera camera)
        {
            var near = ScreenRect(nearObject, camera); var far = ScreenRect(farObject, camera);
            if (far.zMax > near.zMin)   // ensure far object is indeed further away than near object
            {
                if (far.xMax < near.xMin || far.xMin > near.xMax) return false;
                if (far.yMax < near.yMin || far.yMin > near.yMax) return false;
                return true;
            }
            return false;
        }

        struct MinMax3D { public float xMin, xMax, yMin, yMax, zMin, zMax; }

        static MinMax3D ScreenRect(MeshFilter mf, Camera cam)
        {
            var r = new MinMax3D { xMin = float.MaxValue, yMin = float.MaxValue, zMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue, zMax = float.MinValue };
            var b = mf.sharedMesh.bounds; bool anyInFront = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents, cubeCornerOffsets[i])));
                if (v.z > 0) anyInFront = true;
                else { v.x = v.x <= 0.5f ? 1 : 0; v.y = v.y <= 0.5f ? 1 : 0; }   // behind the camera: clamp to the opposite edge
                r.xMin = Mathf.Min(r.xMin, v.x); r.xMax = Mathf.Max(r.xMax, v.x);
                r.yMin = Mathf.Min(r.yMin, v.y); r.yMax = Mathf.Max(r.yMax, v.y);
                r.zMin = Mathf.Min(r.zMin, v.z); r.zMax = Mathf.Max(r.zMax, v.z);
            }
            return anyInFront ? r : new MinMax3D();
        }
    }
}

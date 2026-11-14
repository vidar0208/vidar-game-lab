// Port of PlanarReflectionScript from draftPRD/URP_PlanarReflections (MIT, Copyright (c) 2020 William MacLeod), single level only.
// Kept: the settings (direction, clip plane offset, layers, resolution multiplier, shadows, HDR, MSAA, black clear), the reflection
// matrix, the camera-space clip plane, the oblique projection (with the source's 1.05 constant), fog off while the mirror renders,
// GL.invertCulling, and the global shader texture. Removed: Entities, Jobs and Burst (the three jobs are plain functions here),
// recursion, frame skipping and the separate reflection camera (the scene camera renders the mirror pass with Camera.Render).
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public class PlanarReflectionPort
    {
        public enum ResolutionMultipliers { Full, Half, Third, Quarter }

        public string shaderPropertyName = "_PlanarGround";
        public Vector3 direction = Vector3.up;
        public float clipPlaneOffset = 0.07f;
        public LayerMask reflectLayers = -1;
        public ResolutionMultipliers resolutionMultiplier = ResolutionMultipliers.Full;
        public bool shadows, enableHdr, enableMSAA, addBlackColour, occlusion;

        RenderTexture reflTexture;
        int currentWidth; bool currentHdr;

        public RenderTexture Texture => reflTexture;

        float GetScaleValue()
        {
            switch (resolutionMultiplier)
            {
                case ResolutionMultipliers.Full: return 1f;
                case ResolutionMultipliers.Half: return 0.5f;
                case ResolutionMultipliers.Third: return 0.33f;
                case ResolutionMultipliers.Quarter: return 0.25f;
                default: return 0.5f;
            }
        }

        public Vector2Int ReflectionResolution(Camera cam, float scale) =>
            new Vector2Int((int)(cam.pixelWidth * scale * GetScaleValue()), (int)(cam.pixelHeight * scale * GetScaleValue()));

        public void Render(Camera cam)
        {
            var fog = RenderSettings.fog;
            RenderSettings.fog = false;
            CreateMirrorTexture(cam);

            Vector3 normal = direction;
            float d = -clipPlaneOffset;
            Matrix4x4 reflection = CalculateReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, d));
            Vector4 clipPlane = CameraSpacePlane(cam.worldToCameraMatrix, reflection, normal * clipPlaneOffset, normal, 1f);
            Matrix4x4 projection = MakeProjectionMatrixOblique(cam.projectionMatrix, clipPlane);

            // The source renders a separate reflection camera. In this Unity 6.3 / URP 17.3 setup a second camera's
            // Camera.Render came back with the unmirrored view, so the scene camera itself is rendered a second time
            // with the mirrored view matrix, the oblique projection and the reflection texture, then put back.
            var data = cam.GetUniversalAdditionalCameraData();
            bool oldShadows = data.renderShadows, oldPost = data.renderPostProcessing;
            int oldMask = cam.cullingMask; var oldTarget = cam.targetTexture;
            bool oldHdr = cam.allowHDR, oldMsaa = cam.allowMSAA, oldOcc = cam.useOcclusionCulling;
            var oldClear = cam.clearFlags; var oldBg = cam.backgroundColor;

            data.renderShadows = shadows; data.renderPostProcessing = false;
            cam.cullingMask = reflectLayers; cam.allowHDR = enableHdr; cam.allowMSAA = enableMSAA; cam.useOcclusionCulling = occlusion;
            if (addBlackColour) { cam.clearFlags = CameraClearFlags.Color; cam.backgroundColor = Color.black; }
            cam.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
            cam.projectionMatrix = projection;
            cam.targetTexture = reflTexture;

            var oldInvertCulling = GL.invertCulling;
            GL.invertCulling = true;
            cam.Render();
            GL.invertCulling = oldInvertCulling;

            cam.ResetWorldToCameraMatrix(); cam.ResetProjectionMatrix();
            cam.targetTexture = oldTarget; cam.cullingMask = oldMask;
            cam.allowHDR = oldHdr; cam.allowMSAA = oldMsaa; cam.useOcclusionCulling = oldOcc;
            cam.clearFlags = oldClear; cam.backgroundColor = oldBg;
            data.renderShadows = oldShadows; data.renderPostProcessing = oldPost;

            Shader.SetGlobalTexture(shaderPropertyName, reflTexture);
            RenderSettings.fog = fog;
        }

        void CreateMirrorTexture(Camera currentCamera)
        {
            var size = ReflectionResolution(currentCamera, UniversalRenderPipeline.asset.renderScale);
            if (reflTexture && enableHdr == currentHdr && currentWidth == size.x) return;
            if (reflTexture) RenderTexture.ReleaseTemporary(reflTexture);
            var desc = new RenderTextureDescriptor(size.x, size.y, enableHdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, 24);
            desc.msaaSamples = enableMSAA && QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 1;
            reflTexture = RenderTexture.GetTemporary(desc);
            currentWidth = size.x; currentHdr = enableHdr;
        }

        static float SignCheck(float a) => a > 0f ? 1f : (a < 0f ? -1f : 0f);

        // was MakeProjectionMatrixObliqueJob
        static Matrix4x4 MakeProjectionMatrixOblique(Matrix4x4 matrix, Vector4 clipPlane)
        {
            Vector4 q;
            q.x = (SignCheck(clipPlane.x) + matrix[8]) / matrix[0];
            q.y = (SignCheck(clipPlane.y) + matrix[9]) / matrix[5];
            q.z = -1.0f;
            q.w = (1.05f + matrix[10]) / matrix[14];
            Vector4 c = clipPlane * (2.0f / Vector4.Dot(clipPlane, q));
            matrix[2] = c.x; matrix[6] = c.y; matrix[10] = c.z + 1.0f; matrix[14] = c.w;
            return matrix;
        }

        // was CameraSpacePlaneJob
        static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Matrix4x4 reflection, Vector3 offsetPos, Vector3 normal, float sideSign)
        {
            var m = worldToCamera * reflection;
            Vector3 cameraPosition = m.MultiplyPoint(offsetPos);
            Vector3 cameraNormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
        }

        // was CalculateReflectionMatrixJob
        static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
        {
            var r = Matrix4x4.identity;
            r.m00 = 1f - 2f * plane[0] * plane[0]; r.m01 = -2f * plane[0] * plane[1]; r.m02 = -2f * plane[0] * plane[2]; r.m03 = -2f * plane[3] * plane[0];
            r.m10 = -2f * plane[1] * plane[0]; r.m11 = 1f - 2f * plane[1] * plane[1]; r.m12 = -2f * plane[1] * plane[2]; r.m13 = -2f * plane[3] * plane[1];
            r.m20 = -2f * plane[2] * plane[0]; r.m21 = -2f * plane[2] * plane[1]; r.m22 = 1f - 2f * plane[2] * plane[2]; r.m23 = -2f * plane[3] * plane[2];
            r.m30 = 0f; r.m31 = 0f; r.m32 = 0f; r.m33 = 1f;
            return r;
        }
    }
}

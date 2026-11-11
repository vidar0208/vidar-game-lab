// Poses the IA model (imported in T-pose, no animation clips): arms down plus a small idle motion.
// All rotations are applied as world-space deltas on top of the bind pose, so bone-local axes do not matter.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech
{
    public class IARig : MonoBehaviour
    {
        public float armDown = 72f;
        public bool idle = true;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();

        public Transform Bone(string name) => bones.TryGetValue(name, out var t) ? t : null;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                bind[t] = t.localRotation;
            }
        }

        public void ResetPose() { foreach (var kv in bind) kv.Key.localRotation = kv.Value; }

        // rotate a bone by deg around an axis given in the character's own frame (x right, y up, z forward)
        public void Turn(string bone, float deg, Vector3 axis)
        {
            var b = Bone(bone); if (b == null) return;
            b.rotation = Quaternion.AngleAxis(deg, transform.TransformDirection(axis)) * b.rotation;
        }

        public void ArmsDown(float swingL = 0, float swingR = 0)
        {
            Turn("Upperarm_L", armDown, Vector3.forward); Turn("Upperarm_R", -armDown, Vector3.forward);
            Turn("Upperarm_L", swingL, Vector3.right); Turn("Upperarm_R", swingR, Vector3.right);
            Turn("Lowerarm_L", -12f, Vector3.right); Turn("Lowerarm_R", -12f, Vector3.right);
        }

        void LateUpdate()
        {
            if (!idle) return;
            float t = FrameCapturer.T;
            ResetPose();
            ArmsDown(Mathf.Sin(t * 0.9f) * 1.5f, Mathf.Sin(t * 0.9f + 1f) * 1.5f);
            Turn("Spine", Mathf.Sin(t * 0.7f) * 1.2f, Vector3.forward);
            Turn("Chest", Mathf.Sin(t * 1.6f) * 1.0f, Vector3.right);
            Turn("Head", Mathf.Sin(t * 0.55f + 0.4f) * 7f, Vector3.up);
            Turn("Head", Mathf.Sin(t * 0.8f) * 2f, Vector3.right);
        }
    }
}

// Poses the scene character: arms down from the bind pose plus a small idle motion.
// Bones are found by Unity Humanoid mapping when the model has a human Avatar, otherwise by transform name
// (the stand-in mannequin uses the names below). All rotations are world-space deltas on top of the bind pose,
// so bone-local axes do not matter. armDown assumes a T-pose bind pose; lower it for an A-pose model.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech
{
    public class CharacterRig : MonoBehaviour
    {
        public float armDown = 72f;
        public bool idle = true;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();

        static readonly Dictionary<string, HumanBodyBones> Human = new Dictionary<string, HumanBodyBones>
        {
            { "Hips", HumanBodyBones.Hips }, { "Spine", HumanBodyBones.Spine }, { "Chest", HumanBodyBones.Chest },
            { "Neck", HumanBodyBones.Neck }, { "Head", HumanBodyBones.Head },
            { "Upperarm_L", HumanBodyBones.LeftUpperArm }, { "Lowerarm_L", HumanBodyBones.LeftLowerArm }, { "Hand_L", HumanBodyBones.LeftHand },
            { "Upperarm_R", HumanBodyBones.RightUpperArm }, { "Lowerarm_R", HumanBodyBones.RightLowerArm }, { "Hand_R", HumanBodyBones.RightHand },
            { "Upperleg_L", HumanBodyBones.LeftUpperLeg }, { "Lowerleg_L", HumanBodyBones.LeftLowerLeg }, { "Foot_L", HumanBodyBones.LeftFoot },
            { "Upperleg_R", HumanBodyBones.RightUpperLeg }, { "Lowerleg_R", HumanBodyBones.RightLowerLeg }, { "Foot_R", HumanBodyBones.RightFoot },
        };

        public Transform Bone(string name) => bones.TryGetValue(name, out var t) ? t : null;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                bind[t] = t.localRotation;
            }
            var anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
                foreach (var kv in Human) { var b = anim.GetBoneTransform(kv.Value); if (b != null) bones[kv.Key] = b; }
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

// Poses the scene character: arms down from the bind pose plus a small idle motion.
// Bones are found by Unity Humanoid mapping when the model has a human Avatar, otherwise by transform name
// (the stand-in mannequin uses the names below). All rotations are world-space deltas on top of the bind pose,
// so bone-local axes do not matter. armDown is measured from the bind pose (T-pose or A-pose) unless autoArmDown is off.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech
{
    public class CharacterRig : MonoBehaviour
    {
        public float armDown = 72f;
        public bool autoArmDown = true;   // set armDown from the bind pose so the arms end about 18 degrees out from vertical
        public bool idle = true;
        public bool blink = true;          // drives a blend shape named "Blink" (0 open, 100 closed) if the model has one
        SkinnedMeshRenderer blinkMesh; int blinkShape = -1; float nextBlink = 1.5f; bool lidsOnly;
        readonly System.Random blinkRng = new System.Random(7);   // fixed seed: the same blinks on every render
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
                if (t != transform) bind[t] = t.localRotation;   // never reset the root: techniques move and turn it
            }
            var anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
                foreach (var kv in Human) { var b = anim.GetBoneTransform(kv.Value); if (b != null) bones[kv.Key] = b; }
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex("Blink") >= 0) { blinkMesh = smr; blinkShape = smr.sharedMesh.GetBlendShapeIndex("Blink"); lidsOnly = smr.name.ToLowerInvariant().Contains("lid"); break; }
            var up = Bone("Upperarm_L"); var low = Bone("Lowerarm_L");
            if (autoArmDown && up != null && low != null)
                armDown = Vector3.Angle(low.position - up.position, -transform.up) - 18f;   // T-pose: 72, A-pose 8 degrees out: -10
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

        // blink now (the eyes close for 0.1 s); otherwise blinks come every 2.5 to 6 s
        public void Blink() { nextBlink = FrameCapturer.T; }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            if (blinkShape >= 0)
            {
                if (t >= nextBlink + 0.1f) nextBlink = t + 2.5f + (float)blinkRng.NextDouble() * 3.5f;
                // on/off, no fade: the lids are hidden inside the head when open
                bool shut = blink && t >= nextBlink && t < nextBlink + 0.1f;
                blinkMesh.SetBlendShapeWeight(blinkShape, shut ? 100f : 0f);
                // a separate eyelid mesh is hidden while the eyes are open, so outlines or shadows of the hidden lids never show
                if (lidsOnly) blinkMesh.enabled = shut;
            }
            if (!idle) return;
            ResetPose();
            ArmsDown(Mathf.Sin(t * 0.9f) * 1.5f, Mathf.Sin(t * 0.9f + 1f) * 1.5f);
            Turn("Spine", Mathf.Sin(t * 0.7f) * 1.2f, Vector3.forward);
            Turn("Chest", Mathf.Sin(t * 1.6f) * 1.0f, Vector3.right);
            Turn("Head", Mathf.Sin(t * 0.55f + 0.4f) * 7f, Vector3.up);
            Turn("Head", Mathf.Sin(t * 0.8f) * 2f, Vector3.right);
        }
    }
}

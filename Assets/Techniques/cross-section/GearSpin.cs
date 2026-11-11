using UnityEngine;

namespace GameTech.Posts
{
    public class GearSpin : MonoBehaviour
    {
        public float degPerSec = 40;
        Quaternion baseRot;
        void Awake() { baseRot = transform.localRotation; }
        void Update() { transform.localRotation = baseRot * Quaternion.AngleAxis(degPerSec * FrameCapturer.T, Vector3.up); }
    }
}

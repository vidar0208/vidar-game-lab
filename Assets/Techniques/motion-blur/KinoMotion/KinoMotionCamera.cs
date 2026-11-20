// Per-camera settings for the URP port of Kino Motion (the source's Motion component: shutter angle, sample count,
// frame blending), plus that camera's frame-blending history (4 frames), so split-screen cameras do not share it.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.KinoMotionURP
{
    [RequireComponent(typeof(Camera))]
    public class KinoMotionCamera : MonoBehaviour
    {
        [Range(0, 360)] public float shutterAngle = 270;   // source default 270
        public int sampleCount = 8;                         // source default 8
        [Range(0, 1)] public float frameBlending = 0;       // source default 0

        // Frames to skip the blur after a camera cut, when the previous-frame matrix belongs to another shot.
        [System.NonSerialized] public int suppressFrames;

        internal readonly RTHandle[] history = new RTHandle[4];
        internal readonly float[] historyTime = new float[4];
        internal readonly bool[] historyValid = new bool[4];
        internal int lastPushFrame = -1;

        // Frame.CalculateWeight of the source
        internal float Weight(int slot, float strength, float now)
        {
            if (!historyValid[slot]) return 0;
            var coeff = Mathf.Lerp(80.0f, 16.0f, strength);
            return Mathf.Exp((historyTime[slot] - now) * coeff);
        }

        public void ClearHistory() { for (int i = 0; i < 4; i++) historyValid[i] = false; }

        void OnDestroy()
        {
            for (int i = 0; i < 4; i++) { history[i]?.Release(); history[i] = null; }
        }
    }
}

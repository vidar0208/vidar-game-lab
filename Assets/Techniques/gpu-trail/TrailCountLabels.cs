// Runs after the trail renderers so the count labels show the frame being rendered, not the one before.
using UnityEngine;

namespace GameTech.GpuTrails
{
    [DefaultExecutionOrder(100)]
    public class TrailCountLabels : MonoBehaviour
    {
        public TrailDirector director;
        void LateUpdate() { if (director != null) director.AfterTrails(); }
    }
}

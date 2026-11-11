// On-frame text label ("OFF" / "ON: <technique>"). Drawn by FrameCapturer after all cameras.
// anchor is in 0..1 of the full frame, measured from the bottom-left corner.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech
{
    public class LabelSpec : MonoBehaviour
    {
        public static readonly List<LabelSpec> all = new List<LabelSpec>();
        public string text = "";
        public Vector2 anchor = new Vector2(0.03f, 0.05f);
        public int size = 40;              // pixel height at 1080p
        public Color color = Color.white;
        public bool backing = true;
        public bool alignRight = false;
        public float tStart = 0, tEnd = 1e9f;   // visible while tStart <= FrameCapturer.T < tEnd
        void OnEnable() { all.Add(this); }
        void OnDisable() { all.Remove(this); }

        public static LabelSpec Create(string text, float x, float y, int size = 40, bool alignRight = false)
        {
            var go = new GameObject("Label: " + text);
            var l = go.AddComponent<LabelSpec>();
            l.text = text; l.anchor = new Vector2(x, y); l.size = size; l.alignRight = alignRight;
            return l;
        }
        public LabelSpec During(float a, float b) { tStart = a; tEnd = b; return this; }
    }
}

// Drives the motion blur post: animates the props (fan, turntable, pendulum light, bouncing ball, waving mannequin),
// moves the cameras and sets each camera's Kino Motion settings. Blur lengths in the labels are measured here by
// projecting points of the moving props with the camera, one frame apart.
using System.Collections.Generic;
using GameTech.KinoMotionURP;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class MotionDirector : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        public Camera camA, camB, camC;
        public Transform fanRotor, fanTip, turntable, pendulum, ball, character;
        public LabelSpec title, l1, l2, m1, m2, r1, r2;

        RenderPipelineAsset previous;
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        int phase = -1;
        Vector3 prevTip, prevHand; bool havePrev;
        float logEvery = -1;

        void Awake()
        {
            previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                bones[t.name] = t;
                if (t != character) bind[t] = t.localRotation;
            }
        }

        void OnDestroy() { QualitySettings.renderPipeline = previous; }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        void Animate(float t)
        {
            fanRotor.localRotation = Quaternion.Euler(0, 0, -t * 2.6f * 360f);                // 2.6 rev/s
            turntable.localRotation = Quaternion.Euler(0, t * 0.6f * 360f, 0);              // 0.6 rev/s
            pendulum.localRotation = Quaternion.Euler(0, 0, 50f * Mathf.Sin(t * 2f * Mathf.PI / 2.2f));   // 50 deg, 2.2 s period
            ball.localPosition = new Vector3(0, 0.22f + 1.3f * Mathf.Abs(Mathf.Sin(t * Mathf.PI / 0.85f)), 0);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            Turn("Upperarm_L", 77f, Vector3.forward);
            Turn("Lowerarm_L", -10f, Vector3.right);
            Turn("Upperarm_R", 55f, Vector3.forward);                                        // right arm raised
            Turn("Lowerarm_R", 40f + 38f * Mathf.Sin(t * 2.5f * 2f * Mathf.PI), Vector3.forward);   // waving, 2.5 Hz
            Turn("Chest", 3f * Mathf.Sin(t * 2.5f * 2f * Mathf.PI + 1f), Vector3.forward);
            Turn("Head", 6f * Mathf.Sin(t * 0.9f), Vector3.up);
            Turn("Upperleg_L", 3f, Vector3.forward); Turn("Upperleg_R", -3f, Vector3.forward);
        }

        static void Pose(Camera c, float yaw, float dist, float height, Vector3 target)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            c.transform.position = target + dir * dist + Vector3.up * (height - target.y);
            c.transform.LookAt(target);
        }

        // pan: camera stays in place and turns (yaw added on top of looking at the target)
        static void Pan(Camera c, Vector3 pos, Vector3 target, float yaw)
        {
            c.transform.position = pos;
            c.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.LookRotation(target - pos);
        }

        static void Set(Camera c, float shutter, int samples, float blend)
        {
            var k = c.GetComponent<KinoMotionCamera>();
            k.shutterAngle = shutter; k.sampleCount = samples; k.frameBlending = blend;
        }

        void Cut()
        {
            foreach (var c in new[] { camA, camB, camC }) { var k = c.GetComponent<KinoMotionCamera>(); k.suppressFrames = 2; k.ClearHistory(); }
        }

        void Show(Camera c, bool on, Rect r, float fov) { c.enabled = on; if (on) { c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f); } }

        void Labels(string t, string a1, string a2, string b1 = "", string b2 = "", string c1 = "", string c2 = "")
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; r1.text = c1; r2.text = c2;
        }

        // whip pan: fast swing that settles, u in 0..1
        static float Whip(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Animate(t);
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var target = new Vector3(0f, 1.15f, 3.4f);
            int p = t < 3.5f ? 0 : t < 11f ? 1 : t < 16.5f ? 2 : t < 22f ? 3 : 4;
            if (p != phase) { Cut(); }
            string camState = "";
            if (p == 0)
            {
                Show(camA, true, full, 40f); Show(camB, false, full, 0); Show(camC, false, full, 0);
                Set(camA, 180, 32, 0);
                Pose(camA, Mathf.Lerp(-8f, -2f, t / 3.5f), Mathf.Lerp(7.4f, 6.6f, t / 3.5f), 1.5f, target);
                Labels("Motion blur from motion vectors", "", "");
                l1.text = "180 degree shutter, 32 samples: moving things smear, the still room stays sharp";
            }
            else if (p == 1)
            {
                Show(camA, true, h0, 52f); Show(camB, true, h1, 52f); Show(camC, false, full, 0);
                Set(camA, 0, 32, 0); Set(camB, 180, 32, 0);
                float yaw = t < 7f ? 0f : Mathf.Lerp(0f, 32f, Whip((t - 7f) / 0.6f)) - Mathf.Lerp(0f, 32f, Whip((t - 8.6f) / 0.7f));
                camState = t < 7f ? "camera still" : (t < 9.4f ? "whip pan" : "camera still");
                foreach (var c in new[] { camA, camB }) Pan(c, new Vector3(-0.4f, 1.45f, -2.2f), target + new Vector3(-0.4f, 0, 0), yaw);
                Labels("Motion blur off vs on (" + camState + ")", "Off", "every frame is a sharp instant",
                    "On: 180 degree shutter", "blur along each pixel's motion vector");
            }
            else if (p == 2)
            {
                Show(camA, true, h0, 50f); Show(camB, true, h1, 50f); Show(camC, false, full, 0);
                Set(camA, 180, 32, 0); Set(camB, 180, 32, 1f);
                var tg = new Vector3(-1.6f, 1.25f, 3.6f);
                foreach (var c in new[] { camA, camB }) Pose(c, Mathf.Lerp(-12f, -4f, (t - 11f) / 5.5f), 3.6f, 1.45f, tg);
                Labels("Adding frame blending", "Reconstruction filter only", "one frame, blurred along its vectors",
                    "Reconstruction + frame blending", "plus the last 4 frames at weights 0.59 / 0.34 / 0.20 / 0.12");
            }
            else if (p == 3)
            {
                float w = 0.3313f;
                Show(camA, true, new Rect(0, 0, w, 1), 50f); Show(camB, true, new Rect(w + 0.003f, 0, w, 1), 50f); Show(camC, true, new Rect(2 * w + 0.006f, 0, w, 1), 50f);
                Set(camA, 90, 32, 0); Set(camB, 180, 32, 0); Set(camC, 360, 32, 0);
                var tg = new Vector3(-1.75f, 1.35f, 3.6f);
                foreach (var c in new[] { camA, camB, camC }) Pose(c, Mathf.Lerp(-6f, 4f, (t - 16.5f) / 5.5f), 3.3f, 1.4f, tg);
                Labels("Shutter angle: how much of the frame time the shutter is open", "90 degrees", "1/4 of the motion per frame",
                    "180 degrees", "1/2", "360 degrees", "the whole frame");
            }
            else
            {
                Show(camA, true, full, 40f); Show(camB, false, full, 0); Show(camC, false, full, 0);
                Set(camA, 180, 32, 0.5f);
                float yaw = Mathf.Lerp(-24f, 6f, Whip((t - 22.5f) / 0.9f));
                Pan(camA, new Vector3(0.3f, 1.4f, -2.4f + 0.3f * (t - 22f)), target, yaw);
                Labels("Motion blur on", "", "");
                l1.text = "Unity 6.3 URP, Kino Motion ported: 180 degree shutter, 32 samples, frame blending 0.5";
            }
            if (p != phase)
            {
                phase = p; havePrev = false;
                Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}' | '{r1.text}' '{r2.text}'");
            }
            // anchors
            l1.anchor = new Vector2(0.012f, p == 0 || p == 4 ? 0.03f : 0.058f); l2.anchor = new Vector2(0.012f, 0.018f);
            float mx = p == 3 ? 0.346f : 0.512f; m1.anchor = new Vector2(mx, 0.058f); m2.anchor = new Vector2(mx, 0.018f);
            r1.anchor = new Vector2(0.68f, 0.058f); r2.anchor = new Vector2(0.68f, 0.018f);

            // measure: screen speed of the fan tip and the waving hand, in pixels per frame at 1080p
            var cam = camA; var hand = bones.TryGetValue("Hand_R", out var hb) ? hb.position : Vector3.zero;
            if (havePrev && t >= logEvery)
            {
                logEvery = t + 1f;
                var px = new Vector2(1920f * cam.rect.width, 1080f * cam.rect.height);
                float tipPx = Vector2.Scale((Vector2)(cam.WorldToViewportPoint(fanTip.position) - cam.WorldToViewportPoint(prevTip)), px).magnitude;
                float handPx = Vector2.Scale((Vector2)(cam.WorldToViewportPoint(hand) - cam.WorldToViewportPoint(prevHand)), px).magnitude;
                Debug.Log($"[gt] t {t:F2} phase {p} camA px/frame (object motion only, 1080p): fan tip {tipPx:F1}, hand {handPx:F1}; max blur radius {KinoMotionPass.LastMaxBlurPixels} px, tile {KinoMotionPass.LastTileSize}; cam h {cam.pixelHeight}; blend weights {KinoMotionPass.LastWeights[0]:F3} {KinoMotionPass.LastWeights[1]:F3} {KinoMotionPass.LastWeights[2]:F3} {KinoMotionPass.LastWeights[3]:F3}");
            }
            prevTip = fanTip.position; prevHand = hand; havePrev = true;
        }
    }
}

// Unattended frame capture for the game-tech post pipeline.
// Active only when the editor was started with -gtCapture. Renders every enabled
// screen camera into one RenderTexture at a fixed timestep and writes numbered frames.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameTech
{
    public class FrameCapturer : MonoBehaviour
    {
        public int width = 1920, height = 1080, fps = 30, frames = 360, warmup = 20;
        public string outDir = "";
        public bool png = false;

        RenderTexture rt;
        Texture2D tex;
        int frame;
        static FrameCapturer inst;
        // Timeline clock for post scripts: seconds since the first captured frame (0 during warm-up). Falls back to Time.time outside capture.
        public static float T => inst != null ? Mathf.Max(0, inst.frame) / (float)inst.fps : Time.time;
        readonly List<Camera> cams = new List<Camera>();

        public static string Arg(string name, string fallback = null)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }
        public static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Flag("-gtCapture")) return;
            var go = new GameObject("~FrameCapturer");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameCapturer>();   // Awake runs inside AddComponent, so it reads the arguments itself
        }

        void Awake()
        {
            outDir = Arg("-gtOut", "frames");
            width = int.Parse(Arg("-gtW", "1920"));
            height = int.Parse(Arg("-gtH", "1080"));
            fps = int.Parse(Arg("-gtFps", "30"));
            frames = int.Parse(Arg("-gtFrames", "360"));
            warmup = int.Parse(Arg("-gtWarmup", "20"));
            png = Flag("-gtPng");
            Time.captureFramerate = fps;            // fixed dt = 1/fps, independent of render speed
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            frame = -warmup;
            inst = this;
            Directory.CreateDirectory(outDir);
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 1;
            rt.Create();
            tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            Debug.Log($"[gt] capture start {width}x{height} {fps} fps, {frames} frames, warmup {warmup}, out {outDir}");
        }

        void LateUpdate()
        {
            try { Step(); }
            catch (Exception e) { Debug.LogError("[gt] capture failed: " + e); Finish(4); }
        }

        void Step()
        {
            if (frame >= 0)
            {
                RenderAll();
                DrawLabels();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                RenderTexture.active = prev;
                byte[] bytes = png ? tex.EncodeToPNG() : tex.EncodeToJPG(96);
                File.WriteAllBytes(Path.Combine(outDir, $"f_{frame:0000}." + (png ? "png" : "jpg")), bytes);
                if (frame % 30 == 0) Debug.Log($"[gt] frame {frame}/{frames}");
            }
            frame++;
            if (frame >= frames) Finish(0);
        }

        void RenderAll()
        {
            cams.Clear();
            foreach (var c in Camera.allCameras)
                if (c.isActiveAndEnabled && c.targetTexture == null) cams.Add(c);
            cams.Sort((a, b) => a.depth.CompareTo(b.depth));
            if (cams.Count == 0) { Debug.LogError("[gt] no enabled camera"); Finish(3); return; }
            foreach (var c in cams)
            {
                c.targetTexture = rt;   // camera.rect is respected, so split-screen layouts work
                c.Render();
                c.targetTexture = null;
            }
        }

        static Font font;
        static Material solid;

        // Labels are drawn straight onto the captured frame after all cameras, so post effects never touch them.
        void DrawLabels()
        {
            if (LabelSpec.all.Count == 0) return;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (solid == null)
            {
                solid = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                solid.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                solid.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                solid.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                solid.SetInt("_ZWrite", 0);
                solid.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, 0, height);
            foreach (var l in LabelSpec.all)
            {
                if (string.IsNullOrEmpty(l.text) || T < l.tStart || T >= l.tEnd) continue;
                int px = Mathf.Max(8, Mathf.RoundToInt(l.size * height / 1080f));
                font.RequestCharactersInTexture(l.text, px, FontStyle.Bold);
                float w = 0;
                foreach (char ch in l.text) { font.GetCharacterInfo(ch, out var ci, px, FontStyle.Bold); w += ci.advance; }
                float x = l.anchor.x * width, y = l.anchor.y * height, pad = px * 0.4f;
                if (l.alignRight) x -= w;
                if (l.backing)
                {
                    solid.SetPass(0);
                    GL.Begin(GL.QUADS);
                    GL.Color(new Color(0, 0, 0, 0.55f));
                    GL.Vertex3(x - pad, y - pad, 0); GL.Vertex3(x + w + pad, y - pad, 0);
                    GL.Vertex3(x + w + pad, y + px + pad * 0.5f, 0); GL.Vertex3(x - pad, y + px + pad * 0.5f, 0);
                    GL.End();
                }
                font.material.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(l.color);
                float cx = x, baseY = y + px * 0.22f;
                foreach (char ch in l.text)
                {
                    font.GetCharacterInfo(ch, out var ci, px, FontStyle.Bold);
                    GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(cx + ci.minX, baseY + ci.minY, 0);
                    GL.TexCoord(ci.uvBottomRight); GL.Vertex3(cx + ci.maxX, baseY + ci.minY, 0);
                    GL.TexCoord(ci.uvTopRight); GL.Vertex3(cx + ci.maxX, baseY + ci.maxY, 0);
                    GL.TexCoord(ci.uvTopLeft); GL.Vertex3(cx + ci.minX, baseY + ci.maxY, 0);
                    cx += ci.advance;
                }
                GL.End();
            }
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        void Finish(int code)
        {
            Debug.Log($"[gt] capture done, exit {code}");
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}

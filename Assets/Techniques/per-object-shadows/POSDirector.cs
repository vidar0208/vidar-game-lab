// Drives the per-object shadow post. Four moving objects (stand-in mannequin, bouncing ball, hovering drone,
// swinging lantern) in a courtyard of static props. Each frame the scene camera renders twice:
//  A: URP main-light shadows for everything (the movers cast into URP's cascaded shadow map too);
//  B: the movers do not cast URP shadows; they get the per-object atlas instead (POSChain), blended over the frame.
// The static props cast URP shadows in both.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class POSDirector : MonoBehaviour
    {
        public Camera src;
        public Light sun;
        public Material compose, resolveColour, resolveMask, atlasViewMat;
        public Transform character, ball, drone, lantern;
        public string[] moverNames = { "Character", "Ball", "Drone", "Lantern" };
        public LabelSpec title;
        public LabelSpec[] l1, l2, slice;
        public float shadowAlpha = 0.75f;

        RenderTexture rtA, rtB, rtOut, atlasView;
        POSChain chain;
        Renderer[] all, movers;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        int W, H, logged = -1, loggedSec = -1;

        static readonly Color[] SliceCol = { new Color(1f, 0.55f, 0.15f), new Color(0.2f, 0.8f, 1f), new Color(0.4f, 0.95f, 0.4f), new Color(1f, 0.35f, 0.8f) };

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rtA.Create();
            rtB = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rtB.Create();
            rtOut = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rtOut.Create();
            atlasView = new RenderTexture(2048, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); atlasView.Create();
            src.enabled = false;

            var s = new PerObjectShadowSettings
            {
                maxObjects = 4,
                sliceMaxResolution = PerObjectShadowSettings.SliceResolution._512,
                sliceMinResolution = PerObjectShadowSettings.SliceResolution._128,
                objectToDraw = PerObjectShadowSettings.ObjectToDraw.Manually,
                updateMethod = PerObjectShadowSettings.UpdateMethod.Manually,
                lightDirectionFrom = PerObjectShadowSettings.LightDirectionFrom.EulerAngles,
                eulerAngles = sun.transform.eulerAngles,
            };
            chain = new POSChain(s, W, H);
            foreach (var t in new[] { character, ball, drone, lantern }) chain.impl.RegisterGameObject(t.gameObject);

            var list = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID)) if (r.gameObject.layer != 8) list.Add(r);
            all = list.ToArray();
            var mv = new List<Renderer>();
            foreach (var t in new[] { character, ball, drone, lantern }) mv.AddRange(t.GetComponentsInChildren<Renderer>());
            movers = mv.ToArray();
            foreach (var r in movers) if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;

            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                if (t != character) bind[t] = t.localRotation;
            }

            // ambient light: a scene built from code has no baked ambient probe, so URP's shadowed areas came out almost black;
            // set a flat sky ambient so shadowed ground keeps some light
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(0.45f, 0.5f, 0.62f));
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
            resolveColour.SetColor("_ShadowColor", new Color(0, 0, 0, shadowAlpha));
            resolveMask.SetColor("_ShadowColor", new Color(0, 0, 0, 1));
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Debug.Log($"[gt] URP shadows: res {rp.mainLightShadowmapResolution}, cascades {rp.shadowCascadeCount}, distance {rp.shadowDistance}, split4 {rp.cascade4Split}, soft {rp.supportsSoftShadows}");
            Debug.Log($"[gt] depth pass index {POSChain.DepthPass(movers[0].sharedMaterial)}, renderers {all.Length}, movers {movers.Length}, light euler {s.eulerAngles}");
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // a simple walk on the stand-in mannequin (T-pose bind): arms down, leg and arm swing, knee bend
        void Walk(float t)
        {
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = t * Mathf.PI * 2f * 0.95f, s = Mathf.Sin(ph);
            Turn("Spine", 3f, Vector3.right);
            Turn("Upperleg_L", -28f * s, Vector3.right); Turn("Upperleg_R", 28f * s, Vector3.right);
            Turn("Lowerleg_L", 5f + 35f * Mathf.Max(0, Mathf.Sin(ph + 1.6f)), Vector3.right);
            Turn("Lowerleg_R", 5f + 35f * Mathf.Max(0, Mathf.Sin(ph + 1.6f + Mathf.PI)), Vector3.right);
            Turn("Upperarm_L", 78f, Vector3.forward); Turn("Upperarm_R", -78f, Vector3.forward);
            Turn("Upperarm_L", 22f * s, Vector3.right); Turn("Upperarm_R", -22f * s, Vector3.right);
            Turn("Lowerarm_L", -15f, Vector3.right); Turn("Lowerarm_R", -15f, Vector3.right);
        }

        Vector3 CharPos(float t)
        {
            float a = 0.579f * t + 2.2f;
            return new Vector3(2.0f * Mathf.Cos(a), 0, 0.5f + 1.4f * Mathf.Sin(a));
        }

        void Animate(float t)
        {
            var p = CharPos(t); var q = CharPos(t + 0.05f);
            character.position = p + new Vector3(0, 0.025f * Mathf.Abs(Mathf.Cos(t * Mathf.PI * 2f * 0.95f)), 0);
            character.rotation = Quaternion.LookRotation(q - p, Vector3.up);
            Walk(t);
            ball.position = new Vector3(-3.0f + 0.7f * Mathf.Sin(0.45f * t), 0.25f + 1.2f * Mathf.Abs(Mathf.Sin(Mathf.PI * t / 1.1f)), -1.2f);
            drone.position = new Vector3(2.9f + 1.0f * Mathf.Sin(0.6f * t), 1.55f + 0.15f * Mathf.Sin(1.7f * t), -1.0f + 0.8f * Mathf.Sin(1.2f * t));
            drone.rotation = Quaternion.Euler(8f * Mathf.Sin(1.2f * t), 25f * t, 6f * Mathf.Cos(0.6f * t));
            for (int i = 0; i < drone.childCount; i++) { var c = drone.GetChild(i); if (c.name.StartsWith("Rotor")) c.localRotation = Quaternion.Euler(0, t * 1440f + i * 37f, 0); }
            lantern.localRotation = Quaternion.Euler(28f * Mathf.Sin(t * Mathf.PI * 2f / 2.3f), 0, 6f * Mathf.Sin(t * 1.1f));
        }

        // camera keys: time, radius, height, look height, look blend (0 = follow the mannequin, 1 = courtyard centre)
        static readonly float[,] Keys = {
            { 0f, 7.5f, 3.6f, 0.6f, 0.6f }, { 3f, 7.0f, 3.3f, 0.6f, 0.5f }, { 8f, 2.3f, 0.8f, 0.25f, 0f }, { 12f, 2.3f, 0.8f, 0.25f, 0f },
            { 14f, 2.6f, 0.9f, 0.3f, 0f }, { 17.5f, 11f, 6.5f, 0.5f, 1f }, { 19f, 11f, 6.5f, 0.5f, 1f }, { 20.5f, 5f, 2.4f, 0.5f, 0f }, { 26f, 6f, 3f, 0.6f, 0.2f } };

        void CameraMove(float t)
        {
            int k = 0; while (k < Keys.GetLength(0) - 2 && t > Keys[k + 1, 0]) k++;
            float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - Keys[k, 0]) / (Keys[k + 1, 0] - Keys[k, 0])));
            float r = Mathf.Lerp(Keys[k, 1], Keys[k + 1, 1], u), h = Mathf.Lerp(Keys[k, 2], Keys[k + 1, 2], u);
            float ly = Mathf.Lerp(Keys[k, 3], Keys[k + 1, 3], u), lb = Mathf.Lerp(Keys[k, 4], Keys[k + 1, 4], u);
            var focus = Vector3.Lerp(character.position, new Vector3(0, 0, 0.3f), lb);
            float az = Mathf.Atan2(0.5f, 0.87f) - 0.35f + 0.02f * t;
            var off = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az)) * r;
            src.transform.position = focus + off + Vector3.up * h;
            src.transform.LookAt(focus + Vector3.up * ly);
        }

        void SetCast(bool on) { foreach (var r in movers) r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off; }

        string SliceText(int i, out Vector4 uvRect)
        {
            var s = chain.impl.SliceDataList[i]; var d = s.sliceDataPerFrame;
            uvRect = d.sliceUVOffsetExtend;
            if (!s.ShouldRender) return moverNames[i] + ": culled";
            float texel = (d.boundsSVS.size.x + 2f * chain.settings.padding) / d.sliceResolution.x * 100f;
            return $"{moverNames[i]} {d.sliceResolution.x} x {d.sliceResolution.y}, {texel:F2} cm/texel";
        }

        void Labels(string ttl, float s1, string[] a, string[] b)
        {
            title.text = ttl;
            float[] start = { 0f, s1 };
            for (int i = 0; i < 2; i++)
            {
                l1[i].text = a != null && i < a.Length ? a[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = b != null && i < b.Length ? b[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
            compose.SetFloat("_S1", s1);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Animate(t); CameraMove(t);

            SetCast(true); src.targetTexture = rtA; src.Render();
            SetCast(false); src.targetTexture = rtB; src.Render();
            chain.Atlas(src);
            chain.SceneDepth(src, all);
            Graphics.Blit(rtB, rtOut);
            chain.Resolve(src, resolveColour, rtOut, false);
            chain.Resolve(src, resolveMask, chain.mask, true);

            var rects = new Vector4[4]; var cols = new Vector4[4]; var txt = new string[4]; int n = 0;
            for (int i = 0; i < 4; i++) { txt[i] = SliceText(i, out var r); if (chain.impl.SliceDataList[i].ShouldRender) { rects[n] = r; cols[n] = SliceCol[i]; n++; } }
            atlasViewMat.SetVectorArray("_Slices", rects); atlasViewMat.SetVectorArray("_SliceColours", cols);
            atlasViewMat.SetFloat("_SliceCount", n); atlasViewMat.SetFloat("_Invert", 1); // reversed Z: show the stored depth, 1 = nearest the light
            var ar = chain.impl.AtlasResolution;
            atlasViewMat.SetVector("_AtlasSize", new Vector4(ar.x, ar.y, 0, 0));
            atlasViewMat.SetTexture(PerObjectShadowImpl.s_PerObjectShadowAtlas, chain.atlas);
            Graphics.Blit(Texture2D.blackTexture, atlasView, atlasViewMat);

            int phase; bool atlasOn = false;
            string urpA = "2048 map, 4 cascades, 50 m, soft shadows high";
            string atl = $"atlas {ar.x} x {ar.y}, {n} slices of 128-512 px, shadow black at {shadowAlpha * 100f:F0}%";
            compose.SetFloat("_Gray0", 0); compose.SetFloat("_Crop", 0);
            compose.SetTexture("_B0", rtOut); compose.SetTexture("_B1", rtOut);
            if (t < 3f)
            {
                phase = 0;
                Labels("Per-object shadows: each moving object gets its own slice of one shadow atlas", 1f,
                    new[] { "4 movers in the atlas, static props in URP's shadow map" }, new[] { atl });
            }
            else if (t < 12f)
            {
                phase = 1; compose.SetTexture("_B0", rtA); compose.SetFloat("_Crop", 1);
                Labels("Centre of the same frame: URP shadows for everything vs per-object atlas for the movers", 0.5f,
                    new[] { "URP shadows for all objects", "Per-object atlas for the 4 movers" },
                    new[] { urpA, "character slice: " + txt[0].Substring(moverNames[0].Length + 1) });
            }
            else if (t < 19f)
            {
                phase = 2; atlasOn = true;
                Labels($"Debug: the atlas ({ar.x} x {ar.y}), slices packed left to right, sized by screen coverage", 1f,
                    new[] { "Brighter = closer to the light; slice size steps between 128 and 512 px" }, new[] { "each slice: depth of one object, its own light-space box" });
            }
            else if (t < 23.5f)
            {
                phase = 3; compose.SetTexture("_B0", chain.mask); compose.SetFloat("_Gray0", 1); compose.SetFloat("_Crop", 1);
                Labels("Resolve (centre of the frame): boxes along the light rebuild positions from depth and test the atlas", 0.5f,
                    new[] { "Into a screen-space shadow mask (R8)", "Blended straight into the scene colour" },
                    new[] { "white = lit, black = full shadow", $"shadow colour black at {shadowAlpha * 100f:F0}%" });
            }
            else
            {
                phase = 4;
                Labels("Per-object atlas for the movers, URP cascades for the static props", 1f,
                    new[] { "Per-object shadows" }, new[] { atl });
            }
            compose.SetFloat("_AtlasOn", atlasOn ? 1 : 0);
            var panel = new Vector4(0.15f, 0.582f, 0.70f, 0.311f);
            compose.SetVector("_AtlasRect", panel); compose.SetTexture("_Atlas", atlasView);
            for (int i = 0; i < 4; i++)
            {
                bool vis = atlasOn && chain.impl.SliceDataList[i].ShouldRender;
                slice[i].text = vis ? txt[i] : "";
                slice[i].anchor = new Vector2(panel.x + (i % 2) * 0.36f, panel.y - 0.045f - 0.036f * (i / 2));
                slice[i].color = SliceCol[i];
            }

            int sec = Mathf.FloorToInt(t);
            if (phase != logged || sec != loggedSec)
            {
                logged = phase; loggedSec = sec;
                Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | atlas {ar} | {txt[0]} | {txt[1]} | {txt[2]} | {txt[3]}");
            }
        }
    }
}

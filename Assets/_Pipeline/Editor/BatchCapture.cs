// Entry points for batchmode runs:
//   -executeMethod GameTech.BatchCapture.Run -gtCapture -gtScene Assets/Techniques/<technique>/Main.unity -gtOut <dir> ...
//   -executeMethod GameTech.BatchCapture.Check          (compile + import only, then quit)
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameTech
{
    public static class BatchCapture
    {
        public static void Run()
        {
            string scene = FrameCapturer.Arg("-gtScene");
            string builder = FrameCapturer.Arg("-gtBuilder");   // e.g. GameTech.Posts.SmokeTest.Build
            if (!string.IsNullOrEmpty(builder) && !string.IsNullOrEmpty(scene))
            {
                // Scenes are generated from code so every post is reproducible from its source files alone.
                int dot = builder.LastIndexOf('.');
                System.Type type = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(builder.Substring(0, dot));
                    if (type != null) break;
                }
                var method = type != null ? type.GetMethod(builder.Substring(dot + 1)) : null;
                if (method == null) { Debug.LogError("[gt] builder not found: " + builder); EditorApplication.Exit(2); return; }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                method.Invoke(null, null);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(scene));
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[gt] scene built by " + builder);
            }
            if (string.IsNullOrEmpty(scene) || !System.IO.File.Exists(scene))
            {
                Debug.LogError("[gt] scene not found: " + scene);
                EditorApplication.Exit(2);
                return;
            }
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            EditorSettings.asyncShaderCompilation = false;   // no placeholder shaders in captured frames
            Debug.Log("[gt] entering play mode: " + scene);
            EditorApplication.EnterPlaymode();   // FrameCapturer boots itself and exits the editor when done
        }

        public static void Check()
        {
            Debug.Log("[gt] project compiled and imported, Unity " + Application.unityVersion
                      + ", device " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType
                      + ", pipeline " + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                          ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "built-in"));
            EditorApplication.Exit(0);
        }
    }
}

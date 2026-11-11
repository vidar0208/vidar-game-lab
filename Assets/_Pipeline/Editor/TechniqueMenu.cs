// Editor window that lists every technique under Assets/Techniques and builds its scene.
// Each technique has Editor/BuildScene.cs with a static class and a static Build() method
// that creates the scene, its materials and Main.unity from code.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameTech
{
    public class TechniqueMenu : EditorWindow
    {
        const string Root = "Assets/Techniques";
        Vector2 scroll;

        [MenuItem("Vidar Game Lab/Techniques")]
        static void Open() => GetWindow<TechniqueMenu>("Techniques");

        void OnGUI()
        {
            if (!Directory.Exists(Root)) { EditorGUILayout.HelpBox("No Assets/Techniques folder.", MessageType.Info); return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var dir in Directory.GetDirectories(Root).OrderBy(d => d))
            {
                string name = Path.GetFileName(dir);
                string builder = Path.Combine(dir, "Editor", "BuildScene.cs");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(name);
                GUI.enabled = File.Exists(builder);
                if (GUILayout.Button("Build", GUILayout.Width(70))) Build(builder);
                string scene = Path.Combine(dir, "Main.unity").Replace('\\', '/');
                GUI.enabled = File.Exists(scene);
                if (GUILayout.Button("Open", GUILayout.Width(70)))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        static void Build(string builderFile)
        {
            string src = File.ReadAllText(builderFile);
            string ns = Regex.Match(src, @"namespace\s+([\w.]+)").Groups[1].Value;
            string cls = Regex.Match(src, @"public\s+static\s+class\s+(\w+)").Groups[1].Value;
            string full = string.IsNullOrEmpty(ns) ? cls : ns + "." + cls;
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(full)).FirstOrDefault(t => t != null);
            var m = type?.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) { Debug.LogError($"No public static Build() in {full}"); return; }
            m.Invoke(null, null);
        }
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds light setup tools and transition test buttons for BackgroundManager to the Inspector.
/// Place this file inside a folder named "Editor".
/// </summary>
[CustomEditor(typeof(BackgroundManager))]
public class BackgroundManagerEditor : Editor
{
    float scrub;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var mgr = (BackgroundManager)target;

        DrawLightTools(mgr);
        DrawTransitionTest(mgr);
    }

    // ------------------------------------------------------------------
    // Light setup: capture the light's current pose into Day / Dusk / Night,
    // or preview a stored stage. Works in Edit Mode and Play Mode.
    // ------------------------------------------------------------------
    void DrawLightTools(BackgroundManager mgr)
    {
        if (mgr.lights == null || mgr.lights.Count == 0) return;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Light Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Set the light in the Scene (rotate / move / color / intensity), then press Capture for that time of day. " +
            "Preview applies a stored stage to the light.", MessageType.None);

        for (int i = 0; i < mgr.lights.Count; i++)
        {
            var lt = mgr.lights[i];
            if (lt == null) continue;

            string name = lt.light != null ? lt.light.name : "(no light)";
            EditorGUILayout.LabelField($"Light {i}: {name}", EditorStyles.miniBoldLabel);

            using (new EditorGUI.DisabledScope(lt.light == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Capture", GUILayout.Width(60));
                    for (int s = 0; s < 3; s++)
                    {
                        if (GUILayout.Button(BackgroundManager.StageNames[s]))
                        {
                            Undo.RecordObject(mgr, $"Capture {BackgroundManager.StageNames[s]} Light");
                            lt.CaptureInto(lt.Get(s));
                            EditorUtility.SetDirty(mgr);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Preview", GUILayout.Width(60));
                    for (int s = 0; s < 3; s++)
                    {
                        if (GUILayout.Button(BackgroundManager.StageNames[s]))
                        {
                            Undo.RecordObject(lt.light.transform, "Preview Light");
                            Undo.RecordObject(lt.light, "Preview Light");
                            lt.ApplyImmediate(s);
                            EditorUtility.SetDirty(lt.light);
                        }
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Transition test (Play Mode only)
    // ------------------------------------------------------------------
    void DrawTransitionTest(BackgroundManager mgr)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Transition Test", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test buttons.", MessageType.Info);
            return;
        }
        if (!mgr.IsReady)
        {
            EditorGUILayout.HelpBox("BackgroundManager is not initialized (check the GameManager reference).", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Current Stage", mgr.CurrentStageName);
        EditorGUILayout.LabelField("Fading", mgr.IsFading ? $"Yes ({mgr.FadeProgress:P0})" : "No");

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Next Stage", GUILayout.Height(28)))
            mgr.DebugNextStage();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Day")) mgr.DebugGoToStage(0);
            if (GUILayout.Button("Dusk")) mgr.DebugGoToStage(1);
            if (GUILayout.Button("Night")) mgr.DebugGoToStage(2);
        }

        if (GUILayout.Button("Reset To Day (instant)"))
            mgr.DebugResetToDay();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Scrub (tick 'Debug Pause Fade' first)", EditorStyles.miniBoldLabel);
        using (new EditorGUI.DisabledScope(!mgr.debugPauseFade || !mgr.IsFading))
        {
            EditorGUI.BeginChangeCheck();
            scrub = EditorGUILayout.Slider("Fade Progress", mgr.IsFading ? mgr.FadeProgress : 0f, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                mgr.DebugSetProgress(scrub);
        }

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Log Renderers (Console)"))
            mgr.DebugLogRenderers();

        // Keep the progress display updating while fading.
        if (mgr.IsFading) Repaint();
    }
}
#endif
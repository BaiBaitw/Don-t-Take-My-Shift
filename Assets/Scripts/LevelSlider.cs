using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Slides whole levels (world objects + UI) sideways in sync, like swiping between screens.
///
/// How it stays in sync:
///   Both systems are driven by the same progress value and moved by exactly one screen width,
///   measured in their own units:
///     - World: screen width in world units  = orthographicSize * 2 * aspect
///     - UI:    screen width in canvas units = canvasRect.rect.width
///   So 50% progress = half a screen on both, regardless of resolution or Canvas Scaler.
///
/// Setup:
///   - Camera must be Orthographic (2D).
///   - Each level has one world root (Transform) and one UI root (RectTransform under the Canvas,
///     Anchor Preset = full-screen stretch).
///   - Lay out every level at the same "centered" position. Non-current levels are hidden at start.
///   - Hook Next() / Previous() / SlideTo(index) to buttons or your own input.
///   - Test buttons appear in the Inspector during Play Mode.
/// </summary>
public class LevelSlider : MonoBehaviour
{
    [System.Serializable]
    public class Level
    {
        public Transform worldRoot;      // Parent of all scene objects of this level
        public RectTransform uiRoot;     // Parent of all UI of this level (full-screen stretch)
    }

    [Header("References")]
    public Camera worldCamera;           // Main (orthographic) camera
    public RectTransform canvasRect;     // The Canvas' RectTransform

    [Header("Levels")]
    public Level[] levels;
    public int startIndex = 0;

    [Header("Transition")]
    public float duration = 0.6f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Use unscaled time so the slide still plays when Time.timeScale = 0")]
    public bool useUnscaledTime = true;

    [Header("Test")]
    [Tooltip("Pause between slide-out and slide-in when simulating with a single level")]
    public float simulatePause = 0.2f;

    public int CurrentIndex { get; private set; }
    public bool IsSliding { get; private set; }

    /// <summary>Current level's offset in screen widths (0 = centered).</summary>
    public float CurrentOffset { get; private set; }

    Vector3[] worldBase;
    Vector2[] uiBase;

    void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;

        if (!worldCamera.orthographic)
            Debug.LogWarning("[LevelSlider] Camera is not orthographic; world and UI may not stay in sync.", this);

        // Cache each level's centered position
        worldBase = new Vector3[levels.Length];
        uiBase = new Vector2[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i].worldRoot) worldBase[i] = levels[i].worldRoot.position;
            if (levels[i].uiRoot) uiBase[i] = levels[i].uiRoot.anchoredPosition;
        }

        CurrentIndex = Mathf.Clamp(startIndex, 0, levels.Length - 1);
        for (int i = 0; i < levels.Length; i++)
        {
            SetActive(i, i == CurrentIndex);
            ApplyOffset(i, 0f);
        }
        CurrentOffset = 0f;
    }

    // ---------- Public API ----------

    public void Next() => SlideTo(CurrentIndex + 1);
    public void Previous() => SlideTo(CurrentIndex - 1);

    /// <summary>Slide to a level. Higher index comes in from the right, lower from the left.</summary>
    public void SlideTo(int index)
    {
        if (IsSliding || index == CurrentIndex || index < 0 || index >= levels.Length) return;

        // If the current level was moved by a test button, snap it back first
        if (CurrentOffset != 0f) { ApplyOffset(CurrentIndex, 0f); CurrentOffset = 0f; }

        int dir = index > CurrentIndex ? 1 : -1;
        StartCoroutine(SlideRoutine(CurrentIndex, index, dir));
    }

    // ---------- Single-level test API ----------

    /// <summary>Slide the current level off-screen. dir = -1 (left) or 1 (right).</summary>
    public void TestSlideOut(int dir)
    {
        if (IsSliding) return;
        StartCoroutine(Run(Tween(CurrentIndex, CurrentOffset, dir)));
    }

    /// <summary>Slide the current level back to the center.</summary>
    public void TestSlideBack()
    {
        if (IsSliding || CurrentOffset == 0f) return;
        StartCoroutine(Run(Tween(CurrentIndex, CurrentOffset, 0f)));
    }

    /// <summary>
    /// Simulates a level switch with only one level:
    /// slide out one side, then come back in from the other side.
    /// dir = 1 behaves like Next(), dir = -1 like Previous().
    /// </summary>
    public void TestSimulateSwitch(int dir)
    {
        if (IsSliding) return;
        StartCoroutine(Run(SimulateRoutine(dir)));
    }

    // ---------- Internals ----------

    IEnumerator SlideRoutine(int from, int to, int dir)
    {
        IsSliding = true;

        SetActive(to, true);
        ApplyOffset(to, dir);           // Place incoming level one screen away

        float time = 0f;
        while (time < duration)
        {
            time += DeltaTime();
            float e = ease.Evaluate(Mathf.Clamp01(time / duration));

            // Same eased value drives both levels and both systems
            ApplyOffset(from, -dir * e);        // Outgoing: 0 -> -dir screens
            ApplyOffset(to, dir * (1f - e));    // Incoming: dir -> 0 screens
            yield return null;
        }

        ApplyOffset(from, 0f);           // Reset outgoing to center for next time
        SetActive(from, false);
        ApplyOffset(to, 0f);

        CurrentIndex = to;
        CurrentOffset = 0f;
        IsSliding = false;
    }

    IEnumerator SimulateRoutine(int dir)
    {
        yield return Tween(CurrentIndex, CurrentOffset, -dir);   // Out

        if (simulatePause > 0f)
            yield return useUnscaledTime
                ? new WaitForSecondsRealtime(simulatePause)
                : new WaitForSeconds(simulatePause);

        yield return Tween(CurrentIndex, dir, 0f);               // In from the other side
    }

    /// <summary>Wraps a routine with the IsSliding flag.</summary>
    IEnumerator Run(IEnumerator routine)
    {
        IsSliding = true;
        yield return routine;
        IsSliding = false;
    }

    /// <summary>Moves one level from one screen offset to another.</summary>
    IEnumerator Tween(int i, float fromScreens, float toScreens)
    {
        float time = 0f;
        while (time < duration)
        {
            time += DeltaTime();
            float e = ease.Evaluate(Mathf.Clamp01(time / duration));
            CurrentOffset = Mathf.LerpUnclamped(fromScreens, toScreens, e);
            ApplyOffset(i, CurrentOffset);
            yield return null;
        }
        CurrentOffset = toScreens;
        ApplyOffset(i, CurrentOffset);
    }

    float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    /// <summary>
    /// Moves a level horizontally by a number of screen widths (e.g. 1 = one screen to the right).
    /// </summary>
    void ApplyOffset(int i, float screens)
    {
        // Recalculated every call so it also handles window resizing
        float worldWidth = worldCamera.orthographicSize * 2f * worldCamera.aspect;
        float uiWidth = canvasRect.rect.width;

        if (levels[i].worldRoot)
            levels[i].worldRoot.position = worldBase[i] + Vector3.right * (screens * worldWidth);

        if (levels[i].uiRoot)
            levels[i].uiRoot.anchoredPosition = uiBase[i] + Vector2.right * (screens * uiWidth);
    }

    void SetActive(int i, bool active)
    {
        if (levels[i].worldRoot) levels[i].worldRoot.gameObject.SetActive(active);
        if (levels[i].uiRoot) levels[i].uiRoot.gameObject.SetActive(active);
    }
}

#if UNITY_EDITOR
/// <summary>Adds test buttons to the LevelSlider Inspector (Play Mode only).</summary>
[CustomEditor(typeof(LevelSlider))]
public class LevelSliderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var slider = (LevelSlider)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test buttons.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(
            $"Level: {slider.CurrentIndex}   Offset: {slider.CurrentOffset:0.00} screens   Sliding: {slider.IsSliding}");

        using (new EditorGUI.DisabledScope(slider.IsSliding))
        {
            EditorGUILayout.LabelField("Simulate switch (works with one level)", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("< Simulate Previous")) slider.TestSimulateSwitch(-1);
            if (GUILayout.Button("Simulate Next >")) slider.TestSimulateSwitch(1);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Step by step", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("< Slide Out Left")) slider.TestSlideOut(-1);
            if (GUILayout.Button("Slide Back In")) slider.TestSlideBack();
            if (GUILayout.Button("Slide Out Right >")) slider.TestSlideOut(1);
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(slider.levels == null || slider.levels.Length < 2))
            {
                EditorGUILayout.LabelField("Real switch (needs 2+ levels)", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("< Previous")) slider.Previous();
                if (GUILayout.Button("Next >")) slider.Next();
                EditorGUILayout.EndHorizontal();
            }
        }

        // Keep the status line updating while sliding
        if (slider.IsSliding) Repaint();
    }
}
#endif
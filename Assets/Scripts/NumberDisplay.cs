using UnityEngine;

/// <summary>
/// Controls the visibility of:
///   number1 ~ number5 / number1Pressed ~ number5Pressed
///   note1   ~ note6   / note1Pressed   ~ note6Pressed
///
/// Usage:
///   ShowNumber(3);   // shows number3 + a random note at a random Y position
///   PressCurrent();  // swaps number3 -> number3Pressed and noteN -> noteNPressed
/// </summary>
public class NumberDisplay : MonoBehaviour
{
    private const int NumberCount = 5;
    private const int NoteCount = 6;

    // ------------------------------------------------------------------
    // Inspector settings
    // ------------------------------------------------------------------

    [Header("Numbers (leave empty to find by name)")]
    [SerializeField] private GameObject[] numbers = new GameObject[NumberCount];         // number1 ~ number5
    [SerializeField] private GameObject[] numbersPressed = new GameObject[NumberCount];  // number1Pressed ~ number5Pressed

    [Header("Notes (leave empty to find by name)")]
    [SerializeField] private GameObject[] notes = new GameObject[NoteCount];             // note1 ~ note6
    [SerializeField] private GameObject[] notesPressed = new GameObject[NoteCount];      // note1Pressed ~ note6Pressed

    [Header("Random Y Position (local space, relative to the notes' parent)")]
    [SerializeField] private float minY = -2f;
    [SerializeField] private float maxY = 2f;

    [Header("Floating Motion")]
    [SerializeField] private bool enableFloat = true;
    [Tooltip("Up/down floating distance")]
    [SerializeField] private float floatAmplitudeY = 0.2f;
    [Tooltip("Up/down cycles per second")]
    [SerializeField] private float floatSpeedY = 1f;
    [Tooltip("Left/right drifting distance")]
    [SerializeField] private float floatAmplitudeX = 0.05f;
    [Tooltip("Left/right cycles per second")]
    [SerializeField] private float floatSpeedX = 0.5f;
    [Tooltip("Max tilt angle in degrees (Z axis)")]
    [SerializeField] private float rotationAmplitude = 5f;
    [Tooltip("Tilt cycles per second")]
    [SerializeField] private float rotationSpeed = 0.8f;
    [Tooltip("Start each note at a random point of its floating cycle")]
    [SerializeField] private bool randomizePhase = true;

    [Header("Scene View Guides")]
    [SerializeField] private bool showGuides = true;
    [SerializeField] private float guideLineWidth = 10f;
    [SerializeField] private Color minLineColor = Color.green;
    [SerializeField] private Color maxLineColor = Color.red;
    [SerializeField] private Color floatRangeColor = new Color(1f, 1f, 0f, 0.4f);

    // ------------------------------------------------------------------
    // Runtime state
    // ------------------------------------------------------------------

    // Number state (1~5, 0 = none shown)
    private int currentIndex = 0;
    private bool isPressed = false;

    // Note state (1~6, 0 = none shown)
    private int currentNoteIndex = 0;
    private bool isNotePressed = false;

    // Original transforms of the notes, captured on Awake
    private Vector3[] noteOriginalPositions = new Vector3[NoteCount];
    private Quaternion[] noteOriginalRotations = new Quaternion[NoteCount];

    // Shared transform data for the active note (used by both noteN and noteNPressed,
    // so swapping between them never causes a visual jump)
    private Vector3 noteBasePosition;
    private Quaternion noteBaseRotation;
    private float floatTime;
    private float phaseX, phaseY, phaseRot;

    // When true, notes freeze where they are (see StopFloating / ResumeFloating)
    private bool isFloatStopped = false;

    public bool IsFloatStopped => isFloatStopped;
    public int CurrentNumber => currentIndex;
    public bool IsPressed => isPressed;
    public int CurrentNote => currentNoteIndex;
    public bool IsNotePressed => isNotePressed;

    // ------------------------------------------------------------------
    // Unity callbacks
    // ------------------------------------------------------------------

    private void Awake()
    {
        LoadObjects();

        for (int i = 0; i < NoteCount; i++)
        {
            if (notes[i] != null)
            {
                // Local space, so notes follow their parent when it moves
                noteOriginalPositions[i] = notes[i].transform.localPosition;
                noteOriginalRotations[i] = notes[i].transform.localRotation;
            }
        }

        HideAll();
    }

    private void Update()
    {
        if (currentNoteIndex == 0) return;

        // Frozen: keep floatTime unchanged so the note stays exactly where it stopped
        if (!isFloatStopped)
            floatTime += Time.deltaTime;
        ApplyNoteTransform(GetActiveNoteObject());
    }

    private void OnValidate()
    {
        if (maxY < minY) maxY = minY;
        if (guideLineWidth < 0f) guideLineWidth = 0f;
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    /// <summary>
    /// Finds any objects not assigned in the Inspector by name.
    /// Searches this object's children first, then the whole scene.
    /// </summary>
    private void LoadObjects()
    {
        numbers = EnsureSize(numbers, NumberCount);
        numbersPressed = EnsureSize(numbersPressed, NumberCount);
        notes = EnsureSize(notes, NoteCount);
        notesPressed = EnsureSize(notesPressed, NoteCount);

        for (int i = 0; i < NumberCount; i++)
        {
            if (numbers[i] == null) numbers[i] = FindByName("number" + (i + 1));
            if (numbersPressed[i] == null) numbersPressed[i] = FindByName("number" + (i + 1) + "Pressed");
        }

        for (int i = 0; i < NoteCount; i++)
        {
            if (notes[i] == null) notes[i] = FindByName("note" + (i + 1));
            if (notesPressed[i] == null) notesPressed[i] = FindByName("note" + (i + 1) + "Pressed");
        }
    }

    private static GameObject[] EnsureSize(GameObject[] array, int size)
    {
        if (array != null && array.Length == size) return array;

        GameObject[] resized = new GameObject[size];
        if (array != null)
        {
            for (int i = 0; i < Mathf.Min(array.Length, size); i++) resized[i] = array[i];
        }
        return resized;
    }

    private GameObject FindByName(string objName)
    {
        // Search children (including inactive ones)
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objName) return t.gameObject;
        }

        // Search the scene (GameObject.Find only finds active objects)
        GameObject found = GameObject.Find(objName);
        if (found == null)
            Debug.LogWarning($"[NumberDisplay] Could not find an object named \"{objName}\"");
        return found;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Hides all numbers and notes.
    /// </summary>
    public void HideAll()
    {
        HideNumbers();
        HideNotes();
    }

    /// <summary>
    /// Shows one of number1 ~ number5 (pass 1~5) and hides the others.
    /// Also shows a random note (note1 ~ note6) at a random Y position.
    /// </summary>
    public void ShowNumber(int number)
    {
        if (number < 1 || number > NumberCount)
        {
            Debug.LogWarning($"[NumberDisplay] ShowNumber expects 1~{NumberCount}, got: {number}");
            return;
        }

        // --- Number ---
        HideNumbers();
        GameObject target = numbers[number - 1];
        if (target != null) target.SetActive(true);
        currentIndex = number;
        isPressed = false;

        // --- Random note ---
        ShowRandomNote();
    }

    /// <summary>
    /// Swaps the shown numberN for numberNPressed,
    /// and the shown noteN for noteNPressed (keeping the exact same position).
    /// </summary>
    public void PressCurrent()
    {
        if (currentIndex == 0 && currentNoteIndex == 0)
        {
            Debug.LogWarning("[NumberDisplay] Nothing is currently shown. Call ShowNumber first.");
            return;
        }

        // --- Number ---
        if (currentIndex != 0)
        {
            int i = currentIndex - 1;
            if (numbers[i] != null) numbers[i].SetActive(false);
            if (numbersPressed[i] != null) numbersPressed[i].SetActive(true);
            isPressed = true;
        }

        // --- Note ---
        if (currentNoteIndex != 0 && !isNotePressed)
        {
            int n = currentNoteIndex - 1;
            GameObject pressed = notesPressed[n];

            // Move the pressed version into place BEFORE showing it, so there is no jump
            ApplyNoteTransform(pressed);

            if (notes[n] != null) notes[n].SetActive(false);
            if (pressed != null) pressed.SetActive(true);
            isNotePressed = true;
        }
    }

    /// <summary>
    /// Stops the floating motion of all notes. They freeze at their current position.
    /// Notes shown later by ShowNumber also stay still until ResumeFloating is called.
    /// </summary>
    public void StopFloating()
    {
        isFloatStopped = true;
    }

    /// <summary>
    /// Resumes the floating motion from where it was stopped (no jump).
    /// </summary>
    public void ResumeFloating()
    {
        isFloatStopped = false;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private void HideNumbers()
    {
        for (int i = 0; i < NumberCount; i++)
        {
            if (numbers[i] != null) numbers[i].SetActive(false);
            if (numbersPressed[i] != null) numbersPressed[i].SetActive(false);
        }
        currentIndex = 0;
        isPressed = false;
    }

    private void HideNotes()
    {
        for (int i = 0; i < NoteCount; i++)
        {
            if (notes[i] != null) notes[i].SetActive(false);
            if (notesPressed[i] != null) notesPressed[i].SetActive(false);
        }
        currentNoteIndex = 0;
        isNotePressed = false;
    }

    private void ShowRandomNote()
    {
        HideNotes();

        int n = Random.Range(0, NoteCount); // 0~5
        GameObject note = notes[n];
        if (note == null)
        {
            Debug.LogWarning($"[NumberDisplay] note{n + 1} is missing, cannot show it.");
            return;
        }

        // Keep the original X/Z, randomize Y
        noteBasePosition = noteOriginalPositions[n];
        noteBasePosition.y = Random.Range(minY, maxY);
        noteBaseRotation = noteOriginalRotations[n];

        // Reset the floating cycle
        floatTime = 0f;
        if (randomizePhase)
        {
            phaseX = Random.Range(0f, Mathf.PI * 2f);
            phaseY = Random.Range(0f, Mathf.PI * 2f);
            phaseRot = Random.Range(0f, Mathf.PI * 2f);
        }
        else
        {
            phaseX = phaseY = phaseRot = 0f;
        }

        currentNoteIndex = n + 1;
        isNotePressed = false;

        // Position first, then show
        ApplyNoteTransform(note);
        note.SetActive(true);
    }

    private GameObject GetActiveNoteObject()
    {
        if (currentNoteIndex == 0) return null;
        int n = currentNoteIndex - 1;
        return isNotePressed ? notesPressed[n] : notes[n];
    }

    /// <summary>
    /// Applies the shared base position + floating offset to the given object.
    /// noteN and noteNPressed both use this, so they always line up exactly.
    /// </summary>
    private void ApplyNoteTransform(GameObject go)
    {
        if (go == null) return;

        Vector3 offset = Vector3.zero;
        float tilt = 0f;

        if (enableFloat)
        {
            const float TwoPi = Mathf.PI * 2f;
            offset.x = Mathf.Sin(floatTime * floatSpeedX * TwoPi + phaseX) * floatAmplitudeX;
            offset.y = Mathf.Sin(floatTime * floatSpeedY * TwoPi + phaseY) * floatAmplitudeY;
            tilt = Mathf.Sin(floatTime * rotationSpeed * TwoPi + phaseRot) * rotationAmplitude;
        }

        go.transform.localPosition = noteBasePosition + offset;
        go.transform.localRotation = noteBaseRotation * Quaternion.Euler(0f, 0f, tilt);
    }

    // ------------------------------------------------------------------
    // Scene view guides
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!showGuides) return;

        // Lines are drawn in the local space of note1's parent (the same space the
        // random Y uses), so they move together with that parent.
        // If note1 is not assigned yet, this object is assumed to be the parent.
        Transform space = transform;
        Vector3 center = Vector3.zero;
        if (notes != null && notes.Length > 0 && notes[0] != null)
        {
            space = notes[0].transform.parent; // may be null (scene root = world space)
            center = Application.isPlaying ? noteOriginalPositions[0] : notes[0].transform.localPosition;
        }

        float half = guideLineWidth * 0.5f;

        // Min / Max Y lines
        DrawHorizontalLine(space, center, minY, half, minLineColor);
        DrawHorizontalLine(space, center, maxY, half, maxLineColor);

        // Full range including floating amplitude
        if (enableFloat && floatAmplitudeY > 0f)
        {
            DrawHorizontalLine(space, center, minY - floatAmplitudeY, half, floatRangeColor);
            DrawHorizontalLine(space, center, maxY + floatAmplitudeY, half, floatRangeColor);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.color = minLineColor;
        UnityEditor.Handles.Label(ToWorld(space, new Vector3(center.x + half, minY, center.z)), $" Min Y ({minY})");
        UnityEditor.Handles.color = maxLineColor;
        UnityEditor.Handles.Label(ToWorld(space, new Vector3(center.x + half, maxY, center.z)), $" Max Y ({maxY})");
#endif
    }

    private static Vector3 ToWorld(Transform space, Vector3 localPoint)
    {
        return space != null ? space.TransformPoint(localPoint) : localPoint;
    }

    private static void DrawHorizontalLine(Transform space, Vector3 center, float y, float halfWidth, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(ToWorld(space, new Vector3(center.x - halfWidth, y, center.z)),
                        ToWorld(space, new Vector3(center.x + halfWidth, y, center.z)));
    }
}

#if UNITY_EDITOR
// ----------------------------------------------------------------------
// Test buttons in the Inspector (only active in Play mode)
// ----------------------------------------------------------------------
[UnityEditor.CustomEditor(typeof(NumberDisplay))]
public class NumberDisplayEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        NumberDisplay display = (NumberDisplay)target;

        UnityEditor.EditorGUILayout.Space(10);
        UnityEditor.EditorGUILayout.LabelField("Test", UnityEditor.EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            UnityEditor.EditorGUILayout.HelpBox("Enter Play mode to use the test buttons.", UnityEditor.MessageType.Info);
        }

        using (new UnityEditor.EditorGUI.DisabledScope(!Application.isPlaying))
        {
            // ShowNumber(1) ~ ShowNumber(5)
            GUILayout.BeginHorizontal();
            for (int i = 1; i <= 5; i++)
            {
                if (GUILayout.Button("ShowNumber(" + i + ")"))
                    display.ShowNumber(i);
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("ShowNumber (Random 1~5)"))
                display.ShowNumber(Random.Range(1, 6));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PressCurrent", GUILayout.Height(30)))
                display.PressCurrent();
            if (GUILayout.Button("HideAll", GUILayout.Height(30)))
                display.HideAll();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("StopFloating"))
                display.StopFloating();
            if (GUILayout.Button("ResumeFloating"))
                display.ResumeFloating();
            GUILayout.EndHorizontal();

            if (Application.isPlaying)
            {
                UnityEditor.EditorGUILayout.Space(5);
                UnityEditor.EditorGUILayout.LabelField("Current Number",
                    display.CurrentNumber == 0 ? "-" : display.CurrentNumber + (display.IsPressed ? " (Pressed)" : ""));
                UnityEditor.EditorGUILayout.LabelField("Current Note",
                    display.CurrentNote == 0 ? "-" : display.CurrentNote + (display.IsNotePressed ? " (Pressed)" : ""));
                UnityEditor.EditorGUILayout.LabelField("Floating",
                    display.IsFloatStopped ? "Stopped" : "Running");
                Repaint();
            }
        }
    }
}
#endif
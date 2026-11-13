using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Core of the tracing game: the player holds the mouse button at the start point
/// and drags along the dashed outline to draw it.
/// Leaving the path pauses drawing and the progress stays where it is;
/// pressing on the progress marker again resumes drawing.
/// A random outline is picked from Path Roots at start, and a different one
/// is picked each time an outline is completed.
/// Usage: for each outline, place its vertices (in order) as children of one empty
/// GameObject, add those GameObjects to Path Roots, and attach this script to any GameObject.
/// The dashed guide line, the drawn line and the progress marker are created at runtime.
/// </summary>
public class IllustratorGame : MonoBehaviour
{
    [Header("Paths (each element's children are its vertices, in order)")]
    public Transform[] pathRoots;
    [Tooltip("If checked, the last vertex connects back to the first one to form a closed outline")]
    public bool closedLoop = true;
    [Tooltip("Seconds to wait after finishing an outline before showing the next one")]
    public float nextPuzzleDelay = 1f;

    [Header("Game Flow")]
    [Tooltip("The GameObject that has the GameManager script. CompleteCurrentStage() is called on it after each outline, and its IsFinished and IsGameOver values are checked")]
    public GameObject gameManager;
    [Tooltip("Start automatically when Play is pressed (handy for testing this level alone). When off, the game waits until StartGame() is called")]
    public bool startOnPlay = false;
    [Tooltip("The GameObject that has the PlayerScript script. PlayWrongAnimation() is called on it each time the cursor leaves the path")]
    public GameObject player;

    [Header("Detection")]
    [Tooltip("How close (in world units) the mouse press must be to the progress marker to start drawing")]
    public float resumeRadius = 0.5f;
    [Tooltip("Max distance from the path while dragging; going further pauses drawing")]
    public float pathTolerance = 0.35f;
    [Tooltip("Only search this far ahead along the path, so the player cannot shortcut to a nearby segment")]
    public float lookAhead = 1.5f;

    [Header("Background")]
    [Tooltip("Background shown normally")]
    public GameObject painterBackground;
    [Tooltip("Background shown for a while after the cursor leaves the path")]
    public GameObject painterBackgroundWrong;
    [Tooltip("Seconds the wrong background stays visible before switching back")]
    public float wrongDisplayDuration = 1f;

    [Header("Cursor")]
    [Tooltip("Custom mouse cursor for this level; leave empty to keep the system cursor. Set the image's Texture Type to Cursor")]
    public Texture2D cursorTexture;
    [Tooltip("The click point in pixels, measured from the top-left corner of the cursor image")]
    public Vector2 cursorHotspot = Vector2.zero;

    [Header("Appearance")]
    public float lineWidth = 0.08f;
    public Color guideColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Color drawColor = Color.black;
    public Color markerColor = new Color(1f, 0.45f, 0.2f, 1f);
    [Tooltip("Dash density; higher values give denser dashes")]
    public float dashesPerUnit = 4f;
    public float markerSize = 0.35f;
    [Tooltip("Optional; uses Sprites/Default when empty")]
    public Material lineMaterial;

    [Header("Sorting")]
    [Tooltip("Sorting Layer used by the dashed line, the drawn line and the progress marker")]
    [IllustratorSortingLayer]
    public string sortingLayer = "Default";
    public int guideOrderInLayer = 0;
    public int drawnOrderInLayer = 1;
    public int markerOrderInLayer = 2;

    [Header("Events (hook up sounds, UI, etc. in the Inspector)")]
    public UnityEvent onStartDrawing = new UnityEvent();
    public UnityEvent onPaused = new UnityEvent();
    public UnityEvent onCompleted = new UnityEvent();
    public UnityEvent onNewPuzzle = new UnityEvent();

    // Runtime data
    readonly List<Vector2> pts = new List<Vector2>();
    readonly List<Vector3> drawnBuffer = new List<Vector3>();
    readonly List<int> validPaths = new List<int>();
    float[] cum = new float[0];   // cumulative path length at each vertex
    float total;                  // total path length
    float progress;               // length drawn so far (the core value!)
    bool drawing;
    bool done = true;
    int currentIndex = -1;
    Vector2 lastMouse;

    LineRenderer guideLine, drawnLine;
    Transform marker;
    SpriteRenderer markerRenderer;
    Camera cam;
    Coroutine wrongRoutine;
    Coroutine nextRoutine;
    GameManager gm;
    PlayerScript playerScript;
    bool initialized;
    bool gameOver;

    /// <summary>Completion from 0 to 1, useful for a progress bar</summary>
    public float Progress01 => total > 0f ? progress / total : 0f;
    public bool IsDone => done;
    /// <summary>Index in Path Roots of the outline being drawn</summary>
    public int CurrentPathIndex => currentIndex;

    void OnEnable()
    {
        if (cursorTexture != null) Cursor.SetCursor(cursorTexture, cursorHotspot, CursorMode.Auto);
    }

    void OnDisable()
    {
        // Restore the system cursor when leaving this level
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void Awake()
    {
        cam = Camera.main;

        for (int i = 0; i < pathRoots.Length; i++)
        {
            if (pathRoots[i] != null && pathRoots[i].childCount >= 2) validPaths.Add(i);
            else Debug.LogWarning($"IllustratorGame: Path Roots element {i} is empty or has fewer than 2 child vertices and will be skipped.");
        }
        if (validPaths.Count == 0)
        {
            Debug.LogError("IllustratorGame: Path Roots needs at least one element with 2 or more child objects as vertices.");
            enabled = false;
            return;
        }

        guideLine = CreateLine("GuideLine", guideColor, 0, true);
        drawnLine = CreateLine("DrawnLine", drawColor, 1, false);
        marker = CreateMarker();
        ShowWrongBackground(false);
        guideLine.positionCount = 0;          // nothing is shown until StartGame()
        drawnLine.positionCount = 0;
        marker.gameObject.SetActive(false);

        if (gameManager != null)
        {
            gm = gameManager.GetComponent<GameManager>();
            if (gm == null) Debug.LogError("IllustratorGame: the assigned Game Manager object has no GameManager script.");
        }
        if (player != null)
        {
            playerScript = player.GetComponent<PlayerScript>();
            if (playerScript == null) Debug.LogError("IllustratorGame: the assigned Player object has no PlayerScript script.");
        }
        ApplyAppearance();
        initialized = true;
    }

    void Start()
    {
        if (startOnPlay) StartGame();
    }

    // ---------- Called from outside ----------

    /// <summary>Starts (or restarts) the game with a random outline. Call this from outside, e.g. from GameManager</summary>
    public void StartGame()
    {
        if (!initialized)
        {
            Debug.LogError("IllustratorGame: StartGame() was called before the script was set up. Make sure the GameObject is active and Path Roots is filled in.");
            return;
        }
        if (nextRoutine != null)
        {
            StopCoroutine(nextRoutine);
            nextRoutine = null;
        }
        NextPuzzle();
    }

    /// <summary>Makes the game harder. Called from outside; not implemented yet</summary>
    public void IncreaseDifficulty()
    {
        // TODO: e.g. lower pathTolerance or lookAhead, or switch to harder outlines
    }

    /// <summary>
    /// While GameManager.IsGameOver is true: show the wrong background and block drawing.
    /// When it turns false again, the normal background comes back and drawing is allowed.
    /// </summary>
    void CheckGameOver()
    {
        bool now = gm != null && gm.IsGameOver;
        if (now == gameOver) return;
        gameOver = now;

        if (gameOver)
        {
            drawing = false;
            if (wrongRoutine != null)
            {
                StopCoroutine(wrongRoutine);
                wrongRoutine = null;
            }
            if (nextRoutine != null)
            {
                StopCoroutine(nextRoutine);
                nextRoutine = null;
            }
            ShowWrongBackground(true);
            marker.gameObject.SetActive(false);
        }
        else
        {
            ShowWrongBackground(false);
            if (currentIndex >= 0) UpdateVisuals();
        }
    }

    /// <summary>True when GameManager reports that the game is finished</summary>
    bool IsGameFinished()
    {
        return gm != null && gm.IsFinished;
    }

    void Update()
    {
        ApplyAppearance();
        CheckGameOver();
        if (gameOver || done) return;
        if (!ReadMouse(out Vector2 m, out bool down, out bool held, out bool up)) return;

        // 1. Press: drawing only starts near the progress marker
        if (down && Vector2.Distance(m, PointAt(progress)) <= resumeRadius)
        {
            drawing = true;
            lastMouse = m;
            onStartDrawing.Invoke();
        }
        // 2. Release: stop drawing
        if (up || !held) drawing = false;

        // 3. Drag: split this frame's mouse movement into small steps and check each one
        //    (so fast mouse movement is not missed)
        if (drawing)
        {
            float stepLen = Mathf.Max(0.01f, pathTolerance * 0.5f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(lastMouse, m) / stepLen));
            for (int k = 1; k <= steps && drawing && !done; k++)
                Advance(Vector2.Lerp(lastMouse, m, (float)k / steps));
            lastMouse = m;
        }

        UpdateVisuals();
    }

    void Advance(Vector2 p)
    {
        float sMin = Mathf.Max(0f, progress - 0.3f);
        float sMax = Mathf.Min(total, progress + lookAhead);
        float s = Project(p, sMin, sMax, out float dist);

        if (dist > pathTolerance)
        {
            drawing = false;          // off the path: pause, progress stays
            TriggerWrong();
            if (playerScript != null) playerScript.PlayWrongAnimation();
            onPaused.Invoke();
            return;
        }
        if (s > progress) progress = s;   // progress only moves forward

        if (progress >= total - 0.01f)
        {
            progress = total;
            done = true;
            drawing = false;
            UpdateVisuals();
            onCompleted.Invoke();
            if (gm != null) gm.CompleteCurrentStage();

            // When GameManager says the game is finished, keep showing this drawing
            // until StartGame() is called again
            if (!IsGameFinished()) nextRoutine = StartCoroutine(NextPuzzleAfterDelay());
        }
    }

    // ---------- Puzzles ----------

    /// <summary>Switch to a random outline different from the current one</summary>
    public void NextPuzzle()
    {
        int next = validPaths[Random.Range(0, validPaths.Count)];
        if (validPaths.Count > 1)
            while (next == currentIndex) next = validPaths[Random.Range(0, validPaths.Count)];
        LoadPath(next);
    }

    /// <summary>Restart the current outline</summary>
    public void ResetDrawing()
    {
        if (currentIndex < 0) return;   // no outline loaded yet
        progress = 0f;
        done = false;
        drawing = false;
        UpdateVisuals();
    }

    IEnumerator NextPuzzleAfterDelay()
    {
        yield return new WaitForSeconds(nextPuzzleDelay);
        nextRoutine = null;
        if (IsGameFinished()) yield break;
        NextPuzzle();
    }

    void LoadPath(int index)
    {
        currentIndex = index;
        BuildPath(pathRoots[index]);

        guideLine.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++) guideLine.SetPosition(i, pts[i]);

        ShowWrongBackground(false);
        ResetDrawing();
        onNewPuzzle.Invoke();
    }

    // ---------- Background ----------

    void TriggerWrong()
    {
        if (wrongRoutine != null) StopCoroutine(wrongRoutine);
        wrongRoutine = StartCoroutine(WrongRoutine());
    }

    IEnumerator WrongRoutine()
    {
        ShowWrongBackground(true);
        yield return new WaitForSeconds(wrongDisplayDuration);
        ShowWrongBackground(false);
        wrongRoutine = null;
    }

    void ShowWrongBackground(bool wrong)
    {
        if (!wrong && wrongRoutine != null)
        {
            StopCoroutine(wrongRoutine);
            wrongRoutine = null;
        }
        if (painterBackground != null) painterBackground.SetActive(!wrong);
        if (painterBackgroundWrong != null) painterBackgroundWrong.SetActive(wrong);
    }

    // ---------- Path math ----------

    void BuildPath(Transform root)
    {
        pts.Clear();
        foreach (Transform child in root) pts.Add(child.position);
        if (closedLoop && pts.Count > 2) pts.Add(pts[0]);

        cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        total = pts.Count > 0 ? cum[pts.Count - 1] : 0f;
    }

    /// <summary>Position on the path at length s</summary>
    Vector2 PointAt(float s)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (s <= cum[i + 1])
            {
                float seg = cum[i + 1] - cum[i];
                float t = seg < 1e-5f ? 0f : (s - cum[i]) / seg;
                return Vector2.Lerp(pts[i], pts[i + 1], t);
            }
        }
        return pts[pts.Count - 1];
    }

    /// <summary>Projects point p onto the path within [sMin, sMax]; returns the length of the closest point and its distance</summary>
    float Project(Vector2 p, float sMin, float sMax, out float bestDist)
    {
        bestDist = float.MaxValue;
        float bestS = sMin;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            float a = cum[i], b = cum[i + 1];
            if (b < sMin || a > sMax || b - a < 1e-5f) continue;

            Vector2 dir = (pts[i + 1] - pts[i]) / (b - a);
            float s = Mathf.Clamp(a + Vector2.Dot(p - pts[i], dir), Mathf.Max(a, sMin), Mathf.Min(b, sMax));
            float d = Vector2.Distance(p, pts[i] + dir * (s - a));
            if (d < bestDist) { bestDist = d; bestS = s; }
        }
        return bestS;
    }

    // ---------- Visuals ----------

    void UpdateVisuals()
    {
        drawnBuffer.Clear();
        for (int i = 0; i < pts.Count && cum[i] < progress; i++) drawnBuffer.Add(pts[i]);
        drawnBuffer.Add(PointAt(progress));
        drawnLine.positionCount = drawnBuffer.Count;
        drawnLine.SetPositions(drawnBuffer.ToArray());

        Vector2 mp = PointAt(progress);
        marker.position = new Vector3(mp.x, mp.y, -0.1f);
        marker.gameObject.SetActive(!done);
        // Pulse the marker while not drawing, to show where to resume
        float pulse = drawing ? 1f : 1f + 0.2f * Mathf.Sin(Time.time * 6f);
        marker.localScale = Vector3.one * markerSize * pulse;
    }

    // Applies the Appearance settings every frame, so changes in the Inspector show up during Play
    void ApplyAppearance()
    {
        guideLine.widthMultiplier = lineWidth;
        drawnLine.widthMultiplier = lineWidth;
        guideLine.startColor = guideLine.endColor = guideColor;
        drawnLine.startColor = drawnLine.endColor = drawColor;
        markerRenderer.color = markerColor;

        guideLine.sortingLayerName = sortingLayer;
        drawnLine.sortingLayerName = sortingLayer;
        markerRenderer.sortingLayerName = sortingLayer;
        guideLine.sortingOrder = guideOrderInLayer;
        drawnLine.sortingOrder = drawnOrderInLayer;
        markerRenderer.sortingOrder = markerOrderInLayer;

        // Sprites/Default ignores the material's tiling, so the dash density is set on the LineRenderer itself
#if UNITY_2022_2_OR_NEWER
        guideLine.textureScale = new Vector2(dashesPerUnit, 1f);
#endif
        guideLine.material.mainTextureScale = new Vector2(dashesPerUnit, 1f);
    }

    LineRenderer CreateLine(string objName, Color color, int order, bool dashed)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = lineWidth;
        lr.numCornerVertices = 4;
        lr.numCapVertices = dashed ? 0 : 4;
        lr.startColor = lr.endColor = color;
        lr.sortingOrder = order;

        Material mat = lineMaterial != null ? new Material(lineMaterial) : new Material(Shader.Find("Sprites/Default"));
        if (dashed)
        {
            mat.mainTexture = MakeDashTexture();
            mat.mainTextureScale = new Vector2(dashesPerUnit, 1f);
            lr.textureMode = LineTextureMode.Tile;
        }
        lr.material = mat;
        return lr;
    }

    Transform CreateMarker()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
            }
        tex.Apply();

        var go = new GameObject("ProgressMarker");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sr.color = markerColor;
        sr.sortingOrder = 2;
        markerRenderer = sr;
        return go.transform;
    }

    static Texture2D MakeDashTexture()
    {
        var tex = new Texture2D(8, 2, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point
        };
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 2; y++)
                tex.SetPixel(x, y, x < 5 ? Color.white : Color.clear);
        tex.Apply();
        return tex;
    }

    bool ReadMouse(out Vector2 world, out bool down, out bool held, out bool up)
    {
        world = default; down = held = up = false;
        if (cam == null) return false;
        Vector3 sp;
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse == null) return false;
        sp = mouse.position.ReadValue();
        down = mouse.leftButton.wasPressedThisFrame;
        held = mouse.leftButton.isPressed;
        up = mouse.leftButton.wasReleasedThisFrame;
#else
        sp = Input.mousePosition;
        down = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        up = Input.GetMouseButtonUp(0);
#endif
        sp.z = -cam.transform.position.z;   // distance from the camera to the z = 0 plane
        world = cam.ScreenToWorldPoint(sp);
        return true;
    }

    // Preview every path in the Scene view to help place vertices (green = start point)
    void OnDrawGizmos()
    {
        if (pathRoots == null) return;
        foreach (Transform root in pathRoots)
        {
            if (root == null || root.childCount == 0) continue;
            Gizmos.color = Color.cyan;
            Transform prev = null, first = null;
            foreach (Transform c in root)
            {
                if (first == null) first = c;
                if (prev != null) Gizmos.DrawLine(prev.position, c.position);
                Gizmos.DrawWireSphere(c.position, 0.08f);
                prev = c;
            }
            if (closedLoop && root.childCount > 2) Gizmos.DrawLine(prev.position, first.position);
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(first.position, 0.15f);
        }
    }
}

/// <summary>Shows a string field as a Sorting Layer dropdown in the Inspector</summary>
public class IllustratorSortingLayerAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(IllustratorSortingLayerAttribute))]
public class IllustratorSortingLayerDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        SortingLayer[] layers = SortingLayer.layers;
        string[] names = new string[layers.Length];
        int current = 0;
        for (int i = 0; i < layers.Length; i++)
        {
            names[i] = layers[i].name;
            if (names[i] == property.stringValue) current = i;
        }
        int selected = UnityEditor.EditorGUI.Popup(position, label.text, current, names);
        property.stringValue = names[selected];
    }
}
#endif
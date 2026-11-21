using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
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
/// Usage: for each outline, create an empty UI object (RectTransform) under a Canvas and place
/// its vertices (in order) as UI children; add those objects to Path Roots and attach this
/// script to any GameObject.
/// The dashed guide line, the drawn line and the progress marker are created at runtime
/// as UI Images inside that Canvas, so they can sit above other UI.
/// </summary>
public class IllustratorGame : MonoBehaviour
{
    [Header("Paths (UI objects under a Canvas; each element's children are its vertices, in order)")]
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
    [Tooltip("The GameObject that has the PlayerScript (or SupermanPlayerScript) script. PlayWrongAnimation() is called on it each time the cursor leaves the path")]
    public GameObject player;

    [Header("Detection")]
    [Tooltip("How close (in units) the mouse press must be to the progress marker to start drawing")]
    public float resumeRadius = 0.5f;
    [Tooltip("Max distance from the path while dragging; going further pauses drawing")]
    public float pathTolerance = 0.35f;
    [Tooltip("Only search this far ahead along the path, so the player cannot shortcut to a nearby segment")]
    public float lookAhead = 1.5f;

    [Header("Error Feedback")]
    [Tooltip("Shown for a while when the cursor leaves the path; hidden the rest of the time")]
    public GameObject errorRedbox;
    [Tooltip("Seconds the Error Redbox stays visible before it disappears")]
    public float errorDisplayDuration = 1f;
    [Tooltip("Optional: ScreenShake component to shake the screen each time the cursor leaves the path (uses its Default Settings)")]
    public ScreenShake screenShake;
    [Tooltip("Optional: CountdownBar component; ApplyPenalty(penaltyAmount) is called each time the cursor leaves the path")]
    public CountdownBar countdownBar;
    [Tooltip("Value passed to CountdownBar.ApplyPenalty() on each mistake")]
    public float penaltyAmount = 1f;

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

    [Header("UI")]
    [Tooltip("How many canvas units count as 1 unit for every distance setting (Resume Radius, Path Tolerance, Look Ahead, Line Width, Marker Size, Dashes Per Unit)")]
    public float uiUnitsPerUnit = 100f;
    [Tooltip("Optional: the UI object the lines are created under (as its last child). Empty = the parent of the first Path Root. Use it to put UI such as the Error Redbox above the lines")]
    public RectTransform uiLinesParent;

    [Header("Events (hook up sounds, UI, etc. in the Inspector)")]
    public UnityEvent onStartDrawing = new UnityEvent();
    public UnityEvent onPaused = new UnityEvent();
    public UnityEvent onCompleted = new UnityEvent();
    public UnityEvent onNewPuzzle = new UnityEvent();

    // Runtime data
    readonly List<Vector2> pts = new List<Vector2>();
    readonly List<Vector2> drawnBuffer = new List<Vector2>();
    readonly List<int> validPaths = new List<int>();
    float[] cum = new float[0];   // cumulative path length at each vertex
    float total;                  // total path length
    float progress;               // length drawn so far (the core value!)
    bool drawing;
    bool done = true;
    int currentIndex = -1;
    Vector2 lastMouse;

    // Visuals
    Canvas uiCanvas;
    RectTransform uiContainer;
    UIImageLine uiGuide, uiDrawn;
    Image uiMarkerImage;
    readonly List<Vector2> uiBuffer = new List<Vector2>();
    RectTransform marker;
    Sprite circleSprite;
    Coroutine errorRoutine;
    Coroutine nextRoutine;
    GameManager gm;
    PlayerScript playerScript;
    SupermanPlayerScript supermanPlayerScript;
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
        for (int i = 0; i < pathRoots.Length; i++)
        {
            Transform root = pathRoots[i];
            if (root == null || root.childCount < 2)
                Debug.LogWarning($"IllustratorGame: Path Roots element {i} is empty or has fewer than 2 child vertices and will be skipped.");
            else if (!(root is RectTransform) || root.GetComponentInParent<Canvas>() == null)
                Debug.LogWarning($"IllustratorGame: Path Roots element {i} ('{root.name}') is not a UI object under a Canvas and will be skipped.");
            else
                validPaths.Add(i);
        }
        if (validPaths.Count == 0)
        {
            Debug.LogError("IllustratorGame: Path Roots needs at least one UI object under a Canvas with 2 or more child objects as vertices.");
            enabled = false;
            return;
        }

        circleSprite = MakeCircleSprite();
        SetupUI(pathRoots[validPaths[0]]);

        ShowErrorRedbox(false);
        marker.gameObject.SetActive(false);   // nothing is shown until StartGame()

        if (gameManager != null)
        {
            gm = gameManager.GetComponent<GameManager>();
            if (gm == null) Debug.LogError("IllustratorGame: the assigned Game Manager object has no GameManager script.");
        }
        if (player != null)
        {
            playerScript = player.GetComponent<PlayerScript>();
            // No PlayerScript: try SupermanPlayerScript instead
            if (playerScript == null) supermanPlayerScript = player.GetComponent<SupermanPlayerScript>();
            if (playerScript == null && supermanPlayerScript == null)
                Debug.LogError("IllustratorGame: the assigned Player object has neither a PlayerScript nor a SupermanPlayerScript script.");
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
    /// While GameManager.IsGameOver is true: keep the Error Redbox visible and block drawing.
    /// When it turns false again, the Error Redbox is hidden and drawing is allowed.
    /// </summary>
    void CheckGameOver()
    {
        bool now = gm != null && gm.IsGameOver;
        if (now == gameOver) return;
        gameOver = now;

        if (gameOver)
        {
            drawing = false;
            if (errorRoutine != null)
            {
                StopCoroutine(errorRoutine);
                errorRoutine = null;
            }
            if (nextRoutine != null)
            {
                StopCoroutine(nextRoutine);
                nextRoutine = null;
            }
            ShowErrorRedbox(true);
            marker.gameObject.SetActive(false);
        }
        else
        {
            ShowErrorRedbox(false);
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
            TriggerError();
            if (screenShake != null) screenShake.Shake();
            if (countdownBar != null) countdownBar.ApplyPenalty(penaltyAmount);
            if (playerScript != null) playerScript.PlayWrongAnimation();
            else if (supermanPlayerScript != null) supermanPlayerScript.PlayWrongAnimation();
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

        if (total * uiUnitsPerUnit < 100f)
            Debug.LogWarning($"IllustratorGame: path '{pathRoots[index].name}' is only {total * uiUnitsPerUnit:0} canvas units long, so its lines are too small to see. " +
                             "UI positions are in pixels: spread the vertices hundreds of units apart (check Pos X / Pos Y in their Rect Transform).");

        ToCanvas(pts, uiBuffer);
        uiGuide.SetPoints(uiBuffer);

        ShowErrorRedbox(false);
        ResetDrawing();
        onNewPuzzle.Invoke();
    }

    // ---------- Error feedback ----------

    void TriggerError()
    {
        if (errorRoutine != null) StopCoroutine(errorRoutine);
        errorRoutine = StartCoroutine(ErrorRoutine());
    }

    IEnumerator ErrorRoutine()
    {
        ShowErrorRedbox(true);
        yield return new WaitForSeconds(errorDisplayDuration);
        ShowErrorRedbox(false);
        errorRoutine = null;
    }

    void ShowErrorRedbox(bool show)
    {
        if (!show && errorRoutine != null)
        {
            StopCoroutine(errorRoutine);
            errorRoutine = null;
        }
        if (errorRedbox != null) errorRedbox.SetActive(show);
    }

    // ---------- Path math ----------

    void BuildPath(Transform root)
    {
        pts.Clear();
        foreach (Transform child in root) pts.Add(ToUnits(child.position));
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
        Vector2 mp = PointAt(progress);
        marker.gameObject.SetActive(!done);
        // Pulse the marker while not drawing, to show where to resume
        float pulse = drawing ? 1f : 1f + 0.2f * Mathf.Sin(Time.time * 6f);

        ToCanvas(drawnBuffer, uiBuffer);
        uiDrawn.SetPoints(uiBuffer);
        marker.localPosition = mp * uiUnitsPerUnit;
        marker.localScale = Vector3.one * pulse;
    }

    // Applies the Appearance settings every frame, so changes in the Inspector show up during Play
    void ApplyAppearance()
    {
        float period = uiUnitsPerUnit / Mathf.Max(0.01f, dashesPerUnit);
        uiGuide.SetStyle(lineWidth * uiUnitsPerUnit, true, period, guideColor);
        uiDrawn.SetStyle(lineWidth * uiUnitsPerUnit, false, period, drawColor);
        uiMarkerImage.color = markerColor;
        marker.sizeDelta = Vector2.one * markerSize * uiUnitsPerUnit;
    }

    // ---------- Setup ----------

    void SetupUI(Transform firstRoot)
    {
        RectTransform parent = uiLinesParent != null ? uiLinesParent : (RectTransform)firstRoot.parent;

        uiContainer = CreateUIChild("IllustratorLines", parent);
        uiContainer.SetAsLastSibling();   // drawn above the UI that comes before it
        uiCanvas = uiContainer.GetComponentInParent<Canvas>().rootCanvas;

        uiGuide = new UIImageLine(CreateUIChild("GuideLine", uiContainer), circleSprite);
        uiDrawn = new UIImageLine(CreateUIChild("DrawnLine", uiContainer), circleSprite);

        RectTransform m = CreateUIChild("ProgressMarker", uiContainer);
        m.anchorMin = m.anchorMax = new Vector2(0.5f, 0.5f);
        uiMarkerImage = m.gameObject.AddComponent<Image>();
        uiMarkerImage.sprite = circleSprite;
        uiMarkerImage.raycastTarget = false;
        marker = m;
    }

    /// <summary>Creates a RectTransform that fills its parent, with the pivot in the middle</summary>
    static RectTransform CreateUIChild(string objName, RectTransform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        return rt;
    }

    // ---------- Space conversion ----------
    // All path math works in "units": canvas units (relative to the lines container) / uiUnitsPerUnit.

    Vector2 ToUnits(Vector3 worldPos)
    {
        return (Vector2)uiContainer.InverseTransformPoint(worldPos) / uiUnitsPerUnit;
    }

    void ToCanvas(List<Vector2> source, List<Vector2> result)
    {
        result.Clear();
        foreach (Vector2 v in source) result.Add(v * uiUnitsPerUnit);
    }

    static Sprite MakeCircleSprite()
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
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    bool ReadMouse(out Vector2 pos, out bool down, out bool held, out bool up)
    {
        pos = default; down = held = up = false;
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
        Camera uiCam = uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(uiContainer, sp, uiCam, out Vector2 local))
            return false;
        pos = local / uiUnitsPerUnit;
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

/// <summary>
/// Draws a solid or dashed polyline in UI using pooled Image components
/// (one stretched Image per segment or dash, round Images at the joints).
/// Points are in the local space of the root RectTransform, in canvas units.
/// </summary>
class UIImageLine
{
    const float DashFill = 5f / 8f;   // part of each dash period that is drawn

    readonly RectTransform root;
    readonly Sprite dotSprite;
    readonly List<Image> bars = new List<Image>();
    readonly List<Image> dots = new List<Image>();
    readonly List<Vector2> points = new List<Vector2>();
    float width = 8f;
    bool dashed;
    float period = 25f;
    Color color = Color.white;
    int barCount, dotCount;

    public UIImageLine(RectTransform root, Sprite dotSprite)
    {
        this.root = root;
        this.dotSprite = dotSprite;
    }

    public void SetPoints(List<Vector2> newPoints)
    {
        points.Clear();
        points.AddRange(newPoints);
        Rebuild();
    }

    public void SetStyle(float lineWidth, bool isDashed, float dashPeriod, Color lineColor)
    {
        dashPeriod = Mathf.Max(0.01f, dashPeriod);
        if (lineWidth == width && isDashed == dashed && dashPeriod == period && lineColor == color) return;
        width = lineWidth;
        dashed = isDashed;
        period = dashPeriod;
        color = lineColor;
        Rebuild();
    }

    void Rebuild()
    {
        barCount = dotCount = 0;
        if (points.Count >= 2 && width > 0f)
        {
            if (!dashed)
            {
                for (int i = 0; i < points.Count - 1; i++) AddBar(points[i], points[i + 1]);
                foreach (Vector2 p in points) AddDot(p);   // round joins and caps
            }
            else
            {
                float onLength = period * DashFill;
                float distance = 0f;
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector2 a = points[i], b = points[i + 1];
                    float len = Vector2.Distance(a, b);
                    if (len < 1e-4f) continue;
                    Vector2 dir = (b - a) / len;
                    float segStart = distance, segEnd = distance + len;
                    for (int k = Mathf.FloorToInt(segStart / period); k * period < segEnd; k++)
                    {
                        float dashStart = Mathf.Max(segStart, k * period);
                        float dashEnd = Mathf.Min(segEnd, k * period + onLength);
                        if (dashEnd > dashStart)
                            AddBar(a + dir * (dashStart - segStart), a + dir * (dashEnd - segStart));
                    }
                    distance = segEnd;
                }
            }
        }
        for (int i = barCount; i < bars.Count; i++) bars[i].enabled = false;
        for (int i = dotCount; i < dots.Count; i++) dots[i].enabled = false;
    }

    void AddBar(Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1e-3f) return;
        RectTransform rt = Get(bars, ref barCount, null).rectTransform;
        rt.localPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(len, width);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    void AddDot(Vector2 p)
    {
        RectTransform rt = Get(dots, ref dotCount, dotSprite).rectTransform;
        rt.localPosition = p;
        rt.sizeDelta = Vector2.one * width;
        rt.localRotation = Quaternion.identity;
    }

    Image Get(List<Image> pool, ref int count, Sprite sprite)
    {
        if (count == pool.Count)
        {
            var go = new GameObject(sprite == null ? "Segment" : "Joint", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            pool.Add(img);
        }
        Image result = pool[count++];
        result.enabled = true;
        result.color = color;
        return result;
    }
}
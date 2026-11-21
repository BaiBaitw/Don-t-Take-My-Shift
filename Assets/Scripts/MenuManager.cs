// MenuManager.cs
// Menu screen: pseudo-3D circular level carousel + level unlock saving (PlayerPrefs)
// + first-unlock pop-in animation + floating effect + Inspector test tools.
//
// Quick setup:
// 1. Put all xxxOption buttons under the SAME parent (needed for front/back draw ordering).
// 2. Add this component to any object in the menu scene (e.g. an empty "MenuManager").
//    When first added, it auto-finds typistOption ... supermanOption by name.
// 3. Fill in each level's Scene name in "levels" (scenes must be in Build Settings).
// 4. To go back to the menu from a level, call:
//      MenuManager.CompleteCurrentLevelAndReturn();   // level cleared -> unlock next, back to menu
//      MenuManager.ReturnToMenu();                    // back to menu without clearing

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MenuManager : MonoBehaviour
{
    // ------------------------- Types -------------------------
    public enum EaseType { Linear, InQuad, OutQuad, InOutQuad, OutCubic, InOutCubic, OutBack, OutElastic, OutBounce, Custom }
    public enum RevealEffect { Scale, Fade, ScaleAndFade, DropIn, RiseUp, SpinIn, FlipIn }

    [System.Serializable]
    public class LevelOption
    {
        [Tooltip("Display name (only used as the Inspector element title)")]
        public string label;
        [Tooltip("The Button for this level (the xxxOption object in the scene)")]
        public Button button;
        [Tooltip("Scene to load when pressed (must be added to Build Settings / Build Profiles)")]
        public string sceneName;
    }

    // PlayerPrefs keys
    public const string KeyUnlocked = "MenuManager_UnlockedCount";  // number of unlocked levels (1 = only level 1)
    public const string KeyRevealed = "MenuManager_RevealedCount";  // number of levels whose pop-in has already played
    public const string KeyLastLaunched = "MenuManager_LastLaunched";   // last level launched from the menu (0-based)
    public const string KeyMenuScene = "MenuManager_MenuScene";      // menu scene name

    static readonly string[] DefaultNames =
    {
        "typistOption", "illustratorOption", "composerOption", "factoryOption",
        "engineerOption", "scamOption", "supermanOption"
    };

    // ------------------------- Inspector settings -------------------------
    [Header("Levels (top to bottom = level 1 -> last level)")]
    public LevelOption[] levels;

    [Header("Menu Scene")]
    [Tooltip("Leave empty to use the name of the scene this MenuManager is in")]
    public string menuSceneName = "";

    [Header("Pseudo-3D Ring Layout")]
    [Tooltip("Ring center (anchoredPosition, relative to the buttons' parent)")]
    public Vector2 ringCenter = new Vector2(0f, 60f);
    [Tooltip("X = horizontal radius; Y = vertical offset between front and back (bigger = viewed more from above; front item sits lower)")]
    public Vector2 ringRadius = new Vector2(420f, 80f);
    [Tooltip("Scale of the front-most button")]
    public float frontScale = 1f;
    [Tooltip("Scale of the back-most button")]
    public float backScale = 0.55f;

    [Header("Left / Right Switching")]
    [Tooltip("Seconds to rotate by one slot")]
    public float slideDuration = 0.45f;
    [Tooltip("Easing used when rotating")]
    public EaseType slideEase = EaseType.OutCubic;
    [Tooltip("Used when slideEase is Custom")]
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Allow pressing the front level while the ring is still rotating")]
    public bool allowClickWhileSliding = false;
    [Tooltip("Also allow A / D keys to switch")]
    public bool useADKeys = true;
    [Tooltip("Drag distance in pixels for one switch (based on 1080p height, scaled with resolution)")]
    public float swipeThreshold = 60f;
    [Tooltip("Keep switching while dragging further")]
    public bool continuousSwipe = true;
    [Tooltip("Invert swipe direction")]
    public bool invertSwipe = false;
    [Tooltip("Only drags that start inside this area count (empty = full screen)")]
    public RectTransform swipeArea;

    [Header("Dimming (non-front buttons)")]
    [Tooltip("Color multiplied onto dimmed buttons (alpha is multiplied too, so it can also add transparency)")]
    public Color dimColor = new Color(0.35f, 0.35f, 0.4f, 1f);
    [Tooltip("Seconds to fade between bright and dim")]
    public float dimDuration = 0.3f;

    [Header("Floating Effect")]
    public bool enableFloat = true;
    [Tooltip("Float offset amplitude (X = horizontal, Y = vertical, in UI pixels)")]
    public Vector2 floatAmplitude = new Vector2(4f, 12f);
    [Tooltip("Float frequency (cycles per second)")]
    public float floatFrequency = 0.5f;
    [Tooltip("Sway angle (degrees)")]
    public float swayAngle = 3f;
    [Tooltip("Sway frequency (cycles per second)")]
    public float swayFrequency = 0.35f;
    [Tooltip("Breathing scale amount (0.03 = +/-3%)")]
    public float breatheAmount = 0.03f;
    [Tooltip("Breathing frequency (cycles per second)")]
    public float breatheFrequency = 0.6f;
    [Tooltip("Phase offset between buttons so they don't float in sync")]
    public float phaseSpread = 1.1f;

    [Header("First-Unlock Pop-in")]
    [Tooltip("Type of pop-in effect")]
    public RevealEffect revealEffect = RevealEffect.ScaleAndFade;
    [Tooltip("Pop-in easing (OutBack / OutElastic / OutBounce give a bouncy feel)")]
    public EaseType revealEase = EaseType.OutBack;
    [Tooltip("Used when revealEase is Custom")]
    public AnimationCurve revealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Pop-in animation duration (seconds)")]
    public float revealDuration = 0.6f;
    [Tooltip("Delay after rotating to the new level before it pops in")]
    public float revealDelay = 0.35f;
    [Tooltip("Gap between pop-ins when several levels unlock at once")]
    public float revealInterval = 0.25f;
    [Tooltip("Offset distance for DropIn / RiseUp")]
    public float revealOffsetDistance = 250f;
    [Tooltip("Rotation angle for SpinIn")]
    public float revealSpinAngle = 360f;
    [Tooltip("Block input while a pop-in is playing")]
    public bool lockInputDuringReveal = true;

    [Header("Loading / Time")]
    [Tooltip("Delay before switching scene after pressing (so the press feedback is visible)")]
    public float loadDelay = 0.15f;
    [Tooltip("Use time that ignores Time.timeScale")]
    public bool useUnscaledTime = true;

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip slideSfx;
    public AudioClip revealSfx;
    public AudioClip confirmSfx;

    // ------------------------- Runtime state -------------------------
    class OptionRuntime
    {
        public GameObject go;
        public RectTransform rect;
        public CanvasGroup group;
        public Graphic[] graphics;
        public Color[] baseColors;
        public Vector3 baseScale;
        public float dim;
        public float appliedDim = -1f;
        public float reveal = 1f;
        public bool revealing;
        public float depth;
        public float phase;
    }

    OptionRuntime[] rt;
    int unlockedCount;
    int shownCount;            // number of levels currently on the ring
    float ringPos, ringTarget; // ring rotation, measured in level slots
    float displayCount = 1f, targetCount = 1f;
    float slideFromPos, slideFromCount, slideTimer;
    bool sliding;
    bool inputLocked, isLoading;
    bool pointerTracking, swipedThisPress;
    Vector2 pointerStart;
    Coroutine revealRoutine;
    Canvas canvas;
    bool canSort;
    int siblingBase;
    readonly List<int> sortBuffer = new List<int>();
    System.Comparison<int> depthComparer;

    float Dt => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    /// <summary>Index of the current front level (0-based), or -1 if none.</summary>
    public int FrontIndex => shownCount <= 0 ? -1 : IMod(Mathf.RoundToInt(ringTarget), shownCount);
    public bool IsSliding => sliding;

    // ------------------------- Unity lifecycle -------------------------
    void Reset()
    {
        levels = null;
        AutoFindOptions();
    }

    void Awake()
    {
        string menuName = string.IsNullOrEmpty(menuSceneName) ? gameObject.scene.name : menuSceneName;
        PlayerPrefs.SetString(KeyMenuScene, menuName);

        depthComparer = (a, b) => rt[a].depth.CompareTo(rt[b].depth);
        int n = levels != null ? levels.Length : 0;
        rt = new OptionRuntime[n];

        Transform commonParent = null;
        canSort = true;
        siblingBase = int.MaxValue;

        for (int i = 0; i < n; i++)
        {
            Button b = levels[i] != null ? levels[i].button : null;
            if (b == null)
            {
                Debug.LogWarning($"[MenuManager] Level {i + 1} has no Button assigned.", this);
                continue;
            }

            var r = new OptionRuntime();
            r.go = b.gameObject;
            r.rect = (RectTransform)b.transform;
            r.group = b.GetComponent<CanvasGroup>();
            if (r.group == null) r.group = b.gameObject.AddComponent<CanvasGroup>();
            r.graphics = b.GetComponentsInChildren<Graphic>(true);
            r.baseColors = new Color[r.graphics.Length];
            for (int j = 0; j < r.graphics.Length; j++) r.baseColors[j] = r.graphics[j].color;
            r.baseScale = r.rect.localScale;
            r.phase = i * phaseSpread;
            rt[i] = r;

            // Disable EventSystem arrow-key navigation (so other buttons can't be selected)
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;

            int idx = i;
            b.onClick.AddListener(() => TrySelect(idx, true));

            if (commonParent == null) commonParent = r.rect.parent;
            else if (commonParent != r.rect.parent) canSort = false;
            siblingBase = Mathf.Min(siblingBase, r.rect.GetSiblingIndex());

            if (canvas == null) canvas = b.GetComponentInParent<Canvas>();
        }

        if (!canSort)
            Debug.LogWarning("[MenuManager] Level buttons are not under the same parent; front/back ordering is disabled.", this);
    }

    void Start()
    {
        Rebuild(false);
    }

    void OnValidate()
    {
        if (rt == null) return;
        foreach (var r in rt) if (r != null) r.appliedDim = -1f; // apply color changes immediately
    }

    void Update()
    {
        if (rt == null || rt.Length == 0) return;
        float dt = Dt;
        UpdateSlide(dt);
        HandleInput();
        ApplyLayout(dt);
    }

    void LateUpdate()
    {
        // Never let a level button stay "selected" by the EventSystem
        // (prevents Enter from triggering a non-front button or firing twice)
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null || rt == null) return;
        foreach (var r in rt)
            if (r != null && es.currentSelectedGameObject == r.go)
            {
                es.SetSelectedGameObject(null);
                break;
            }
    }

    // ------------------------- Progress / rebuild -------------------------
    /// <summary>Rebuilds the menu from PlayerPrefs; plays pop-ins for any unlocks not yet revealed.</summary>
    public void Rebuild(bool keepCurrentFocus)
    {
        if (rt == null || rt.Length == 0) return;

        int prevFront = FrontIndex;
        if (revealRoutine != null) { StopCoroutine(revealRoutine); revealRoutine = null; }
        inputLocked = false;
        isLoading = false;

        int n = rt.Length;
        unlockedCount = Mathf.Clamp(PlayerPrefs.GetInt(KeyUnlocked, 1), 1, n);
        int revealed = Mathf.Clamp(PlayerPrefs.GetInt(KeyRevealed, 0), 0, unlockedCount);
        shownCount = revealed;

        for (int i = 0; i < n; i++)
        {
            var r = rt[i];
            if (r == null) continue;
            r.reveal = 1f;
            r.revealing = false;
            r.go.SetActive(i < shownCount);
        }

        int focus = (keepCurrentFocus && prevFront >= 0) ? prevFront : PlayerPrefs.GetInt(KeyLastLaunched, 0);
        focus = Mathf.Clamp(focus, 0, Mathf.Max(0, shownCount - 1));
        SnapTo(focus, shownCount);

        int front = FrontIndex;
        for (int i = 0; i < n; i++)
        {
            if (rt[i] == null) continue;
            rt[i].dim = (i == front) ? 0f : 1f;
            rt[i].appliedDim = -1f;
        }

        if (revealed < unlockedCount)
            revealRoutine = StartCoroutine(RevealRoutine(revealed, unlockedCount));

        ApplyLayout(0f);
    }

    IEnumerator RevealRoutine(int from, int to)
    {
        if (lockInputDuringReveal) inputLocked = true;

        for (int k = from; k < to; k++)
        {
            while (sliding) yield return null;

            var r = rt[k];
            bool wasEmpty = shownCount == 0;
            shownCount = k + 1;

            if (r != null)
            {
                r.reveal = 0f;
                r.revealing = true;
                r.dim = 0f;
                r.appliedDim = -1f;
                r.go.SetActive(true);
            }

            if (wasEmpty) SnapTo(k, shownCount);
            else StartSlide(NearestTarget(k, shownCount), shownCount);

            while (sliding) yield return null;
            yield return Wait(revealDelay);

            Play(revealSfx);
            if (r != null)
            {
                float t = 0f;
                while (t < revealDuration)
                {
                    t += Dt;
                    r.reveal = Mathf.Clamp01(t / revealDuration);
                    yield return null;
                }
                r.reveal = 1f;
                r.revealing = false;
            }

            PlayerPrefs.SetInt(KeyRevealed, k + 1);
            PlayerPrefs.Save();

            if (k < to - 1) yield return Wait(revealInterval);
        }

        inputLocked = false;
        revealRoutine = null;
    }

    IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Dt; yield return null; }
    }

    // ------------------------- Rotation -------------------------
    /// <summary>dir = -1 brings the left level to the front, +1 brings the right level to the front.</summary>
    public void Step(int dir)
    {
        if (shownCount <= 1 || inputLocked || isLoading) return;
        StartSlide(ringTarget + dir, shownCount);
        Play(slideSfx);
    }

    void SnapTo(int index, int count)
    {
        displayCount = targetCount = Mathf.Max(1, count);
        ringPos = ringTarget = index;
        sliding = false;
    }

    void StartSlide(float target, int count)
    {
        slideFromPos = ringPos;
        slideFromCount = displayCount;
        ringTarget = target;
        targetCount = Mathf.Max(1, count);
        slideTimer = 0f;
        sliding = slideDuration > 0f;
        if (!sliding)
        {
            ringPos = ringTarget;
            displayCount = targetCount;
            NormalizeRing();
        }
    }

    void UpdateSlide(float dt)
    {
        if (!sliding) return;
        slideTimer += dt;
        float p = Mathf.Clamp01(slideTimer / slideDuration);
        float e = Ease(slideEase, p, slideCurve);
        ringPos = Mathf.LerpUnclamped(slideFromPos, ringTarget, e);
        displayCount = Mathf.LerpUnclamped(slideFromCount, targetCount, e);
        if (p >= 1f)
        {
            ringPos = ringTarget;
            displayCount = targetCount;
            sliding = false;
            NormalizeRing();
        }
    }

    void NormalizeRing()
    {
        float n = Mathf.Max(1, shownCount);
        ringTarget = FMod(ringTarget, n);
        ringPos = ringTarget;
    }

    float NearestTarget(int index, int count)
    {
        float cur = FMod(ringPos, count);
        float delta = index - cur;
        if (delta > count * 0.5f) delta -= count;
        if (delta < -count * 0.5f) delta += count;
        return ringPos + delta;
    }

    // ------------------------- Input -------------------------
    void HandleInput()
    {
        if (shownCount == 0 || isLoading) { pointerTracking = false; return; }

        if (!inputLocked)
        {
            if (KeyLeft()) Step(-1);
            if (KeyRight()) Step(1);
            if (KeySubmit()) TrySelect(FrontIndex, false);
        }

        // Mouse / touch horizontal swipe
        if (PointerDown())
        {
            Vector2 p = PointerPos();
            pointerTracking = swipeArea == null ||
                RectTransformUtility.RectangleContainsScreenPoint(swipeArea, p, UICamera());
            pointerStart = p;
            swipedThisPress = false;
        }

        if (pointerTracking && PointerHeld())
        {
            Vector2 p = PointerPos();
            float dx = p.x - pointerStart.x;
            float th = swipeThreshold * (Screen.height / 1080f);
            if (Mathf.Abs(dx) >= th)
            {
                swipedThisPress = true; // this press was a swipe, so releasing won't count as a click
                int dir = dx < 0f ? 1 : -1; // swipe left -> right-hand level comes to the front
                if (invertSwipe) dir = -dir;
                Step(dir);
                if (continuousSwipe) pointerStart = p;
                else pointerTracking = false;
            }
        }

        if (PointerUp()) pointerTracking = false;
    }

    Camera UICamera()
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return canvas.worldCamera;
    }

    /// <summary>Tries to launch a level (only succeeds for the front, available level).</summary>
    public void TrySelect(int index, bool fromPointer)
    {
        if (index < 0 || index >= rt.Length || index != FrontIndex) return;
        if (isLoading || inputLocked) return;
        if (sliding && !allowClickWhileSliding) return;
        if (fromPointer && swipedThisPress) return;
        var r = rt[index];
        if (r == null || r.revealing) return;

        string scene = levels[index].sceneName;
        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning($"[MenuManager] Level {index + 1} ({levels[index].label}) has no sceneName set.", this);
            return;
        }
        StartCoroutine(LoadRoutine(index, scene));
    }

    IEnumerator LoadRoutine(int index, string scene)
    {
        isLoading = true;
        PlayerPrefs.SetInt(KeyLastLaunched, index);
        PlayerPrefs.Save();
        Play(confirmSfx);
        if (loadDelay > 0f) yield return new WaitForSecondsRealtime(loadDelay);
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }

    // ------------------------- Layout / visuals -------------------------
    void ApplyLayout(float dt)
    {
        if (rt == null) return;
        float n = Mathf.Max(1f, displayCount);
        float step = Mathf.PI * 2f / n;
        float time = Now;
        int front = FrontIndex;
        const float TAU = Mathf.PI * 2f;

        sortBuffer.Clear();

        for (int i = 0; i < shownCount && i < rt.Length; i++)
        {
            var r = rt[i];
            if (r == null) continue;

            // Ring position
            float a = (i - ringPos) * step;
            float cos = Mathf.Cos(a);
            float depth = (cos + 1f) * 0.5f; // 1 = front, 0 = back
            r.depth = depth;
            Vector2 pos = ringCenter + new Vector2(Mathf.Sin(a) * ringRadius.x, -cos * ringRadius.y);
            float ringScale = Mathf.Lerp(backScale, frontScale, depth);

            // Floating
            Vector2 floatOff = Vector2.zero;
            float sway = 0f, breathe = 1f;
            if (enableFloat)
            {
                float ph = r.phase;
                float w = time * floatFrequency * TAU + ph;
                floatOff = new Vector2(Mathf.Sin(w * 0.5f + ph) * floatAmplitude.x, Mathf.Sin(w) * floatAmplitude.y);
                sway = Mathf.Sin(time * swayFrequency * TAU + ph * 1.3f) * swayAngle;
                breathe = 1f + Mathf.Sin(time * breatheFrequency * TAU + ph * 0.7f) * breatheAmount;
            }

            // Pop-in effect
            Vector3 rScale = Vector3.one;
            float rAlpha = 1f, rRot = 0f;
            Vector2 rOff = Vector2.zero;
            if (r.reveal < 1f)
            {
                float p = r.reveal;
                float e = Ease(revealEase, p, revealCurve);
                float fade = Mathf.Clamp01(Ease(EaseType.OutQuad, p, null));
                switch (revealEffect)
                {
                    case RevealEffect.Scale: rScale = Vector3.one * e; break;
                    case RevealEffect.Fade: rAlpha = Mathf.Clamp01(e); break;
                    case RevealEffect.ScaleAndFade: rScale = Vector3.one * e; rAlpha = fade; break;
                    case RevealEffect.DropIn: rOff = new Vector2(0f, (1f - e) * revealOffsetDistance); rAlpha = fade; break;
                    case RevealEffect.RiseUp: rOff = new Vector2(0f, -(1f - e) * revealOffsetDistance); rAlpha = fade; break;
                    case RevealEffect.SpinIn: rScale = Vector3.one * e; rRot = (1f - e) * revealSpinAngle; rAlpha = fade; break;
                    case RevealEffect.FlipIn: rScale = new Vector3(e, 1f, 1f); rAlpha = fade; break;
                }
            }

            r.rect.anchoredPosition = pos + floatOff + rOff;
            r.rect.localScale = Vector3.Scale(r.baseScale * (ringScale * breathe), rScale);
            r.rect.localRotation = Quaternion.Euler(0f, 0f, sway + rRot);
            r.group.alpha = rAlpha;

            // Only the front button receives pointer clicks
            r.group.blocksRaycasts = (i == front) && !r.revealing && !inputLocked && !isLoading;

            // Dimming
            float targetDim = (i == front) ? 0f : 1f;
            r.dim = dimDuration > 0f ? Mathf.MoveTowards(r.dim, targetDim, dt / dimDuration) : targetDim;
            if (!Mathf.Approximately(r.dim, r.appliedDim)) ApplyColors(r);

            sortBuffer.Add(i);
        }

        // Front/back occlusion: closer items get a higher sibling index (drawn later, on top)
        if (canSort && sortBuffer.Count > 1)
        {
            sortBuffer.Sort(depthComparer);
            for (int k = 0; k < sortBuffer.Count; k++)
            {
                var rect = rt[sortBuffer[k]].rect;
                int want = siblingBase + k;
                if (rect.GetSiblingIndex() != want) rect.SetSiblingIndex(want);
            }
        }
    }

    void ApplyColors(OptionRuntime r)
    {
        float k = Mathf.SmoothStep(0f, 1f, r.dim);
        for (int j = 0; j < r.graphics.Length; j++)
        {
            if (r.graphics[j] == null) continue;
            Color c = r.baseColors[j];
            r.graphics[j].color = Color.Lerp(c, c * dimColor, k);
        }
        r.appliedDim = r.dim;
    }

    void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }

    // ------------------------- Static API for other scenes -------------------------
    /// <summary>Menu scene name (remembered automatically once the menu scene has loaded).</summary>
    public static string MenuScene => PlayerPrefs.GetString(KeyMenuScene, "Menu");

    /// <summary>Number of unlocked levels (1 = only level 1).</summary>
    public static int UnlockedCount => PlayerPrefs.GetInt(KeyUnlocked, 1);

    /// <summary>Whether level levelNumber (1-based) has been cleared.</summary>
    public static bool IsLevelCleared(int levelNumber) => UnlockedCount > levelNumber;

    /// <summary>Go back to the menu without clearing the level.</summary>
    public static void ReturnToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(MenuScene);
    }

    /// <summary>Mark level levelNumber (1-based) as cleared and unlock the next one (saves only, no scene change).</summary>
    public static void CompleteLevel(int levelNumber)
    {
        int next = levelNumber + 1;
        if (next > UnlockedCount) PlayerPrefs.SetInt(KeyUnlocked, next);
        PlayerPrefs.Save();
    }

    /// <summary>Mark level levelNumber (1-based) as cleared and go back to the menu.</summary>
    public static void CompleteLevelAndReturn(int levelNumber)
    {
        CompleteLevel(levelNumber);
        ReturnToMenu();
    }

    /// <summary>Mark the level last launched from the menu as cleared and go back to the menu.</summary>
    public static void CompleteCurrentLevelAndReturn()
    {
        int idx = PlayerPrefs.GetInt(KeyLastLaunched, -1);
        if (idx < 0) Debug.LogWarning("[MenuManager] Current level unknown (never launched from the menu); returning to menu only.");
        else CompleteLevel(idx + 1);
        ReturnToMenu();
    }

    /// <summary>Clear all progress.</summary>
    public static void ResetProgress()
    {
        PlayerPrefs.SetInt(KeyUnlocked, 1);
        PlayerPrefs.SetInt(KeyRevealed, 0);
        PlayerPrefs.SetInt(KeyLastLaunched, 0);
        PlayerPrefs.Save();
    }

    // ------------------------- Utilities -------------------------
    [ContextMenu("Auto Find Option Buttons")]
    public void AutoFindOptions()
    {
        if (levels == null || levels.Length != DefaultNames.Length)
        {
            var old = levels;
            levels = new LevelOption[DefaultNames.Length];
            for (int i = 0; i < levels.Length; i++)
                levels[i] = (old != null && i < old.Length && old[i] != null) ? old[i] : new LevelOption();
        }
        for (int i = 0; i < DefaultNames.Length; i++)
        {
            if (levels[i] == null) levels[i] = new LevelOption();
            if (string.IsNullOrEmpty(levels[i].label)) levels[i].label = DefaultNames[i];
            if (levels[i].button == null)
            {
                var go = GameObject.Find(DefaultNames[i]);
                if (go != null) levels[i].button = go.GetComponent<Button>();
            }
        }
    }

    static float FMod(float a, float n) => a - n * Mathf.Floor(a / n);
    static int IMod(int a, int n) => ((a % n) + n) % n;

    public static float Ease(EaseType type, float x, AnimationCurve curve)
    {
        switch (type)
        {
            case EaseType.Linear: return x;
            case EaseType.InQuad: return x * x;
            case EaseType.OutQuad: return 1f - (1f - x) * (1f - x);
            case EaseType.InOutQuad: return x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;
            case EaseType.OutCubic: return 1f - Mathf.Pow(1f - x, 3f);
            case EaseType.InOutCubic: return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
            case EaseType.OutBack:
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
                }
            case EaseType.OutElastic:
                {
                    if (x <= 0f) return 0f;
                    if (x >= 1f) return 1f;
                    const float c4 = (2f * Mathf.PI) / 3f;
                    return Mathf.Pow(2f, -10f * x) * Mathf.Sin((x * 10f - 0.75f) * c4) + 1f;
                }
            case EaseType.OutBounce:
                {
                    const float n1 = 7.5625f, d1 = 2.75f;
                    if (x < 1f / d1) return n1 * x * x;
                    if (x < 2f / d1) { x -= 1.5f / d1; return n1 * x * x + 0.75f; }
                    if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
                    x -= 2.625f / d1; return n1 * x * x + 0.984375f;
                }
            case EaseType.Custom: return curve != null ? curve.Evaluate(x) : x;
        }
        return x;
    }

    // Supports both the new Input System and the legacy Input Manager
#if ENABLE_INPUT_SYSTEM
    bool KeyLeft()
    {
        var k = Keyboard.current; if (k == null) return false;
        return k.leftArrowKey.wasPressedThisFrame || (useADKeys && k.aKey.wasPressedThisFrame);
    }
    bool KeyRight()
    {
        var k = Keyboard.current; if (k == null) return false;
        return k.rightArrowKey.wasPressedThisFrame || (useADKeys && k.dKey.wasPressedThisFrame);
    }
    bool KeySubmit()
    {
        var k = Keyboard.current; if (k == null) return false;
        return k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
    }
    bool PointerDown() => Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
    bool PointerHeld() => Pointer.current != null && Pointer.current.press.isPressed;
    bool PointerUp() => Pointer.current != null && Pointer.current.press.wasReleasedThisFrame;
    Vector2 PointerPos() => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
#else
    bool KeyLeft() => Input.GetKeyDown(KeyCode.LeftArrow) || (useADKeys && Input.GetKeyDown(KeyCode.A));
    bool KeyRight() => Input.GetKeyDown(KeyCode.RightArrow) || (useADKeys && Input.GetKeyDown(KeyCode.D));
    bool KeySubmit() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    bool PointerDown() => Input.GetMouseButtonDown(0);
    bool PointerHeld() => Input.GetMouseButton(0);
    bool PointerUp() => Input.GetMouseButtonUp(0);
    Vector2 PointerPos() => Input.mousePosition;
#endif
}

// ------------------------- Inspector test tools -------------------------
#if UNITY_EDITOR
[CustomEditor(typeof(MenuManager))]
public class MenuManagerEditor : Editor
{
    int targetLevel = 1;

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var m = (MenuManager)target;
        int n = m.levels != null ? m.levels.Length : 0;
        if (n == 0) return;

        int unlocked = PlayerPrefs.GetInt(MenuManager.KeyUnlocked, 1);
        int revealed = PlayerPrefs.GetInt(MenuManager.KeyRevealed, 0);
        int last = PlayerPrefs.GetInt(MenuManager.KeyLastLaunched, 0);
        int unlockedClamped = Mathf.Clamp(unlocked, 1, n);

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("== Test Tools ==", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Unlocked: {unlockedClamped} / {n}{(unlocked > n ? " (all cleared)" : "")}\n" +
            $"Pop-in already played: {revealed}\n" +
            $"Last launched: level {last + 1}\n" +
            $"Menu scene: {MenuManager.MenuScene}" +
            (Application.isPlaying ? $"\nCurrent front: level {m.FrontIndex + 1}" : ""),
            MessageType.Info);

        if (GUILayout.Button("Reset Progress (simulate first launch)"))
            Set(m, 1, 0, 0);

        if (GUILayout.Button("Simulate Clear: Unlock Next Level (with pop-in)"))
            Set(m, Mathf.Min(unlockedClamped + 1, n), Mathf.Min(revealed, unlockedClamped), last);

        if (GUILayout.Button("Replay Latest Level Pop-in"))
            Set(m, unlockedClamped, unlockedClamped - 1, last);

        EditorGUILayout.Space(4);
        targetLevel = EditorGUILayout.IntSlider("Unlock Up To Level", targetLevel, 1, n);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply (with pop-in)"))
                Set(m, targetLevel, Mathf.Min(revealed, targetLevel), last);
            if (GUILayout.Button("Apply (no pop-in)"))
                Set(m, targetLevel, targetLevel, last);
        }

        if (GUILayout.Button("Unlock All (with pop-in)"))
            Set(m, n, Mathf.Min(revealed, n), last);

        EditorGUILayout.Space(4);
        if (Application.isPlaying)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("< Left")) m.Step(-1);
                if (GUILayout.Button("Right >")) m.Step(1);
            }
            if (GUILayout.Button("Press Front Level (loads scene)")) m.TrySelect(m.FrontIndex, false);
        }
        else
        {
            EditorGUILayout.HelpBox("In Play Mode the buttons above take effect immediately; outside Play Mode they only change the saved progress.", MessageType.None);
        }

        // Validate scene setup
        for (int i = 0; i < n; i++)
        {
            var lv = m.levels[i];
            if (lv == null) continue;
            if (lv.button == null)
                EditorGUILayout.HelpBox($"Level {i + 1} ({lv.label}) has no Button assigned", MessageType.Warning);
            if (string.IsNullOrEmpty(lv.sceneName))
                EditorGUILayout.HelpBox($"Level {i + 1} ({lv.label}) has no sceneName set", MessageType.Warning);
            else if (!SceneInBuild(lv.sceneName))
                EditorGUILayout.HelpBox($"Level {i + 1} scene \"{lv.sceneName}\" is not in the Build Settings scene list", MessageType.Error);
        }
    }

    static void Set(MenuManager m, int unlocked, int revealed, int last)
    {
        PlayerPrefs.SetInt(MenuManager.KeyUnlocked, unlocked);
        PlayerPrefs.SetInt(MenuManager.KeyRevealed, revealed);
        PlayerPrefs.SetInt(MenuManager.KeyLastLaunched, last);
        PlayerPrefs.Save();
        if (Application.isPlaying) m.Rebuild(true);
    }

    static bool SceneInBuild(string name)
    {
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && System.IO.Path.GetFileNameWithoutExtension(s.path) == name) return true;
        return false;
    }
}
#endif
// MenuManager.cs
// Menu screen: pseudo-3D circular level carousel + level unlock saving (PlayerPrefs)
// + question marks for locked levels + unlock sequence + floating effect + Inspector test tools.
//
// Quick setup:
// 1. Put all xxxOption buttons under the SAME parent (needed for front/back draw ordering).
// 2. Create one "questionMark" UI object (e.g. an Image). It is used as a template:
//    one copy is created for every level at runtime, and the template itself is hidden.
// 3. Add this component to any object in the menu scene (e.g. an empty "MenuManager").
//    When first added, it auto-finds typistOption ... supermanOption and questionMark by name.
// 4. Fill in each level's Scene name in "levels" (scenes must be in Build Settings).
// 5. To go back to the menu from a level, call:
//      MenuManager.CompleteCurrentLevelAndReturn();   // level cleared -> unlock next, back to menu
//      MenuManager.ReturnToMenu();                    // back to menu without clearing
//
// Unlock sequence (per newly unlocked level):
//   rotate to its slot -> revealDelay -> question mark hides while neighbours close the gap
//   -> gapWaitTime -> level button pops in while the space opens again -> revealInterval -> next

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
    // NOTE: new values are appended at the end so existing Inspector settings keep their meaning.
    public enum EaseType { Linear, InQuad, OutQuad, InOutQuad, OutCubic, InOutCubic, OutBack, OutElastic, OutBounce, Custom, InCubic, InBack }
    public enum RevealEffect { Scale, Fade, ScaleAndFade, DropIn, RiseUp, SpinIn, FlipIn }
    public enum HideEffect { Shrink, Fade, ShrinkAndFade, FlyUp, FallDown, SpinOut, FlipOut }

    enum SlotState { Locked, Hiding, Gap, Revealing, Unlocked }

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
    public const string KeyUnlocked = "MenuManager_UnlockedCount";     // number of unlocked levels (1 = only level 1)
    public const string KeyRevealed = "MenuManager_RevealedCount";     // number of levels whose unlock sequence has played
    public const string KeyLastLaunched = "MenuManager_LastLaunched";  // last level launched from the menu (0-based)
    public const string KeyMenuScene = "MenuManager_MenuScene";        // menu scene name

    static readonly string[] DefaultNames =
    {
        "typistOption", "illustratorOption", "composerOption", "factoryOption",
        "engineerOption", "scamOption", "supermanOption"
    };
    const string DefaultQuestionMarkName = "questionMark";

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
    [Tooltip("Scale of the front-most item")]
    public float frontScale = 1f;
    [Tooltip("Scale of the back-most item")]
    public float backScale = 0.55f;

    [Header("Question Marks (locked levels)")]
    [Tooltip("Template object for locked levels. One copy is created per level at runtime; the template itself is hidden")]
    public GameObject questionMarkTemplate;
    [Tooltip("Size of a question mark relative to a level button (1 = same size). Also sets how much ring space it takes")]
    public float questionMarkSize = 0.6f;
    [Tooltip("Can the player rotate onto question marks? (they can never be pressed) If off, switching skips over them")]
    public bool questionMarksNavigable = true;

    [Header("Left / Right Switching")]
    [Tooltip("Seconds to rotate to the next item")]
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

    [Header("Dimming (non-front items)")]
    [Tooltip("Color multiplied onto dimmed items (alpha is multiplied too, so it can also add transparency)")]
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
    [Tooltip("Phase offset between items so they don't float in sync")]
    public float phaseSpread = 1.1f;

    [Header("Unlock Sequence - 1. Rotate")]
    [Tooltip("Delay after rotating to the new level's slot before the question mark starts hiding")]
    public float revealDelay = 0.35f;

    [Header("Unlock Sequence - 2. Question Mark Hide + Close Gap")]
    [Tooltip("How the question mark disappears")]
    public HideEffect questionMarkHideEffect = HideEffect.ShrinkAndFade;
    [Tooltip("Hide easing (InBack = grows slightly, then shrinks away)")]
    public EaseType questionMarkHideEase = EaseType.InBack;
    [Tooltip("Used when questionMarkHideEase is Custom")]
    public AnimationCurve questionMarkHideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Seconds for the question mark to disappear (neighbours close the gap during the same time)")]
    public float questionMarkHideDuration = 0.4f;
    [Tooltip("Easing for closing / opening ring space")]
    public EaseType spaceEase = EaseType.InOutCubic;
    [Tooltip("Used when spaceEase is Custom")]
    public AnimationCurve spaceCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Unlock Sequence - 3. Wait")]
    [Tooltip("Seconds to wait after the gap closes before the new level appears")]
    public float gapWaitTime = 0.3f;

    [Header("Unlock Sequence - 4. Level Pop-in + Open Space")]
    [Tooltip("Type of pop-in effect")]
    public RevealEffect revealEffect = RevealEffect.ScaleAndFade;
    [Tooltip("Pop-in easing (OutBack / OutElastic / OutBounce give a bouncy feel)")]
    public EaseType revealEase = EaseType.OutBack;
    [Tooltip("Used when revealEase is Custom")]
    public AnimationCurve revealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Seconds for the level to pop in (the space opens during the same time)")]
    public float revealDuration = 0.6f;

    [Header("Unlock Sequence - Shared")]
    [Tooltip("Gap between sequences when several levels unlock at once")]
    public float revealInterval = 0.25f;
    [Tooltip("Offset distance for DropIn / RiseUp / FlyUp / FallDown")]
    public float revealOffsetDistance = 250f;
    [Tooltip("Rotation angle for SpinIn / SpinOut")]
    public float revealSpinAngle = 360f;
    [Tooltip("Block input while an unlock sequence is playing")]
    public bool lockInputDuringReveal = true;

    [Header("Loading / Time")]
    [Tooltip("Delay before switching scene after pressing (so the press feedback is visible)")]
    public float loadDelay = 0.15f;
    [Tooltip("Use time that ignores Time.timeScale")]
    public bool useUnscaledTime = true;

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip slideSfx;
    public AudioClip questionMarkHideSfx;
    public AudioClip revealSfx;
    public AudioClip confirmSfx;

    [Header("Superman Flag")]
    [Tooltip("Name of the button GameObject that sets SupermanSelected (case-insensitive)")]
    public string supermanButtonName = "supermanOption";
    [Tooltip("If true, SupermanSelected stays true after reaching Superman, even when switching away")]
    public bool keepSupermanFlag = false;

    /// <summary>
    /// True while the menu is switched to supermanOption (front-most, unlocked).
    /// Readable from anywhere: MenuManager.SupermanSelected
    /// </summary>
    public static bool SupermanSelected { get; private set; }

    /// <summary>Raised whenever SupermanSelected changes (true / false). Remember to unsubscribe in OnDisable.</summary>
    public static event System.Action<bool> OnSupermanSelectedChanged;

    // ------------------------- Runtime state -------------------------
    class Visual
    {
        public GameObject go;
        public RectTransform rect;
        public CanvasGroup group;
        public Graphic[] graphics;
        public Color[] baseColors;
        public Vector3 baseScale;
        public float dim;
        public float appliedDim = -1f;
        public float depth;
        public float phase;
        // effect values written by the unlock sequence
        public Vector3 fxScale = Vector3.one;
        public float fxAlpha = 1f;
        public Vector2 fxOffset;
        public float fxRot;

        public void ResetFx()
        {
            fxScale = Vector3.one;
            fxAlpha = 1f;
            fxOffset = Vector2.zero;
            fxRot = 0f;
        }
    }

    const float TAU = Mathf.PI * 2f;

    Visual[] options;       // level buttons
    Visual[] marks;         // question marks
    SlotState[] states;
    float[] spaceProgress;  // 0..1 progress of closing / opening space for Hiding / Revealing slots
    float[] weights;        // ring space each slot takes this frame
    float[] centers;        // center angle of each slot this frame
    int slotCount;
    int unlockedCount;

    float ringPos, ringTarget; // ring rotation, measured in slots
    float slideFromPos, slideTimer;
    bool sliding;
    bool inputLocked, isLoading;
    bool pointerTracking, swipedThisPress;
    Vector2 pointerStart;
    Coroutine revealRoutine;
    Canvas canvas;
    bool canSort;
    int siblingBase;
    int supermanIndex = -1;
    readonly List<Visual> sortBuffer = new List<Visual>();
    static readonly System.Comparison<Visual> DepthComparer = (a, b) => a.depth.CompareTo(b.depth);

    float Dt => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    /// <summary>Index of the current front slot (0-based), or -1 if none.</summary>
    public int FrontIndex => slotCount <= 0 ? -1 : IMod(Mathf.RoundToInt(ringTarget), slotCount);
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
        SupermanSelected = false;
        supermanIndex = -1;

        slotCount = levels != null ? levels.Length : 0;
        options = new Visual[slotCount];
        marks = new Visual[slotCount];
        states = new SlotState[slotCount];
        spaceProgress = new float[slotCount];
        weights = new float[slotCount];
        centers = new float[slotCount];

        Transform commonParent = null;
        RectTransform anchorRef = null;
        canSort = true;
        siblingBase = int.MaxValue;

        // Level buttons
        for (int i = 0; i < slotCount; i++)
        {
            Button b = levels[i] != null ? levels[i].button : null;
            if (b == null)
            {
                Debug.LogWarning($"[MenuManager] Level {i + 1} has no Button assigned.", this);
                continue;
            }

            var v = CreateVisual(b.gameObject, i * phaseSpread);
            options[i] = v;

            // Disable EventSystem arrow-key navigation (so other buttons can't be selected)
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;

            int idx = i;
            b.onClick.AddListener(() => TrySelect(idx, true));

            if (supermanIndex < 0 && (NameMatches(b.gameObject.name) || NameMatches(levels[i].label)))
                supermanIndex = i;

            if (commonParent == null) commonParent = v.rect.parent;
            else if (commonParent != v.rect.parent) canSort = false;
            if (anchorRef == null) anchorRef = v.rect;
            siblingBase = Mathf.Min(siblingBase, v.rect.GetSiblingIndex());

            if (canvas == null) canvas = b.GetComponentInParent<Canvas>();
        }

        // Question marks (one copy per level, placed under the same parent as the buttons)
        if (questionMarkTemplate != null && commonParent != null)
        {
            bool templateInScene = questionMarkTemplate.scene.IsValid();
            for (int i = 0; i < slotCount; i++)
            {
                GameObject go = Instantiate(questionMarkTemplate, commonParent, false);
                go.name = $"{questionMarkTemplate.name}_{i + 1}";
                var rect = go.transform as RectTransform;
                if (rect == null)
                {
                    Debug.LogWarning("[MenuManager] questionMarkTemplate must be a UI object (RectTransform).", this);
                    Destroy(go);
                    break;
                }
                if (anchorRef != null)
                {
                    rect.anchorMin = anchorRef.anchorMin;
                    rect.anchorMax = anchorRef.anchorMax;
                    rect.pivot = anchorRef.pivot;
                }
                var m = CreateVisual(go, i * phaseSpread + 0.5f);
                m.group.blocksRaycasts = false; // question marks can never be clicked
                marks[i] = m;
            }
            if (templateInScene) questionMarkTemplate.SetActive(false);
        }
        else if (questionMarkTemplate == null)
        {
            Debug.LogWarning("[MenuManager] No questionMarkTemplate assigned; locked levels will be hidden.", this);
        }

        if (!canSort)
            Debug.LogWarning("[MenuManager] Level buttons are not under the same parent; front/back ordering is disabled.", this);

        if (supermanIndex < 0 && slotCount > 0)
        {
            var names = new List<string>();
            for (int i = 0; i < slotCount; i++)
            {
                Button b = levels[i] != null ? levels[i].button : null;
                names.Add($"{i + 1}: " + (b != null ? $"\"{b.gameObject.name}\"" : "(no Button)"));
            }
            Debug.LogWarning($"[MenuManager] No button named \"{supermanButtonName}\" found; SupermanSelected will never be true.\n" +
                             "Assigned buttons -> " + string.Join(", ", names), this);
        }
    }

    Visual CreateVisual(GameObject go, float phase)
    {
        var v = new Visual();
        v.go = go;
        v.rect = (RectTransform)go.transform;
        v.group = go.GetComponent<CanvasGroup>();
        if (v.group == null) v.group = go.AddComponent<CanvasGroup>();
        v.graphics = go.GetComponentsInChildren<Graphic>(true);
        v.baseColors = new Color[v.graphics.Length];
        for (int j = 0; j < v.graphics.Length; j++) v.baseColors[j] = v.graphics[j].color;
        v.baseScale = v.rect.localScale;
        v.phase = phase;
        return v;
    }

    void Start()
    {
        Rebuild(false);
    }

    void OnValidate()
    {
        // apply color changes immediately
        if (options != null) foreach (var v in options) if (v != null) v.appliedDim = -1f;
        if (marks != null) foreach (var v in marks) if (v != null) v.appliedDim = -1f;
    }

    void Update()
    {
        if (slotCount == 0) return;
        float dt = Dt;
        UpdateSlide(dt);
        HandleInput();
        UpdateSupermanFlag();
        ApplyLayout(dt);
    }

    void UpdateSupermanFlag()
    {
        bool onSuperman = supermanIndex >= 0 && FrontIndex == supermanIndex &&
                          (states[supermanIndex] == SlotState.Unlocked || states[supermanIndex] == SlotState.Revealing);
        bool value = keepSupermanFlag ? (SupermanSelected || onSuperman) : onSuperman;
        if (value == SupermanSelected) return;
        SupermanSelected = value;
        OnSupermanSelectedChanged?.Invoke(value);
    }

    void LateUpdate()
    {
        // Never let a level button stay "selected" by the EventSystem
        // (prevents Enter from triggering a non-front button or firing twice)
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null || options == null) return;
        foreach (var v in options)
            if (v != null && es.currentSelectedGameObject == v.go)
            {
                es.SetSelectedGameObject(null);
                break;
            }
    }

    // ------------------------- Progress / rebuild -------------------------
    /// <summary>Rebuilds the menu from PlayerPrefs; plays the unlock sequence for any unlocks not yet revealed.</summary>
    public void Rebuild(bool keepCurrentFocus)
    {
        if (slotCount == 0) return;

        int prevFront = FrontIndex;
        if (revealRoutine != null) { StopCoroutine(revealRoutine); revealRoutine = null; }
        inputLocked = false;
        isLoading = false;

        unlockedCount = Mathf.Clamp(PlayerPrefs.GetInt(KeyUnlocked, 1), 1, slotCount);
        int revealed = Mathf.Clamp(PlayerPrefs.GetInt(KeyRevealed, 0), 0, unlockedCount);

        for (int i = 0; i < slotCount; i++)
        {
            bool open = i < revealed;
            states[i] = open ? SlotState.Unlocked : SlotState.Locked;
            spaceProgress[i] = 0f;
            if (options[i] != null) { options[i].ResetFx(); options[i].go.SetActive(open); }
            if (marks[i] != null) { marks[i].ResetFx(); marks[i].go.SetActive(!open); }
        }

        int focus;
        if (keepCurrentFocus && prevFront >= 0) focus = prevFront;
        else
        {
            focus = PlayerPrefs.GetInt(KeyLastLaunched, 0);
            focus = revealed > 0 ? Mathf.Clamp(focus, 0, revealed - 1) : 0;
        }
        SnapTo(focus);

        int front = FrontIndex;
        for (int i = 0; i < slotCount; i++)
        {
            float d = (i == front) ? 0f : 1f;
            if (options[i] != null) { options[i].dim = d; options[i].appliedDim = -1f; }
            if (marks[i] != null) { marks[i].dim = d; marks[i].appliedDim = -1f; }
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
            // 1. Rotate to the slot
            while (sliding) yield return null;
            if (FrontIndex != k)
            {
                StartSlide(NearestTarget(k));
                while (sliding) yield return null;
            }
            yield return Wait(revealDelay);

            // 2. Question mark hides while the neighbours close the gap
            var m = marks[k];
            states[k] = SlotState.Hiding;
            spaceProgress[k] = 0f;
            if (m != null)
            {
                Play(questionMarkHideSfx);
                float t = 0f;
                while (t < questionMarkHideDuration)
                {
                    t += Dt;
                    float p = Mathf.Clamp01(t / questionMarkHideDuration);
                    spaceProgress[k] = p;
                    ApplyHideFx(m, p);
                    yield return null;
                }
                m.go.SetActive(false);
                m.ResetFx();
            }
            states[k] = SlotState.Gap;
            spaceProgress[k] = 0f;

            // 3. Wait
            yield return Wait(gapWaitTime);

            // 4. Level pops in while the space opens
            var o = options[k];
            states[k] = SlotState.Revealing;
            spaceProgress[k] = 0f;
            if (o != null)
            {
                o.dim = (FrontIndex == k) ? 0f : 1f;
                o.appliedDim = -1f;
                ApplyRevealFx(o, 0f);
                o.go.SetActive(true);
            }
            Play(revealSfx);
            float rt = 0f;
            while (rt < revealDuration)
            {
                rt += Dt;
                float p = Mathf.Clamp01(rt / revealDuration);
                spaceProgress[k] = p;
                if (o != null) ApplyRevealFx(o, p);
                yield return null;
            }
            if (o != null) o.ResetFx();
            states[k] = SlotState.Unlocked;
            spaceProgress[k] = 0f;

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

    void ApplyRevealFx(Visual v, float p)
    {
        v.ResetFx();
        float e = Ease(revealEase, p, revealCurve);
        float fade = Mathf.Clamp01(Ease(EaseType.OutQuad, p, null));
        switch (revealEffect)
        {
            case RevealEffect.Scale: v.fxScale = Vector3.one * e; break;
            case RevealEffect.Fade: v.fxAlpha = Mathf.Clamp01(e); break;
            case RevealEffect.ScaleAndFade: v.fxScale = Vector3.one * e; v.fxAlpha = fade; break;
            case RevealEffect.DropIn: v.fxOffset = new Vector2(0f, (1f - e) * revealOffsetDistance); v.fxAlpha = fade; break;
            case RevealEffect.RiseUp: v.fxOffset = new Vector2(0f, -(1f - e) * revealOffsetDistance); v.fxAlpha = fade; break;
            case RevealEffect.SpinIn: v.fxScale = Vector3.one * e; v.fxRot = (1f - e) * revealSpinAngle; v.fxAlpha = fade; break;
            case RevealEffect.FlipIn: v.fxScale = new Vector3(e, 1f, 1f); v.fxAlpha = fade; break;
        }
    }

    void ApplyHideFx(Visual m, float p)
    {
        m.ResetFx();
        float k = 1f - Ease(questionMarkHideEase, p, questionMarkHideCurve); // 1 -> 0
        float s = Mathf.Max(0f, k);
        float fade = 1f - Mathf.Clamp01(Ease(EaseType.InQuad, p, null));
        switch (questionMarkHideEffect)
        {
            case HideEffect.Shrink: m.fxScale = Vector3.one * s; break;
            case HideEffect.Fade: m.fxAlpha = Mathf.Clamp01(k); break;
            case HideEffect.ShrinkAndFade: m.fxScale = Vector3.one * s; m.fxAlpha = fade; break;
            case HideEffect.FlyUp: m.fxOffset = new Vector2(0f, (1f - k) * revealOffsetDistance); m.fxAlpha = fade; break;
            case HideEffect.FallDown: m.fxOffset = new Vector2(0f, -(1f - k) * revealOffsetDistance); m.fxAlpha = fade; break;
            case HideEffect.SpinOut: m.fxScale = Vector3.one * s; m.fxRot = (1f - k) * revealSpinAngle; m.fxAlpha = fade; break;
            case HideEffect.FlipOut: m.fxScale = new Vector3(s, 1f, 1f); m.fxAlpha = fade; break;
        }
    }

    // ------------------------- Rotation -------------------------
    /// <summary>dir = -1 brings the left item to the front, +1 brings the right item to the front.</summary>
    public void Step(int dir)
    {
        if (slotCount <= 1 || inputLocked || isLoading) return;
        int cur = FrontIndex;
        for (int s = 1; s < slotCount; s++)
        {
            int j = IMod(cur + dir * s, slotCount);
            if (IsNavigable(j))
            {
                StartSlide(ringTarget + dir * s);
                Play(slideSfx);
                return;
            }
        }
    }

    bool IsNavigable(int i)
    {
        if (states[i] == SlotState.Unlocked && options[i] != null) return true;
        if (questionMarksNavigable && states[i] == SlotState.Locked && marks[i] != null) return true;
        return false;
    }

    void SnapTo(int index)
    {
        ringPos = ringTarget = index;
        sliding = false;
    }

    void StartSlide(float target)
    {
        slideFromPos = ringPos;
        ringTarget = target;
        slideTimer = 0f;
        sliding = slideDuration > 0f;
        if (!sliding)
        {
            ringPos = ringTarget;
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
        if (p >= 1f)
        {
            ringPos = ringTarget;
            sliding = false;
            NormalizeRing();
        }
    }

    void NormalizeRing()
    {
        ringTarget = FMod(ringTarget, Mathf.Max(1, slotCount));
        ringPos = ringTarget;
    }

    float NearestTarget(int index)
    {
        int count = slotCount;
        float cur = FMod(ringPos, count);
        float delta = index - cur;
        if (delta > count * 0.5f) delta -= count;
        if (delta < -count * 0.5f) delta += count;
        return ringPos + delta;
    }

    // ------------------------- Input -------------------------
    void HandleInput()
    {
        if (slotCount == 0 || isLoading) { pointerTracking = false; return; }

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
                int dir = dx < 0f ? 1 : -1; // swipe left -> right-hand item comes to the front
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

    /// <summary>Tries to launch a level (only succeeds for the front, unlocked level).</summary>
    public void TrySelect(int index, bool fromPointer)
    {
        if (index < 0 || index >= slotCount || index != FrontIndex) return;
        if (isLoading || inputLocked) return;
        if (sliding && !allowClickWhileSliding) return;
        if (fromPointer && swipedThisPress) return;
        if (states[index] != SlotState.Unlocked || options[index] == null) return;

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
    float SlotWeight(int i)
    {
        float lockedW = marks[i] != null ? Mathf.Max(0f, questionMarkSize) : 0f;
        float openW = options[i] != null ? 1f : 0f;
        switch (states[i])
        {
            case SlotState.Locked: return lockedW;
            case SlotState.Hiding: return lockedW * Mathf.Max(0f, 1f - Ease(spaceEase, spaceProgress[i], spaceCurve));
            case SlotState.Gap: return 0f;
            case SlotState.Revealing: return openW * Mathf.Max(0f, Ease(spaceEase, spaceProgress[i], spaceCurve));
            default: return openW;
        }
    }

    Visual ActiveVisual(int i)
    {
        switch (states[i])
        {
            case SlotState.Locked:
            case SlotState.Hiding: return marks[i];
            case SlotState.Revealing:
            case SlotState.Unlocked: return options[i];
            default: return null;
        }
    }

    // Angle of a (fractional) slot position, interpolating between slot centers
    float FocusAngle(float f)
    {
        float fl = Mathf.Floor(f);
        float t = f - fl;
        int j0 = IMod((int)fl, slotCount);
        int j1 = IMod(j0 + 1, slotCount);
        float a0 = centers[j0];
        float a1 = centers[j1];
        if (j1 <= j0) a1 += TAU;
        return Mathf.LerpUnclamped(a0, a1, t);
    }

    void ApplyLayout(float dt)
    {
        if (slotCount == 0) return;

        // Ring space: each slot takes space proportional to its size
        float total = 0f;
        for (int i = 0; i < slotCount; i++) { weights[i] = SlotWeight(i); total += weights[i]; }
        if (total > 0.0001f)
        {
            float cum = 0f;
            for (int i = 0; i < slotCount; i++)
            {
                centers[i] = (cum + weights[i] * 0.5f) / total * TAU;
                cum += weights[i];
            }
        }
        else
        {
            for (int i = 0; i < slotCount; i++) centers[i] = 0f;
        }

        float focusAngle = FocusAngle(ringPos);
        float time = Now;
        int front = FrontIndex;
        sortBuffer.Clear();

        for (int i = 0; i < slotCount; i++)
        {
            Visual v = ActiveVisual(i);
            if (v == null) continue;
            bool isMark = v == marks[i];

            // Ring position
            float a = centers[i] - focusAngle;
            float cos = Mathf.Cos(a);
            float depth = (cos + 1f) * 0.5f; // 1 = front, 0 = back
            v.depth = depth;
            Vector2 pos = ringCenter + new Vector2(Mathf.Sin(a) * ringRadius.x, -cos * ringRadius.y);
            float ringScale = Mathf.Lerp(backScale, frontScale, depth);
            float size = isMark ? questionMarkSize : 1f;

            // Floating
            Vector2 floatOff = Vector2.zero;
            float sway = 0f, breathe = 1f;
            if (enableFloat)
            {
                float ph = v.phase;
                float w = time * floatFrequency * TAU + ph;
                floatOff = new Vector2(Mathf.Sin(w * 0.5f + ph) * floatAmplitude.x, Mathf.Sin(w) * floatAmplitude.y);
                sway = Mathf.Sin(time * swayFrequency * TAU + ph * 1.3f) * swayAngle;
                breathe = 1f + Mathf.Sin(time * breatheFrequency * TAU + ph * 0.7f) * breatheAmount;
            }

            v.rect.anchoredPosition = pos + floatOff + v.fxOffset;
            v.rect.localScale = Vector3.Scale(v.baseScale * (ringScale * size * breathe), v.fxScale);
            v.rect.localRotation = Quaternion.Euler(0f, 0f, sway + v.fxRot);
            v.group.alpha = v.fxAlpha;

            // Only the front, unlocked level button receives pointer clicks
            v.group.blocksRaycasts = !isMark && i == front && states[i] == SlotState.Unlocked && !inputLocked && !isLoading;

            // Dimming
            float targetDim = (i == front) ? 0f : 1f;
            v.dim = dimDuration > 0f ? Mathf.MoveTowards(v.dim, targetDim, dt / dimDuration) : targetDim;
            if (!Mathf.Approximately(v.dim, v.appliedDim)) ApplyColors(v);

            sortBuffer.Add(v);
        }

        // Front/back occlusion: closer items get a higher sibling index (drawn later, on top)
        if (canSort && sortBuffer.Count > 1)
        {
            sortBuffer.Sort(DepthComparer);
            for (int k = 0; k < sortBuffer.Count; k++)
            {
                var rect = sortBuffer[k].rect;
                int want = siblingBase + k;
                if (rect.GetSiblingIndex() != want) rect.SetSiblingIndex(want);
            }
        }
    }

    void ApplyColors(Visual v)
    {
        float k = Mathf.SmoothStep(0f, 1f, v.dim);
        for (int j = 0; j < v.graphics.Length; j++)
        {
            if (v.graphics[j] == null) continue;
            Color c = v.baseColors[j];
            v.graphics[j].color = Color.Lerp(c, c * dimColor, k);
        }
        v.appliedDim = v.dim;
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
        if (questionMarkTemplate == null)
            questionMarkTemplate = GameObject.Find(DefaultQuestionMarkName);
    }

    // Case-insensitive, ignores leading/trailing spaces
    bool NameMatches(string s)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(supermanButtonName)) return false;
        return string.Equals(s.Trim(), supermanButtonName.Trim(), System.StringComparison.OrdinalIgnoreCase);
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
            case EaseType.InCubic: return x * x * x;
            case EaseType.OutCubic: return 1f - Mathf.Pow(1f - x, 3f);
            case EaseType.InOutCubic: return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
            case EaseType.InBack:
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    return c3 * x * x * x - c1 * x * x;
                }
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
            $"Unlock sequence already played: {revealed}\n" +
            $"Last launched: level {last + 1}\n" +
            $"Menu scene: {MenuManager.MenuScene}" +
            (Application.isPlaying ? $"\nCurrent front: slot {m.FrontIndex + 1}\nSupermanSelected: {MenuManager.SupermanSelected}" : ""),
            MessageType.Info);

        if (m.questionMarkTemplate == null)
            EditorGUILayout.HelpBox("No questionMarkTemplate assigned; locked levels will be hidden.", MessageType.Warning);

        if (GUILayout.Button("Reset Progress (simulate first launch)"))
            Set(m, 1, 0, 0);

        if (GUILayout.Button("Simulate Clear: Unlock Next Level (with sequence)"))
            Set(m, Mathf.Min(unlockedClamped + 1, n), Mathf.Min(revealed, unlockedClamped), last);

        if (GUILayout.Button("Replay Latest Level Unlock Sequence"))
            Set(m, unlockedClamped, unlockedClamped - 1, last);

        EditorGUILayout.Space(4);
        targetLevel = EditorGUILayout.IntSlider("Unlock Up To Level", targetLevel, 1, n);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply (with sequence)"))
                Set(m, targetLevel, Mathf.Min(revealed, targetLevel), last);
            if (GUILayout.Button("Apply (no sequence)"))
                Set(m, targetLevel, targetLevel, last);
        }

        if (GUILayout.Button("Unlock All (with sequence)"))
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
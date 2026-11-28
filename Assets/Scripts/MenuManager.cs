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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
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
    public enum IntroOrder { FrontToBack, LeftToRight, RightToLeft, SlotOrder, AllAtOnce }
    public enum ZoomFocus { SelectedLevel, ScreenCenter }

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

    [Header("Unlock Sequence - 0. Stay On Previous Level")]
    [Tooltip("Before the unlock sequence starts, wait until the IrisBackTransition object has been destroyed")]
    public bool unlockWaitForIrisTransition = true;
    [Tooltip("Seconds to stay on the previously played level before rotating to the newly unlocked one")]
    public float unlockHoldOnPreviousLevel = 0.6f;

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

    [Header("Press Feedback")]
    [Tooltip("Scale multiplier while a level button is pressed (0.88 = 12% smaller). Works for both mouse and Enter")]
    public float pressScale = 0.88f;
    [Tooltip("Seconds to shrink when pressed")]
    public float pressDownDuration = 0.06f;
    [Tooltip("Seconds to grow back when released")]
    public float pressReleaseDuration = 0.15f;
    [Tooltip("Run the level's action when the mouse button / Enter is RELEASED. Holding keeps the button shrunk. If off, the action runs as soon as it is pressed")]
    public bool activateOnRelease = true;
    [Tooltip("Only used when activateOnRelease is off: how long the button stays pressed after Enter")]
    public float keyboardPressHoldTime = 0.12f;
    [Tooltip("When chosen with Enter, also show the Button's own Pressed state (Color Tint / Sprite Swap / Animation)")]
    public bool showButtonPressedStateOnEnter = true;

    [Header("Loading / Time")]
    [Tooltip("Delay before switching scene after a level is chosen (press feedback and zoom play during this time)")]
    public float loadDelay = 0.15f;

    [Header("Level Select Zoom")]
    [Tooltip("Zoom in after a level is chosen, before the scene loads")]
    public bool enableSelectZoom = true;
    [Tooltip("Zoom amount (2 = twice as close)")]
    public float selectZoomAmount = 2f;
    [Tooltip("Seconds of zooming. Timed to END when the scene loads: it starts (loadDelay - selectZoomDuration) seconds after the level is chosen. If longer than loadDelay, it starts immediately and is cut off by the scene load")]
    public float selectZoomDuration = 1.5f;
    public EaseType selectZoomEase = EaseType.InCubic;
    [Tooltip("Used when selectZoomEase is Custom")]
    public AnimationCurve selectZoomCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Zoom toward the chosen level, or toward the screen center")]
    public ZoomFocus selectZoomFocus = ZoomFocus.SelectedLevel;
    [Tooltip("Zoom the camera (scene / world objects)")]
    public bool selectZoomCamera = true;
    [Tooltip("Zoom the Canvas (UI). Skipped for World Space canvases, since the camera zoom already covers them")]
    public bool selectZoomCanvas = true;
    [Tooltip("UI object to scale for the Canvas zoom. Empty = everything in the root Canvas is wrapped in a 'ZoomRoot' automatically")]
    public RectTransform zoomCanvasRoot;

    [Header("Level Select Zoom - Fisheye (URP Lens Distortion)")]
    [Tooltip("Add a fisheye / wide-angle distortion during the select zoom when Superman is chosen")]
    public bool fisheyeOnSuperman = true;
    [Tooltip("Use the fisheye for every level, not only Superman")]
    public bool fisheyeOnAllLevels = false;
    [Tooltip("Distortion strength at the end of the zoom. Positive and negative bend the image in opposite directions (bulge vs. pinch): try both")]
    [Range(-1f, 1f)] public float fisheyeIntensity = -0.6f;
    [Tooltip("Lens Distortion 'Scale' at the end of the zoom (below 1 shrinks the image to hide stretched edges, above 1 crops in)")]
    [Range(0.01f, 5f)] public float fisheyeScale = 1f;
    [Tooltip("Speed curve of the distortion (uses the same timing as the zoom: selectZoomDuration)")]
    public EaseType fisheyeEase = EaseType.InCubic;
    [Tooltip("Used when fisheyeEase is Custom")]
    public AnimationCurve fisheyeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Global Volume used for the effect (empty = one is created automatically)")]
    public Volume fisheyeVolume;
    [Tooltip("Post-processing can't distort a Screen Space - Overlay Canvas. If on, the Canvas is switched to Screen Space - Camera (drawn on top) when the fisheye starts, so the UI is distorted too")]
    public bool fisheyeMoveCanvasToCamera = true;
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

    [Header("Iris Back Transition (when returning to the menu)")]
    [Tooltip("Prefab spawned as the last child of the Canvas (drawn on top of everything) whenever the menu loads, e.g. an iris-open transition")]
    [FormerlySerializedAs("fadeInPrefab")]
    public GameObject irisBackTransitionPrefab;
    [Tooltip("Canvas to spawn it in (empty = the root Canvas the level buttons are in)")]
    [FormerlySerializedAs("fadeInCanvas")]
    public Canvas irisBackTransitionCanvas;
    [Tooltip("Seconds the transition stays on its first frame (child animations paused) before they start playing (real time)")]
    public float irisBackTransitionHoldTime = 0.5f;
    [Tooltip("Seconds AFTER the animations start playing before the object is destroyed (real time, ignores Time.timeScale)")]
    [FormerlySerializedAs("fadeInDestroyDelay")]
    public float irisBackTransitionDestroyDelay = 1.5f;
    [Tooltip("Also spawn it the first time the menu loads after the game starts (otherwise only when coming back from another scene)")]
    [FormerlySerializedAs("fadeInOnGameStart")]
    public bool irisBackTransitionOnGameStart = false;

    [Header("Title Screen (first game start only, scene changes don't count)")]
    [Tooltip("Show the logo + press-any-key screen the first time the game starts")]
    public bool enableTitleScreen = true;
    [Tooltip("Logo object in the Canvas")]
    public RectTransform logo;
    [Tooltip("Press-any-key object in the Canvas")]
    public RectTransform pressAny;
    [Tooltip("Keep showing the logo (at its moved position) after the title screen and when returning to the menu")]
    public bool showLogoAfterIntro = true;
    [Tooltip("Draw helper lines for the logo target and the intro camera in the Scene view")]
    public bool showTitleGizmos = true;

    [Header("Title - Logo Floating")]
    [Tooltip("Float offset amplitude (X = horizontal, Y = vertical, in UI pixels)")]
    public Vector2 logoFloatAmplitude = new Vector2(0f, 15f);
    [Tooltip("Float frequency (cycles per second)")]
    public float logoFloatFrequency = 0.4f;
    [Tooltip("Sway angle (degrees)")]
    public float logoSwayAngle = 2f;
    [Tooltip("Sway frequency (cycles per second)")]
    public float logoSwayFrequency = 0.25f;
    [Tooltip("Breathing scale amount (0.02 = +/-2%)")]
    public float logoBreatheAmount = 0.02f;
    [Tooltip("Breathing frequency (cycles per second)")]
    public float logoBreatheFrequency = 0.5f;

    [Header("Title - Press Any Blink")]
    [Tooltip("Seconds pressAny stays visible in each blink")]
    public float pressAnyOnTime = 0.8f;
    [Tooltip("Seconds pressAny stays hidden in each blink")]
    public float pressAnyOffTime = 0.4f;
    [Tooltip("Seconds to fade between visible and hidden (0 = hard on/off)")]
    public float pressAnyBlinkFadeTime = 0.2f;
    [Range(0f, 1f)] public float pressAnyMinAlpha = 0f;
    [Range(0f, 1f)] public float pressAnyMaxAlpha = 1f;
    [Tooltip("Ignore key presses for this many seconds after the title appears")]
    public float pressAnyInputDelay = 0.3f;
    [Tooltip("Mouse clicks also count as 'any key'")]
    public bool pressAnyAcceptsMouse = false;
    [Tooltip("Gamepad buttons also count as 'any key'")]
    public bool pressAnyAcceptsGamepad = true;

    [Header("Title - After Key Press")]
    [Tooltip("Seconds for pressAny to fade out")]
    public float pressAnyFadeOutDuration = 0.3f;
    [Tooltip("Delay after pressAny has faded before the logo starts moving")]
    public float logoMoveDelay = 0.1f;
    [Tooltip("How far the logo moves (anchoredPosition offset). You can also drag the handle in the Scene view")]
    public Vector2 logoMoveOffset = new Vector2(0f, 250f);
    [Tooltip("Seconds for the logo to move")]
    public float logoMoveDuration = 0.8f;
    public EaseType logoMoveEase = EaseType.InOutCubic;
    [Tooltip("Used when logoMoveEase is Custom")]
    public AnimationCurve logoMoveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Delay after the logo has moved before the level items appear")]
    public float levelsAppearDelay = 0.2f;

    [Header("Title - Level Items Appear")]
    [Tooltip("How the 7 items (levels + question marks) appear")]
    public RevealEffect introAppearEffect = RevealEffect.ScaleAndFade;
    public EaseType introAppearEase = EaseType.OutBack;
    [Tooltip("Used when introAppearEase is Custom")]
    public AnimationCurve introAppearCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Seconds for each item's animation")]
    public float introAppearDuration = 0.5f;
    [Tooltip("Seconds between items starting (ignored for AllAtOnce)")]
    public float introAppearStagger = 0.08f;
    [Tooltip("Order the items appear in")]
    public IntroOrder introAppearOrder = IntroOrder.FrontToBack;

    [Header("Title - Camera")]
    [Tooltip("Camera to zoom (empty = Camera.main). Note: Screen Space UI is not affected by camera zoom")]
    public Camera introCamera;
    [Tooltip("Camera position offset during the title screen (world units, from its normal position). You can also drag the handle in the Scene view")]
    public Vector3 introCameraOffset = new Vector3(0f, 2f, 0f);
    [Tooltip("Zoom during the title screen (1 = none, 2 = twice as close). Uses Orthographic Size or Field of View")]
    public float introCameraZoom = 1.5f;
    [Tooltip("Delay after the key press before the camera starts returning")]
    public float cameraRestoreDelay = 0f;
    [Tooltip("Seconds for the camera to return to its normal position")]
    public float cameraRestoreDuration = 1.2f;
    public EaseType cameraRestoreEase = EaseType.InOutCubic;
    [Tooltip("Used when cameraRestoreEase is Custom")]
    public AnimationCurve cameraRestoreCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Title - Audio (optional)")]
    public AudioClip pressAnySfx;
    public AudioClip introAppearSfx;

    static bool menuLoadedBefore;
    static int startupSceneHandle = -1; // the scene the game (or Play Mode) started in

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RecordStartupScene()
    {
        startupSceneHandle = SceneManager.GetActiveScene().handle;
    }

    // Resets static state when entering Play Mode (works even with Domain Reload disabled)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        menuLoadedBefore = false;
        startupSceneHandle = -1;
        SupermanSelected = false;
        OnSupermanSelectedChanged = null;
    }

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
        public float ringX;
        public float phase;
        // effect values written by the unlock sequence
        public Vector3 fxScale = Vector3.one;
        public float fxAlpha = 1f;
        public Vector2 fxOffset;
        public float fxRot;
        // press feedback
        public float press;      // 0 = normal, 1 = fully pressed
        public bool keyHeld;     // pressed via Enter

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
    int pointerPressIndex = -1; // slot currently held down by the mouse / touch
    int keyPressIndex = -1;     // slot currently held down by Enter
    GameObject currentIrisTransition;

    // Title screen state
    bool introPlaying;
    float[] introProgress;   // 0..1 per slot, 1 = fully shown
    Coroutine introRoutine, cameraRoutine;
    CanvasGroup pressAnyGroup;
    bool pressAnyBlinking;
    float pressAnyBlinkStart;
    bool logoCaptured;
    Vector2 logoStartPos, logoBasePos;
    Vector3 logoBaseScale;
    Quaternion logoBaseRot;
    bool cameraCaptured;
    Vector3 camInitPos;
    float camInitSize, camInitFov;

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
        introProgress = new float[slotCount];
        for (int i = 0; i < slotCount; i++) introProgress[i] = 1f;

        // Title screen objects
        if (logo != null)
        {
            logoStartPos = logo.anchoredPosition;
            logoBasePos = logoStartPos;
            logoBaseScale = logo.localScale;
            logoBaseRot = logo.localRotation;
            logoCaptured = true;
        }
        if (pressAny != null)
        {
            pressAnyGroup = pressAny.GetComponent<CanvasGroup>();
            if (pressAnyGroup == null) pressAnyGroup = pressAny.gameObject.AddComponent<CanvasGroup>();
            pressAnyGroup.blocksRaycasts = false;
        }
        if (introCamera == null) introCamera = Camera.main;
        if (introCamera != null)
        {
            camInitPos = introCamera.transform.position;
            camInitSize = introCamera.orthographicSize;
            camInitFov = introCamera.fieldOfView;
            cameraCaptured = true;
        }

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
        // Any way of getting back to this scene counts (ReturnToMenu, CompleteLevel..., or a plain SceneManager.LoadScene)
        // Only the very first load of the scene the game started in counts as "game start".
        // Starting Play Mode from a level scene and then going to the menu also counts as returning.
        bool isGameStart = !menuLoadedBefore && gameObject.scene.handle == startupSceneHandle;
        menuLoadedBefore = true;
        if (!isGameStart || irisBackTransitionOnGameStart) SpawnIrisBackTransition();

        if (isGameStart && enableTitleScreen)
        {
            PlayIntro(false);
        }
        else
        {
            Rebuild(false);
            SetupWithoutIntro();
        }
    }

    // ------------------------- Title screen -------------------------
    /// <summary>Plays the title screen (logo + press any key). Also used by the Inspector test button.</summary>
    public void PlayIntro() => PlayIntro(true);

    void PlayIntro(bool keepCurrentFocus)
    {
        if (introRoutine != null) StopCoroutine(introRoutine);
        if (cameraRoutine != null) { StopCoroutine(cameraRoutine); cameraRoutine = null; }
        introPlaying = true;
        for (int i = 0; i < slotCount; i++) introProgress[i] = 0f;
        Rebuild(keepCurrentFocus);
        introRoutine = StartCoroutine(IntroRoutine());
    }

    void SetupWithoutIntro()
    {
        if (logo != null)
        {
            bool show = showLogoAfterIntro;
            logo.gameObject.SetActive(show);
            if (show) logoBasePos = logoStartPos + logoMoveOffset;
        }
        if (pressAny != null) pressAny.gameObject.SetActive(false);
        pressAnyBlinking = false;
        SetCameraPose(1f);
    }

    IEnumerator IntroRoutine()
    {
        inputLocked = true;

        // Initial state: logo in the middle, pressAny blinking, camera zoomed in, ring hidden
        if (logo != null)
        {
            logo.gameObject.SetActive(true);
            logoBasePos = logoStartPos;
        }
        if (pressAny != null)
        {
            pressAny.gameObject.SetActive(true);
            pressAnyGroup.alpha = pressAnyMaxAlpha;
        }
        pressAnyBlinking = true;
        pressAnyBlinkStart = Now;
        SetCameraPose(0f);
        UpdateTitleVisuals(); // apply logo / pressAny start state this frame (avoids a one-frame flash)

        // Wait for any key
        float waited = 0f;
        while (true)
        {
            waited += Dt;
            if (waited >= pressAnyInputDelay && AnyKeyPressed()) break;
            yield return null;
        }
        Play(pressAnySfx);
        pressAnyBlinking = false;

        // Camera returns (runs in parallel)
        cameraRoutine = StartCoroutine(CameraRestoreRoutine());

        // pressAny fades out
        if (pressAny != null)
        {
            float from = pressAnyGroup.alpha;
            float t = 0f;
            while (t < pressAnyFadeOutDuration)
            {
                t += Dt;
                pressAnyGroup.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / pressAnyFadeOutDuration));
                yield return null;
            }
            pressAnyGroup.alpha = 0f;
            pressAny.gameObject.SetActive(false);
        }

        // Logo moves
        yield return Wait(logoMoveDelay);
        if (logo != null)
        {
            Vector2 target = logoStartPos + logoMoveOffset;
            float t = 0f;
            while (t < logoMoveDuration)
            {
                t += Dt;
                float e = Ease(logoMoveEase, Mathf.Clamp01(t / logoMoveDuration), logoMoveCurve);
                logoBasePos = Vector2.LerpUnclamped(logoStartPos, target, e);
                yield return null;
            }
            logoBasePos = target;
        }

        // Level items appear
        yield return Wait(levelsAppearDelay);
        List<int> order = BuildIntroOrder();
        float stagger = introAppearOrder == IntroOrder.AllAtOnce ? 0f : Mathf.Max(0f, introAppearStagger);
        float dur = Mathf.Max(0f, introAppearDuration);
        float totalTime = (order.Count > 0 ? (order.Count - 1) * stagger : 0f) + dur;
        Play(introAppearSfx);
        float time = 0f;
        while (time < totalTime)
        {
            time += Dt;
            for (int k = 0; k < order.Count; k++)
            {
                float local = time - k * stagger;
                introProgress[order[k]] = dur > 0f ? Mathf.Clamp01(local / dur) : (local >= 0f ? 1f : 0f);
            }
            yield return null;
        }
        for (int i = 0; i < slotCount; i++) introProgress[i] = 1f;

        // Done: hand over to the normal menu (and the unlock sequence, if any)
        introPlaying = false;
        inputLocked = false;
        introRoutine = null;
        StartRevealIfNeeded();
    }

    List<int> BuildIntroOrder()
    {
        ApplyLayout(0f); // make sure depth / x are up to date
        var list = new List<int>();
        for (int i = 0; i < slotCount; i++)
            if (ActiveVisual(i) != null) list.Add(i);

        switch (introAppearOrder)
        {
            case IntroOrder.FrontToBack:
                list.Sort((a, b) => ActiveVisual(b).depth.CompareTo(ActiveVisual(a).depth));
                break;
            case IntroOrder.LeftToRight:
                list.Sort((a, b) => ActiveVisual(a).ringX.CompareTo(ActiveVisual(b).ringX));
                break;
            case IntroOrder.RightToLeft:
                list.Sort((a, b) => ActiveVisual(b).ringX.CompareTo(ActiveVisual(a).ringX));
                break;
        }
        return list;
    }

    IEnumerator CameraRestoreRoutine()
    {
        yield return Wait(cameraRestoreDelay);
        float t = 0f;
        while (t < cameraRestoreDuration)
        {
            t += Dt;
            SetCameraPose(Ease(cameraRestoreEase, Mathf.Clamp01(t / cameraRestoreDuration), cameraRestoreCurve));
            yield return null;
        }
        SetCameraPose(1f);
        cameraRoutine = null;
    }

    // t = 0 -> title pose (offset + zoomed in), t = 1 -> normal pose
    void SetCameraPose(float t)
    {
        if (!cameraCaptured || introCamera == null) return;
        float zoom = Mathf.Max(0.01f, introCameraZoom);
        introCamera.transform.position = Vector3.LerpUnclamped(camInitPos + introCameraOffset, camInitPos, t);
        if (introCamera.orthographic)
            introCamera.orthographicSize = Mathf.LerpUnclamped(camInitSize / zoom, camInitSize, t);
        else
            introCamera.fieldOfView = Mathf.LerpUnclamped(camInitFov / zoom, camInitFov, t);
    }

    void UpdateTitleVisuals()
    {
        float time = Now;

        // Logo floating
        if (logo != null && logoCaptured && logo.gameObject.activeSelf)
        {
            Vector2 off = new Vector2(
                Mathf.Sin(time * logoFloatFrequency * 0.5f * TAU + 1.3f) * logoFloatAmplitude.x,
                Mathf.Sin(time * logoFloatFrequency * TAU) * logoFloatAmplitude.y);
            logo.anchoredPosition = logoBasePos + off;
            logo.localRotation = logoBaseRot * Quaternion.Euler(0f, 0f, Mathf.Sin(time * logoSwayFrequency * TAU) * logoSwayAngle);
            logo.localScale = logoBaseScale * (1f + Mathf.Sin(time * logoBreatheFrequency * TAU) * logoBreatheAmount);
        }

        // pressAny blinking
        if (pressAnyBlinking && pressAnyGroup != null)
        {
            float on = Mathf.Max(0f, pressAnyOnTime), offT = Mathf.Max(0f, pressAnyOffTime);
            float cycle = on + offT;
            float a = 1f;
            if (cycle > 0f)
            {
                float tt = FMod(time - pressAnyBlinkStart, cycle);
                float f = pressAnyBlinkFadeTime;
                if (tt < on) a = f > 0f ? Mathf.Clamp01(tt / f) : 1f;
                else a = f > 0f ? 1f - Mathf.Clamp01((tt - on) / f) : 0f;
            }
            pressAnyGroup.alpha = Mathf.Lerp(pressAnyMinAlpha, pressAnyMaxAlpha, a);
        }
    }

    bool AnyKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame && !kb.escapeKey.wasPressedThisFrame) return true;
        if (pressAnyAcceptsGamepad && Gamepad.current != null)
        {
            foreach (var c in Gamepad.current.allControls)
                if (c is ButtonControl b && !b.synthetic && b.wasPressedThisFrame) return true;
        }
        if (pressAnyAcceptsMouse && Mouse.current != null &&
            (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame))
            return true;
        return false;
#else
        if (!Input.anyKeyDown || Input.GetKeyDown(KeyCode.Escape)) return false;
        bool mouse = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        if (mouse && !pressAnyAcceptsMouse) return false;
        return true;
#endif
    }

    void SpawnIrisBackTransition()
    {
        if (irisBackTransitionPrefab == null)
        {
            Debug.LogWarning("[MenuManager] irisBackTransitionPrefab is not assigned, so no transition was spawned.", this);
            return;
        }

        Canvas target = irisBackTransitionCanvas != null ? irisBackTransitionCanvas : (canvas != null ? canvas.rootCanvas : null);
        if (target == null)
        {
            Debug.LogWarning("[MenuManager] No Canvas found for the IrisBackTransition prefab; assign irisBackTransitionCanvas.", this);
            return;
        }

        GameObject transition = Instantiate(irisBackTransitionPrefab, target.transform, false);
        currentIrisTransition = transition;
        transition.transform.SetAsLastSibling(); // last child = drawn on top
        Debug.Log($"[MenuManager] Spawned \"{transition.name}\" under Canvas \"{target.name}\".", transition);
        StartCoroutine(IrisBackTransitionRoutine(transition));
    }

    // Spawned -> hold on the first frame (animations paused) -> play animations -> destroy
    IEnumerator IrisBackTransitionRoutine(GameObject go)
    {
        // Pause every Animator (keeps its first frame on screen)
        Animator[] animators = go.GetComponentsInChildren<Animator>(true);
        float[] animatorSpeeds = new float[animators.Length];
        for (int i = 0; i < animators.Length; i++)
        {
            animatorSpeeds[i] = animators[i].speed;
            animators[i].speed = 0f;
        }

        // Pause every legacy Animation clip as well
        var animStates = new List<AnimationState>();
        var stateSpeeds = new List<float>();
        foreach (Animation anim in go.GetComponentsInChildren<Animation>(true))
        {
            foreach (AnimationState s in anim)
            {
                animStates.Add(s);
                stateSpeeds.Add(s.speed);
                s.speed = 0f;
            }
        }

        if (irisBackTransitionHoldTime > 0f)
            yield return new WaitForSecondsRealtime(irisBackTransitionHoldTime);

        // Resume
        for (int i = 0; i < animators.Length; i++)
            if (animators[i] != null) animators[i].speed = animatorSpeeds[i];
        for (int i = 0; i < animStates.Count; i++)
            if (animStates[i] != null) animStates[i].speed = stateSpeeds[i];

        if (irisBackTransitionDestroyDelay > 0f)
            yield return new WaitForSecondsRealtime(irisBackTransitionDestroyDelay);
        if (go != null) Destroy(go);
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
        UpdateTitleVisuals();
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
        inputLocked = introPlaying;
        isLoading = false;
        pointerPressIndex = -1;
        EndKeyboardPress();

        unlockedCount = Mathf.Clamp(PlayerPrefs.GetInt(KeyUnlocked, 1), 1, slotCount);
        int revealed = Mathf.Clamp(PlayerPrefs.GetInt(KeyRevealed, 0), 0, unlockedCount);

        for (int i = 0; i < slotCount; i++)
        {
            bool open = i < revealed;
            states[i] = open ? SlotState.Unlocked : SlotState.Locked;
            spaceProgress[i] = 0f;
            if (options[i] != null) { options[i].ResetFx(); options[i].press = 0f; options[i].keyHeld = false; options[i].go.SetActive(open); }
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

        // The unlock sequence waits until the title screen is finished
        if (!introPlaying) StartRevealIfNeeded();

        ApplyLayout(0f);
    }

    void StartRevealIfNeeded()
    {
        if (revealRoutine != null || slotCount == 0) return;
        int revealed = Mathf.Clamp(PlayerPrefs.GetInt(KeyRevealed, 0), 0, unlockedCount);
        if (revealed < unlockedCount)
            revealRoutine = StartCoroutine(RevealRoutine(revealed, unlockedCount));
    }

    IEnumerator RevealRoutine(int from, int to)
    {
        if (lockInputDuringReveal) inputLocked = true;

        // 0. Stay on the previously played level (the menu opens focused on it)
        if (unlockWaitForIrisTransition)
            while (currentIrisTransition != null) yield return null;
        yield return Wait(unlockHoldOnPreviousLevel);

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
            ApplyLayout(0f);

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
            // Apply the start state right now: coroutines run after Update, so without this
            // the button would be drawn for one frame at its old size (the "flash")
            ApplyLayout(0f);
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
        EvaluateReveal(revealEffect, revealEase, revealCurve, p, out v.fxScale, out v.fxAlpha, out v.fxOffset, out v.fxRot);
    }

    // Shared by the unlock pop-in and the title-screen appear animation
    void EvaluateReveal(RevealEffect effect, EaseType ease, AnimationCurve curve, float p,
                        out Vector3 scale, out float alpha, out Vector2 offset, out float rot)
    {
        scale = Vector3.one; alpha = 1f; offset = Vector2.zero; rot = 0f;
        float e = Ease(ease, p, curve);
        float fade = Mathf.Clamp01(Ease(EaseType.OutQuad, p, null));
        switch (effect)
        {
            case RevealEffect.Scale: scale = Vector3.one * e; break;
            case RevealEffect.Fade: alpha = Mathf.Clamp01(e); break;
            case RevealEffect.ScaleAndFade: scale = Vector3.one * e; alpha = fade; break;
            case RevealEffect.DropIn: offset = new Vector2(0f, (1f - e) * revealOffsetDistance); alpha = fade; break;
            case RevealEffect.RiseUp: offset = new Vector2(0f, -(1f - e) * revealOffsetDistance); alpha = fade; break;
            case RevealEffect.SpinIn: scale = Vector3.one * e; rot = (1f - e) * revealSpinAngle; alpha = fade; break;
            case RevealEffect.FlipIn: scale = new Vector3(e, 1f, 1f); alpha = fade; break;
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
        // Release the mouse press first, even if we return early below (e.g. loading already started on release)
        if (pointerPressIndex >= 0 && (!PointerHeld() || swipedThisPress || pointerPressIndex != FrontIndex))
            pointerPressIndex = -1;

        if (slotCount == 0 || isLoading || introPlaying) { pointerTracking = false; return; }

        if (!inputLocked)
        {
            if (KeyLeft()) Step(-1);
            if (KeyRight()) Step(1);
            if (KeySubmit())
            {
                if (activateOnRelease) BeginKeyboardPress(FrontIndex);
                else TrySelect(FrontIndex, false);
            }
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

        // Mouse / touch press feedback on the front level button
        if (PointerDown() && !inputLocked)
        {
            int f = FrontIndex;
            pointerPressIndex = -1;
            if (f >= 0 && states[f] == SlotState.Unlocked && options[f] != null && options[f].go.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(options[f].rect, PointerPos(), UICamera()))
                pointerPressIndex = f;
        }
        if (pointerPressIndex >= 0 && (swipedThisPress || !PointerHeld() || pointerPressIndex != FrontIndex))
            pointerPressIndex = -1;
        // Press mode: run the action as soon as the mouse goes down (otherwise the Button's onClick runs it on release)
        if (!activateOnRelease && pointerPressIndex >= 0 && PointerDown())
            TrySelect(pointerPressIndex, true);

        // Enter held: stays pressed until released, then runs the action
        if (keyPressIndex >= 0)
        {
            if (keyPressIndex != FrontIndex || inputLocked)
            {
                EndKeyboardPress(); // ring moved or input got locked -> cancel
            }
            else if (!KeySubmitHeld())
            {
                int idx = keyPressIndex;
                EndKeyboardPress();
                TrySelect(idx, false);
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
        if (isLoading || inputLocked || introPlaying) return;
        if (sliding && !allowClickWhileSliding) return;
        if (fromPointer && swipedThisPress) return;
        if (states[index] != SlotState.Unlocked || options[index] == null) return;

        string scene = levels[index].sceneName;
        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning($"[MenuManager] Level {index + 1} ({levels[index].label}) has no sceneName set.", this);
            return;
        }
        if (fromPointer && activateOnRelease) pointerPressIndex = -1; // released -> grow back
        if (!fromPointer && !activateOnRelease) StartCoroutine(KeyboardPressRoutine(index));
        StartCoroutine(LoadRoutine(index, scene));
    }

    void BeginKeyboardPress(int index)
    {
        if (index < 0 || index >= slotCount || index != FrontIndex) return;
        if (isLoading || inputLocked || introPlaying) return;
        if (sliding && !allowClickWhileSliding) return;
        if (states[index] != SlotState.Unlocked || options[index] == null) return;

        if (keyPressIndex >= 0) EndKeyboardPress();
        keyPressIndex = index;
        options[index].keyHeld = true;
        if (showButtonPressedStateOnEnter) SetButtonPressedVisual(levels[index].button, true);
    }

    void EndKeyboardPress()
    {
        if (keyPressIndex < 0) return;
        if (options[keyPressIndex] != null) options[keyPressIndex].keyHeld = false;
        if (showButtonPressedStateOnEnter) SetButtonPressedVisual(levels[keyPressIndex].button, false);
        keyPressIndex = -1;
    }

    // Press mode (activateOnRelease off): hold the button down for a moment, then release
    IEnumerator KeyboardPressRoutine(int index)
    {
        Visual v = options[index];
        Button b = levels[index].button;
        if (v != null) v.keyHeld = true;
        if (showButtonPressedStateOnEnter) SetButtonPressedVisual(b, true);

        if (keyboardPressHoldTime > 0f) yield return new WaitForSecondsRealtime(keyboardPressHoldTime);

        if (v != null) v.keyHeld = false;
        if (showButtonPressedStateOnEnter) SetButtonPressedVisual(b, false);
    }

    // Shows / clears the Button's own Pressed look, following its Transition setting
    static void SetButtonPressedVisual(Button b, bool pressed)
    {
        if (b == null) return;
        switch (b.transition)
        {
            case Selectable.Transition.ColorTint:
                if (b.targetGraphic != null)
                {
                    ColorBlock cb = b.colors;
                    Color c = (pressed ? cb.pressedColor : cb.normalColor) * cb.colorMultiplier;
                    b.targetGraphic.CrossFadeColor(c, cb.fadeDuration, true, true);
                }
                break;
            case Selectable.Transition.SpriteSwap:
                var img = b.targetGraphic as Image;
                if (img != null) img.overrideSprite = pressed ? b.spriteState.pressedSprite : null;
                break;
            case Selectable.Transition.Animation:
                Animator anim = b.animator;
                if (anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null)
                {
                    AnimationTriggers t = b.animationTriggers;
                    anim.ResetTrigger(t.normalTrigger);
                    anim.ResetTrigger(t.pressedTrigger);
                    anim.SetTrigger(pressed ? t.pressedTrigger : t.normalTrigger);
                }
                break;
        }
    }

    IEnumerator LoadRoutine(int index, string scene)
    {
        isLoading = true;
        PlayerPrefs.SetInt(KeyLastLaunched, index);
        PlayerPrefs.Save();
        Play(confirmSfx);
        if (enableSelectZoom || UseFisheye(index)) StartCoroutine(SelectZoomRoutine(index));
        if (loadDelay > 0f) yield return new WaitForSecondsRealtime(loadDelay);
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }

    // ------------------------- Level select zoom -------------------------
    IEnumerator SelectZoomRoutine(int index)
    {
        // Start so that the zoom ends exactly when the scene loads
        float startDelay = Mathf.Max(0f, loadDelay - selectZoomDuration);
        if (startDelay > 0f) yield return new WaitForSecondsRealtime(startDelay);

        float amount = Mathf.Max(0.01f, selectZoomAmount);

        // Focus point on screen
        Vector2 screenFocus = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RectTransform btnRect = options[index] != null ? options[index].rect : null;
        if (selectZoomFocus == ZoomFocus.SelectedLevel && btnRect != null)
            screenFocus = RectTransformUtility.WorldToScreenPoint(UICamera(), btnRect.position);

        // Camera setup
        Camera cam = introCamera != null ? introCamera : Camera.main;
        if (cameraRoutine != null) { StopCoroutine(cameraRoutine); cameraRoutine = null; }
        bool doCam = enableSelectZoom && selectZoomCamera && cam != null;
        Vector3 camStart = Vector3.zero, camShift = Vector3.zero;
        float camSize = 0f, camFov = 0f;
        if (doCam)
        {
            camStart = cam.transform.position;
            camSize = cam.orthographicSize;
            camFov = cam.fieldOfView;
            float depth = Mathf.Abs(Vector3.Dot(-camStart, cam.transform.forward));
            if (depth < 0.01f) depth = 10f;
            Vector3 f = cam.ScreenToWorldPoint(new Vector3(screenFocus.x, screenFocus.y, depth));
            Vector3 c = cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, depth));
            camShift = f - c; // camera moves toward the focus point as it zooms
        }

        // Fisheye setup (before the Canvas setup, because it may change the Canvas render mode)
        LensDistortion lens = null;
        if (UseFisheye(index) && cam != null)
        {
            lens = GetLensDistortion();
            if (lens != null)
            {
                lens.active = true;
                lens.intensity.overrideState = true;
                lens.scale.overrideState = true;
                lens.center.overrideState = true;
                lens.intensity.value = 0f;
                lens.scale.value = 1f;
                lens.center.value = new Vector2(
                    Mathf.Clamp01(screenFocus.x / Mathf.Max(1, Screen.width)),
                    Mathf.Clamp01(screenFocus.y / Mathf.Max(1, Screen.height)));

                var camData = cam.GetUniversalAdditionalCameraData();
                if (camData != null) camData.renderPostProcessing = true;

                if (fisheyeMoveCanvasToCamera) MoveCanvasToCamera(cam);
            }
        }

        // Canvas setup
        RectTransform root = null;
        Vector3 rootStartPos = Vector3.zero, rootStartScale = Vector3.one, pivotLocal = Vector3.zero;
        if (enableSelectZoom && selectZoomCanvas)
        {
            Canvas rc = canvas != null ? canvas.rootCanvas : null;
            if (rc != null && rc.renderMode != RenderMode.WorldSpace)
            {
                root = zoomCanvasRoot != null ? zoomCanvasRoot : WrapCanvasInZoomRoot(rc);
                if (root != null && root.parent != null)
                {
                    rootStartPos = root.localPosition;
                    rootStartScale = root.localScale;
                    Camera uiCam = rc.renderMode == RenderMode.ScreenSpaceOverlay ? null : rc.worldCamera;
                    RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)root.parent, screenFocus, uiCam, out Vector3 worldPivot);
                    pivotLocal = root.parent.InverseTransformPoint(worldPivot);
                    pivotLocal.z = rootStartPos.z;
                }
            }
        }

        float t = 0f;
        float dur = Mathf.Max(0f, selectZoomDuration);
        while (true)
        {
            float p = dur > 0f ? Mathf.Clamp01(t / dur) : 1f;
            float z = Mathf.LerpUnclamped(1f, amount, Ease(selectZoomEase, p, selectZoomCurve));
            z = Mathf.Max(0.01f, z);

            if (doCam && cam != null)
            {
                cam.transform.position = camStart + camShift * (1f - 1f / z);
                if (cam.orthographic) cam.orthographicSize = camSize / z;
                else cam.fieldOfView = camFov / z;
            }
            if (root != null)
            {
                root.localScale = rootStartScale * z;
                root.localPosition = pivotLocal + (rootStartPos - pivotLocal) * z;
            }
            if (lens != null)
            {
                float f = Ease(fisheyeEase, p, fisheyeCurve);
                lens.intensity.value = Mathf.LerpUnclamped(0f, fisheyeIntensity, f);
                lens.scale.value = Mathf.Max(0.01f, Mathf.LerpUnclamped(1f, fisheyeScale, f));
            }

            if (p >= 1f) break;
            yield return null;
            t += Time.unscaledDeltaTime;
        }
    }

    bool UseFisheye(int index) => fisheyeOnAllLevels || (fisheyeOnSuperman && index == supermanIndex);

    LensDistortion GetLensDistortion()
    {
        if (fisheyeVolume == null)
        {
            var go = new GameObject("FisheyeVolume");
            go.layer = 0; // Default layer (included in the camera's Volume Mask by default)
            fisheyeVolume = go.AddComponent<Volume>();
            fisheyeVolume.isGlobal = true;
            fisheyeVolume.priority = 100f;
            fisheyeVolume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        }
        fisheyeVolume.weight = 1f;
        VolumeProfile profile = fisheyeVolume.profile; // runtime copy, so a profile asset is never modified
        if (!profile.TryGet(out LensDistortion lens)) lens = profile.Add<LensDistortion>(false);
        return lens;
    }

    // Post-processing only affects what the camera renders, so an Overlay Canvas is moved onto the camera
    void MoveCanvasToCamera(Camera cam)
    {
        Canvas rc = canvas != null ? canvas.rootCanvas : null;
        if (rc == null || rc.renderMode != RenderMode.ScreenSpaceOverlay) return;
        rc.renderMode = RenderMode.ScreenSpaceCamera;
        rc.worldCamera = cam;
        rc.planeDistance = cam.nearClipPlane + 0.5f; // in front of the scene
        var layers = SortingLayer.layers;
        if (layers.Length > 0) rc.sortingLayerID = layers[layers.Length - 1].id; // top-most sorting layer
        rc.sortingOrder = 32767; // drawn on top of sprites (2D renderer sorts by layer / order)
    }

    // Moves every child of the root Canvas under one full-screen "ZoomRoot" so the whole UI can be scaled together
    RectTransform WrapCanvasInZoomRoot(Canvas rc)
    {
        Transform ct = rc.transform;
        var go = new GameObject("ZoomRoot", typeof(RectTransform));
        var rootRect = (RectTransform)go.transform;
        rootRect.SetParent(ct, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        go.layer = rc.gameObject.layer;

        var children = new List<Transform>();
        foreach (Transform child in ct) if (child != rootRect) children.Add(child);
        foreach (var child in children) child.SetParent(rootRect, false); // same rect, so layout is unchanged
        zoomCanvasRoot = rootRect;
        return rootRect;
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
            v.ringX = pos.x;
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

            // Title-screen appear animation (multiplied on top of everything else)
            Vector3 iScale = Vector3.one; float iAlpha = 1f, iRot = 0f; Vector2 iOff = Vector2.zero;
            if (introProgress[i] < 1f)
                EvaluateReveal(introAppearEffect, introAppearEase, introAppearCurve, introProgress[i], out iScale, out iAlpha, out iOff, out iRot);

            v.rect.anchoredPosition = pos + floatOff + v.fxOffset + iOff;
            // Press feedback (shrink while held)
            float pressTarget = (!isMark && (i == pointerPressIndex || v.keyHeld)) ? 1f : 0f;
            float pressDur = pressTarget > v.press ? pressDownDuration : pressReleaseDuration;
            v.press = pressDur > 0f ? Mathf.MoveTowards(v.press, pressTarget, dt / pressDur) : pressTarget;
            float pressMul = Mathf.Lerp(1f, pressScale, Mathf.SmoothStep(0f, 1f, v.press));

            v.rect.localScale = Vector3.Scale(Vector3.Scale(v.baseScale * (ringScale * size * breathe * pressMul), v.fxScale), iScale);
            v.rect.localRotation = Quaternion.Euler(0f, 0f, sway + v.fxRot + iRot);
            v.group.alpha = v.fxAlpha * iAlpha;

            // Only the front, unlocked level button receives pointer clicks
            v.group.blocksRaycasts = !isMark && i == front && states[i] == SlotState.Unlocked && !inputLocked && !isLoading && !introPlaying;

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
        if (logo == null)
        {
            var go = GameObject.Find("logo");
            if (go != null) logo = go.transform as RectTransform;
        }
        if (pressAny == null)
        {
            var go = GameObject.Find("pressAny");
            if (go != null) pressAny = go.transform as RectTransform;
        }
    }

    // ------------------------- Scene view helper lines -------------------------
    void OnDrawGizmos()
    {
        if (!showTitleGizmos) return;

        // Logo: where it moves to
        if (logo != null && logo.parent != null)
        {
            Vector3 startLocal = (Application.isPlaying && logoCaptured)
                ? logo.localPosition + (Vector3)(logoStartPos - logo.anchoredPosition)
                : logo.localPosition;
            Vector3 startWorld = logo.parent.TransformPoint(startLocal);
            Vector3 targetWorld = logo.parent.TransformPoint(startLocal + (Vector3)logoMoveOffset);
            Vector3 delta = targetWorld - logo.position;

            var corners = new Vector3[4];
            logo.GetWorldCorners(corners);
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 1f);
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(corners[i] + delta, corners[(i + 1) % 4] + delta);
            Gizmos.DrawLine(startWorld, targetWorld);
            Gizmos.DrawWireSphere(targetWorld, logo.parent.lossyScale.x * 10f);
        }

        // Camera: title-screen view
        Camera cam = introCamera != null ? introCamera : Camera.main;
        if (cam != null)
        {
            bool useCaptured = Application.isPlaying && cameraCaptured;
            Vector3 basePos = useCaptured ? camInitPos : cam.transform.position;
            Vector3 introPos = basePos + introCameraOffset;
            float zoom = Mathf.Max(0.01f, introCameraZoom);
            Quaternion rot = cam.transform.rotation;

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 1f);
            Gizmos.DrawLine(basePos, introPos);
            if (cam.orthographic)
            {
                float h = (useCaptured ? camInitSize : cam.orthographicSize) / zoom;
                float w = h * cam.aspect;
                Vector3 r = rot * Vector3.right * w, u = rot * Vector3.up * h;
                Vector3 c0 = introPos - r - u, c1 = introPos + r - u, c2 = introPos + r + u, c3 = introPos - r + u;
                Gizmos.DrawLine(c0, c1); Gizmos.DrawLine(c1, c2); Gizmos.DrawLine(c2, c3); Gizmos.DrawLine(c3, c0);
            }
            else
            {
                float fov = (useCaptured ? camInitFov : cam.fieldOfView) / zoom;
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(introPos, rot, Vector3.one);
                Gizmos.DrawFrustum(Vector3.zero, fov, Mathf.Min(cam.farClipPlane, 30f), cam.nearClipPlane, cam.aspect);
                Gizmos.matrix = old;
            }
        }
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
    bool KeySubmitHeld()
    {
        var k = Keyboard.current; if (k == null) return false;
        return k.enterKey.isPressed || k.numpadEnterKey.isPressed;
    }
    bool PointerDown() => Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
    bool PointerHeld() => Pointer.current != null && Pointer.current.press.isPressed;
    bool PointerUp() => Pointer.current != null && Pointer.current.press.wasReleasedThisFrame;
    Vector2 PointerPos() => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
#else
    bool KeyLeft() => Input.GetKeyDown(KeyCode.LeftArrow) || (useADKeys && Input.GetKeyDown(KeyCode.A));
    bool KeyRight() => Input.GetKeyDown(KeyCode.RightArrow) || (useADKeys && Input.GetKeyDown(KeyCode.D));
    bool KeySubmit() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    bool KeySubmitHeld() => Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
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
        if (m.enableSelectZoom && m.selectZoomDuration > m.loadDelay)
            EditorGUILayout.HelpBox($"selectZoomDuration ({m.selectZoomDuration}s) is longer than loadDelay ({m.loadDelay}s): the zoom starts immediately and is cut off when the scene loads.", MessageType.Warning);
        else if (m.enableSelectZoom)
            EditorGUILayout.HelpBox($"Select zoom starts {Mathf.Max(0f, m.loadDelay - m.selectZoomDuration):0.##}s after choosing a level and lasts {m.selectZoomDuration:0.##}s.", MessageType.None);

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
            if (GUILayout.Button("Replay Title Screen")) m.PlayIntro();
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

    // Drag handles in the Scene view (edit mode only, select the MenuManager object)
    void OnSceneGUI()
    {
        if (Application.isPlaying) return;
        var m = (MenuManager)target;

        if (m.logo != null && m.logo.parent != null)
        {
            Transform parent = m.logo.parent;
            Vector3 world = parent.TransformPoint(m.logo.localPosition + (Vector3)m.logoMoveOffset);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, parent.rotation);
            Handles.Label(world, "  Logo target");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m, "Move Logo Target");
                Vector3 local = parent.InverseTransformPoint(moved);
                m.logoMoveOffset = (Vector2)(local - m.logo.localPosition);
            }
        }

        Camera cam = m.introCamera != null ? m.introCamera : Camera.main;
        if (cam != null)
        {
            Vector3 world = cam.transform.position + m.introCameraOffset;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, cam.transform.rotation);
            Handles.Label(world, "  Title camera");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m, "Move Title Camera");
                m.introCameraOffset = moved - cam.transform.position;
            }
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
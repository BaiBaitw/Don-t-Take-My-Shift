using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Superman mode: all six mini games live in one scene.
///
/// Flow:
///  -1. On scene load: fisheye zoom-out intro (URP Lens Distortion on a Volume; affects the scene
///      and any Canvas set to Screen Space - Camera).
///   0. Debug Mode OFF (default): a 3-2-1-GO countdown plays on countdownNumber, then the game starts.
///      Debug Mode ON: StartGameButton / NextGameButton are shown instead (for testing).
///   1. Press StartGameButton -> a random level slides in and its StartGame() is called.
///   2. When the level calls CompleteCurrentStage(), score is added (with Fever multiplier),
///      then a new random level is picked:
///        - same level      -> its StartGame() is called again (next question, no slide)
///        - different level -> the new level slides in, the old one slides out
///   3. Repeats until the TimeManager (CountdownBar) calls GameOver().
///
/// Why it inherits GameManager:
///   The six game scripts all reference "GameManager" and read IsFinished / IsGameOver and call
///   CompleteCurrentStage(). Because SupermanGameManager IS a GameManager, they work with it
///   without any changes (drag the SupermanGameManager object into their Game Manager field).
///
/// How the games are stopped between questions:
///   Inside CompleteCurrentStage() IsFinished is set to true, so every game stops itself
///   (that's how they already behave at the end of a normal stage). After Next Question Delay,
///   IsFinished goes back to false and the next StartGame() is called.
/// </summary>
public class SupermanGameManager : GameManager
{
    // ============================================================
    //  Level data
    // ============================================================
    [Serializable]
    public class SupermanLevel
    {
        [Tooltip("Only used as a label in the Inspector")]
        public string levelName;

        [Tooltip("This level's GameUI (RectTransform under the Canvas, Anchor Preset = full-screen stretch)")]
        public RectTransform gameUI;

        [Tooltip("This level's GameScreen (parent of all scene objects of this level)")]
        public Transform gameScreen;

        [Tooltip("Optional: the level script (TypistGame, ScamManager, ...). Empty = searched automatically under GameUI / GameScreen")]
        public MonoBehaviour gameScript;

        [Tooltip("Optional: this level's Error Redbox, used to detect mistakes for Fever. Empty = read automatically from the level script")]
        public GameObject errorRedbox;

        // Runtime
        [NonSerialized] public MethodInfo startGameMethod;
        [NonSerialized] public MethodInfo increaseDifficultyMethod;
        [NonSerialized] public Vector3 screenBase;
        [NonSerialized] public Vector2 uiBase;
        [NonSerialized] public int pendingDifficulty;   // IncreaseDifficulty() calls waiting for this level's next StartGame()
        [NonSerialized] public bool redboxWasActive;

        public bool IsValid => gameScript != null && startGameMethod != null;
    }

    public enum SlideFrom { Right, Left, Random }
    public enum IntroFocus { Player, Target, ScreenCenter }

    /// <summary>Settings for a pop-up (e.g. "+200", "+2s") that jumps up, stays, fades out and is destroyed.</summary>
    [Serializable]
    public class PopupSettings
    {
        [Tooltip("Prefab to spawn (a UI object for the Canvas). A scene object can also be used as a template; it is hidden at start")]
        public GameObject prefab;
        [Tooltip("Where it appears, e.g. ScoreUI or the countdown bar. Empty = the prefab's own position under Spawn Parent")]
        public RectTransform spawnAt;
        [Tooltip("Parent it is created under. Empty = Spawn At's parent, or the root Canvas")]
        public RectTransform spawnParent;
        [Tooltip("Offset from the spawn position (UI units)")]
        public Vector2 spawnOffset = Vector2.zero;
        [Tooltip("Random extra offset (+/- this much), so several pop-ups don't sit exactly on top of each other")]
        public Vector2 randomOffset = new Vector2(15f, 0f);
        [Tooltip("Text format, {0} = the value. e.g. \"+{0}\", \"+{0}s\". Empty = keep the prefab's text")]
        public string textFormat = "+{0}";

        [Header("Pop In (scale)")]
        [Tooltip("Scale when it appears (1 = the prefab's normal size)")]
        public float startScale = 0.3f;
        [Tooltip("Seconds to grow to normal size")]
        public float scaleInDuration = 0.25f;
        [Tooltip("OutBack / OutElastic / OutBounce give a bouncy pop")]
        public MenuManager.EaseType scaleInEase = MenuManager.EaseType.OutBack;
        [Tooltip("Used when Scale In Ease is Custom")]
        public AnimationCurve scaleInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Jump Up (position)")]
        [Tooltip("How far it jumps up (UI units)")]
        public float jumpHeight = 80f;
        [Tooltip("Seconds of the jump (speed)")]
        public float jumpDuration = 0.45f;
        [Tooltip("OutBack = overshoots and settles, OutBounce = bounces at the top, OutElastic = springy, OutCubic = smooth")]
        public MenuManager.EaseType jumpEase = MenuManager.EaseType.OutBack;
        [Tooltip("Used when Jump Ease is Custom")]
        public AnimationCurve jumpCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Show / Fade Out")]
        [Tooltip("Seconds it stays fully visible after the jump")]
        public float showTime = 0.4f;
        [Tooltip("Seconds to fade out (then it is destroyed)")]
        public float fadeDuration = 0.4f;
        [Tooltip("Extra distance it drifts up while fading (UI units)")]
        public float fadeDrift = 20f;
    }

    [Serializable] public class IntEvent : UnityEvent<int> { }

    // ============================================================
    //  Inspector
    // ============================================================
    [Header("Levels (6 games)")]
    [Tooltip("One entry per game: drag in its GameUI and GameScreen")]
    [SerializeField] private SupermanLevel[] levels = new SupermanLevel[6];

    [Header("Slide (from LevelSlider)")]
    [Tooltip("Main orthographic camera. Empty = Camera.main")]
    [SerializeField] private Camera worldCamera;
    [Tooltip("The Canvas' RectTransform. Empty = root Canvas of the first GameUI")]
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private float slideDuration = 0.6f;
    [SerializeField] private AnimationCurve slideEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Which side the new level comes in from")]
    [SerializeField] private SlideFrom slideFrom = SlideFrom.Right;
    [Tooltip("Use unscaled time so sliding still works when Time.timeScale = 0")]
    [SerializeField] private bool useUnscaledTime = true;
    [Tooltip("On: the new question is generated before sliding, so it slides in already showing (input is accepted during the slide).\nOff: the question is generated after the slide finishes.")]
    [SerializeField] private bool startGameBeforeSlide = false;

    [Header("Flow")]
    [Tooltip("On: a random level is already shown on screen before the game starts (no input), and the game begins on that level without sliding.\nOff: the screen is empty and the first level slides in after Start is pressed.")]
    [SerializeField] private bool showPreviewOnLoad = true;
    [Tooltip("Only shown in Debug Mode (Debug Settings at the top). Starts the game")]
    [SerializeField] private Button startGameButton;
    [Tooltip("Only shown in Debug Mode, after time is up. Loads Next Scene Name (empty = reload this scene)")]
    [SerializeField] private Button nextGameButton;
    [SerializeField] private string nextSceneName = "";
    [Tooltip("GameObject with the CountdownBar script (receives StartTimer)")]
    [SerializeField] private GameObject timeManager;
    [Tooltip("Player GameObject (e.g. with SupermanPlayerScript). Receives GameOver(), and PlayWrongAnimation() on every mistake")]
    [SerializeField] private GameObject playerObject;
    [Tooltip("Call PlayWrongAnimation() on the Player for every mistake, in all six levels.\nLeave the Player field EMPTY in TypistGame / IllustratorGame, so the animation isn't played twice")]
    [SerializeField] private bool playWrongAnimOnMistake = true;
    [Tooltip("GameObject with a ScreenShake script (e.g. shakeRootforPlayer). Shake() is called on every mistake, in all six levels")]
    [SerializeField] private GameObject shakeRootForPlayer;
    [Tooltip("Seconds between finishing a question and the next question / slide. Keep it above FactoryManager's Effect Duration (0.25)")]
    [SerializeField] private float nextQuestionDelay = 0.35f;
    [Tooltip("Every N clears in total (all levels added together), ALL six levels get IncreaseDifficulty() once (0 = never)")]
    [SerializeField] private int increaseDifficultyEvery = 2;

    [Header("Intro Zoom-out (on scene load, before the countdown) - same method as MenuManager")]
    [Tooltip("Play the zoom-out (+ fisheye) when the scene loads")]
    [SerializeField] private bool playIntroZoom = true;
    [Tooltip("Seconds the start look is held before it begins to go back to normal")]
    [SerializeField] private float introHoldTime = 0.2f;
    [Tooltip("Seconds to go back to normal")]
    [SerializeField] private float introDuration = 1.2f;

    [Header("Intro Zoom-out - Focus (where the zoom-out starts from)")]
    [Tooltip("Player = zoom out from Player Object, Target = from Intro Focus Target, ScreenCenter = from the middle of the screen")]
    [SerializeField] private IntroFocus introFocus = IntroFocus.Player;
    [Tooltip("Used when Intro Focus is Target (e.g. the character's head)")]
    [SerializeField] private Transform introFocusTarget;
    [Tooltip("Offset added to the focus point (world units), e.g. (0, 1) = a bit above the character's feet. See the yellow gizmo")]
    [SerializeField] private Vector2 introFocusOffset = Vector2.zero;
    [Tooltip("Show the start frame of the zoom-out in the Scene view")]
    [SerializeField] private bool showIntroGizmo = true;

    [Header("Intro Zoom-out - Zoom")]
    [Tooltip("Zoom at the start (2 = twice as close). Same value as MenuManager's Select Zoom Amount makes the menu -> Superman change seamless")]
    [SerializeField] private float introZoomAmount = 2f;
    [Tooltip("Zoom the camera (scene / world objects)")]
    [SerializeField] private bool introZoomCamera = true;
    [Tooltip("Zoom the Canvas (UI). Skipped for World Space canvases")]
    [SerializeField] private bool introZoomCanvas = true;
    [Tooltip("UI object to scale for the Canvas zoom. Empty = everything in the root Canvas is wrapped in a 'ZoomRoot' automatically")]
    [SerializeField] private RectTransform introZoomCanvasRoot;
    [SerializeField] private MenuManager.EaseType introZoomEase = MenuManager.EaseType.OutCubic;
    [Tooltip("Used when Intro Zoom Ease is Custom")]
    [SerializeField] private AnimationCurve introZoomCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Intro Zoom-out - Fisheye (URP Lens Distortion)")]
    [Tooltip("Add a fisheye / wide-angle distortion that goes back to normal together with the zoom")]
    [SerializeField] private bool introFisheye = true;
    [Tooltip("Distortion at the start. Same value as MenuManager's Fisheye Intensity makes the change seamless. Positive and negative bend in opposite directions: try both")]
    [Range(-1f, 1f)][SerializeField] private float introFisheyeIntensity = -0.6f;
    [Tooltip("Lens Distortion 'Scale' at the start (below 1 shrinks the image to hide stretched edges, above 1 crops in)")]
    [Range(0.01f, 5f)][SerializeField] private float introFisheyeScale = 1f;
    [SerializeField] private MenuManager.EaseType introFisheyeEase = MenuManager.EaseType.OutCubic;
    [Tooltip("Used when Intro Fisheye Ease is Custom")]
    [SerializeField] private AnimationCurve introFisheyeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Global Volume used for the effect (empty = one is created automatically)")]
    [SerializeField] private Volume introVolume;
    [Tooltip("Post-processing can't distort a Screen Space - Overlay Canvas. If on, the Canvas is switched to Screen Space - Camera (drawn on top) during the intro, so the UI is distorted too")]
    [SerializeField] private bool introMoveCanvasToCamera = true;
    [Tooltip("After the intro, put the Canvas back to how it was (e.g. Screen Space - Overlay)")]
    [SerializeField] private bool introRestoreCanvas = true;

    [Header("Leave Buttons (after Game Over)")]
    [Tooltip("Reloads this scene WITHOUT the fisheye zoom-out (the countdown still plays). Empty = found by name \"restartButton\" (hidden objects included)")]
    [SerializeField] private Button restartButton;
    [Tooltip("Zooms in (Home Zoom-in settings), then calls MenuManager.CompleteCurrentLevelAndReturnWithFisheye(). Empty = found by name \"homeButton\" (hidden objects included)")]
    [SerializeField] private Button homeButton;

    [Header("Home Zoom-in (homeButton -> Menu)")]
    [Tooltip("Play the zoom-in before going back to the Menu")]
    [SerializeField] private bool playHomeZoom = true;
    [Tooltip("Player = zoom in on Player Object, Target = on Home Focus Target, ScreenCenter = on the middle of the screen")]
    [SerializeField] private IntroFocus homeFocus = IntroFocus.Player;
    [SerializeField] private Transform homeFocusTarget;
    [Tooltip("Offset added to the focus point (world units)")]
    [SerializeField] private Vector2 homeFocusOffset = Vector2.zero;
    [Tooltip("Seconds of zooming in")]
    [SerializeField] private float homeZoomDuration = 1f;
    [Tooltip("Seconds to wait at the end of the zoom before the Menu loads")]
    [SerializeField] private float homeHoldTime = 0.1f;
    [Tooltip("Zoom at the end (2 = twice as close)")]
    [SerializeField] private float homeZoomAmount = 2f;
    [SerializeField] private bool homeZoomCamera = true;
    [SerializeField] private bool homeZoomCanvas = true;
    [SerializeField] private MenuManager.EaseType homeZoomEase = MenuManager.EaseType.InCubic;
    [Tooltip("Used when Home Zoom Ease is Custom")]
    [SerializeField] private AnimationCurve homeZoomCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Add the fisheye during the zoom-in")]
    [SerializeField] private bool homeFisheye = true;
    [Tooltip("Distortion at the end of the zoom-in. Positive and negative bend in opposite directions: try both")]
    [Range(-1f, 1f)][SerializeField] private float homeFisheyeIntensity = -0.6f;
    [Tooltip("Lens Distortion 'Scale' at the end (below 1 shrinks the image to hide stretched edges, above 1 crops in)")]
    [Range(0.01f, 5f)][SerializeField] private float homeFisheyeScale = 1f;
    [SerializeField] private MenuManager.EaseType homeFisheyeEase = MenuManager.EaseType.InCubic;
    [Tooltip("Used when Home Fisheye Ease is Custom")]
    [SerializeField] private AnimationCurve homeFisheyeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Start Countdown (when Debug Mode is OFF)")]
    [Tooltip("TextMeshPro - Text (UI) under the Canvas. Shows 3, 2, 1, GO, then is hidden")]
    [SerializeField] private TMP_Text countdownNumber;
    [Tooltip("Counts down from this number (3 = 3, 2, 1, GO)")]
    [SerializeField] private int countdownFrom = 3;
    [Tooltip("Seconds to wait after the scene loads before the first number")]
    [SerializeField] private float countdownStartDelay = 0.3f;
    [Tooltip("Seconds each number (3, 2, 1) stays on screen. This sets the countdown speed")]
    [SerializeField] private float numberDuration = 0.8f;
    [Tooltip("Number scale when it appears")]
    [SerializeField] private float numberStartScale = 1.5f;
    [Tooltip("Number scale at the end of its time (smaller = shrinks more)")]
    [SerializeField] private float numberEndScale = 0.6f;
    [Tooltip("How the number shrinks over its time (0 = start scale, 1 = end scale)")]
    [SerializeField] private AnimationCurve numberScaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Text shown after the last number")]
    [SerializeField] private string goText = "GO";
    [Tooltip("Seconds GO stays on screen before the game starts")]
    [SerializeField] private float goDuration = 0.8f;
    [Tooltip("GO scale when it appears")]
    [SerializeField] private float goStartScale = 0.6f;
    [Tooltip("GO scale at the end (bigger = grows more)")]
    [SerializeField] private float goEndScale = 1.8f;
    [Tooltip("How GO grows over its time (0 = start scale, 1 = end scale). Values above 1 overshoot")]
    [SerializeField]
    private AnimationCurve goScaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.35f, 1.15f), new Keyframe(1f, 1f));
    [Tooltip("GO shake strength in UI units (0 = no shake)")]
    [SerializeField] private float goShakeStrength = 20f;
    [Tooltip("How many times per second GO jumps to a new shake position")]
    [SerializeField] private float goShakeFrequency = 30f;
    [Tooltip("Shake gets weaker towards the end of GO")]
    [SerializeField] private bool goShakeFadeOut = true;

    [Header("Score")]
    [Tooltip("ScoreUI (TextMeshPro - Text (UI)). Can be left empty for now")]
    [SerializeField] private TMP_Text scoreUI;
    [Tooltip("ScoreUI object (the whole panel). Made visible every time the scene loads, including Restart " +
             "(the leaderboard hides it at Game Over). Empty = found by name \"ScoreUI\" (any case, hidden objects included)")]
    [SerializeField] private GameObject scoreUIObject;
    [Tooltip("Text format, {0} = score. e.g. \"{0}\", \"Score: {0:N0}\"")]
    [SerializeField] private string scoreFormat = "{0}";
    [Tooltip("Score added per cleared question")]
    [SerializeField] private int scorePerClear = 200;

    [Header("Records (saved on this computer with PlayerPrefs)")]
    [Tooltip("Optional: TextMeshPro - Text (UI) that shows the local high score")]
    [SerializeField] private TMP_Text highScoreUI;
    [Tooltip("Text format, {0} = high score. e.g. \"BEST {0}\"")]
    [SerializeField] private string highScoreFormat = "BEST {0}";

    [Header("Pop-up: Add Score (+200 / Fever +400)")]
    [SerializeField] private PopupSettings scorePopup = new PopupSettings();
    [Tooltip("Change the text colour of the score pop-up during Fever")]
    [SerializeField] private bool scorePopupFeverColor = true;
    [SerializeField] private Color scorePopupFeverTextColor = new Color(1f, 0.75f, 0.1f, 1f);
    [Tooltip("Extra size of the score pop-up during Fever (1 = same size)")]
    [SerializeField] private float scorePopupFeverScale = 1.25f;

    [Header("Pop-up: Add Time (when CountdownBar.AddBonusTime() adds time)")]
    [Tooltip("The prefab's text is never changed (Text Format is ignored here)")]
    [SerializeField] private PopupSettings timePopup = new PopupSettings { textFormat = "" };

    [Header("Fever")]
    [Tooltip("Clears in a row with no mistake needed to start Fever")]
    [SerializeField] private int feverStreakRequired = 3;
    [Tooltip("Score multiplier during Fever")]
    [SerializeField] private float feverMultiplier = 2f;
    [Tooltip("Optional: shown while Fever is on")]
    [SerializeField] private GameObject feverEffect;
    [Tooltip("Seconds added to the countdown bar for each clear while Fever is on (0 = none)")]
    [SerializeField] private float feverTimeBonus = 2f;
    [Tooltip("Bool parameter in the Player's Animator (read from Player Object): true while Fever is on, false otherwise")]
    [SerializeField] private string feverAnimParam = "Fever";

    [Header("Events")]
    [SerializeField] private IntEvent onScoreChanged = new IntEvent();
    [SerializeField] private UnityEvent onFeverStart = new UnityEvent();
    [SerializeField] private UnityEvent onFeverEnd = new UnityEvent();
    [SerializeField] private UnityEvent onMistake = new UnityEvent();
    [SerializeField] private UnityEvent onTimeUp = new UnityEvent();
    [Tooltip("Called at Game Over when the score beats the local high score (passes the new high score)")]
    [SerializeField] private IntEvent onNewHighScore = new IntEvent();
    [Tooltip("Called for each countdown step: 3, 2, 1, then 0 for GO (e.g. play a sound)")]
    [SerializeField] private IntEvent onCountdownStep = new IntEvent();

    // ============================================================
    //  Runtime state
    // ============================================================
    private enum Phase { Idle, Playing, Transition, Over }
    private Phase phase = Phase.Idle;

    private readonly List<int> validLevels = new List<int>();
    private int currentLevel = -1;
    private int previewLevel = -1;   // level shown on screen before the game starts
    private CountdownBar countdown;
    private ScreenShake playerShake;
    private int streak;
    private bool mistakeThisClear;
    private Coroutine flowRoutine;
    private Animator playerAnimator;   // read from Player Object (or its children)
    private int feverParamHash;
    private bool hasFeverParam;

    public int Score { get; private set; }
    public int Streak => streak;
    public bool IsFever { get; private set; }
    /// <summary>Questions completed this run</summary>
    public int TotalClears { get; private set; }
    /// <summary>Longest COMBO this run (most clears in a row without a mistake)</summary>
    public int MaxCombo { get; private set; }
    /// <summary>Mistakes made this run</summary>
    public int Mistakes { get; private set; }
    /// <summary>True after Game Over if this run set a new local high score</summary>
    public bool IsNewHighScore { get; private set; }

    // ---------- Saved records (readable from any scene, e.g. a leaderboard) ----------
    public const string KeyHighScore = "Superman_HighScore";
    public const string KeyHighScoreClears = "Superman_HighScore_Clears";
    public const string KeyHighScoreMaxCombo = "Superman_HighScore_MaxCombo";
    public const string KeyHighScoreMistakes = "Superman_HighScore_Mistakes";
    public const string KeyLastScore = "Superman_Last_Score";
    public const string KeyLastClears = "Superman_Last_Clears";
    public const string KeyLastMaxCombo = "Superman_Last_MaxCombo";
    public const string KeyLastMistakes = "Superman_Last_Mistakes";

    /// <summary>Local high score, and the stats of that run</summary>
    public static int HighScore => PlayerPrefs.GetInt(KeyHighScore, 0);
    public static int HighScoreClears => PlayerPrefs.GetInt(KeyHighScoreClears, 0);
    public static int HighScoreMaxCombo => PlayerPrefs.GetInt(KeyHighScoreMaxCombo, 0);
    public static int HighScoreMistakes => PlayerPrefs.GetInt(KeyHighScoreMistakes, 0);

    /// <summary>Stats of the most recent finished run (saved at Game Over)</summary>
    public static int LastScore => PlayerPrefs.GetInt(KeyLastScore, 0);
    public static int LastClears => PlayerPrefs.GetInt(KeyLastClears, 0);
    public static int LastMaxCombo => PlayerPrefs.GetInt(KeyLastMaxCombo, 0);
    public static int LastMistakes => PlayerPrefs.GetInt(KeyLastMistakes, 0);

    /// <summary>Deletes the local high score (and its stats) and the last run's stats.</summary>
    public static void ResetRecords()
    {
        PlayerPrefs.DeleteKey(KeyHighScore);
        PlayerPrefs.DeleteKey(KeyHighScoreClears);
        PlayerPrefs.DeleteKey(KeyHighScoreMaxCombo);
        PlayerPrefs.DeleteKey(KeyHighScoreMistakes);
        PlayerPrefs.DeleteKey(KeyLastScore);
        PlayerPrefs.DeleteKey(KeyLastClears);
        PlayerPrefs.DeleteKey(KeyLastMaxCombo);
        PlayerPrefs.DeleteKey(KeyLastMistakes);
        PlayerPrefs.Save();
    }
    /// <summary>How many times the global difficulty has gone up</summary>
    public int DifficultyLevel { get; private set; }
    public int CurrentLevelIndex => currentLevel;
    public bool IsSliding { get; private set; }
    public bool IsCountingDown { get; private set; }
    public bool IsPlayingIntro { get; private set; }

    // Intro zoom-out runtime
    private LensDistortion lensDistortion;
    private Camera introCam;
    private float camInitSize, camInitFov;
    private Vector3 camInitPos;
    private Vector3 introCamShift;                       // camera moves toward the focus point as it zooms
    private Vector2 introFocusViewport = new Vector2(0.5f, 0.5f);   // focus point on screen (0..1)
    private RectTransform introZoomRoot;
    private Vector3 zoomRootBaseScale = Vector3.one;
    private Vector3 zoomRootBasePos = Vector3.zero;
    private Canvas introCanvas;
    private bool canvasMoved;
    private RenderMode savedRenderMode;
    private Camera savedCanvasCamera;
    private float savedPlaneDistance;
    private int savedSortingLayerId, savedSortingOrder;
    private UniversalAdditionalCameraData introCamData;
    private bool savedPostProcessing;
    private bool introReady;
    private bool leaving;   // restart / home pressed

    // Set by the restart button: the next load of this scene skips the zoom-out
    private static bool skipIntroOnce;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => skipIntroOnce = false;

    // ============================================================
    //  Lifecycle
    //  (These replace GameManager's own Awake / Start, which are not run.)
    // ============================================================
    private void Reset()
    {
        string[] names = { "Typist", "Scam", "Illustrator", "Factory", "Engineer", "Composer" };
        levels = new SupermanLevel[names.Length];
        for (int i = 0; i < names.Length; i++) levels[i] = new SupermanLevel { levelName = names[i] };
    }

    private void Awake()
    {
        IsFinished = false;
        IsGameOver = false;

        if (worldCamera == null) worldCamera = Camera.main;

        if (timeManager != null)
        {
            countdown = timeManager.GetComponent<CountdownBar>();
            if (countdown == null)
                Debug.LogWarning("[SupermanGameManager] Time Manager has no CountdownBar; Fever time bonus will not work.", this);
            else
            {
                // Spawn the AddTime pop-up whenever the bar's bonus time is actually added (by anyone)
                if (countdown.onBonusTime == null) countdown.onBonusTime = new UnityEvent();
                countdown.onBonusTime.AddListener(OnCountdownBonusTime);
            }
        }
        if (shakeRootForPlayer != null)
        {
            playerShake = shakeRootForPlayer.GetComponent<ScreenShake>();
            if (playerShake == null)
                Debug.LogWarning("[SupermanGameManager] shakeRootForPlayer has no ScreenShake script.", shakeRootForPlayer);
        }

        if (worldCamera != null && !worldCamera.orthographic)
            Debug.LogWarning("[SupermanGameManager] Camera is not orthographic; world and UI may not slide in sync.", this);

        if (levels == null) levels = new SupermanLevel[0];

        for (int i = 0; i < levels.Length; i++)
        {
            SupermanLevel lv = levels[i];
            if (lv == null) continue;

            if (canvasRect == null && lv.gameUI != null)
            {
                Canvas c = lv.gameUI.GetComponentInParent<Canvas>(true);
                if (c != null) canvasRect = (RectTransform)c.rootCanvas.transform;
            }

            ResolveGameScript(lv);
            if (lv.errorRedbox == null) lv.errorRedbox = FindRedbox(lv.gameScript);

            if (lv.gameScreen != null) lv.screenBase = lv.gameScreen.position;
            if (lv.gameUI != null) lv.uiBase = lv.gameUI.anchoredPosition;

            if (lv.IsValid) validLevels.Add(i);
            else Debug.LogWarning($"[SupermanGameManager] Level {i} ({lv.levelName}) has no script with StartGame(); it will be skipped.", this);
        }

        if (validLevels.Count == 0)
            Debug.LogError("[SupermanGameManager] No usable level. Assign GameUI / GameScreen (and Game Script if needed).", this);

        // Hide every level until the game starts
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] == null) continue;
            ApplyOffset(i, 0f);
            SetLevelActive(i, false);
        }

        // Show a random level on screen before the game starts.
        // Its script stays disabled, so it reads no input until StartSuperman().
        if (showPreviewOnLoad && validLevels.Count > 0)
        {
            previewLevel = PickRandomLevel();
            ApplyOffset(previewLevel, 0f);
            SetLevelActive(previewLevel, true);
            SetScriptEnabled(previewLevel, false);
            currentLevel = previewLevel;
        }

        // Start / Next buttons are only shown in Debug Mode
        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(StartSuperman);
            startGameButton.gameObject.SetActive(DebugMode);
        }
        if (nextGameButton != null)
        {
            nextGameButton.onClick.AddListener(OnNextGameClicked);
            nextGameButton.gameObject.SetActive(false);
        }

        if (countdownNumber != null) countdownNumber.gameObject.SetActive(false);

        // ScoreUI: hidden by the leaderboard at Game Over, so make sure it is visible again on every load
        if (scoreUIObject == null) scoreUIObject = FindInSceneIgnoreCase("ScoreUI");
        if (scoreUIObject == null && scoreUI != null) scoreUIObject = scoreUI.gameObject;
        if (scoreUIObject != null)
        {
            if (!scoreUIObject.activeSelf) scoreUIObject.SetActive(true);
        }
        else Debug.LogWarning("[SupermanGameManager] \"ScoreUI\" not found.", this);

        // Restart / Home buttons (they are usually inside a hidden panel, so hidden objects are searched too)
        if (restartButton == null) restartButton = FindInScene<Button>("restartButton");
        if (homeButton == null) homeButton = FindInScene<Button>("homeButton");
        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
        else Debug.LogWarning("[SupermanGameManager] \"restartButton\" not found.", this);
        if (homeButton != null) homeButton.onClick.AddListener(GoHome);
        else Debug.LogWarning("[SupermanGameManager] \"homeButton\" not found.", this);

        // Apply the zoom-out start look right away, so the first frame is already zoomed in
        SetupIntro();

        if (feverEffect != null) feverEffect.SetActive(false);

        // Pop-up templates that are scene objects are only used for cloning
        HideIfSceneTemplate(scorePopup.prefab);
        HideIfSceneTemplate(timePopup.prefab);

        SetupPlayerAnimator();
        UpdateScoreUI();
        UpdateHighScoreUI();
    }

    private void Start()
    {
        StartCoroutine(IntroRoutine());
    }

    /// <summary>Fisheye zoom-out first, then (Debug Mode OFF) the 3-2-1-GO countdown.</summary>
    private IEnumerator IntroRoutine()
    {
        if (introReady)
            yield return IntroZoomRoutine();

        // Debug Mode OFF: play 3-2-1-GO, then start the game automatically
        if (!DebugMode) yield return CountdownRoutine();
    }

    // ============================================================
    //  Intro zoom-out + fisheye (same method as MenuManager's select zoom, played in reverse)
    // ============================================================
    private void SetupIntro()
    {
        if (!playIntroZoom) return;

        // Restart button: reload without the zoom-out (the countdown still plays)
        if (skipIntroOnce)
        {
            skipIntroOnce = false;
            return;
        }

        PrepareZoom(introFocus, introFocusTarget, introFocusOffset, introFisheye, introZoomCanvas);
        introReady = true;
        ApplyIntroPose(0f);
    }

    /// <summary>
    /// Captures the camera / Canvas in their normal state and sets up the fisheye and the UI ZoomRoot,
    /// anchored on the given focus point. Used by the intro zoom-out and the Home zoom-in.
    /// </summary>
    private void PrepareZoom(IntroFocus focus, Transform focusTarget, Vector2 focusOffset, bool useFisheye, bool zoomCanvas)
    {
        introCam = worldCamera != null ? worldCamera : Camera.main;
        introCamShift = Vector3.zero;
        introFocusViewport = new Vector2(0.5f, 0.5f);

        if (introCam != null)
        {
            camInitSize = introCam.orthographicSize;
            camInitFov = introCam.fieldOfView;
            camInitPos = introCam.transform.position;

            // Focus point: where on screen the zoom is anchored (it stays at the same screen spot)
            if (TryGetFocusWorld(focus, focusTarget, focusOffset, out Vector3 focusWorld))
            {
                Vector3 vp = introCam.WorldToViewportPoint(focusWorld);
                introFocusViewport = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
                float depth = Mathf.Max(0.01f, Vector3.Dot(focusWorld - camInitPos, introCam.transform.forward));
                Vector3 center = introCam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth));
                introCamShift = focusWorld - center;
            }
        }

        Canvas c = canvasRect != null ? canvasRect.GetComponentInParent<Canvas>() : null;
        introCanvas = c != null ? c.rootCanvas : null;

        // Fisheye setup (before the Canvas zoom, because it may change the Canvas render mode)
        lensDistortion = null;
        if (useFisheye && introCam != null)
        {
            lensDistortion = GetLensDistortion();
            if (lensDistortion != null)
            {
                lensDistortion.active = true;
                lensDistortion.intensity.overrideState = true;
                lensDistortion.scale.overrideState = true;
                lensDistortion.center.overrideState = true;
                lensDistortion.center.value = introFocusViewport;   // fisheye bulges from the focus point
                lensDistortion.intensity.value = 0f;
                lensDistortion.scale.value = 1f;

                introCamData = introCam.GetUniversalAdditionalCameraData();
                if (introCamData != null)
                {
                    savedPostProcessing = introCamData.renderPostProcessing;
                    introCamData.renderPostProcessing = true;
                }

                if (introMoveCanvasToCamera) MoveCanvasToCamera(introCam);
            }
        }

        // Canvas zoom setup (the ZoomRoot is created once and reused)
        introZoomRoot = null;
        if (zoomCanvas && introCanvas != null && introCanvas.renderMode != RenderMode.WorldSpace)
        {
            introZoomRoot = introZoomCanvasRoot != null ? introZoomCanvasRoot : WrapCanvasInZoomRoot(introCanvas);
            if (introZoomRoot != null)
            {
                zoomRootBaseScale = introZoomRoot.localScale;
                zoomRootBasePos = introZoomRoot.localPosition;
            }
        }
    }

    /// <summary>z = zoom (1 = normal, 2 = twice as close), plus the fisheye values.</summary>
    private void ApplyZoom(float z, bool zoomCamera, float fisheyeIntensity, float fisheyeScale)
    {
        z = Mathf.Max(0.01f, z);

        if (zoomCamera && introCam != null)
        {
            introCam.transform.position = camInitPos + introCamShift * (1f - 1f / z);
            if (introCam.orthographic) introCam.orthographicSize = camInitSize / z;
            else introCam.fieldOfView = camInitFov / z;
        }

        if (introZoomRoot != null)
        {
            // Scale the UI around the focus point's screen position (same spot as the camera zoom)
            Vector2 size = canvasRect != null ? canvasRect.rect.size : new Vector2(Screen.width, Screen.height);
            Vector3 pivot = new Vector3((introFocusViewport.x - 0.5f) * size.x, (introFocusViewport.y - 0.5f) * size.y, 0f);
            introZoomRoot.localScale = zoomRootBaseScale * z;
            pivot.z = zoomRootBasePos.z;
            introZoomRoot.localPosition = pivot + (zoomRootBasePos - pivot) * z;
        }

        if (lensDistortion != null)
        {
            lensDistortion.intensity.value = fisheyeIntensity;
            lensDistortion.scale.value = Mathf.Max(0.01f, fisheyeScale);
        }
    }

    /// <summary>t = 0: start look (zoomed in + fisheye), t = 1: normal.</summary>
    private void ApplyIntroPose(float t)
    {
        t = Mathf.Clamp01(t);
        float zEase = MenuManager.Ease(introZoomEase, t, introZoomCurve);
        float z = Mathf.LerpUnclamped(Mathf.Max(0.01f, introZoomAmount), 1f, zEase);
        float f = MenuManager.Ease(introFisheyeEase, t, introFisheyeCurve);
        ApplyZoom(z, introZoomCamera,
                  Mathf.LerpUnclamped(introFisheyeIntensity, 0f, f),
                  Mathf.LerpUnclamped(introFisheyeScale, 1f, f));
    }

    private IEnumerator IntroZoomRoutine()
    {
        IsPlayingIntro = true;
        ApplyIntroPose(0f);

        if (introHoldTime > 0f) yield return Wait(introHoldTime);

        float t = 0f;
        float d = Mathf.Max(0f, introDuration);
        while (t < d)
        {
            ApplyIntroPose(t / d);
            yield return null;
            t += DeltaTime();
        }
        ApplyIntroPose(1f);
        FinishIntro();
        IsPlayingIntro = false;
    }

    /// <summary>Puts everything back exactly to normal after the intro.</summary>
    private void FinishIntro()
    {
        if (introCam != null)
        {
            introCam.transform.position = camInitPos;
            introCam.orthographicSize = camInitSize;
            introCam.fieldOfView = camInitFov;
        }
        if (introZoomRoot != null)
        {
            introZoomRoot.localScale = zoomRootBaseScale;
            introZoomRoot.localPosition = zoomRootBasePos;
        }

        if (lensDistortion != null)
        {
            lensDistortion.intensity.value = 0f;
            lensDistortion.scale.value = 1f;
            lensDistortion.active = false;
        }
        if (introCamData != null) introCamData.renderPostProcessing = savedPostProcessing;

        if (introRestoreCanvas && canvasMoved && introCanvas != null)
        {
            introCanvas.renderMode = savedRenderMode;
            introCanvas.worldCamera = savedCanvasCamera;
            introCanvas.planeDistance = savedPlaneDistance;
            introCanvas.sortingLayerID = savedSortingLayerId;
            introCanvas.sortingOrder = savedSortingOrder;
            canvasMoved = false;
        }
    }

    private bool TryGetFocusWorld(IntroFocus focus, Transform focusTarget, Vector2 focusOffset, out Vector3 world)
    {
        Transform t = null;
        if (focus == IntroFocus.Player && playerObject != null) t = playerObject.transform;
        else if (focus == IntroFocus.Target) t = focusTarget;

        world = t != null ? t.position + (Vector3)focusOffset : Vector3.zero;
        if (focus != IntroFocus.ScreenCenter && t == null)
            Debug.LogWarning("[SupermanGameManager] Zoom focus object is not assigned; zooming on the screen center.", this);
        return t != null;
    }

    // Yellow frame = what the camera sees at the start of the zoom-out (select this object to see it)
    private void OnDrawGizmosSelected()
    {
        if (!showIntroGizmo || !playIntroZoom || !introZoomCamera) return;
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null || !cam.orthographic) return;

        Vector3 camPos = Application.isPlaying && introReady ? camInitPos : cam.transform.position;
        float size = Application.isPlaying && introReady ? camInitSize : cam.orthographicSize;
        float z = Mathf.Max(0.01f, introZoomAmount);

        Vector3 shift = Vector3.zero;
        Transform t = introFocus == IntroFocus.Player && playerObject != null ? playerObject.transform
                    : introFocus == IntroFocus.Target ? introFocusTarget : null;
        if (t != null)
        {
            Vector3 focus = t.position + (Vector3)introFocusOffset;
            shift = new Vector3(focus.x - camPos.x, focus.y - camPos.y, 0f);
            Gizmos.color = new Color(1f, 0.85f, 0.1f);
            Gizmos.DrawWireSphere(focus, size * 0.03f);
        }

        Vector3 c = camPos + shift * (1f - 1f / z);
        float h = size / z, w = h * cam.aspect;
        Gizmos.color = new Color(1f, 0.85f, 0.1f);
        Gizmos.DrawWireCube(new Vector3(c.x, c.y, 0f), new Vector3(w * 2f, h * 2f, 0f));
    }

    private LensDistortion GetLensDistortion()
    {
        if (introVolume == null)
        {
            var go = new GameObject("IntroFisheyeVolume");
            go.layer = 0; // Default layer (included in the camera's Volume Mask by default)
            introVolume = go.AddComponent<Volume>();
            introVolume.isGlobal = true;
            introVolume.priority = 100f;
            introVolume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        }
        introVolume.weight = 1f;
        VolumeProfile profile = introVolume.profile; // runtime copy, so a profile asset is never modified
        if (!profile.TryGet(out LensDistortion lens)) lens = profile.Add<LensDistortion>(false);
        return lens;
    }

    // Post-processing only affects what the camera renders, so an Overlay Canvas is moved onto the camera
    private void MoveCanvasToCamera(Camera cam)
    {
        if (introCanvas == null || introCanvas.renderMode != RenderMode.ScreenSpaceOverlay) return;

        savedRenderMode = introCanvas.renderMode;
        savedCanvasCamera = introCanvas.worldCamera;
        savedPlaneDistance = introCanvas.planeDistance;
        savedSortingLayerId = introCanvas.sortingLayerID;
        savedSortingOrder = introCanvas.sortingOrder;
        canvasMoved = true;

        introCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        introCanvas.worldCamera = cam;
        introCanvas.planeDistance = cam.nearClipPlane + 0.5f; // in front of the scene
        var layers = SortingLayer.layers;
        if (layers.Length > 0) introCanvas.sortingLayerID = layers[layers.Length - 1].id; // top-most sorting layer
        introCanvas.sortingOrder = 32767; // drawn on top of sprites
    }

    // Moves every child of the root Canvas under one full-screen "ZoomRoot" so the whole UI can be scaled together
    private RectTransform WrapCanvasInZoomRoot(Canvas rc)
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
        introZoomCanvasRoot = rootRect;
        return rootRect;
    }

    private void Update()
    {
        if (phase == Phase.Playing && !IsGameOver)
            DetectMistake();
    }

    // ============================================================
    //  Public API
    // ============================================================

    /// <summary>Starts Superman mode. Hooked to StartGameButton automatically.</summary>
    public void StartSuperman()
    {
        if (phase != Phase.Idle || IsGameOver || validLevels.Count == 0) return;

        if (startGameButton != null) startGameButton.gameObject.SetActive(false);

        Score = 0;
        TotalClears = 0;
        MaxCombo = 0;
        Mistakes = 0;
        IsNewHighScore = false;
        DifficultyLevel = 0;
        streak = 0;
        mistakeThisClear = false;
        SetFever(false);
        UpdateScoreUI();

        IsFinished = false;
        phase = Phase.Transition;
        flowRoutine = StartCoroutine(FirstLevelRoutine());
    }

    /// <summary>
    /// Called by the current game when its question is done.
    /// Sets IsFinished = true so the game stops itself, adds score, then moves on.
    /// </summary>
    public override void CompleteCurrentStage()
    {
        if (phase != Phase.Playing || IsGameOver || IsFinished) return;

        IsFinished = true;              // the game sees this and stops by itself
        phase = Phase.Transition;
        TotalClears++;

        // Score (multiplier applies if Fever was already on)
        float mult = IsFever ? feverMultiplier : 1f;
        AddScore(Mathf.RoundToInt(scorePerClear * mult));

        // Fever time bonus (also only if Fever was already on)
        if (IsFever && feverTimeBonus > 0f && countdown != null)
            countdown.AddBonusTime(feverTimeBonus);   // plays the green bonus effect

        // Streak / Fever
        if (!mistakeThisClear)
        {
            streak++;
            if (streak > MaxCombo) MaxCombo = streak;
            if (!IsFever && feverStreakRequired > 0 && streak >= feverStreakRequired)
                SetFever(true);
        }
        mistakeThisClear = false;

        // Difficulty: every N clears in total, all levels go up one step.
        // Queued per level and applied right before that level's next StartGame(),
        // because a level that was never shown hasn't run Awake() yet
        // (ScamManager's Awake() would reset its difficulty).
        if (increaseDifficultyEvery > 0 && TotalClears % increaseDifficultyEvery == 0)
        {
            DifficultyLevel++;
            foreach (int i in validLevels) levels[i].pendingDifficulty++;
        }

        if (flowRoutine != null) StopCoroutine(flowRoutine);
        flowRoutine = StartCoroutine(NextRoundRoutine());
    }

    /// <summary>
    /// Counts a mistake: resets the streak and ends Fever.
    /// Called automatically when the current level's Error Redbox appears,
    /// but can also be called from other scripts.
    /// </summary>
    public void ReportMistake()
    {
        if (phase == Phase.Over || IsGameOver) return;

        mistakeThisClear = true;
        Mistakes++;
        streak = 0;
        if (IsFever) SetFever(false);

        // Works with any player script (SupermanPlayerScript, PlayerScript, ...) that has PlayWrongAnimation()
        if (playWrongAnimOnMistake && playerObject != null)
            playerObject.SendMessage("PlayWrongAnimation", SendMessageOptions.DontRequireReceiver);

        // Shake the player's root too (uses that ScreenShake's Default Settings)
        if (playerShake != null)
            playerShake.Shake();

        onMistake.Invoke();
    }

    /// <summary>Time is up (called by CountdownBar). Stops everything and shows NextGameButton.</summary>
    public override void GameOver()
    {
        if (IsGameOver) return;

        IsGameOver = true;          // every game locks itself and keeps its red box
        phase = Phase.Over;
        Debug.Log($"[SupermanGameManager] Game Over. Score: {Score}");

        // A running slide is allowed to finish (so screens don't stop half-way);
        // it checks IsGameOver and won't start a new question.

        if (playerObject != null)
            playerObject.SendMessage("GameOver", SendMessageOptions.DontRequireReceiver);

        if (IsFever) SetFever(false);
        if (DebugMode && nextGameButton != null) nextGameButton.gameObject.SetActive(true);
        SaveRecords();
        onTimeUp.Invoke();
    }

    /// <summary>restartButton: reloads this scene without the fisheye zoom-out.</summary>
    public void RestartGame()
    {
        if (leaving) return;
        leaving = true;
        SetLeaveButtonsInteractable(false);

        skipIntroOnce = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameObject.scene.name);
    }

    /// <summary>homeButton: zooms in (Home Zoom-in settings), then MenuManager.CompleteCurrentLevelAndReturnWithFisheye().</summary>
    public void GoHome()
    {
        if (leaving) return;
        leaving = true;
        SetLeaveButtonsInteractable(false);
        StartCoroutine(HomeRoutine());
    }

    private IEnumerator HomeRoutine()
    {
        if (playHomeZoom)
        {
            PrepareZoom(homeFocus, homeFocusTarget, homeFocusOffset, homeFisheye, homeZoomCanvas);

            float t = 0f;
            float d = Mathf.Max(0f, homeZoomDuration);
            while (true)
            {
                float p = d > 0f ? Mathf.Clamp01(t / d) : 1f;
                float z = Mathf.LerpUnclamped(1f, Mathf.Max(0.01f, homeZoomAmount), MenuManager.Ease(homeZoomEase, p, homeZoomCurve));
                float f = MenuManager.Ease(homeFisheyeEase, p, homeFisheyeCurve);
                ApplyZoom(z, homeZoomCamera,
                          Mathf.LerpUnclamped(0f, homeFisheyeIntensity, f),
                          Mathf.LerpUnclamped(1f, homeFisheyeScale, f));

                if (p >= 1f) break;
                yield return null;
                t += DeltaTime();
            }
        }

        if (homeHoldTime > 0f) yield return Wait(homeHoldTime);

        // Marks this level as cleared and goes back to the Menu (MenuManager handles the scene change)
        Time.timeScale = 1f;
        MenuManager.CompleteCurrentLevelAndReturnWithFisheye();
    }

    private void SetLeaveButtonsInteractable(bool value)
    {
        if (restartButton != null) restartButton.interactable = value;
        if (homeButton != null) homeButton.interactable = value;
    }

    /// <summary>Finds a GameObject by name in this scene, ignoring upper / lower case (hidden objects included).</summary>
    private GameObject FindInSceneIgnoreCase(string objName)
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid()) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, objName, StringComparison.OrdinalIgnoreCase)) return t.gameObject;
        return null;
    }

    /// <summary>Finds a component by GameObject name in this scene (hidden objects included).</summary>
    private T FindInScene<T>(string objName) where T : Component
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid()) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objName)
                {
                    T c = t.GetComponent<T>();
                    if (c != null) return c;
                }
        return null;
    }

    public void OnNextGameClicked()
    {
        string scene = string.IsNullOrEmpty(nextSceneName) ? SceneManager.GetActiveScene().name : nextSceneName;
        SceneManager.LoadScene(scene);
    }

    // ============================================================
    //  Flow
    // ============================================================
    /// <summary>
    /// 3, 2, 1: each number appears at Number Start Scale and shrinks to Number End Scale.
    /// GO: grows from GO Start Scale to GO End Scale while shaking. Then the text is hidden
    /// and StartSuperman() is called. Speed is set by Number Duration / GO Duration (not real seconds of a clock).
    /// </summary>
    private IEnumerator CountdownRoutine()
    {
        IsCountingDown = true;

        if (countdownStartDelay > 0f) yield return Wait(countdownStartDelay);

        if (countdownNumber == null)
        {
            Debug.LogWarning("[SupermanGameManager] Countdown Number is not assigned; starting the game without a countdown.", this);
            IsCountingDown = false;
            StartSuperman();
            yield break;
        }

        RectTransform rt = countdownNumber.rectTransform;
        Vector3 baseScale = rt.localScale;
        Vector2 basePos = rt.anchoredPosition;
        countdownNumber.gameObject.SetActive(true);

        // 3, 2, 1 (shrink)
        for (int n = Mathf.Max(0, countdownFrom); n >= 1; n--)
        {
            countdownNumber.text = n.ToString();
            onCountdownStep.Invoke(n);

            float t = 0f;
            float d = Mathf.Max(0.01f, numberDuration);
            while (t < d)
            {
                float k = numberScaleCurve.Evaluate(Mathf.Clamp01(t / d));
                rt.localScale = baseScale * Mathf.LerpUnclamped(numberStartScale, numberEndScale, k);
                yield return null;
                t += DeltaTime();
            }
        }

        // GO (grow + shake)
        countdownNumber.text = goText;
        onCountdownStep.Invoke(0);
        {
            float t = 0f;
            float d = Mathf.Max(0.01f, goDuration);
            float nextShake = 0f;
            Vector2 offset = Vector2.zero;
            while (t < d)
            {
                float p = Mathf.Clamp01(t / d);
                rt.localScale = baseScale * Mathf.LerpUnclamped(goStartScale, goEndScale, goScaleCurve.Evaluate(p));

                if (goShakeStrength > 0f && t >= nextShake)
                {
                    float strength = goShakeStrength * (goShakeFadeOut ? 1f - p : 1f);
                    offset = UnityEngine.Random.insideUnitCircle * strength;
                    nextShake = t + 1f / Mathf.Max(1f, goShakeFrequency);
                }
                rt.anchoredPosition = basePos + offset;

                yield return null;
                t += DeltaTime();
            }
        }

        // Restore and hide, then start
        rt.localScale = baseScale;
        rt.anchoredPosition = basePos;
        countdownNumber.gameObject.SetActive(false);

        IsCountingDown = false;
        StartSuperman();
    }

    private float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private IEnumerator FirstLevelRoutine()
    {
        if (previewLevel >= 0 && currentLevel == previewLevel)
        {
            // The preview level is already on screen: start it right here, no slide
            SetScriptEnabled(previewLevel, true);
            yield return null;          // one frame so its Start() runs before StartGame()
            StartLevelGame(previewLevel);
        }
        else
        {
            int first = PickRandomLevel();
            yield return SwitchToLevel(first);
        }

        // Timer starts once the first level is on screen
        if (timeManager != null && !IsGameOver)
            timeManager.SendMessage("StartTimer", SendMessageOptions.DontRequireReceiver);

        flowRoutine = null;
    }

    private IEnumerator NextRoundRoutine()
    {
        if (nextQuestionDelay > 0f) yield return Wait(nextQuestionDelay);
        if (IsGameOver) { flowRoutine = null; yield break; }

        // Games have stopped by now; let them accept StartGame() again
        IsFinished = false;

        // CountdownBar pauses itself whenever IsFinished is true (stoppedByGameManager),
        // and never resumes on its own, so start it again here.
        ResumeTimer();

        int next = PickRandomLevel();
        if (next == currentLevel)
            StartLevelGame(next);           // same level: just a new question
        else
            yield return SwitchToLevel(next);

        flowRoutine = null;
    }

    /// <summary>Slides the current level out and the new one in, then starts it.</summary>
    private IEnumerator SwitchToLevel(int next)
    {
        int from = currentLevel;
        int dir = PickSlideDir();       // +1: incoming starts on the right

        IsSliding = true;

        if (from >= 0) SetScriptEnabled(from, false);    // outgoing level stops reading input
        ApplyOffset(next, dir);                          // place incoming one screen away
        SetLevelActive(next, true);

        // One frame so a freshly activated level runs its Awake / Start before StartGame()
        yield return null;

        bool started = false;
        if (startGameBeforeSlide && !IsGameOver)
        {
            StartLevelGame(next);
            started = true;
        }

        float t = 0f;
        while (t < slideDuration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float e = slideEase.Evaluate(Mathf.Clamp01(t / slideDuration));
            if (from >= 0) ApplyOffset(from, -dir * e);  // outgoing: 0 -> -dir
            ApplyOffset(next, dir * (1f - e));           // incoming: dir -> 0
            yield return null;
        }

        if (from >= 0)
        {
            ApplyOffset(from, 0f);
            SetLevelActive(from, false);
        }
        ApplyOffset(next, 0f);
        currentLevel = next;
        IsSliding = false;

        if (!started && !IsGameOver) StartLevelGame(next);
    }

    private void StartLevelGame(int index)
    {
        if (IsGameOver) return;

        SupermanLevel lv = levels[index];
        currentLevel = index;
        lv.redboxWasActive = lv.errorRedbox != null && lv.errorRedbox.activeSelf;
        phase = Phase.Playing;

        // Apply difficulty steps earned since this level was last played
        while (lv.pendingDifficulty > 0)
        {
            lv.pendingDifficulty--;
            CallMethod(lv, lv.increaseDifficultyMethod);
        }

        CallMethod(lv, lv.startGameMethod);
    }

    private void ResumeTimer()
    {
        if (timeManager != null && !IsGameOver)
            timeManager.SendMessage("StartTimer", SendMessageOptions.DontRequireReceiver);
    }

    private int PickRandomLevel()
    {
        return validLevels[UnityEngine.Random.Range(0, validLevels.Count)];
    }

    private int PickSlideDir()
    {
        switch (slideFrom)
        {
            case SlideFrom.Left: return -1;
            case SlideFrom.Random: return UnityEngine.Random.value < 0.5f ? -1 : 1;
            default: return 1;
        }
    }

    private object Wait(float seconds)
    {
        return useUnscaledTime ? (object)new WaitForSecondsRealtime(seconds) : new WaitForSeconds(seconds);
    }

    // ============================================================
    //  Score / Fever / Mistakes
    // ============================================================
    private void AddScore(int amount)
    {
        Score += amount;
        UpdateScoreUI();
        onScoreChanged.Invoke(Score);

        // "+200" (or "+400" during Fever)
        GameObject pop = SpawnPopup(scorePopup, amount.ToString(), IsFever ? scorePopupFeverScale : 1f);
        if (pop != null && IsFever && scorePopupFeverColor)
        {
            TMP_Text txt = pop.GetComponentInChildren<TMP_Text>(true);
            if (txt != null) txt.color = scorePopupFeverTextColor;
        }
    }

    private void OnCountdownBonusTime()
    {
        SpawnPopup(timePopup, null, 1f);   // null = keep the prefab's own text
    }

    // ============================================================
    //  Pop-ups (AddScore / AddTime)
    // ============================================================
    private static void HideIfSceneTemplate(GameObject obj)
    {
        if (obj != null && obj.scene.IsValid()) obj.SetActive(false);
    }

    /// <summary>Creates a pop-up, sets its text (value null = text untouched) and plays: pop in + jump up -> show -> fade out -> destroy.</summary>
    private GameObject SpawnPopup(PopupSettings p, string value, float sizeMultiplier)
    {
        if (p == null || p.prefab == null) return null;

        Transform parent = p.spawnParent != null ? p.spawnParent
                         : p.spawnAt != null ? p.spawnAt.parent
                         : canvasRect;
        if (parent == null) return null;

        GameObject go = Instantiate(p.prefab, parent, false);
        go.SetActive(true);
        go.transform.SetAsLastSibling();   // drawn on top

        RectTransform rt = go.transform as RectTransform;
        if (rt == null)
        {
            Debug.LogWarning("[SupermanGameManager] Pop-up prefab must be a UI object (RectTransform).", p.prefab);
            Destroy(go);
            return null;
        }

        if (p.spawnAt != null) rt.position = p.spawnAt.position;
        rt.anchoredPosition += p.spawnOffset + new Vector2(
            UnityEngine.Random.Range(-p.randomOffset.x, p.randomOffset.x),
            UnityEngine.Random.Range(-p.randomOffset.y, p.randomOffset.y));

        if (value != null && !string.IsNullOrEmpty(p.textFormat))
        {
            TMP_Text txt = go.GetComponentInChildren<TMP_Text>(true);
            if (txt != null)
            {
                try { txt.text = string.Format(p.textFormat, value); }
                catch (FormatException) { txt.text = p.textFormat; }
            }
        }

        foreach (Graphic g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null) group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        StartCoroutine(PopupRoutine(p, rt, group, rt.localScale * Mathf.Max(0.01f, sizeMultiplier)));
        return go;
    }

    private IEnumerator PopupRoutine(PopupSettings p, RectTransform rt, CanvasGroup group, Vector3 baseScale)
    {
        Vector2 start = rt.anchoredPosition;
        float scaleIn = Mathf.Max(0f, p.scaleInDuration);
        float jump = Mathf.Max(0f, p.jumpDuration);
        float appear = Mathf.Max(scaleIn, jump);
        float show = Mathf.Max(0f, p.showTime);
        float fade = Mathf.Max(0f, p.fadeDuration);
        float total = appear + show + fade;

        float t = 0f;
        while (rt != null)
        {
            // Pop in
            float s = scaleIn > 0f ? MenuManager.Ease(p.scaleInEase, Mathf.Clamp01(t / scaleIn), p.scaleInCurve) : 1f;
            rt.localScale = baseScale * Mathf.LerpUnclamped(p.startScale, 1f, s);

            // Jump up
            float j = jump > 0f ? MenuManager.Ease(p.jumpEase, Mathf.Clamp01(t / jump), p.jumpCurve) : 1f;
            float y = p.jumpHeight * j;

            // Fade out (drifting up a bit more)
            float f = fade > 0f ? Mathf.Clamp01((t - appear - show) / fade) : (t >= appear + show ? 1f : 0f);
            y += p.fadeDrift * f;
            group.alpha = 1f - f;

            rt.anchoredPosition = start + Vector2.up * y;

            if (t >= total) break;
            yield return null;
            t += DeltaTime();
        }

        if (rt != null) Destroy(rt.gameObject);
    }

    /// <summary>Saves this run's stats, and the high score if it was beaten.</summary>
    private void SaveRecords()
    {
        PlayerPrefs.SetInt(KeyLastScore, Score);
        PlayerPrefs.SetInt(KeyLastClears, TotalClears);
        PlayerPrefs.SetInt(KeyLastMaxCombo, MaxCombo);
        PlayerPrefs.SetInt(KeyLastMistakes, Mistakes);

        IsNewHighScore = Score > HighScore;
        if (IsNewHighScore)
        {
            PlayerPrefs.SetInt(KeyHighScore, Score);
            PlayerPrefs.SetInt(KeyHighScoreClears, TotalClears);
            PlayerPrefs.SetInt(KeyHighScoreMaxCombo, MaxCombo);
            PlayerPrefs.SetInt(KeyHighScoreMistakes, Mistakes);
        }
        PlayerPrefs.Save();

        Debug.Log($"[SupermanGameManager] Run: Score {Score}, Clears {TotalClears}, Max Combo {MaxCombo}, Mistakes {Mistakes}" +
                  (IsNewHighScore ? "  -> NEW HIGH SCORE" : $"  (High Score {HighScore})"), this);

        UpdateHighScoreUI();
        if (IsNewHighScore) onNewHighScore.Invoke(Score);
    }

    private void UpdateHighScoreUI()
    {
        if (highScoreUI == null) return;
        try { highScoreUI.text = string.Format(highScoreFormat, HighScore); }
        catch (FormatException) { highScoreUI.text = HighScore.ToString(); }
    }

    private void UpdateScoreUI()
    {
        if (scoreUI == null) return;
        try { scoreUI.text = string.Format(scoreFormat, Score); }
        catch (FormatException) { scoreUI.text = Score.ToString(); }
    }

    private void SetFever(bool on)
    {
        if (IsFever == on)
        {
            if (feverEffect != null && feverEffect.activeSelf != on) feverEffect.SetActive(on);
            return;
        }

        IsFever = on;
        if (feverEffect != null) feverEffect.SetActive(on);
        SetPlayerFeverAnim(on);
        if (on) onFeverStart.Invoke();
        else onFeverEnd.Invoke();
    }

    private void SetupPlayerAnimator()
    {
        if (playerObject != null)
            playerAnimator = playerObject.GetComponentInChildren<Animator>(true);

        if (playerAnimator == null || string.IsNullOrEmpty(feverAnimParam)) return;

        feverParamHash = Animator.StringToHash(feverAnimParam);
        foreach (AnimatorControllerParameter p in playerAnimator.parameters)
        {
            if (p.nameHash == feverParamHash && p.type == AnimatorControllerParameterType.Bool)
            {
                hasFeverParam = true;
                break;
            }
        }

        if (!hasFeverParam)
            Debug.LogWarning($"[SupermanGameManager] Player Animator has no Bool parameter named \"{feverAnimParam}\".", playerAnimator);

        SetPlayerFeverAnim(false);
    }

    /// <summary>Sets the Player Animator's Fever bool (true = Fever on).</summary>
    private void SetPlayerFeverAnim(bool on)
    {
        if (!hasFeverParam || playerAnimator == null) return;
        if (!playerAnimator.isActiveAndEnabled) return;   // Animator ignores parameters while disabled
        playerAnimator.SetBool(feverParamHash, on);
    }

    /// <summary>A mistake = the current level's Error Redbox turning on.</summary>
    private void DetectMistake()
    {
        if (currentLevel < 0) return;
        SupermanLevel lv = levels[currentLevel];
        if (lv.errorRedbox == null) return;

        bool active = lv.errorRedbox.activeSelf;
        if (active && !lv.redboxWasActive) ReportMistake();
        lv.redboxWasActive = active;
    }

    // ============================================================
    //  Level visibility / slide offset
    // ============================================================
    private void SetLevelActive(int i, bool active)
    {
        SupermanLevel lv = levels[i];
        if (lv.gameScreen != null) lv.gameScreen.gameObject.SetActive(active);
        if (lv.gameUI != null) lv.gameUI.gameObject.SetActive(active);
        SetScriptEnabled(i, active);
    }

    /// <summary>
    /// Also disables the level script itself, so it can't read input even if
    /// it isn't placed under its own GameUI / GameScreen.
    /// </summary>
    private void SetScriptEnabled(int i, bool enabledState)
    {
        SupermanLevel lv = levels[i];
        if (lv.gameScript != null && lv.gameScript != this) lv.gameScript.enabled = enabledState;
    }

    /// <summary>Moves a level sideways by a number of screen widths (same method as LevelSlider).</summary>
    private void ApplyOffset(int i, float screens)
    {
        SupermanLevel lv = levels[i];

        if (lv.gameScreen != null && worldCamera != null)
        {
            float worldWidth = worldCamera.orthographicSize * 2f * worldCamera.aspect;
            lv.gameScreen.position = lv.screenBase + Vector3.right * (screens * worldWidth);
        }

        if (lv.gameUI != null && canvasRect != null)
        {
            float uiWidth = canvasRect.rect.width;
            lv.gameUI.anchoredPosition = lv.uiBase + Vector2.right * (screens * uiWidth);
        }
    }

    // ============================================================
    //  Finding / calling the level scripts
    // ============================================================
    private const BindingFlags MethodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void ResolveGameScript(SupermanLevel lv)
    {
        // A GameObject dragged into the field may give the wrong component; check it has StartGame()
        if (lv.gameScript != null && !HasGameMethods(lv.gameScript))
        {
            MonoBehaviour fixedScript = FindGameScriptIn(lv.gameScript.gameObject, false);
            lv.gameScript = fixedScript;
        }

        if (lv.gameScript == null && lv.gameUI != null) lv.gameScript = FindGameScriptIn(lv.gameUI.gameObject, true);
        if (lv.gameScript == null && lv.gameScreen != null) lv.gameScript = FindGameScriptIn(lv.gameScreen.gameObject, true);

        if (lv.gameScript != null)
        {
            Type t = lv.gameScript.GetType();
            lv.startGameMethod = t.GetMethod("StartGame", MethodFlags, null, Type.EmptyTypes, null);
            lv.increaseDifficultyMethod = t.GetMethod("IncreaseDifficulty", MethodFlags, null, Type.EmptyTypes, null);
        }
    }

    private MonoBehaviour FindGameScriptIn(GameObject root, bool children)
    {
        MonoBehaviour[] list = children
            ? root.GetComponentsInChildren<MonoBehaviour>(true)
            : root.GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour mb in list)
            if (mb != null && mb != this && HasGameMethods(mb)) return mb;
        return null;
    }

    private static bool HasGameMethods(MonoBehaviour mb)
    {
        Type t = mb.GetType();
        return t.GetMethod("StartGame", MethodFlags, null, Type.EmptyTypes, null) != null
            && t.GetMethod("IncreaseDifficulty", MethodFlags, null, Type.EmptyTypes, null) != null;
    }

    /// <summary>Reads the level script's own red box field (errorRedbox / error_Redbox / Error_RedBox ...).</summary>
    private static GameObject FindRedbox(MonoBehaviour script)
    {
        if (script == null) return null;

        foreach (FieldInfo f in script.GetType().GetFields(MethodFlags))
        {
            if (!f.Name.Replace("_", "").ToLowerInvariant().Contains("redbox")) continue;

            object value = f.GetValue(script);
            if (value is GameObject go && go != null) return go;
            if (value is Component c && c != null) return c.gameObject;
        }
        return null;
    }

    private void CallMethod(SupermanLevel lv, MethodInfo method)
    {
        if (lv == null || lv.gameScript == null || method == null) return;
        try
        {
            method.Invoke(lv.gameScript, null);
        }
        catch (TargetInvocationException e)
        {
            Debug.LogException(e.InnerException ?? e, lv.gameScript);
        }
    }

#if UNITY_EDITOR
    // Every serialized field of GameManager EXCEPT debugMode (Debug Settings stays visible).
    // If you add new fields to GameManager, add their names here to hide them too.
    public static readonly string[] UnusedBaseFields =
    {
        "levelText", "startButton", "nextButton", "totalCounts", "backgroundObject", "backgroundScenes",
        "typeGameManager", "timerManager", "player", "boss", "exitTransition", "stagesToExit",
        "enableGameOverTransition", "gameOverCamera", "irisTransition", "irisCanvas", "gameOverZoomSize", "gameOverZoomFieldOfView",
        "gameOverZoomXOffset", "gameOverZoomYOffset", "gameOverZoomDuration", "gameOverZoomCurve", "irisDelay", "gameOverLoadDelay",
        "gameOverSceneName", "showGameOverGizmo", "gameUI", "gameUIDropDelay", "gameUIDropOffsetY", "gameUIDropDuration",
        "gameUIDropCurve", "npcObjects", "minNpcCount", "maxNpcCount", "nextStageDelay"
    };
#endif
}

#if UNITY_EDITOR
/// <summary>Hides GameManager's unused fields and adds test buttons in Play Mode.</summary>
[CustomEditor(typeof(SupermanGameManager))]
public class SupermanGameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, SupermanGameManager.UnusedBaseFields);
        serializedObject.ApplyModifiedProperties();

        var gm = (SupermanGameManager)target;

        // ---- Records (works in and out of Play Mode) ----
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Records (PlayerPrefs)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"High Score: {SupermanGameManager.HighScore}   (Clears {SupermanGameManager.HighScoreClears}, " +
            $"Max Combo {SupermanGameManager.HighScoreMaxCombo}, Mistakes {SupermanGameManager.HighScoreMistakes})\n" +
            $"Last Run: Score {SupermanGameManager.LastScore}, Clears {SupermanGameManager.LastClears}, " +
            $"Max Combo {SupermanGameManager.LastMaxCombo}, Mistakes {SupermanGameManager.LastMistakes}" +
            (Application.isPlaying ? $"\nThis Run: Score {gm.Score}, Clears {gm.TotalClears}, Max Combo {gm.MaxCombo}, Mistakes {gm.Mistakes}" : ""),
            MessageType.Info);
        if (GUILayout.Button("Reset High Score & Records"))
        {
            if (EditorUtility.DisplayDialog("Reset Records",
                "Delete the local high score and the last run's stats?", "Reset", "Cancel"))
            {
                SupermanGameManager.ResetRecords();
                if (Application.isPlaying) gm.SendMessage("UpdateHighScoreUI", SendMessageOptions.DontRequireReceiver);
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test buttons.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(
            $"Level: {gm.CurrentLevelIndex}   Diff: {gm.DifficultyLevel}   Score: {gm.Score}   Streak: {gm.Streak}   Fever: {gm.IsFever}   Sliding: {gm.IsSliding}   Intro: {gm.IsPlayingIntro}   Countdown: {gm.IsCountingDown}   Debug: {gm.DebugMode}");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Start")) gm.StartSuperman();
        if (GUILayout.Button("Complete Stage")) gm.CompleteCurrentStage();
        if (GUILayout.Button("Mistake")) gm.ReportMistake();
        if (GUILayout.Button("Game Over")) gm.GameOver();
        EditorGUILayout.EndHorizontal();

        Repaint();
    }
}
#endif
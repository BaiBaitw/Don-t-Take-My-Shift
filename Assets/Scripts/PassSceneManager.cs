using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Pass scene (shown after a level is cleared).
///
/// Flow:
///   1. Another scene calls PassSceneManager.LoadPassScene(level) (or SetIncomingLevel(level) and loads the scene itself).
///   2. Only that level's Player / Background are shown; the other five are hidden.
///      Boss: that level's Boss is shown. If it isn't assigned yet, the first assigned Boss is used instead.
///   3. Player Animator "Walk" = true, the Player walks in from the left edge of the screen to the Stop Point,
///      then "Walk" = false.
///   4. Boss Animator "WinSay" = true, and smallBubbleChat pops up from the bottom-right with a random line.
///   5. After Win Say Duration, "WinSay" = false. The bubble stays on screen.
///   6. After Return To Menu Delay, MenuManager.CompleteLevelAndReturn(level) is called.
///      The level is whichever of the six Player / Background objects is currently active
///      (index 0-5 + Level Number Offset).
///      The IrisNonStopTransition Prefab is spawned Transition Lead Time seconds before the scene changes.
///
/// Suggested Hierarchy names (the "Auto Assign by Name" button uses these):
///   Player_Typist, Player_Illustrator, Player_Composer, Player_Factory, Player_Engineer, Player_Scam
///   BG_Typist,     BG_Illustrator,     ...
///   Boss_Typist,   Boss_Illustrator,   ...
/// </summary>
public class PassSceneManager : MonoBehaviour
{
    // ============================================================
    //  Level types / data
    // ============================================================
    public enum LevelType { Typist = 0, Illustrator = 1, Composer = 2, Factory = 3, Engineer = 4, Scam = 5 }

    public const int LevelCount = 6;
    public const string DefaultPassSceneName = "PassScene";
    public const string PlayerPrefix = "Player_";
    public const string BackgroundPrefix = "BG_";
    public const string BossPrefix = "Boss_";

    [Serializable]
    public class PassLevelSet
    {
        [Tooltip("Label only (filled automatically)")]
        public string levelName;

        [Tooltip("This level's Player (Animator with Bool \"Walk\" on it or on a child)")]
        public GameObject player;

        [Tooltip("This level's Background")]
        public GameObject background;

        [Tooltip("This level's Boss (Animator with Bool \"WinSay\" on it or on a child).\nEmpty = the first assigned Boss is used")]
        public GameObject boss;
    }

    // ============================================================
    //  Called from other scenes
    // ============================================================
    private static bool hasPendingLevel;
    private static LevelType pendingLevel;

    /// <summary>Tells the Pass scene which level the player came from. Call before loading the scene.</summary>
    public static void SetIncomingLevel(LevelType level)
    {
        pendingLevel = level;
        hasPendingLevel = true;
    }

    /// <summary>Index version: 0 Typist, 1 Illustrator, 2 Composer, 3 Factory, 4 Engineer, 5 Scam.</summary>
    public static void SetIncomingLevel(int index)
    {
        SetIncomingLevel((LevelType)Mathf.Clamp(index, 0, LevelCount - 1));
    }

    /// <summary>Name version ("Typist", "scam", ...). Returns false if the name is unknown.</summary>
    public static bool SetIncomingLevel(string levelName)
    {
        if (!string.IsNullOrEmpty(levelName) && Enum.TryParse(levelName.Trim(), true, out LevelType level))
        {
            SetIncomingLevel(level);
            return true;
        }
        Debug.LogWarning($"[PassSceneManager] Unknown level name \"{levelName}\".");
        return false;
    }

    /// <summary>Sets the level and loads the Pass scene in one call.</summary>
    public static void LoadPassScene(LevelType level, string passSceneName = DefaultPassSceneName)
    {
        SetIncomingLevel(level);
        SceneManager.LoadScene(passSceneName);
    }

    // ============================================================
    //  Inspector
    // ============================================================
    [Header("Levels (Typist, Illustrator, Composer, Factory, Engineer, Scam)")]
    [SerializeField] private PassLevelSet[] levels = CreateDefaultLevels();

    [Header("Default / Test")]
    [Tooltip("Used when this scene is played directly (no level was passed in from another scene)")]
    [SerializeField] private LevelType defaultLevel = LevelType.Typist;
    [Tooltip("Start the walk-in sequence automatically when the scene loads")]
    [SerializeField] private bool playOnStart = true;
    [Tooltip("Use unscaled time so the sequence still runs when Time.timeScale = 0")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Player Walk-in")]
    [Tooltip("Bool parameter on the Player's Animator")]
    [SerializeField] private string walkParam = "Walk";
    [Tooltip("Seconds to walk from the start point to the Stop Point")]
    [SerializeField, Min(0f)] private float walkDuration = 2.5f;
    [Tooltip("Speed curve of the walk (X: time 0-1, Y: progress 0-1)")]
    [SerializeField] private AnimationCurve walkCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("On: start just outside the left edge of the camera.\nOff: start at Walk Start Point")]
    [SerializeField] private bool startFromScreenLeftEdge = true;
    [Tooltip("How far outside the left edge the Player starts (world units)")]
    [SerializeField] private float offscreenMargin = 1.5f;
    [Tooltip("Only used when Start From Screen Left Edge is off. Drag the handle in the Scene view")]
    [SerializeField] private Vector3 walkStartPoint = new Vector3(-12f, -2f, 0f);
    [Tooltip("Where the Player stops. Drag the green handle in the Scene view")]
    [SerializeField] private Vector3 playerStopPoint = new Vector3(-3f, -2f, 0f);
    [Tooltip("On: the Player keeps its own Y (only X moves).\nOff: the Player walks to the Stop Point's Y too")]
    [SerializeField] private bool keepPlayerY = true;
    [Tooltip("Camera used for the screen edge and for UI bubbles. Empty = Camera.main")]
    [SerializeField] private Camera worldCamera;

    [Header("Boss")]
    [Tooltip("Bool parameter on the Boss' Animator")]
    [SerializeField] private string winSayParam = "WinSay";
    [Tooltip("Seconds between the Player stopping and the Boss speaking")]
    [SerializeField, Min(0f)] private float delayBeforeBossSay = 0.3f;
    [Tooltip("Seconds WinSay stays true. The bubble stays on screen afterwards")]
    [SerializeField, Min(0f)] private float winSayDuration = 3f;

    [Header("Small Bubble Chat")]
    [Tooltip("smallBubbleChat Prefab (must have a Text / TextMeshPro on a child)")]
    [SerializeField] private GameObject smallBubbleChatPrefab;
    [Tooltip("Optional parent. For a UI prefab (RectTransform), put your Canvas here. Empty = world root (or the first Canvas for UI prefabs)")]
    [SerializeField] private Transform bubbleParent;
    [Tooltip("Where the bubble ends up (world position). Drag the yellow handle in the Scene view")]
    [SerializeField] private Vector3 bubblePosition = new Vector3(3f, 2f, 0f);
    [Tooltip("Where the bubble pops from, relative to Bubble Position. Default = bottom-right")]
    [SerializeField] private Vector3 popFromOffset = new Vector3(1.5f, -1f, 0f);
    [Tooltip("Seconds of the pop-up animation")]
    [SerializeField, Min(0f)] private float popDuration = 0.35f;
    [Tooltip("Speed curve of the pop-up (X: time 0-1, Y: progress 0-1). Y above 1 = overshoot / bounce")]
    [SerializeField] private AnimationCurve popCurve = CreatePopCurve();
    [Tooltip("Scale when the pop-up starts (x the Prefab's own scale)")]
    [SerializeField, Min(0f)] private float popStartScale = 0f;
    [Tooltip("Final pop-up size (x the Prefab's own scale)")]
    [SerializeField, Min(0f)] private float popScale = 1f;
    [Tooltip("Size of the yellow guide box in the Scene view (world units, before Pop Scale)")]
    [SerializeField] private Vector2 bubbleGuideSize = new Vector2(3f, 1.5f);
    [Tooltip("Boss lines. One is picked at random (never the same one twice in a row). Max 2 lines, max 9 characters per line")]
    [SerializeField, TextArea(2, 3)] private string[] bossLines = (string[])DefaultBossLines.Clone();

    [Header("Return To Menu")]
    [SerializeField] private bool autoReturnToMenu = true;
    [Tooltip("Number passed to MenuManager.CompleteLevelAndReturn() = active slot index (0-5) + this.\n0: Typist=0 ... Scam=5\n1: Typist=1 ... Scam=6 (if MenuManager counts levels from 1)")]
    [SerializeField] private int levelNumberOffset = 0;
    [Tooltip("Seconds after WinSay turns false before the Menu scene loads")]
    [SerializeField, Min(0f)] private float returnToMenuDelay = 1.5f;

    [Header("IrisNonStopTransition")]
    [Tooltip("IrisNonStopTransition Prefab, spawned before the scene changes (plays its own transition animation)")]
    [FormerlySerializedAs("fadePrefab")]
    [SerializeField] private GameObject irisNonStopTransitionPrefab;
    [Tooltip("Spawn IrisNonStopTransition this many seconds before the Menu scene loads.\nIf it is longer than Return To Menu Delay, it spawns right when WinSay ends and the wait becomes this long")]
    [FormerlySerializedAs("fadeLeadTime")]
    [SerializeField, Min(0f)] private float transitionLeadTime = 1f;
    [Tooltip("Optional parent. Empty: a transition with its own Canvas goes to the scene root; a UI transition without a Canvas goes under the first Canvas (drawn on top)")]
    [FormerlySerializedAs("fadeParent")]
    [SerializeField] private Transform transitionParent;

    [Header("Guides")]
    [SerializeField] private bool showGuides = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onPlayerArrived = new UnityEvent();
    [SerializeField] private UnityEvent onBossSay = new UnityEvent();
    [SerializeField] private UnityEvent onBossSayEnd = new UnityEvent();
    [SerializeField] private UnityEvent onBeforeLoadMenu = new UnityEvent();

    // ============================================================
    //  Default data
    // ============================================================
    public static readonly string[] DefaultBossLines =
    {
        "Good job!\nGo rest.",
        "Well done\nmy star!",
        "Nice work\nSee ya!",
        "Great job\ntoday!",
        "Thank you\nso much!",
        "Sales are\ngreat!",
        "You did\ngreat!",
        "Go home\nand rest!",
        "Top work!\nWell done",
        "Proud of\nyou!",
        "Big win\ntoday!",
        "Numbers\nlook good",
        "Bonus\nincoming!",
        "No bugs.\nNice job!",
        "Client\nloved it!",
        "You rock!\nGo relax.",
        "No OT\ntonight!",
        "See you\ntomorrow!",
        "Keep it\nup!",
        "Rest well\nchamp!",
    };

    private static PassLevelSet[] CreateDefaultLevels()
    {
        var list = new PassLevelSet[LevelCount];
        for (int i = 0; i < LevelCount; i++) list[i] = new PassLevelSet { levelName = ((LevelType)i).ToString() };
        return list;
    }

    private static AnimationCurve CreatePopCurve()
    {
        // 0 -> overshoot 1.12 -> settle at 1 (a small bounce)
        return new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f),
            new Keyframe(0.6f, 1.12f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));
    }

    // ============================================================
    //  Runtime state
    // ============================================================
    public LevelType CurrentLevel { get; private set; }
    public string CurrentPhase { get; private set; } = "Idle";
    public bool IsRunning => sequenceRoutine != null;
    public GameObject ActiveBoss => activeBoss;

    private Vector3[] playerHomePositions = new Vector3[LevelCount];
    private Animator playerAnimator;
    private Animator bossAnimator;
    private GameObject activeBoss;

    private GameObject bubbleInstance;
    private RectTransform bubbleRect;
    private Canvas bubbleCanvas;
    private Vector3 bubbleBaseScale = Vector3.one;

    private Coroutine sequenceRoutine;
    private Coroutine bubbleRoutine;
    private GameObject transitionInstance;
    private int lastLineIndex = -1;
    private bool warnedBossFallback;

    // ============================================================
    //  Lifecycle
    // ============================================================
    private void Reset()
    {
        levels = CreateDefaultLevels();
        popCurve = CreatePopCurve();
        bossLines = (string[])DefaultBossLines.Clone();
    }

    private void OnValidate()
    {
        EnsureLevelArray();
    }

    private void Awake()
    {
        EnsureLevelArray();
        if (worldCamera == null) worldCamera = Camera.main;

        for (int i = 0; i < LevelCount; i++)
        {
            GameObject p = levels[i].player;
            playerHomePositions[i] = p != null ? p.transform.position : playerStopPoint;
        }
    }

    private void Start()
    {
        LevelType level = hasPendingLevel ? pendingLevel : defaultLevel;
        hasPendingLevel = false;

        if (playOnStart) Play(level);
        else ApplyLevelVisibility(level);
    }

    // ============================================================
    //  Public API
    // ============================================================

    /// <summary>Shows the given level and (re)starts the whole sequence.</summary>
    public void Play(LevelType level)
    {
        StopAndReset();
        ApplyLevelVisibility(level);
        sequenceRoutine = StartCoroutine(SequenceRoutine());
    }

    /// <summary>Stops the sequence, removes the bubble and puts the Player back to its original position.</summary>
    public void StopAndReset()
    {
        if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
        sequenceRoutine = null;
        DestroyBubble();
        DestroyTransition();

        SetAnimBool(playerAnimator, walkParam, false);
        SetAnimBool(bossAnimator, winSayParam, false);

        int idx = (int)CurrentLevel;
        if (levels != null && idx < levels.Length && levels[idx].player != null)
            levels[idx].player.transform.position = playerHomePositions[idx];

        CurrentPhase = "Idle";
    }

    /// <summary>Pops a bubble with a random line right now (test).</summary>
    public void ShowBubbleNow()
    {
        ShowBubble(PickLine());
    }

    /// <summary>Spawns IrisNonStopTransition, waits Transition Lead Time, then goes back to the Menu.</summary>
    public void GoToMenuWithTransition()
    {
        if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
        sequenceRoutine = StartCoroutine(TransitionThenMenuRoutine(0f));
    }

    /// <summary>Spawns the IrisNonStopTransition Prefab now (test). Returns the instance, or null.</summary>
    public GameObject SpawnIrisNonStopTransition()
    {
        DestroyTransition();
        if (irisNonStopTransitionPrefab == null)
        {
            Debug.LogWarning("[PassSceneManager] IrisNonStopTransition Prefab is not assigned; changing scene without a transition.", this);
            return null;
        }

        Transform parent = transitionParent;
        bool isUI = irisNonStopTransitionPrefab.GetComponent<RectTransform>() != null;
        bool hasOwnCanvas = irisNonStopTransitionPrefab.GetComponent<Canvas>() != null;
        if (parent == null && isUI && !hasOwnCanvas)
        {
            Canvas c = FindAnyCanvas();
            if (c != null) parent = c.rootCanvas.transform;
        }

        transitionInstance = Instantiate(irisNonStopTransitionPrefab, parent, false);
        transitionInstance.name = irisNonStopTransitionPrefab.name;
        if (parent != null) transitionInstance.transform.SetAsLastSibling();   // draw on top of the bubble / UI
        transitionInstance.SetActive(true);
        return transitionInstance;
    }

    /// <summary>Goes back to the Menu right away (no transition): unlocks the next level and returns.</summary>
    public void GoToMenu()
    {
        CurrentPhase = "Loading Menu";
        onBeforeLoadMenu.Invoke();
        CompleteAndBackToMenu();
    }

    /// <summary>
    /// Clear: unlock the next level and go back to the Menu.
    /// Uses whichever slot (0-5) currently has its Player / Background active.
    /// Can be picked in a Button's OnClick (or any UnityEvent).
    /// </summary>
    public void CompleteAndBackToMenu()
    {
        int levelNumber = GetActiveLevelIndex() + levelNumberOffset;
        Debug.Log($"[PassSceneManager] MenuManager.CompleteLevelAndReturn({levelNumber})", this);
        MenuManager.CompleteLevelAndReturn(levelNumber);
    }

    /// <summary>
    /// Index (0 Typist ... 5 Scam) of the slot whose Player is active in the scene
    /// (Background is checked if no Player is active). Falls back to Current Level.
    /// </summary>
    public int GetActiveLevelIndex()
    {
        EnsureLevelArray();
        for (int i = 0; i < LevelCount; i++)
            if (levels[i].player != null && levels[i].player.activeInHierarchy) return i;
        for (int i = 0; i < LevelCount; i++)
            if (levels[i].background != null && levels[i].background.activeInHierarchy) return i;
        return (int)CurrentLevel;
    }

    // ============================================================
    //  Sequence
    // ============================================================
    private IEnumerator SequenceRoutine()
    {
        int idx = (int)CurrentLevel;
        GameObject player = levels[idx].player;

        // 1. Walk in
        if (player != null)
        {
            CurrentPhase = "Walking";
            Vector3 home = playerHomePositions[idx];
            Vector3 start = GetWalkStart(home);
            Vector3 end = GetWalkEnd(home);
            player.transform.position = start;

            // One frame so a freshly activated Animator is ready to take parameters
            yield return null;
            SetAnimBool(playerAnimator, walkParam, true);

            float t = 0f;
            while (t < walkDuration)
            {
                t += DeltaTime;
                float e = walkCurve.Evaluate(Mathf.Clamp01(t / walkDuration));
                player.transform.position = Vector3.LerpUnclamped(start, end, e);
                yield return null;
            }
            player.transform.position = end;
            SetAnimBool(playerAnimator, walkParam, false);
        }
        else
        {
            Debug.LogWarning($"[PassSceneManager] {CurrentLevel} has no Player; skipping the walk-in.", this);
        }
        onPlayerArrived.Invoke();

        // 2. Boss speaks + bubble
        CurrentPhase = "Waiting";
        if (delayBeforeBossSay > 0f) yield return Wait(delayBeforeBossSay);

        CurrentPhase = "Boss Saying";
        SetAnimBool(bossAnimator, winSayParam, true);
        ShowBubble(PickLine());
        onBossSay.Invoke();

        if (winSayDuration > 0f) yield return Wait(winSayDuration);

        SetAnimBool(bossAnimator, winSayParam, false);
        onBossSayEnd.Invoke();

        // 3. Back to Menu
        if (autoReturnToMenu)
        {
            CurrentPhase = "Returning";
            yield return TransitionThenMenuRoutine(returnToMenuDelay);
            yield break;
        }

        CurrentPhase = "Done";
        sequenceRoutine = null;
    }

    /// <summary>
    /// Total wait = max(delay, Transition Lead Time). IrisNonStopTransition is spawned Transition Lead Time seconds before the end,
    /// then the Menu scene loads.
    /// </summary>
    private IEnumerator TransitionThenMenuRoutine(float delay)
    {
        float lead = irisNonStopTransitionPrefab != null ? transitionLeadTime : 0f;
        float beforeTransition = Mathf.Max(0f, delay - lead);
        if (beforeTransition > 0f) yield return Wait(beforeTransition);

        if (irisNonStopTransitionPrefab != null)
        {
            CurrentPhase = "Transition";
            SpawnIrisNonStopTransition();
            if (lead > 0f) yield return Wait(lead);
        }

        sequenceRoutine = null;
        GoToMenu();
    }

    private void DestroyTransition()
    {
        if (transitionInstance != null) Destroy(transitionInstance);
        transitionInstance = null;
    }

    private static Canvas FindAnyCanvas()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<Canvas>();
#else
        return FindObjectOfType<Canvas>();
#endif
    }

    private Vector3 GetWalkStart(Vector3 home)
    {
        Vector3 start = startFromScreenLeftEdge ? new Vector3(GetScreenLeftX(home.z) - offscreenMargin, playerStopPoint.y, home.z)
                                                : walkStartPoint;
        if (keepPlayerY) start.y = home.y;
        start.z = home.z;
        return start;
    }

    private Vector3 GetWalkEnd(Vector3 home)
    {
        return new Vector3(playerStopPoint.x, keepPlayerY ? home.y : playerStopPoint.y, home.z);
    }

    private float GetScreenLeftX(float worldZ)
    {
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return walkStartPoint.x;
        float depth = Mathf.Abs(worldZ - cam.transform.position.z);
        if (depth < 0.01f) depth = 10f;
        return cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, depth)).x;
    }

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private object Wait(float seconds)
    {
        return useUnscaledTime ? (object)new WaitForSecondsRealtime(seconds) : new WaitForSeconds(seconds);
    }

    // ============================================================
    //  Level visibility
    // ============================================================
    private void ApplyLevelVisibility(LevelType level)
    {
        EnsureLevelArray();
        CurrentLevel = level;
        int idx = (int)level;

        for (int i = 0; i < LevelCount; i++)
        {
            PassLevelSet set = levels[i];
            SetActiveSafe(set.player, i == idx);
            SetActiveSafe(set.background, i == idx);
            SetActiveSafe(set.boss, false);
        }

        if (levels[idx].background == null)
            Debug.LogWarning($"[PassSceneManager] {level} has no Background assigned.", this);

        activeBoss = ResolveBoss(idx);
        SetActiveSafe(activeBoss, true);

        playerAnimator = levels[idx].player != null ? levels[idx].player.GetComponentInChildren<Animator>(true) : null;
        bossAnimator = activeBoss != null ? activeBoss.GetComponentInChildren<Animator>(true) : null;

        if (levels[idx].player != null && playerAnimator == null)
            Debug.LogWarning($"[PassSceneManager] Player of {level} has no Animator on it or its children.", levels[idx].player);
        if (activeBoss != null && bossAnimator == null)
            Debug.LogWarning("[PassSceneManager] The Boss has no Animator on it or its children.", activeBoss);
    }

    /// <summary>This level's Boss, or the first assigned Boss if this level has none yet.</summary>
    private GameObject ResolveBoss(int idx)
    {
        if (levels[idx].boss != null) return levels[idx].boss;

        for (int i = 0; i < LevelCount; i++)
        {
            if (levels[i].boss == null) continue;
            if (!warnedBossFallback)
            {
                Debug.Log($"[PassSceneManager] {(LevelType)idx} has no Boss yet; using {(LevelType)i}'s Boss instead.", this);
                warnedBossFallback = true;
            }
            return levels[i].boss;
        }

        Debug.LogWarning("[PassSceneManager] No Boss assigned at all; the bubble is still shown, without Boss animation.", this);
        return null;
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private void EnsureLevelArray()
    {
        if (levels == null) levels = new PassLevelSet[0];
        if (levels.Length != LevelCount) Array.Resize(ref levels, LevelCount);
        for (int i = 0; i < LevelCount; i++)
        {
            if (levels[i] == null) levels[i] = new PassLevelSet();
            levels[i].levelName = ((LevelType)i).ToString();
        }
    }

    // ============================================================
    //  Animator
    // ============================================================
    private void SetAnimBool(Animator anim, string param, bool value)
    {
        if (anim == null || string.IsNullOrEmpty(param)) return;
        if (!anim.isActiveAndEnabled) return;   // Animator ignores parameters while disabled

        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.name == param && p.type == AnimatorControllerParameterType.Bool)
            {
                anim.SetBool(param, value);
                return;
            }
        }
        Debug.LogWarning($"[PassSceneManager] Animator has no Bool parameter named \"{param}\".", anim);
    }

    // ============================================================
    //  Bubble
    // ============================================================
    private string PickLine()
    {
        if (bossLines == null || bossLines.Length == 0) return "";
        if (bossLines.Length == 1) return bossLines[0];

        int i;
        do { i = UnityEngine.Random.Range(0, bossLines.Length); } while (i == lastLineIndex);
        lastLineIndex = i;
        return bossLines[i];
    }

    private void ShowBubble(string line)
    {
        DestroyBubble();
        if (smallBubbleChatPrefab == null)
        {
            Debug.LogWarning("[PassSceneManager] Small Bubble Chat Prefab is not assigned.", this);
            return;
        }

        Transform parent = bubbleParent;
        if (parent == null && smallBubbleChatPrefab.GetComponent<RectTransform>() != null)
        {
            Canvas c = FindAnyCanvas();
            if (c != null) parent = c.rootCanvas.transform;
        }

        bubbleInstance = Instantiate(smallBubbleChatPrefab, parent, false);
        bubbleInstance.name = smallBubbleChatPrefab.name;
        bubbleRect = bubbleInstance.transform as RectTransform;
        bubbleCanvas = bubbleInstance.GetComponentInParent<Canvas>();
        bubbleBaseScale = smallBubbleChatPrefab.transform.localScale;

        SetBubbleText(line);
        ApplyBubblePose(0f);
        bubbleInstance.SetActive(true);

        bubbleRoutine = StartCoroutine(PopRoutine());
    }

    private void DestroyBubble()
    {
        if (bubbleRoutine != null) StopCoroutine(bubbleRoutine);
        bubbleRoutine = null;
        if (bubbleInstance != null) Destroy(bubbleInstance);
        bubbleInstance = null;
        bubbleRect = null;
        bubbleCanvas = null;
    }

    private IEnumerator PopRoutine()
    {
        float t = 0f;
        while (t < popDuration)
        {
            if (bubbleInstance == null) yield break;
            t += DeltaTime;
            ApplyBubblePose(popCurve.Evaluate(Mathf.Clamp01(t / popDuration)));
            yield return null;
        }
        ApplyBubblePose(1f);   // stays on screen until the scene changes
        bubbleRoutine = null;
    }

    /// <summary>e = 0: at the pop-from corner, start scale. e = 1: at Bubble Position, Pop Scale.</summary>
    private void ApplyBubblePose(float e)
    {
        if (bubbleInstance == null) return;
        Vector3 from = bubblePosition + popFromOffset;
        PlaceBubble(Vector3.LerpUnclamped(from, bubblePosition, e));
        float s = Mathf.Max(0f, Mathf.LerpUnclamped(popStartScale, popScale, e));
        bubbleInstance.transform.localScale = bubbleBaseScale * s;
    }

    private void PlaceBubble(Vector3 worldPos)
    {
        bool screenSpaceUI = bubbleRect != null && bubbleCanvas != null && bubbleCanvas.renderMode != RenderMode.WorldSpace;
        if (!screenSpaceUI)
        {
            bubbleInstance.transform.position = worldPos;
            return;
        }

        // UI bubble on a Screen Space Canvas: convert the world guide position to the Canvas
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        RectTransform parentRect = bubbleRect.parent as RectTransform;
        if (cam == null || parentRect == null) return;

        Vector2 screen = cam.WorldToScreenPoint(worldPos);
        Camera uiCam = bubbleCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : bubbleCanvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, uiCam, out Vector2 local))
            bubbleRect.localPosition = new Vector3(local.x, local.y, 0f);
    }

    private void SetBubbleText(string line)
    {
        TMP_Text tmp = bubbleInstance.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = line; return; }

        Text uiText = bubbleInstance.GetComponentInChildren<Text>(true);
        if (uiText != null) { uiText.text = line; return; }

        TextMesh mesh = bubbleInstance.GetComponentInChildren<TextMesh>(true);
        if (mesh != null) { mesh.text = line; return; }

        Debug.LogWarning("[PassSceneManager] smallBubbleChat has no Text / TextMeshPro / TextMesh child.", bubbleInstance);
    }

    // ============================================================
    //  Guides (Scene view)
    // ============================================================
    private void OnDrawGizmos()
    {
        if (!showGuides) return;

        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        float halfH = 5f;
        float camY = playerStopPoint.y;
        if (cam != null && cam.orthographic)
        {
            halfH = cam.orthographicSize;
            camY = cam.transform.position.y;
        }

        // Stop point: vertical line through the whole view + marker
        Gizmos.color = Color.green;
        Gizmos.DrawLine(new Vector3(playerStopPoint.x, camY - halfH, playerStopPoint.z),
                        new Vector3(playerStopPoint.x, camY + halfH, playerStopPoint.z));
        Gizmos.DrawWireSphere(playerStopPoint, 0.25f);

        // Walk path: start -> stop
        Vector3 start = startFromScreenLeftEdge
            ? new Vector3(GetScreenLeftX(playerStopPoint.z) - offscreenMargin, playerStopPoint.y, playerStopPoint.z)
            : walkStartPoint;
        if (keepPlayerY) start.y = playerStopPoint.y;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(start, playerStopPoint);
        Gizmos.DrawWireSphere(start, 0.18f);
        DrawArrowHead(start, playerStopPoint);

        // Bubble: final box (bottom-right pop corner + path)
        Vector3 size = new Vector3(bubbleGuideSize.x * popScale, bubbleGuideSize.y * popScale, 0.01f);
        Vector3 from = bubblePosition + popFromOffset;
        Gizmos.color = new Color(1f, 0.85f, 0f, 1f);
        Gizmos.DrawWireCube(bubblePosition, size);
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.12f);
        Gizmos.DrawCube(bubblePosition, size);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawLine(from, bubblePosition);
        Gizmos.DrawWireSphere(from, 0.15f);
        DrawArrowHead(from, bubblePosition);

#if UNITY_EDITOR
        Handles.Label(playerStopPoint + Vector3.up * 0.5f, "Player Stop Point");
        Handles.Label(start + Vector3.up * 0.4f, startFromScreenLeftEdge ? "Walk Start (screen left edge)" : "Walk Start");
        Handles.Label(bubblePosition + new Vector3(-size.x * 0.5f, size.y * 0.5f + 0.4f, 0f), "Bubble (final size)");
        Handles.Label(from + Vector3.down * 0.3f, "Pop from");
#endif
    }

    private static void DrawArrowHead(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        Vector3 side = Vector3.Cross(dir, Vector3.forward).normalized;
        Gizmos.DrawLine(to, to - dir * 0.35f + side * 0.2f);
        Gizmos.DrawLine(to, to - dir * 0.35f - side * 0.2f);
    }

    // ============================================================
    //  Editor helpers
    // ============================================================
#if UNITY_EDITOR
    /// <summary>Edit mode: shows one level's objects in the Scene view (with Undo).</summary>
    public void EditorPreviewLevel(int idx)
    {
        EnsureLevelArray();
        GameObject boss = ResolveBossNoLog(idx);
        for (int i = 0; i < LevelCount; i++)
        {
            EditorSetActive(levels[i].player, i == idx);
            EditorSetActive(levels[i].background, i == idx);
            EditorSetActive(levels[i].boss, levels[i].boss != null && levels[i].boss == boss);
        }
    }

    public void EditorShowAll()
    {
        EnsureLevelArray();
        foreach (PassLevelSet set in levels)
        {
            EditorSetActive(set.player, true);
            EditorSetActive(set.background, true);
            EditorSetActive(set.boss, true);
        }
    }

    /// <summary>Fills empty slots from objects named Player_Typist / BG_Typist / Boss_Typist ... in this scene.</summary>
    public int EditorAutoAssignByName()
    {
        EnsureLevelArray();
        var byName = new Dictionary<string, GameObject>();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName.Add(t.name, t.gameObject);

        Undo.RecordObject(this, "Auto Assign Pass Objects");
        int found = 0;
        for (int i = 0; i < LevelCount; i++)
        {
            string n = ((LevelType)i).ToString();
            if (levels[i].player == null && byName.TryGetValue(PlayerPrefix + n, out GameObject p)) { levels[i].player = p; found++; }
            if (levels[i].background == null && byName.TryGetValue(BackgroundPrefix + n, out GameObject b)) { levels[i].background = b; found++; }
            if (levels[i].boss == null && byName.TryGetValue(BossPrefix + n, out GameObject s)) { levels[i].boss = s; found++; }
        }
        EditorUtility.SetDirty(this);
        return found;
    }

    private GameObject ResolveBossNoLog(int idx)
    {
        if (levels[idx].boss != null) return levels[idx].boss;
        foreach (PassLevelSet set in levels) if (set.boss != null) return set.boss;
        return null;
    }

    private static void EditorSetActive(GameObject go, bool active)
    {
        if (go == null || go.activeSelf == active) return;
        Undo.RecordObject(go, "Preview Pass Level");
        go.SetActive(active);
    }
#endif
}

#if UNITY_EDITOR
/// <summary>Test buttons in the Inspector + draggable guide handles in the Scene view.</summary>
[CustomEditor(typeof(PassSceneManager))]
public class PassSceneManagerEditor : Editor
{
    private static PassSceneManager.LevelType testLevel = PassSceneManager.LevelType.Typist;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var mgr = (PassSceneManager)target;
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test Controls", EditorStyles.boldLabel);

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField($"Level: {mgr.CurrentLevel}   Active Slot: {mgr.GetActiveLevelIndex()}   Phase: {mgr.CurrentPhase}   Boss: {(mgr.ActiveBoss != null ? mgr.ActiveBoss.name : "none")}");

            testLevel = (PassSceneManager.LevelType)EditorGUILayout.EnumPopup("Test Level", testLevel);
            if (GUILayout.Button("Play Sequence (Test Level)", GUILayout.Height(26))) mgr.Play(testLevel);

            DrawLevelButtons(i => mgr.Play((PassSceneManager.LevelType)i));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Show Bubble Only")) mgr.ShowBubbleNow();
            if (GUILayout.Button("Stop & Reset")) mgr.StopAndReset();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn IrisNonStopTransition Only")) mgr.SpawnIrisNonStopTransition();
            if (GUILayout.Button("Go To Menu (with IrisNonStopTransition)")) mgr.GoToMenuWithTransition();
            if (GUILayout.Button("Complete And Back To Menu Now")) mgr.GoToMenu();

            EditorGUILayout.EndHorizontal();

            Repaint();
        }
        else
        {
            EditorGUILayout.HelpBox("Play Mode: replay the sequence with any level.\nEdit Mode: preview which objects each level shows, and drag the guide handles in the Scene view.", MessageType.Info);

            EditorGUILayout.LabelField("Preview level in Scene view");
            DrawLevelButtons(i => mgr.EditorPreviewLevel(i));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Show All")) mgr.EditorShowAll();
            if (GUILayout.Button("Auto Assign by Name"))
            {
                int n = mgr.EditorAutoAssignByName();
                Debug.Log($"[PassSceneManager] Auto Assign filled {n} empty slot(s). Names: Player_Typist, BG_Typist, Boss_Typist ...");
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private static void DrawLevelButtons(Action<int> onClick)
    {
        for (int row = 0; row < 2; row++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int col = 0; col < 3; col++)
            {
                int i = row * 3 + col;
                if (GUILayout.Button(((PassSceneManager.LevelType)i).ToString())) onClick(i);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void OnSceneGUI()
    {
        serializedObject.Update();

        DrawHandle("playerStopPoint", "Move Player Stop Point");
        DrawHandle("bubblePosition", "Move Bubble Position");

        // Pop-from corner (stored as an offset from Bubble Position)
        SerializedProperty bubble = serializedObject.FindProperty("bubblePosition");
        SerializedProperty offset = serializedObject.FindProperty("popFromOffset");
        EditorGUI.BeginChangeCheck();
        Vector3 from = Handles.PositionHandle(bubble.vector3Value + offset.vector3Value, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) offset.vector3Value = from - bubble.vector3Value;

        if (!serializedObject.FindProperty("startFromScreenLeftEdge").boolValue)
            DrawHandle("walkStartPoint", "Move Walk Start Point");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawHandle(string propName, string undoName)
    {
        SerializedProperty prop = serializedObject.FindProperty(propName);
        EditorGUI.BeginChangeCheck();
        Vector3 p = Handles.PositionHandle(prop.vector3Value, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) prop.vector3Value = p;
    }
}
#endif
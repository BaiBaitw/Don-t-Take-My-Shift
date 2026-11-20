using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Superman mode: all six mini games live in one scene.
///
/// Flow:
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
    [SerializeField] private Button startGameButton;
    [Tooltip("Shown after time is up. Loads Next Scene Name (empty = reload this scene)")]
    [SerializeField] private Button nextGameButton;
    [SerializeField] private string nextSceneName = "";
    [Tooltip("GameObject with the CountdownBar script (receives StartTimer)")]
    [SerializeField] private GameObject timeManager;
    [Tooltip("GameObject with PlayerScript (receives GameOver)")]
    [SerializeField] private GameObject playerObject;
    [Tooltip("Seconds between finishing a question and the next question / slide. Keep it above FactoryManager's Effect Duration (0.25)")]
    [SerializeField] private float nextQuestionDelay = 0.35f;
    [Tooltip("Every N clears in total (all levels added together), ALL six levels get IncreaseDifficulty() once (0 = never)")]
    [SerializeField] private int increaseDifficultyEvery = 2;

    [Header("Score")]
    [Tooltip("ScoreUI (TextMeshPro - Text (UI)). Can be left empty for now")]
    [SerializeField] private TMP_Text scoreUI;
    [Tooltip("Text format, {0} = score. e.g. \"{0}\", \"Score: {0:N0}\"")]
    [SerializeField] private string scoreFormat = "{0}";
    [Tooltip("Score added per cleared question")]
    [SerializeField] private int scorePerClear = 200;

    [Header("Fever")]
    [Tooltip("Clears in a row with no mistake needed to start Fever")]
    [SerializeField] private int feverStreakRequired = 3;
    [Tooltip("Score multiplier during Fever")]
    [SerializeField] private float feverMultiplier = 2f;
    [Tooltip("Optional: shown while Fever is on")]
    [SerializeField] private GameObject feverEffect;
    [Tooltip("Seconds added to the countdown bar for each clear while Fever is on (0 = none)")]
    [SerializeField] private float feverTimeBonus = 2f;

    [Header("Events")]
    [SerializeField] private IntEvent onScoreChanged = new IntEvent();
    [SerializeField] private UnityEvent onFeverStart = new UnityEvent();
    [SerializeField] private UnityEvent onFeverEnd = new UnityEvent();
    [SerializeField] private UnityEvent onMistake = new UnityEvent();
    [SerializeField] private UnityEvent onTimeUp = new UnityEvent();

    // ============================================================
    //  Runtime state
    // ============================================================
    private enum Phase { Idle, Playing, Transition, Over }
    private Phase phase = Phase.Idle;

    private readonly List<int> validLevels = new List<int>();
    private int currentLevel = -1;
    private CountdownBar countdown;
    private int streak;
    private bool mistakeThisClear;
    private Coroutine flowRoutine;

    public int Score { get; private set; }
    public int Streak => streak;
    public bool IsFever { get; private set; }
    public int TotalClears { get; private set; }
    /// <summary>How many times the global difficulty has gone up</summary>
    public int DifficultyLevel { get; private set; }
    public int CurrentLevelIndex => currentLevel;
    public bool IsSliding { get; private set; }

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

        if (startGameButton != null) startGameButton.onClick.AddListener(StartSuperman);
        if (nextGameButton != null)
        {
            nextGameButton.onClick.AddListener(OnNextGameClicked);
            nextGameButton.gameObject.SetActive(false);
        }

        if (feverEffect != null) feverEffect.SetActive(false);
        UpdateScoreUI();
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
        streak = 0;
        if (IsFever) SetFever(false);
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
        if (nextGameButton != null) nextGameButton.gameObject.SetActive(true);
        onTimeUp.Invoke();
    }

    public void OnNextGameClicked()
    {
        string scene = string.IsNullOrEmpty(nextSceneName) ? SceneManager.GetActiveScene().name : nextSceneName;
        SceneManager.LoadScene(scene);
    }

    // ============================================================
    //  Flow
    // ============================================================
    private IEnumerator FirstLevelRoutine()
    {
        int first = PickRandomLevel();
        yield return SwitchToLevel(first);

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
        if (on) onFeverStart.Invoke();
        else onFeverEnd.Invoke();
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
    /// <summary>Names of GameManager fields that Superman mode does not use (hidden in the Inspector).</summary>
    public static readonly string[] UnusedBaseFields =
    {
        "levelText", "startButton", "nextButton", "totalCounts", "backgroundObject", "backgroundScenes",
        "typeGameManager", "timerManager", "player", "npcObjects", "minNpcCount", "maxNpcCount", "nextStageDelay"
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

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test buttons.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(
            $"Level: {gm.CurrentLevelIndex}   Diff: {gm.DifficultyLevel}   Score: {gm.Score}   Streak: {gm.Streak}   Fever: {gm.IsFever}   Sliding: {gm.IsSliding}");

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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Leaderboard panel.
///
/// Flow:
///   1. Initializes itself when the scene loads (even if this GameObject starts hidden), then hides itself.
///   2. Watches IsGameOver on the SupermanGameManager of "supermanManager". When it turns true, this GameObject is shown.
///   3. Fills LastScore / LastClears / LastMaxCombo / LastMistakes into score / totalClears / maxCombo / misses.
///      Shows NewBest if the player beat their own local record.
///   4. Loads the top 10 of leaderboard "DTMShift":
///        - New personal best AND inside the top 10 -> show nameInputField; after the player presses Enter,
///          the score is uploaded and then the top 10 is shown
///        - Otherwise -> the top 10 is shown right away
///   5. One rankBannerPrefab is spawned per entry into rankContainer (its Vertical Layout Group sorts them).
///
/// Required packages: Leaderboards (com.unity.services.leaderboards) and Authentication (com.unity.services.authentication).
/// The project must be linked to a Unity Cloud project in Edit > Project Settings > Services.
/// Anonymous sign-in keeps the Player ID on this computer, so one computer = one entry on the leaderboard.
/// </summary>
public class LeaderBoardManager : MonoBehaviour
{
    // ============================================================
    //  Inspector
    // ============================================================
    [Header("Leaderboard")]
    [SerializeField] private string leaderboardId = "DTMShift";
    [SerializeField] private int topCount = 10;
    [SerializeField] private int maxNameLength = 5;

    [Header("Debug")]
    [Tooltip("On: every round signs in as a brand-new anonymous player, and the local high score is ignored " +
             "(any score above 0 counts as a new best).\nOnly works in the Editor and in Development Builds.")]
    [SerializeField] private bool debugMode = false;
    [Tooltip("Optional: leaderboard used while Debug Mode is on, so test scores don't go to the real one.\nEmpty = same as Leaderboard Id")]
    [SerializeField] private string debugLeaderboardId = "";

    [Header("Objects (empty = found by name, hidden objects included)")]
    [Tooltip("The SupermanGameManager on the \"supermanManager\" GameObject")]
    [SerializeField] private SupermanGameManager supermanManager;
    [SerializeField] private GameObject personalRecord;
    [SerializeField] private GameObject rank;
    [SerializeField] private TMP_Text score;
    [SerializeField] private TMP_Text totalClears;
    [SerializeField] private TMP_Text maxCombo;
    [SerializeField] private TMP_Text misses;
    [SerializeField] private Transform rankContainer;
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private GameObject newBest;
    [Tooltip("Hidden at game over")]
    [SerializeField] private GameObject scoreUI;

    [Header("Old TV Turn-On Effect (personalRecord / rank)")]
    [SerializeField] private bool playTvEffect = true;
    [Tooltip("Use unscaled time so the effect still plays when Time.timeScale = 0")]
    [SerializeField] private bool tvUseUnscaledTime = true;
    [Tooltip("Scale at the start, relative to the object's own scale (0.02 = 2%)")]
    [SerializeField] private Vector2 tvStartScale = new Vector2(0.02f, 0.02f);
    [Tooltip("Step 1: seconds to open Scale X")]
    [SerializeField] private float tvScaleXDuration = 0.25f;
    [Tooltip("Step 1 speed curve (0 = start scale, 1 = full). Values above 1 overshoot")]
    [SerializeField] private AnimationCurve tvScaleXCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Seconds to wait between step 1 and step 2")]
    [SerializeField] private float tvPauseBetween = 0.05f;
    [Tooltip("Step 2: seconds to open Scale Y")]
    [SerializeField] private float tvScaleYDuration = 0.3f;
    [Tooltip("Step 2 speed curve (0 = start scale, 1 = full). Values above 1 overshoot")]
    [SerializeField] private AnimationCurve tvScaleYCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Flash color. Images get a same-shape overlay of this color, texts are tinted toward it (alpha = how white)")]
    [SerializeField] private Color tvFlashColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Optional: material for the white overlay. Empty = built-in \"GUI/Text Shader\" (paints the sprite's shape in one color)")]
    [SerializeField] private Material tvFlashMaterial;
    [Tooltip("Seconds for the white to fade away. Starts together with step 2")]
    [SerializeField] private float tvWhiteFadeDuration = 0.45f;
    [Tooltip("White fade curve (0 = full white, 1 = normal colors)")]
    [SerializeField] private AnimationCurve tvWhiteFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Seconds Rank waits after PersonalRecord starts its effect")]
    [SerializeField] private float tvRankDelay = 0.15f;
    [Tooltip("Random extra delay before each panel is switched on, in seconds (min, max). Re-rolled every time, so the two never open in sync.\nAlso used when Play Tv Effect is off")]
    [SerializeField] private Vector2 tvRandomDelay = new Vector2(0f, 0.15f);
    [Tooltip("Random speed difference for each panel (0.2 = durations vary by up to +/-20%)")]
    [Range(0f, 0.9f)]
    [SerializeField] private float tvRandomSpeed = 0.15f;

    [Header("Prefab (must be assigned by hand)")]
    [Tooltip("Needs three TextMeshPro - Text (UI) children: RankNumber / Name / Score")]
    [SerializeField] private GameObject rankBannerPrefab;

    // ============================================================
    //  Runtime
    // ============================================================
    private bool initialized;
    private bool shown;
    private bool submitting;
    private long previousHighScore;   // local high score before this round
    private long pendingScore;        // score to upload after the player enters a name
    private Coroutine watchRoutine;
    private readonly List<GameObject> spawnedBanners = new List<GameObject>();
    private Vector3 personalRecordBaseScale = Vector3.one;
    private Vector3 rankBaseScale = Vector3.one;

    private static Task servicesTask;
    private static bool forceNewPlayer;

    /// <summary>Debug Mode is on and allowed in this build</summary>
    private bool IsDebug => debugMode && (Application.isEditor || Debug.isDebugBuild);

    private string ActiveLeaderboardId =>
        IsDebug && !string.IsNullOrEmpty(debugLeaderboardId) ? debugLeaderboardId : leaderboardId;

    // ============================================================
    //  Init (Awake does not run on a hidden GameObject, so it is also found and initialized on scene load)
    // ============================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        // Statics survive between Play sessions when Domain Reload is off, so clear them here
        servicesTask = null;
        forceNewPlayer = false;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LeaderBoardManager[] all = FindObjectsByType<LeaderBoardManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (LeaderBoardManager lb in all)
            if (lb != null && lb.gameObject.scene == scene) lb.Init();
    }

    private void Awake()
    {
        Init();   // used when the GameObject is active in the Editor
    }

    private void Init()
    {
        if (initialized) return;
        initialized = true;

        FindReferences();

        if (personalRecord != null) personalRecordBaseScale = personalRecord.transform.localScale;
        if (rank != null) rankBaseScale = rank.transform.localScale;

        if (newBest != null) newBest.SetActive(false);
        if (nameInputField != null)
        {
            SetupNameInput();
            nameInputField.gameObject.SetActive(false);
        }

        if (supermanManager == null)
            Debug.LogError("[LeaderBoardManager] SupermanGameManager on \"supermanManager\" not found.", this);
        else
            // This GameObject is hidden, so its own coroutines can't run. Run the watcher on supermanManager instead.
            watchRoutine = supermanManager.StartCoroutine(WatchGameOver());

        if (rankBannerPrefab == null)
            Debug.LogWarning("[LeaderBoardManager] rankBannerPrefab is not assigned.", this);

        // Hidden by default
        if (!shown && gameObject.activeSelf) gameObject.SetActive(false);

        // Debug Mode: throw away the current player and sign in as a new one this round
        if (IsDebug)
        {
            servicesTask = null;
            forceNewPlayer = true;
            Debug.LogWarning($"[LeaderBoardManager] Debug Mode ON: new anonymous player this round, leaderboard = {ActiveLeaderboardId}", this);
        }
        else if (debugMode)
        {
            Debug.LogWarning("[LeaderBoardManager] Debug Mode is ignored in release builds.", this);
        }

        // Sign-in starts in WatchGameOver() after the first frame, not here.
        // Leaderboards 2.3.x can fail to register when UnityServices.InitializeAsync() is called during Awake.
    }

    private void OnDestroy()
    {
        if (watchRoutine != null && supermanManager != null) supermanManager.StopCoroutine(watchRoutine);
        SetImeOff(false);
    }

    private IEnumerator WatchGameOver()
    {
        // Wait one frame so SupermanGameManager has loaded the local high score
        yield return null;
        previousHighScore = IsDebug ? 0 : SupermanGameManager.HighScore;   // Debug: act like a player with no record

        // Sign in early (after Awake / Start) so there is no wait at game over
        _ = EnsureSignedIn();

        while (supermanManager != null && !supermanManager.IsGameOver)
            yield return null;

        if (supermanManager == null || this == null) yield break;

        // One more frame so GameOver() has saved the Last* values
        yield return null;
        watchRoutine = null;
        if (this != null) Show();
    }

    // ============================================================
    //  Show
    // ============================================================
    public void Show()
    {
        if (shown) return;
        shown = true;
        gameObject.SetActive(true);

        long last = SupermanGameManager.LastScore;

        SetText(score, SupermanGameManager.LastScore.ToString());
        SetText(totalClears, SupermanGameManager.LastClears.ToString());
        SetText(maxCombo, SupermanGameManager.LastMaxCombo.ToString());
        SetText(misses, SupermanGameManager.LastMistakes.ToString());

        bool isNewBest = last > 0 && last > previousHighScore;
        if (newBest != null) newBest.SetActive(isNewBest);
        if (scoreUI != null) scoreUI.SetActive(false);
        // personalRecord / rank are switched on at slightly different random times,
        // so their Animators don't play in sync
        PlayTvEffect();

        ClearRankBanners();
        RunFlow(last, isNewBest);
    }

    // ============================================================
    //  Test controls (used by the Inspector buttons)
    // ============================================================
    public bool IsShown => shown;

    /// <summary>Calls GameOver() on SupermanGameManager, so the normal flow runs</summary>
    public void TestTriggerGameOver()
    {
        if (supermanManager != null) supermanManager.GameOver();
    }

    /// <summary>Shows the panel right away (again), without waiting for game over</summary>
    public void TestShow()
    {
        shown = false;
        Show();
    }

    public void TestHide()
    {
        StopTvEffect();
        HideNameInput();
        ClearRankBanners();
        if (newBest != null) newBest.SetActive(false);
        if (scoreUI != null) scoreUI.SetActive(true);
        shown = false;
        gameObject.SetActive(false);
    }

    public void TestReplayTvEffect()
    {
        if (!gameObject.activeInHierarchy) { TestShow(); return; }
        PlayTvEffect();
    }

    public void TestToggleNewBest()
    {
        if (newBest != null) newBest.SetActive(!newBest.activeSelf);
    }

    /// <summary>Shows the name input (submitting it uploads LastScore)</summary>
    public void TestShowNameInput()
    {
        if (!gameObject.activeInHierarchy) { shown = true; gameObject.SetActive(true); }
        if (nameInputField == null) return;
        pendingScore = SupermanGameManager.LastScore;
        ShowNameInput();
    }

    public async void TestReloadRanking()
    {
        try
        {
            if (!gameObject.activeInHierarchy) { shown = true; gameObject.SetActive(true); }
            List<LeaderboardEntry> top = await GetTopScores();
            if (this != null) ShowRanking(top);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private async void RunFlow(long lastScore, bool isNewBest)
    {
        try
        {
            if (!await EnsureSignedIn()) return;

            List<LeaderboardEntry> top = await GetTopScores();
            if (this == null) return;

            bool canSubmit = isNewBest
                             && IsInTop(top, lastScore)
                             && await BeatsOwnServerScore(lastScore);
            if (this == null) return;

            if (canSubmit && nameInputField != null)
            {
                pendingScore = lastScore;
                ShowNameInput();
            }
            else
            {
                ShowRanking(top);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    // ============================================================
    //  Old TV turn-on effect
    //  tiny + white -> open Scale X -> open Scale Y while the white fades away
    //
    //  The white follows the real shape of each Image: every Image gets a child overlay
    //  that uses the same sprite, so it never goes outside the sprite's outline.
    //  Texts (TextMeshPro) are tinted toward the flash color instead.
    // ============================================================
    private class TvFlash
    {
        public readonly List<Image> overlays = new List<Image>();
        public readonly List<float> overlayAlpha = new List<float>();   // alpha of the Image each overlay covers
        public readonly List<TMP_Text> texts = new List<TMP_Text>();
        public readonly List<Color> textColors = new List<Color>();
    }

    private readonly List<Coroutine> tvRoutines = new List<Coroutine>();
    private readonly List<TvFlash> tvFlashes = new List<TvFlash>();

    private void PlayTvEffect()
    {
        StopTvEffect();
        if (personalRecord != null) tvRoutines.Add(StartCoroutine(TvTurnOn(personalRecord.transform, personalRecordBaseScale, RandomTvDelay(0f), RandomTvSpeed())));
        if (rank != null) tvRoutines.Add(StartCoroutine(TvTurnOn(rank.transform, rankBaseScale, RandomTvDelay(tvRankDelay), RandomTvSpeed())));
    }

    /// <summary>Stops a running effect and puts everything back to normal</summary>
    private void StopTvEffect()
    {
        foreach (Coroutine c in tvRoutines)
            if (c != null) StopCoroutine(c);
        tvRoutines.Clear();

        foreach (TvFlash f in tvFlashes) RemoveFlash(f);
        tvFlashes.Clear();

        if (personalRecord != null)
        {
            personalRecord.transform.localScale = personalRecordBaseScale;
            personalRecord.SetActive(true);
        }
        if (rank != null)
        {
            rank.transform.localScale = rankBaseScale;
            rank.SetActive(true);
        }
    }

    private float RandomTvDelay(float baseDelay)
    {
        float min = Mathf.Min(tvRandomDelay.x, tvRandomDelay.y);
        float max = Mathf.Max(tvRandomDelay.x, tvRandomDelay.y);
        return Mathf.Max(0f, baseDelay + UnityEngine.Random.Range(min, max));
    }

    /// <summary>Duration multiplier, e.g. 0.85 ~ 1.15</summary>
    private float RandomTvSpeed()
    {
        return 1f + UnityEngine.Random.Range(-tvRandomSpeed, tvRandomSpeed);
    }

    /// <summary>Hidden until its delay is over, then switched on. The TV effect plays only if Play Tv Effect is on</summary>
    private IEnumerator TvTurnOn(Transform target, Vector3 baseScale, float delay, float durationScale)
    {
        float xDur = tvScaleXDuration * durationScale;
        float pause = tvPauseBetween * durationScale;
        float yDur = tvScaleYDuration * durationScale;
        float fadeDur = tvWhiteFadeDuration * durationScale;

        float startX = baseScale.x * tvStartScale.x;
        float startY = baseScale.y * tvStartScale.y;

        // Stay hidden during the delay, then switch on.
        // The Animator starts when the object is switched on, so each one starts at a different time.
        target.gameObject.SetActive(false);
        if (delay > 0f) yield return TvWait(delay);
        target.gameObject.SetActive(true);

        if (!playTvEffect)
        {
            target.localScale = baseScale;
            yield break;
        }

        target.localScale = new Vector3(startX, startY, baseScale.z);
        TvFlash flash = CreateFlash(target);
        SetFlash(flash, 0f);

        // Step 1: open Scale X
        float t = 0f;
        while (t < xDur)
        {
            t += TvDeltaTime();
            float e = tvScaleXCurve.Evaluate(Mathf.Clamp01(t / xDur));
            target.localScale = new Vector3(Mathf.LerpUnclamped(startX, baseScale.x, e), startY, baseScale.z);
            yield return null;
        }
        target.localScale = new Vector3(baseScale.x, startY, baseScale.z);

        if (pause > 0f) yield return TvWait(pause);

        // Step 2: open Scale Y, and fade the white away at the same time
        float yTime = 0f, fadeTime = 0f;
        while (yTime < yDur || fadeTime < fadeDur)
        {
            float dt = TvDeltaTime();
            yTime += dt;
            fadeTime += dt;

            float ey = yDur > 0f ? tvScaleYCurve.Evaluate(Mathf.Clamp01(yTime / yDur)) : 1f;
            target.localScale = new Vector3(baseScale.x, Mathf.LerpUnclamped(startY, baseScale.y, ey), baseScale.z);

            float ef = fadeDur > 0f ? tvWhiteFadeCurve.Evaluate(Mathf.Clamp01(fadeTime / fadeDur)) : 1f;
            SetFlash(flash, ef);
            yield return null;
        }

        target.localScale = baseScale;
        RemoveFlash(flash);
        tvFlashes.Remove(flash);
    }

    private TvFlash CreateFlash(Transform target)
    {
        var flash = new TvFlash();

        // Same-shape overlay on every visible Image
        foreach (Image src in target.GetComponentsInChildren<Image>(false))
        {
            if (src == null || !src.enabled) continue;

            var go = new GameObject("TVFlash", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.layer = src.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(src.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = src.rectTransform.pivot;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();   // drawn right on top of its Image, but under that Image's own children

            go.GetComponent<LayoutElement>().ignoreLayout = true;

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = src.sprite;
            img.type = src.type;
            img.preserveAspect = src.preserveAspect;
            img.useSpriteMesh = src.useSpriteMesh;
            img.fillCenter = src.fillCenter;
            img.fillMethod = src.fillMethod;
            img.fillAmount = src.fillAmount;
            img.fillClockwise = src.fillClockwise;
            img.fillOrigin = src.fillOrigin;
            img.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier;
            img.material = FlashMaterial();

            flash.overlays.Add(img);
            flash.overlayAlpha.Add(src.color.a);
        }

        // Texts: tint toward the flash color
        foreach (TMP_Text txt in target.GetComponentsInChildren<TMP_Text>(false))
        {
            if (txt == null) continue;
            flash.texts.Add(txt);
            flash.textColors.Add(txt.color);
        }

        tvFlashes.Add(flash);
        return flash;
    }

    /// <summary>progress 0 = full flash color, 1 = normal colors</summary>
    private void SetFlash(TvFlash flash, float progress)
    {
        if (flash == null) return;
        float strength = Mathf.Clamp01(Mathf.LerpUnclamped(tvFlashColor.a, 0f, progress));

        for (int i = 0; i < flash.overlays.Count; i++)
        {
            Image img = flash.overlays[i];
            if (img == null) continue;
            Color c = tvFlashColor;
            c.a = strength * flash.overlayAlpha[i];
            img.color = c;
        }

        for (int i = 0; i < flash.texts.Count; i++)
        {
            TMP_Text txt = flash.texts[i];
            if (txt == null) continue;
            Color orig = flash.textColors[i];
            Color white = new Color(tvFlashColor.r, tvFlashColor.g, tvFlashColor.b, orig.a);
            txt.color = Color.Lerp(orig, white, strength);
        }
    }

    private void RemoveFlash(TvFlash flash)
    {
        if (flash == null) return;

        foreach (Image img in flash.overlays)
            if (img != null) Destroy(img.gameObject);
        flash.overlays.Clear();

        for (int i = 0; i < flash.texts.Count; i++)
            if (flash.texts[i] != null) flash.texts[i].color = flash.textColors[i];
        flash.texts.Clear();
        flash.textColors.Clear();
    }

    // A white overlay drawn with the default UI shader would still show the sprite's own colors,
    // so the overlay uses a shader that keeps only the sprite's shape (alpha) and paints it one color.
    private static Material flashMaterial;

    private Material FlashMaterial()
    {
        if (tvFlashMaterial != null) return tvFlashMaterial;
        if (flashMaterial != null) return flashMaterial;

        // "GUI/Text Shader": color comes from the Image color, only the alpha comes from the sprite
        Shader shader = Shader.Find("GUI/Text Shader");
        if (shader == null)
        {
            Debug.LogWarning("[LeaderBoardManager] Shader \"GUI/Text Shader\" not found. Add it to " +
                             "Project Settings > Graphics > Always Included Shaders, or assign Tv Flash Material. " +
                             "Using UI/Default for now (sprite colors will show through the white).", this);
            shader = Shader.Find("UI/Default");
        }
        flashMaterial = new Material(shader) { name = "TVFlash (runtime)" };
        return flashMaterial;
    }

    private float TvDeltaTime() => tvUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private object TvWait(float seconds) =>
        tvUseUnscaledTime ? (object)new WaitForSecondsRealtime(seconds) : new WaitForSeconds(seconds);

    // ============================================================
    //  Leaderboard
    // ============================================================
    private static async Task<bool> EnsureSignedIn()
    {
        try
        {
            // Check the real state every time instead of trusting an old finished task
            if (IsServicesReady() && !forceNewPlayer) return true;

            if (servicesTask == null || servicesTask.IsCompleted)
                servicesTask = InitServices();
            await servicesTask;

            if (IsServicesReady()) return true;
            Debug.LogWarning($"[LeaderBoardManager] Unity Services not ready. {ServicesStateText()}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LeaderBoardManager] Unity Services sign-in failed: {e.Message}\n{ServicesStateText()}");
            return false;
        }
    }

    private static bool IsServicesReady()
    {
        return UnityServices.State == ServicesInitializationState.Initialized
               && AuthenticationService.Instance.IsSignedIn;
    }

    private static string ServicesStateText()
    {
        string projectId = Application.cloudProjectId;
        string state = $"UnityServices.State = {UnityServices.State}";
        try { state += $", SignedIn = {AuthenticationService.Instance.IsSignedIn}"; } catch { }
        state += string.IsNullOrEmpty(projectId)
            ? ", Cloud Project ID = (empty: link the project in Project Settings > Services)"
            : $", Cloud Project ID = {projectId}";
        return state;
    }

    private static async Task InitServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (forceNewPlayer)
        {
            // Debug Mode: sign out and delete the saved session, so the next sign-in creates a new player
            forceNewPlayer = false;
            if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut();
            AuthenticationService.Instance.ClearSessionToken();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private async Task<List<LeaderboardEntry>> GetTopScores()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (!await EnsureSignedIn()) return null;
                var page = await LeaderboardsService.Instance.GetScoresAsync(
                    ActiveLeaderboardId, new GetScoresOptions { Limit = topCount });
                return page.Results ?? new List<LeaderboardEntry>();
            }
            catch (Exception e)
            {
                if (attempt == 0 && IsNotInitializedError(e))
                {
                    // Services were reset (e.g. Domain Reload off): initialize again and retry once
                    servicesTask = null;
                    try { await UnityServices.InitializeAsync(); } catch { }
                    continue;
                }
                Debug.LogWarning($"[LeaderBoardManager] Failed to load leaderboard {ActiveLeaderboardId}: {e.Message}\n{ServicesStateText()}", this);
                return null;
            }
        }
        return null;
    }

    private static bool IsNotInitializedError(Exception e)
    {
        return e.Message != null && e.Message.IndexOf("not been initialized", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>The top list isn't full yet, or the score beats the last place</summary>
    private bool IsInTop(List<LeaderboardEntry> top, long lastScore)
    {
        if (top == null || lastScore <= 0) return false;
        if (top.Count < topCount) return true;
        return lastScore > top[topCount - 1].Score;
    }

    /// <summary>This computer's score on the leaderboard is lower than this round (or doesn't exist yet)</summary>
    private async Task<bool> BeatsOwnServerScore(long lastScore)
    {
        try
        {
            LeaderboardEntry mine = await LeaderboardsService.Instance.GetPlayerScoreAsync(ActiveLeaderboardId);
            return mine == null || lastScore > mine.Score;
        }
        catch
        {
            return true;   // player has no entry on the leaderboard yet
        }
    }

    private async void SubmitScore(string playerName)
    {
        try
        {
            try { await AuthenticationService.Instance.UpdatePlayerNameAsync(playerName); }
            catch (Exception e) { Debug.LogWarning($"[LeaderBoardManager] Failed to update player name: {e.Message}", this); }

            try { await LeaderboardsService.Instance.AddPlayerScoreAsync(ActiveLeaderboardId, pendingScore); }
            catch (Exception e) { Debug.LogWarning($"[LeaderBoardManager] Failed to upload score: {e.Message}", this); }

            if (this == null) return;

            List<LeaderboardEntry> top = await GetTopScores();
            if (this == null) return;

            HideNameInput();
            ShowRanking(top);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void ShowRanking(List<LeaderboardEntry> top)
    {
        ClearRankBanners();
        if (top == null || rankBannerPrefab == null || rankContainer == null) return;

        int count = Mathf.Min(top.Count, topCount);
        for (int i = 0; i < count; i++)
        {
            LeaderboardEntry entry = top[i];
            GameObject banner = Instantiate(rankBannerPrefab, rankContainer, false);
            banner.SetActive(true);
            spawnedBanners.Add(banner);

            SetText(FindChildText(banner, "RankNumber"), $"{i + 1}.");
            SetText(FindChildText(banner, "Name"), FormatName(entry.PlayerName));
            SetText(FindChildText(banner, "Score"), ((long)Math.Round(entry.Score)).ToString());
        }
    }

    private void ClearRankBanners()
    {
        foreach (GameObject b in spawnedBanners)
            if (b != null) Destroy(b);
        spawnedBanners.Clear();
    }

    /// <summary>"Abcde#1234" -> "ABCDE": removes the #number suffix, keeps letters only, uppercase</summary>
    private string FormatName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "-----";
        int hash = raw.IndexOf('#');
        if (hash >= 0) raw = raw.Substring(0, hash);
        string clean = SanitizeName(raw);
        return clean.Length > 0 ? clean : "-----";
    }

    // ============================================================
    //  Name input: uppercase English letters only, max 5, Enter to submit
    // ============================================================
    private void SetupNameInput()
    {
        nameInputField.lineType = TMP_InputField.LineType.SingleLine;   // Enter = submit, no new line
        nameInputField.contentType = TMP_InputField.ContentType.Standard;
        nameInputField.characterLimit = maxNameLength;
        nameInputField.onValidateInput = ValidateNameChar;             // every typed character goes through here
        nameInputField.onValueChanged.AddListener(OnNameChanged);      // filters pasted / IME text too
        nameInputField.onSubmit.AddListener(OnNameSubmit);             // Enter
    }

    private char ValidateNameChar(string text, int charIndex, char c)
    {
        if (c >= 'a' && c <= 'z') return char.ToUpperInvariant(c);    // lowercase -> uppercase
        if (c >= 'A' && c <= 'Z') return c;
        return '\0';                                                    // anything else is rejected
    }

    private void OnNameChanged(string value)
    {
        string clean = SanitizeName(value);
        if (clean == value) return;
        nameInputField.SetTextWithoutNotify(clean);
        nameInputField.caretPosition = clean.Length;
    }

    private void OnNameSubmit(string value)
    {
        if (submitting) return;

        string playerName = SanitizeName(value);
        if (playerName.Length == 0)
        {
            nameInputField.ActivateInputField();   // empty name is not submitted
            return;
        }

        submitting = true;
        nameInputField.interactable = false;
        SubmitScore(playerName);
    }

    private string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(maxNameLength);
        foreach (char ch in value.ToUpperInvariant())
        {
            if (ch >= 'A' && ch <= 'Z') sb.Append(ch);
            if (sb.Length >= maxNameLength) break;
        }
        return sb.ToString();
    }

    private void ShowNameInput()
    {
        submitting = false;
        nameInputField.gameObject.SetActive(true);
        nameInputField.interactable = true;
        nameInputField.SetTextWithoutNotify(string.Empty);
        nameInputField.Select();
        nameInputField.ActivateInputField();
        SetImeOff(true);   // turn off IME so Chinese can't be typed
    }

    private void HideNameInput()
    {
        SetImeOff(false);
        if (nameInputField != null)
        {
            nameInputField.DeactivateInputField();
            nameInputField.gameObject.SetActive(false);
        }
    }

    private static void SetImeOff(bool off)
    {
        // Throws when only the new Input System is active; safe to ignore (the name is filtered before submit anyway)
        try { Input.imeCompositionMode = off ? IMECompositionMode.Off : IMECompositionMode.Auto; }
        catch { }
    }

    // ============================================================
    //  Finding objects
    // ============================================================
    private void FindReferences()
    {
        if (supermanManager == null)
        {
            GameObject go = FindByName("supermanManager");
            if (go != null) supermanManager = go.GetComponent<SupermanGameManager>();
            if (supermanManager == null)
                supermanManager = FindFirstObjectByType<SupermanGameManager>(FindObjectsInactive.Include);
        }

        if (personalRecord == null) personalRecord = FindByName("personalRecord");
        if (rank == null) rank = FindByName("rank");
        if (score == null) score = FindComponent<TMP_Text>("score");
        if (totalClears == null) totalClears = FindComponent<TMP_Text>("totalClears");
        if (maxCombo == null) maxCombo = FindComponent<TMP_Text>("maxCombo");
        if (misses == null) misses = FindComponent<TMP_Text>("misses");
        if (rankContainer == null) { GameObject go = FindByName("rankContainer"); if (go != null) rankContainer = go.transform; }
        if (nameInputField == null) nameInputField = FindComponent<TMP_InputField>("nameInputField");
        if (newBest == null) newBest = FindByName("NewBest");
        if (scoreUI == null) scoreUI = FindByName("scoreUI");

        WarnIfMissing(personalRecord, "personalRecord");
        WarnIfMissing(rank, "rank");
        WarnIfMissing(score, "score");
        WarnIfMissing(totalClears, "totalClears");
        WarnIfMissing(maxCombo, "maxCombo");
        WarnIfMissing(misses, "misses");
        WarnIfMissing(rankContainer, "rankContainer");
        WarnIfMissing(nameInputField, "nameInputField");
        WarnIfMissing(newBest, "NewBest");
        WarnIfMissing(scoreUI, "scoreUI");
    }

    private void WarnIfMissing(UnityEngine.Object obj, string objName)
    {
        if (obj == null) Debug.LogWarning($"[LeaderBoardManager] GameObject \"{objName}\" not found.", this);
    }

    private T FindComponent<T>(string objName) where T : Component
    {
        GameObject go = FindByName(objName);
        return go != null ? go.GetComponent<T>() : null;
    }

    /// <summary>Searches this GameObject's children first, then the whole scene. Hidden objects are included.</summary>
    private GameObject FindByName(string objName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == objName) return t.gameObject;

        Scene scene = gameObject.scene;
        if (scene.IsValid() && scene.isLoaded)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == objName) return t.gameObject;
        }
        return null;
    }

    private static TMP_Text FindChildText(GameObject parent, string childName)
    {
        foreach (TMP_Text t in parent.GetComponentsInChildren<TMP_Text>(true))
            if (t.name == childName) return t;
        return null;
    }

    private static void SetText(TMP_Text t, string value)
    {
        if (t != null) t.text = value;
    }
}

#if UNITY_EDITOR
/// <summary>Test buttons in the Inspector (Play Mode only)</summary>
[CustomEditor(typeof(LeaderBoardManager))]
public class LeaderBoardManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var lb = (LeaderBoardManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test buttons.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(
            $"Shown: {lb.IsShown}   LastScore: {SupermanGameManager.LastScore}   HighScore: {SupermanGameManager.HighScore}");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Trigger Game Over")) lb.TestTriggerGameOver();
        if (GUILayout.Button("Show")) lb.TestShow();
        if (GUILayout.Button("Hide")) lb.TestHide();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Replay TV Effect")) lb.TestReplayTvEffect();
        if (GUILayout.Button("Toggle NewBest")) lb.TestToggleNewBest();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Show Name Input")) lb.TestShowNameInput();
        if (GUILayout.Button("Reload Ranking")) lb.TestReloadRanking();
        EditorGUILayout.EndHorizontal();

        Repaint();
    }
}
#endif
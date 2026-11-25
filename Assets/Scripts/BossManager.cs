using System.Collections;
using System.Reflection;
using UnityEngine;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Boss performance controller:
/// Enter -> Hardbreath -> Speech -> Idle -> Exit -> call GameManager.OnStartButtonClicked()
/// The Boss's placed position in the scene is used as the "home" (stop) point.
/// </summary>
public class BossManager : MonoBehaviour
{
    public enum MoveSide { Left, Right, Down }

    [System.Serializable]
    public struct RandomRange
    {
        public float min;
        public float max;
        public RandomRange(float min, float max) { this.min = min; this.max = max; }
        public float Get() => Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
    }

    // --------- References ---------
    [Header("References")]
    public Animator animator;
    [Tooltip("Object holding the GameManager script")]
    public GameObject gameManager;
    [Tooltip("Boss speech bubble (hidden by default). A child must have a TextMeshPro - Text (UI)")]
    public GameObject bossSpeech;

    // --------- Enter / Exit ---------
    [Header("Enter Settings")]
    public MoveSide enterFrom = MoveSide.Right;
    [Tooltip("How far from the home point the Boss starts entering")]
    public float enterDistance = 10f;

    [Header("Exit Settings")]
    public MoveSide exitTo = MoveSide.Left;
    [Tooltip("How far from the home point the Boss moves when exiting")]
    public float exitDistance = 10f;

    [Header("Move Speed (units/sec)")]
    public float moveSpeed = 3f;

    // --------- Durations ---------
    [Header("Durations in seconds (min / max)")]
    public RandomRange hardbreathTime = new RandomRange(1f, 2f);
    public RandomRange speechTime = new RandomRange(2f, 3f);
    public RandomRange idleTime = new RandomRange(1f, 2f);
    public RandomRange winSayingTime = new RandomRange(2f, 3f);

    // --------- Speech Bubble ---------
    [Header("Speech Bubble")]
    [Tooltip("Seconds after showing the bubble before calling GameManager.OnNextButtonClicked()")]
    public float bubbleShowTime = 2f;

    [TextArea(1, 2)]
    public string[] bossLines = new string[]
    {
        "We're cutting jobs. Bye!",
        "AI is cheaper than you.",
        "AI never takes vacations.",
        "AI does your job in 3s.",
        "Your role goes to AI.",
        "Today is your last day.",
        "AI never complains.",
        "Sorry, we bought GPUs.",
        "No bonus needed for AI.",
        "Congrats, you're retired!",
        "Humans: optimized away.",
        "AI writes better reports.",
        "You tried. AI is better.",
        "Your desk is for servers.",
        "Hand your work to the AI.",
        "AI just needs power.",
        "Thanks. The door's there.",
        "Don't worry, AI's got it.",
        "We're going AI-first now.",
        "Headcount: zero needed.",
    };

    // --------- Animator Parameters ---------
    static readonly int WalkHash = Animator.StringToHash("Walk");
    static readonly int WalknSpeechHash = Animator.StringToHash("WalknSpeech");
    static readonly int SpeechHash = Animator.StringToHash("Speech");
    static readonly int HardbreathHash = Animator.StringToHash("Hardbreath");
    static readonly int BossWinsayingHash = Animator.StringToHash("BossWinsaying");
    static readonly int[] AllHashes = { WalkHash, WalknSpeechHash, SpeechHash, HardbreathHash, BossWinsayingHash };

    Vector3 homePosition;          // Home (stop) point
    bool homeCached;
    Coroutine actRoutine, sayRoutine, bubbleRoutine;
    TMP_Text speechText;

    // =============================================

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        CacheHome();
        if (bossSpeech != null)
        {
            speechText = bossSpeech.GetComponentInChildren<TMP_Text>(true);
            bossSpeech.SetActive(false);
        }
    }

    void Start()
    {
        BossAct();
    }

    void CacheHome()
    {
        if (homeCached) return;
        homePosition = transform.position;
        homeCached = true;
    }

    // ============= Public API =============

    /// <summary>Plays the Boss entrance performance, then calls GameManager.OnStartButtonClicked()</summary>
    public void BossAct()
    {
        CacheHome();
        if (actRoutine != null) StopCoroutine(actRoutine);
        actRoutine = StartCoroutine(BossActRoutine());
    }

    /// <summary>Plays BossWinsaying for a random duration</summary>
    public void BossSay()
    {
        if (sayRoutine != null) StopCoroutine(sayRoutine);
        sayRoutine = StartCoroutine(BossSayRoutine());
    }

    /// <summary>Shows the bubble -> calls OnNextButtonClicked() after a delay -> hides it based on nextStageDelay</summary>
    public void BossBubbleChat()
    {
        if (bubbleRoutine != null) StopCoroutine(bubbleRoutine);
        bubbleRoutine = StartCoroutine(BossBubbleRoutine());
    }

    // ============= Routines =============

    IEnumerator BossActRoutine()
    {
        Vector3 enterStart = homePosition + SideToDir(enterFrom) * enterDistance;
        Vector3 exitEnd = homePosition + SideToDir(exitTo) * exitDistance;

        // 1. Enter
        transform.position = enterStart;
        SetAnim(RandomWalkHash());
        yield return MoveTo(homePosition);

        // 2. Hardbreath
        SetAnim(HardbreathHash);
        yield return new WaitForSeconds(hardbreathTime.Get());

        // 3. Speech
        SetAnim(SpeechHash);
        yield return new WaitForSeconds(speechTime.Get());

        // 4. Idle in place
        SetAnim(0);
        yield return new WaitForSeconds(idleTime.Get());

        // 5. Exit
        SetAnim(RandomWalkHash());
        yield return MoveTo(exitEnd);

        // 6. Done
        SetAnim(0);
        actRoutine = null;
        CallOnStartButtonClicked();
    }

    IEnumerator BossSayRoutine()
    {
        SetAnim(BossWinsayingHash);
        yield return new WaitForSeconds(winSayingTime.Get());
        animator.SetBool(BossWinsayingHash, false);
        sayRoutine = null;
    }

    IEnumerator BossBubbleRoutine()
    {
        if (bossSpeech == null)
        {
            Debug.LogWarning("[BossManager] bossSpeech is not assigned");
            yield break;
        }

        if (speechText == null) speechText = bossSpeech.GetComponentInChildren<TMP_Text>(true);
        if (speechText != null && bossLines != null && bossLines.Length > 0)
            speechText.text = bossLines[Random.Range(0, bossLines.Length)];

        bossSpeech.SetActive(true);
        yield return new WaitForSeconds(bubbleShowTime);

        float delay = CallNextButtonAndGetDelay();
        yield return new WaitForSeconds(delay);

        bossSpeech.SetActive(false);
        bubbleRoutine = null;
    }

    // ============= Helpers =============

    IEnumerator MoveTo(Vector3 target)
    {
        while ((transform.position - target).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = target;
    }

    int RandomWalkHash() => Random.value < 0.5f ? WalkHash : WalknSpeechHash;

    /// <summary>Sets every bool to false except the given one (pass 0 to turn all off)</summary>
    void SetAnim(int activeHash)
    {
        if (animator == null) return;
        foreach (int h in AllHashes)
            animator.SetBool(h, h == activeHash);
    }

    static Vector3 SideToDir(MoveSide side)
    {
        switch (side)
        {
            case MoveSide.Left: return Vector3.left;
            case MoveSide.Right: return Vector3.right;
            default: return Vector3.down;
        }
    }

    void CallOnStartButtonClicked()
    {
        if (gameManager == null)
        {
            Debug.LogWarning("[BossManager] gameManager is not assigned");
            return;
        }
        // Boss performance finished -> call GameManager.OnStartButtonClicked()
        gameManager.SendMessage("OnStartButtonClicked", SendMessageOptions.RequireReceiver);
    }

    /// <summary>Calls GameManager.OnNextButtonClicked() and returns nextStageDelay</summary>
    float CallNextButtonAndGetDelay()
    {
        if (gameManager == null)
        {
            Debug.LogWarning("[BossManager] gameManager is not assigned");
            return 0f;
        }

        Component gm = gameManager.GetComponent("GameManager");
        if (gm == null)
        {
            Debug.LogWarning("[BossManager] GameManager script not found on gameManager");
            return 0f;
        }

        // Read the value first (works for public or [SerializeField] private)
        float delay = 0f;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = gm.GetType();
        FieldInfo field = t.GetField("nextStageDelay", flags);
        if (field != null && field.FieldType == typeof(float))
            delay = (float)field.GetValue(gm);
        else
        {
            PropertyInfo prop = t.GetProperty("nextStageDelay", flags);
            if (prop != null && prop.PropertyType == typeof(float))
                delay = (float)prop.GetValue(gm);
            else
                Debug.LogWarning("[BossManager] float nextStageDelay not found in GameManager");
        }

        gameManager.SendMessage("OnNextButtonClicked", SendMessageOptions.RequireReceiver);
        return Mathf.Max(0f, delay);
    }

    // ============= Scene Gizmos =============

    void OnDrawGizmos()
    {
        Vector3 home = Application.isPlaying && homeCached ? homePosition : transform.position;
        Vector3 enterStart = home + SideToDir(enterFrom) * enterDistance;
        Vector3 exitEnd = home + SideToDir(exitTo) * exitDistance;

        // Home point
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(home, 0.3f);

        // Enter path (green)
        Gizmos.color = Color.green;
        Gizmos.DrawLine(enterStart, home);
        Gizmos.DrawWireSphere(enterStart, 0.3f);

        // Exit path (red)
        Gizmos.color = Color.red;
        Gizmos.DrawLine(home, exitEnd);
        Gizmos.DrawWireSphere(exitEnd, 0.3f);

#if UNITY_EDITOR
        Handles.Label(home + Vector3.up * 0.5f, "Home");
        Handles.Label(enterStart + Vector3.up * 0.5f, "Enter Start");
        Handles.Label(exitEnd + Vector3.up * 0.5f, "Exit End");
#endif
    }
}
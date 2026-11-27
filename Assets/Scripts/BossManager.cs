using System.Collections;
using System.Collections.Generic;
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
    public enum MoveSide { Left, Right, Random }
    static readonly MoveSide[] RealSides = { MoveSide.Left, MoveSide.Right };

    /// <summary>Chat bubble appear animation (scales from small to original size)</summary>
    public enum ChatShowAnim { Linear, EaseOut, Pop, Elastic }

    /// <summary>General easing used by the BossBubbleChat slide and BossSpeech scale animations</summary>
    public enum UIEase { Linear, EaseOut, EaseInOut, Back, Elastic, Bounce, EaseIn, BackIn }

    /// <summary>Chat bubble disappear animation</summary>
    public enum ChatHideAnim { Instant, Shrink, ShrinkBack, Fade, ShrinkAndFade }

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
    [Tooltip("Boss speech bubble PREFAB (RectTransform). A child must have a TextMeshPro - Text (UI)")]
    public GameObject bossSpeechPrefab;
    [Tooltip("BossBubbleChat PREFAB (RectTransform). Spawned before BossSpeech and always drawn on top of it")]
    public GameObject bossBubbleChatPrefab;
    [Tooltip("blackGradient PREFAB (RectTransform + Image). Spawned under the Canvas, fades in behind BossBubbleChat and BossSpeech")]
    public GameObject blackGradientPrefab;
    [Tooltip("Canvas the bubbles are spawned under (as its last children). If empty, the first Canvas in the scene is used")]
    public Canvas targetCanvas;
    [Tooltip("Chat bubble ROOT under the Boss object (hidden by default). Scales around its own origin. Text / sprites can be children")]
    public GameObject chatBubble;

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

    [Header("Walk Animation")]
    [Tooltip("Chance of using WalknSpeech instead of Walk when moving (0 = always Walk, 1 = always WalknSpeech)")]
    [Range(0f, 1f)] public float walknSpeechChance = 0.5f;
    [Tooltip("Print which walk animation was chosen to the Console")]
    public bool debugLog = false;

    [Header("Facing")]
    [Tooltip("Flip the Boss sprites horizontally while moving right (chatBubble is never flipped)")]
    public bool flipWhenMovingRight = true;
    [Tooltip("Root of the Boss visuals to flip. If empty, the Animator's object is used")]
    public Transform flipRoot;

    // --------- Durations ---------
    [Header("Durations in seconds (min / max)")]
    public RandomRange hardbreathTime = new RandomRange(1f, 2f);
    public RandomRange speechTime = new RandomRange(2f, 3f);
    public RandomRange idleTime = new RandomRange(1f, 2f);
    public RandomRange winSayingTime = new RandomRange(2f, 3f);

    // --------- Speech Bubble ---------
    [Header("Speech Bubble")]
    [Tooltip("Seconds after BossSpeech is fully shown before calling GameManager.OnNextButtonClicked()")]
    public float bubbleShowTime = 2f;

    [Header("BossBubbleChat Slide-In (moves up into its prefab position)")]
    public UIEase bubbleChatSlideEase = UIEase.EaseOut;
    [Tooltip("How far below its prefab position it starts (UI units)")]
    public float bubbleChatSlideDistance = 300f;
    [Min(0f)] public float bubbleChatSlideDuration = 0.4f;
    [Tooltip("When BossSpeech pops up, BossBubbleChat's Animator bool \"BubbleChat\" is set true for this many seconds, then back to false")]
    [Min(0f)] public float bubbleChatAnimDuration = 1.5f;

    [Header("BossSpeech Scale-Up")]
    [Tooltip("Added to BossSpeech's prefab position (UI units). X = right, Y = up")]
    public Vector2 bossSpeechOffset = Vector2.zero;
    public UIEase bossSpeechScaleEase = UIEase.Back;
    [Tooltip("Size it starts growing from (0 = from nothing, 0.5 = from half size)")]
    [Range(0f, 1f)] public float bossSpeechStartScale = 0f;
    [Tooltip("How far it grows past full size before settling (Back ease only). 0 = no overshoot, 1.7 = default, 4 = strong")]
    [Range(0f, 5f)] public float bossSpeechOvershoot = 1.70158f;
    [Min(0f)] public float bossSpeechScaleDuration = 0.3f;
    [Tooltip("Point it grows from (0,0 = bottom-left, 1,0 = bottom-right)")]
    public Vector2 bossSpeechScalePivot = new Vector2(1f, 0f);

    [Header("blackGradient Fade-In (spawned right before BossSpeech)")]
    public UIEase blackGradientFadeInEase = UIEase.Linear;
    [Min(0f)] public float blackGradientFadeInDuration = 0.3f;
    [Tooltip("On: BossSpeech waits until the fade-in finishes. Off: both play at the same time")]
    public bool waitForGradientBeforeSpeech = false;

    [Header("Hide Animations (all three play together)")]
    [Tooltip("BossBubbleChat moves down by this distance (UI units)")]
    public float bubbleChatHideDistance = 300f;
    public UIEase bubbleChatHideEase = UIEase.EaseIn;
    [Min(0f)] public float bubbleChatHideDuration = 0.3f;

    [Tooltip("BossSpeech shrinks toward its scale pivot")]
    public UIEase bossSpeechHideEase = UIEase.BackIn;
    [Min(0f)] public float bossSpeechHideDuration = 0.25f;

    public UIEase blackGradientHideEase = UIEase.Linear;
    [Min(0f)] public float blackGradientHideDuration = 0.3f;

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

    // --------- Chat Bubble ---------
    [Header("Chat Bubble (shown on WalknSpeech / Speech)")]
    [Tooltip("Appear animation: scales from 0 to the original size")]
    public ChatShowAnim chatShowAnim = ChatShowAnim.Pop;
    [Min(0f)] public float chatShowDuration = 0.25f;
    [Tooltip("How long the bubble stays fully shown (seconds)")]
    [Min(0f)] public float chatDisplayTime = 2f;
    public ChatHideAnim chatHideAnim = ChatHideAnim.ShrinkAndFade;
    [Min(0f)] public float chatHideDuration = 0.2f;

    [TextArea(2, 2)]
    public string[] chatLines = new string[]
    {
        "Work time!\nHit goals!",
        "Raises are\ncoming!",
        "Early bird\ngets worm!",
        "Go team!\nYou got it",
        "Big bonus\nthis year!",
        "Overtime?\nIt's fun!",
        "We're all\nfamily!",
        "Dream big,\nwork hard!",
        "No limits!\nOnly KPIs!",
        "Promotion\nsoon, OK?",
        "Push hard!\nWin big!",
        "Best team\never!",
        "I believe\nin you!",
        "Sales up!\nMood up!",
        "Weekends?\nOptional!",
        "Teamwork\nwins!",
        "Let's make\nmoney!",
        "Hard work\npays off!",
        "Good vibes\nonly!",
        "Coffee's\non me!",
    };

    // --------- Animator Parameters ---------
    static readonly int WalkHash = Animator.StringToHash("Walk");
    static readonly int WalknSpeechHash = Animator.StringToHash("WalknSpeech");
    static readonly int SpeechHash = Animator.StringToHash("Speech");
    static readonly int HardbreathHash = Animator.StringToHash("Hardbreath");
    static readonly int BossWinsayingHash = Animator.StringToHash("BossWinsaying");
    static readonly int BubbleChatHash = Animator.StringToHash("BubbleChat");   // on the BossBubbleChat prefab's Animator
    static readonly int[] AllHashes = { WalkHash, WalknSpeechHash, SpeechHash, HardbreathHash, BossWinsayingHash };

    Vector3 homePosition;          // Home (stop) point
    bool homeCached;
    Coroutine actRoutine, sayRoutine, bubbleRoutine;
    GameObject bossSpeechInstance;     // Currently spawned BossSpeech
    GameObject bossBubbleChatInstance; // Currently spawned BossBubbleChat
    GameObject blackGradientInstance;  // Currently spawned blackGradient

    // Chat bubble state
    Coroutine chatRoutine;
    RectTransform chatRect;
    bool chatInitialized;
    CanvasGroup chatGroup;                 // only used if the bubble is UI
    TMP_Text[] chatTexts;
    SpriteRenderer[] chatSprites;
    Color[] chatTextColors, chatSpriteColors;
    TMP_Text chatText;
    Vector3 chatOriginalScale = Vector3.one;
    readonly List<int> usedChatLines = new List<int>();   // Lines already used in this appearance

    MoveSide currentEnterSide, currentExitSide;   // Sides actually used in the current appearance
    bool sidesResolved;

    SpriteRenderer[] bossSprites;   // Boss sprites to flip (chatBubble excluded)

    // =============================================

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        CacheHome();
        InitChatBubble();
        CacheBossSprites();
    }

    void CacheBossSprites()
    {
        Transform root = flipRoot != null ? flipRoot : (animator != null ? animator.transform : transform);
        var list = new List<SpriteRenderer>();
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            // Skip anything inside the chat bubble so it never flips
            if (chatBubble != null && sr.transform.IsChildOf(chatBubble.transform)) continue;
            list.Add(sr);
        }
        bossSprites = list.ToArray();
    }

    /// <summary>Faces the Boss toward the horizontal move direction (dx > 0 = right). dx == 0 keeps the current facing</summary>
    void FaceDirection(float dx)
    {
        if (bossSprites == null || Mathf.Approximately(dx, 0f)) return;
        bool movingRight = dx > 0f;
        bool flip = movingRight ? flipWhenMovingRight : !flipWhenMovingRight;
        foreach (var sr in bossSprites)
            if (sr != null) sr.flipX = flip;
    }

    void InitChatBubble()
    {
        if (chatBubble == null) return;
        chatRect = chatBubble.GetComponent<RectTransform>();
        chatOriginalScale = chatBubble.transform.localScale;
        chatText = chatBubble.GetComponentInChildren<TMP_Text>(true);   // includes chatBubble itself
        chatGroup = chatBubble.GetComponent<CanvasGroup>();

        // For fading: works for TextMeshPro (3D or UI) and SpriteRenderers in the bubble
        chatTexts = chatBubble.GetComponentsInChildren<TMP_Text>(true);
        chatTextColors = new Color[chatTexts.Length];
        for (int i = 0; i < chatTexts.Length; i++) chatTextColors[i] = chatTexts[i].color;

        chatSprites = chatBubble.GetComponentsInChildren<SpriteRenderer>(true);
        chatSpriteColors = new Color[chatSprites.Length];
        for (int i = 0; i < chatSprites.Length; i++) chatSpriteColors[i] = chatSprites[i].color;

        chatInitialized = true;
        chatBubble.SetActive(false);
    }

    void OnDestroy()
    {
        DestroyBubble();
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
        usedChatLines.Clear();   // New appearance -> lines can be reused again
        actRoutine = StartCoroutine(BossActRoutine());
    }

    /// <summary>Shows the chat bubble with a random line (not repeated within the same appearance)</summary>
    public void ShowChatBubble()
    {
        if (chatBubble == null) return;
        if (!chatInitialized) InitChatBubble();
        if (chatRoutine != null) StopCoroutine(chatRoutine);
        chatRoutine = StartCoroutine(ChatBubbleRoutine(PickChatLine()));
    }

    /// <summary>Hides the chat bubble immediately</summary>
    public void HideChatBubbleImmediate()
    {
        if (chatRoutine != null) StopCoroutine(chatRoutine);
        chatRoutine = null;
        if (chatBubble == null) return;
        chatBubble.transform.localScale = chatOriginalScale;
        SetChatAlpha(1f);
        chatBubble.SetActive(false);
    }

    /// <summary>Plays BossWinsaying for a random duration</summary>
    public void BossSay()
    {
        if (sayRoutine != null) StopCoroutine(sayRoutine);
        sayRoutine = StartCoroutine(BossSayRoutine());
    }

    /// <summary>Spawns the bubble -> calls OnNextButtonClicked() after a delay -> destroys it based on nextStageDelay</summary>
    public void BossBubbleChat()
    {
        if (bubbleRoutine != null) StopCoroutine(bubbleRoutine);
        DestroyBubble();
        bubbleRoutine = StartCoroutine(BossBubbleRoutine(true));
    }

    /// <summary>Test only: plays the BossBubbleChat + BossSpeech animations without calling GameManager</summary>
    public void PreviewBossBubbleChat()
    {
        if (bubbleRoutine != null) StopCoroutine(bubbleRoutine);
        DestroyBubble();
        bubbleRoutine = StartCoroutine(BossBubbleRoutine(false));
    }

    // ============= Routines =============

    IEnumerator BossActRoutine()
    {
        // Resolve "Random" into a real side for this appearance
        currentEnterSide = ResolveSide(enterFrom);
        currentExitSide = ResolveSide(exitTo);
        sidesResolved = true;

        Vector3 enterStart = homePosition + SideToDir(currentEnterSide) * enterDistance;
        Vector3 exitEnd = homePosition + SideToDir(currentExitSide) * exitDistance;

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
        CallGameManager("CameraBackToDefault");
        SetAnim(RandomWalkHash());
        yield return MoveTo(exitEnd);

        // 6. Done
        SetAnim(0);
        HideChatBubbleImmediate();
        actRoutine = null;
        CallOnStartButtonClicked();
    }

    IEnumerator ChatBubbleRoutine(string line)
    {
        if (chatText != null) chatText.text = line;
        else Debug.LogWarning("[BossManager] No TMP_Text found on chatBubble");

        Transform t = chatBubble.transform;
        SetChatAlpha(1f);
        t.localScale = Vector3.zero;
        chatBubble.SetActive(true);

        // Appear: small -> original size (pivot is bottom-left, so it grows from that corner)
        float time = 0f;
        while (time < chatShowDuration)
        {
            time += Time.deltaTime;
            float p = Mathf.Clamp01(time / chatShowDuration);
            t.localScale = chatOriginalScale * EvaluateShow(chatShowAnim, p);
            yield return null;
        }
        t.localScale = chatOriginalScale;

        // Stay
        yield return new WaitForSeconds(chatDisplayTime);

        // Disappear
        if (chatHideAnim != ChatHideAnim.Instant)
        {
            time = 0f;
            while (time < chatHideDuration)
            {
                time += Time.deltaTime;
                float p = Mathf.Clamp01(time / chatHideDuration);
                switch (chatHideAnim)
                {
                    case ChatHideAnim.Shrink:
                        t.localScale = chatOriginalScale * (1f - EaseInQuad(p));
                        break;
                    case ChatHideAnim.ShrinkBack:
                        t.localScale = chatOriginalScale * (1f - EaseInBack(p));
                        break;
                    case ChatHideAnim.Fade:
                        SetChatAlpha(1f - p);
                        break;
                    case ChatHideAnim.ShrinkAndFade:
                        t.localScale = chatOriginalScale * (1f - EaseInQuad(p));
                        SetChatAlpha(1f - p);
                        break;
                }
                yield return null;
            }
        }

        chatBubble.SetActive(false);
        t.localScale = chatOriginalScale;
        SetChatAlpha(1f);
        chatRoutine = null;
    }

    /// <summary>Sets the bubble's transparency (multiplies the original alpha)</summary>
    void SetChatAlpha(float a)
    {
        if (chatGroup != null) chatGroup.alpha = a;
        if (chatTexts != null)
            for (int i = 0; i < chatTexts.Length; i++)
            {
                if (chatTexts[i] == null) continue;
                Color c = chatTextColors[i]; c.a *= a; chatTexts[i].color = c;
            }
        if (chatSprites != null)
            for (int i = 0; i < chatSprites.Length; i++)
            {
                if (chatSprites[i] == null) continue;
                Color c = chatSpriteColors[i]; c.a *= a; chatSprites[i].color = c;
            }
    }

    string PickChatLine()
    {
        if (chatLines == null || chatLines.Length == 0) return "";

        // All lines used in this appearance -> start over
        if (usedChatLines.Count >= chatLines.Length) usedChatLines.Clear();

        int index;
        do { index = Random.Range(0, chatLines.Length); }
        while (usedChatLines.Contains(index));

        usedChatLines.Add(index);
        return chatLines[index];
    }

    static float EvaluateShow(ChatShowAnim anim, float p)
    {
        switch (anim)
        {
            case ChatShowAnim.EaseOut: return 1f - (1f - p) * (1f - p);
            case ChatShowAnim.Pop: return EaseOutBack(p);
            case ChatShowAnim.Elastic: return EaseOutElastic(p);
            default: return p;
        }
    }

    static float EaseInQuad(float p) => p * p;

    static float EaseOutBack(float p) => EaseOutBack(p, 1.70158f);

    static float EaseOutBack(float p, float c1)
    {
        float c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    static float EaseInBack(float p)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return c3 * p * p * p - c1 * p * p;
    }

    static float EaseOutElastic(float p)
    {
        if (p <= 0f) return 0f;
        if (p >= 1f) return 1f;
        const float c4 = (2f * Mathf.PI) / 3f;
        return Mathf.Pow(2f, -10f * p) * Mathf.Sin((p * 10f - 0.75f) * c4) + 1f;
    }

    IEnumerator BossSayRoutine()
    {
        SetAnim(BossWinsayingHash);
        yield return new WaitForSeconds(winSayingTime.Get());
        animator.SetBool(BossWinsayingHash, false);
        sayRoutine = null;
    }

    IEnumerator BossBubbleRoutine(bool callNextStage)
    {
        if (bossSpeechPrefab == null)
        {
            Debug.LogWarning("[BossManager] bossSpeechPrefab is not assigned");
            yield break;
        }

        Canvas canvas = GetCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[BossManager] No Canvas found to spawn the speech bubble under");
            yield break;
        }

        // 1. BossBubbleChat: spawn at the bottom of the Canvas and slide up into its prefab position
        if (bossBubbleChatPrefab != null)
        {
            bossBubbleChatInstance = Instantiate(bossBubbleChatPrefab, canvas.transform, false);
            bossBubbleChatInstance.transform.SetAsLastSibling();
            bossBubbleChatInstance.SetActive(true);

            RectTransform chatRt = bossBubbleChatInstance.GetComponent<RectTransform>();
            if (chatRt != null)
            {
                Vector2 endPos = chatRt.anchoredPosition;
                Vector2 startPos = endPos - new Vector2(0f, bubbleChatSlideDistance);
                chatRt.anchoredPosition = startPos;

                float time = 0f;
                while (time < bubbleChatSlideDuration)
                {
                    time += Time.deltaTime;
                    float p = Mathf.Clamp01(time / bubbleChatSlideDuration);
                    chatRt.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, Ease(bubbleChatSlideEase, p));
                    yield return null;
                }
                chatRt.anchoredPosition = endPos;
            }
        }

        // 2. blackGradient: spawn and fade in (drawn below BossSpeech and BossBubbleChat)
        if (blackGradientPrefab != null)
        {
            blackGradientInstance = Instantiate(blackGradientPrefab, canvas.transform, false);
            blackGradientInstance.transform.SetAsLastSibling();
            if (bossBubbleChatInstance != null) bossBubbleChatInstance.transform.SetAsLastSibling();   // keep BossBubbleChat above it
            blackGradientInstance.SetActive(true);

            CanvasGroup gradientGroup = GetOrAddCanvasGroup(blackGradientInstance);
            gradientGroup.alpha = 0f;
            Coroutine fade = StartCoroutine(AnimateAlpha(gradientGroup, 0f, 1f, blackGradientFadeInDuration, blackGradientFadeInEase));
            if (waitForGradientBeforeSpeech) yield return fade;
        }

        // 3. BossSpeech: spawn, then scale up from the bottom-right
        //    Draw order (bottom -> top): blackGradient, BossSpeech, BossBubbleChat
        bossSpeechInstance = Instantiate(bossSpeechPrefab, canvas.transform, false);
        bossSpeechInstance.transform.SetAsLastSibling();
        if (bossBubbleChatInstance != null) bossBubbleChatInstance.transform.SetAsLastSibling();
        bossSpeechInstance.SetActive(true);

        // BossSpeech pops up -> play BossBubbleChat's "BubbleChat" animation for a while
        if (bossBubbleChatInstance != null)
        {
            Animator chatAnimator = bossBubbleChatInstance.GetComponentInChildren<Animator>(true);
            if (chatAnimator != null) StartCoroutine(BubbleChatAnimRoutine(chatAnimator));
            else Debug.LogWarning("[BossManager] No Animator found on BossBubbleChat");
        }

        TMP_Text speechText = bossSpeechInstance.GetComponentInChildren<TMP_Text>(true);
        if (speechText != null && bossLines != null && bossLines.Length > 0)
            speechText.text = bossLines[Random.Range(0, bossLines.Length)];
        else if (speechText == null)
            Debug.LogWarning("[BossManager] No TMP_Text found in the speech bubble prefab");

        RectTransform speechRt = bossSpeechInstance.GetComponent<RectTransform>();
        if (speechRt != null)
        {
            speechRt.anchoredPosition += bossSpeechOffset;
            SetPivotKeepPosition(speechRt, bossSpeechScalePivot);
            Vector3 fullScale = speechRt.localScale;
            speechRt.localScale = fullScale * bossSpeechStartScale;

            float time = 0f;
            while (time < bossSpeechScaleDuration)
            {
                time += Time.deltaTime;
                float p = Mathf.Clamp01(time / bossSpeechScaleDuration);
                float e = bossSpeechScaleEase == UIEase.Back
                    ? EaseOutBack(p, bossSpeechOvershoot)
                    : Ease(bossSpeechScaleEase, p);
                speechRt.localScale = fullScale * Mathf.LerpUnclamped(bossSpeechStartScale, 1f, e);
                yield return null;
            }
            speechRt.localScale = fullScale;
        }

        // 4. Fully shown -> wait -> next stage
        yield return new WaitForSeconds(bubbleShowTime);

        // Longest hide animation, so hiding can start early and finish right when the next stage begins
        float longest = 0f;
        if (bossBubbleChatInstance != null) longest = Mathf.Max(longest, bubbleChatHideDuration);
        if (bossSpeechInstance != null) longest = Mathf.Max(longest, bossSpeechHideDuration);
        if (blackGradientInstance != null) longest = Mathf.Max(longest, blackGradientHideDuration);

        if (callNextStage)
        {
            float delay = CallNextButtonAndGetDelay();
            yield return new WaitForSeconds(Mathf.Max(0f, delay - longest));
        }

        // 5. Hide all three together (ends when nextStageDelay ends), then remove them
        if (bossBubbleChatInstance != null)
        {
            RectTransform rt = bossBubbleChatInstance.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector2 from = rt.anchoredPosition;
                StartCoroutine(AnimateAnchored(rt, from, from - new Vector2(0f, bubbleChatHideDistance), bubbleChatHideDuration, bubbleChatHideEase));
            }
        }

        if (bossSpeechInstance != null)
        {
            Transform t = bossSpeechInstance.transform;
            StartCoroutine(AnimateScale(t, t.localScale, Vector3.zero, bossSpeechHideDuration, bossSpeechHideEase));
        }

        if (blackGradientInstance != null)
        {
            CanvasGroup cg = GetOrAddCanvasGroup(blackGradientInstance);
            StartCoroutine(AnimateAlpha(cg, cg.alpha, 0f, blackGradientHideDuration, blackGradientHideEase));
        }

        yield return new WaitForSeconds(longest);

        DestroyBubble();
        bubbleRoutine = null;
    }

    IEnumerator BubbleChatAnimRoutine(Animator chatAnimator)
    {
        chatAnimator.SetBool(BubbleChatHash, true);
        yield return new WaitForSeconds(bubbleChatAnimDuration);
        if (chatAnimator != null) chatAnimator.SetBool(BubbleChatHash, false);   // skip if already destroyed
    }

    // ----- Small UI tween helpers (stop safely if the object is destroyed) -----

    IEnumerator AnimateAnchored(RectTransform rt, Vector2 from, Vector2 to, float duration, UIEase ease)
    {
        float time = 0f;
        while (time < duration)
        {
            if (rt == null) yield break;
            time += Time.deltaTime;
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, Ease(ease, Mathf.Clamp01(time / duration)));
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = to;
    }

    IEnumerator AnimateScale(Transform t, Vector3 from, Vector3 to, float duration, UIEase ease)
    {
        float time = 0f;
        while (time < duration)
        {
            if (t == null) yield break;
            time += Time.deltaTime;
            t.localScale = Vector3.LerpUnclamped(from, to, Ease(ease, Mathf.Clamp01(time / duration)));
            yield return null;
        }
        if (t != null) t.localScale = to;
    }

    IEnumerator AnimateAlpha(CanvasGroup cg, float from, float to, float duration, UIEase ease)
    {
        float time = 0f;
        while (time < duration)
        {
            if (cg == null) yield break;
            time += Time.deltaTime;
            cg.alpha = Mathf.LerpUnclamped(from, to, Ease(ease, Mathf.Clamp01(time / duration)));
            yield return null;
        }
        if (cg != null) cg.alpha = to;
    }

    static CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;   // decoration only, never blocks clicks
        return cg;
    }

    /// <summary>Changes a RectTransform's pivot without moving it on screen</summary>
    static void SetPivotKeepPosition(RectTransform rt, Vector2 newPivot)
    {
        Vector2 size = rt.rect.size;
        Vector2 delta = newPivot - rt.pivot;
        Vector3 scale = rt.localScale;
        rt.pivot = newPivot;
        rt.anchoredPosition += new Vector2(delta.x * size.x * scale.x, delta.y * size.y * scale.y);
    }

    static float Ease(UIEase ease, float p)
    {
        switch (ease)
        {
            case UIEase.EaseOut: return 1f - (1f - p) * (1f - p);
            case UIEase.EaseInOut: return p < 0.5f ? 2f * p * p : 1f - Mathf.Pow(-2f * p + 2f, 2f) / 2f;
            case UIEase.Back: return EaseOutBack(p);
            case UIEase.Elastic: return EaseOutElastic(p);
            case UIEase.Bounce: return EaseOutBounce(p);
            case UIEase.EaseIn: return p * p;
            case UIEase.BackIn: return EaseInBack(p);
            default: return p;
        }
    }

    static float EaseOutBounce(float p)
    {
        const float n1 = 7.5625f, d1 = 2.75f;
        if (p < 1f / d1) return n1 * p * p;
        if (p < 2f / d1) { p -= 1.5f / d1; return n1 * p * p + 0.75f; }
        if (p < 2.5f / d1) { p -= 2.25f / d1; return n1 * p * p + 0.9375f; }
        p -= 2.625f / d1; return n1 * p * p + 0.984375f;
    }

    Canvas GetCanvas()
    {
        if (targetCanvas != null) return targetCanvas;
#if UNITY_2023_1_OR_NEWER
        targetCanvas = FindFirstObjectByType<Canvas>();
#else
        targetCanvas = FindObjectOfType<Canvas>();
#endif
        if (targetCanvas != null) targetCanvas = targetCanvas.rootCanvas;
        return targetCanvas;
    }

    void DestroyBubble()
    {
        if (bossSpeechInstance != null)
        {
            Destroy(bossSpeechInstance);
            bossSpeechInstance = null;
        }
        if (bossBubbleChatInstance != null)
        {
            Destroy(bossBubbleChatInstance);
            bossBubbleChatInstance = null;
        }
        if (blackGradientInstance != null)
        {
            Destroy(blackGradientInstance);
            blackGradientInstance = null;
        }
    }

    // ============= Helpers =============

    IEnumerator MoveTo(Vector3 target)
    {
        FaceDirection(target.x - transform.position.x);
        while ((transform.position - target).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = target;
    }

    int RandomWalkHash()
    {
        bool talk = Random.value < walknSpeechChance;
        if (debugLog) Debug.Log("[BossManager] Move animation: " + (talk ? "WalknSpeech" : "Walk"));
        return talk ? WalknSpeechHash : WalkHash;
    }

    /// <summary>Sets every bool to false except the given one (pass 0 to turn all off)</summary>
    void SetAnim(int activeHash)
    {
        if (animator == null) return;
        foreach (int h in AllHashes)
            animator.SetBool(h, h == activeHash);

        // Chat bubble appears whenever WalknSpeech or Speech is turned on
        if (activeHash == WalknSpeechHash || activeHash == SpeechHash)
            ShowChatBubble();
    }

    static MoveSide ResolveSide(MoveSide side)
    {
        return side == MoveSide.Random ? RealSides[Random.Range(0, RealSides.Length)] : side;
    }

    static Vector3 SideToDir(MoveSide side)
    {
        switch (side)
        {
            case MoveSide.Left: return Vector3.left;
            default: return Vector3.right;
        }
    }

    /// <summary>Calls a no-argument method on the GameManager object</summary>
    void CallGameManager(string methodName)
    {
        if (gameManager == null)
        {
            Debug.LogWarning("[BossManager] gameManager is not assigned");
            return;
        }
        gameManager.SendMessage(methodName, SendMessageOptions.RequireReceiver);
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

        // Home point
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(home, 0.3f);
#if UNITY_EDITOR
        Handles.Label(home + Vector3.up * 0.5f, "Home");
#endif

        // In Play Mode show the sides actually picked; otherwise show every possible side
        MoveSide enterShow = Application.isPlaying && sidesResolved ? currentEnterSide : enterFrom;
        MoveSide exitShow = Application.isPlaying && sidesResolved ? currentExitSide : exitTo;

        foreach (var side in RealSides)
        {
            if (enterShow == side || enterShow == MoveSide.Random)
                DrawPath(home, side, enterDistance, Color.green, "Enter Start", 0f);
            if (exitShow == side || exitShow == MoveSide.Random)
                DrawPath(home, side, exitDistance, Color.red, "Exit End", 0.15f);
        }
    }

    void DrawPath(Vector3 home, MoveSide side, float distance, Color color, string label, float offset)
    {
        Vector3 dir = SideToDir(side);
        Vector3 perp = new Vector3(-dir.y, dir.x, 0f) * offset;   // small offset so enter/exit lines don't overlap
        Vector3 a = home + perp;
        Vector3 b = home + dir * distance + perp;

        Gizmos.color = color;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawWireSphere(b, 0.3f);
#if UNITY_EDITOR
        Handles.color = color;
        Handles.Label(b + Vector3.up * (0.5f + offset * 3f), label);
#endif
    }
}

// ===================== Inspector Test Button (Editor only) =====================
#if UNITY_EDITOR
[CustomEditor(typeof(BossManager))]
public class BossManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play Mode to use the test button.", MessageType.Info);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Test BossAct()", GUILayout.Height(30)))
                ((BossManager)target).BossAct();
            if (GUILayout.Button("Test BossBubbleChat()  (calls OnNextButtonClicked)", GUILayout.Height(30)))
                ((BossManager)target).BossBubbleChat();
            if (GUILayout.Button("Preview Bubble Animations  (no GameManager call)", GUILayout.Height(30)))
                ((BossManager)target).PreviewBossBubbleChat();
            if (GUILayout.Button("Test ShowChatBubble()", GUILayout.Height(30)))
                ((BossManager)target).ShowChatBubble();
        }
    }
}
#endif
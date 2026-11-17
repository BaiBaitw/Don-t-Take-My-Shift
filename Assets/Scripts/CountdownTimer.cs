using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Countdown bar made of three stacked UI Images (Normal / Yellow / Red).
/// - The bar shrinks from the right as time counts down
/// - When the time left drops to Yellow Time, the yellow bar is shown
/// - When the time left drops to Red Time, the red bar is shown
/// Only one of the three bars is visible at a time; all three always share the same length.
/// The countdown never starts by itself: call StartTimer() (or RestartTimer()) to begin.
/// Requires DOTween (free on the Asset Store). No extra DOTween modules are needed.
/// </summary>
public class CountdownBar : MonoBehaviour
{
    // ===== Options (shown as dropdowns in the Inspector) =====

    public enum BarState { Normal, Yellow, Red }
    public enum ShrinkMode { Fill, Width }
    public enum IntroEffect { None, FillUp, FadeIn, ScaleIn }
    public enum SwitchEffect { None, Punch, PopIn, Flash, Shake }
    public enum LoopEffect { None, Pulse, Blink, Shake, Bounce }

    [Header("Bars (UI Images, placed on top of each other)")]
    [Tooltip("Bar shown while there is plenty of time left")]
    public Image normalBar;
    [Tooltip("Bar shown when time left <= Yellow Time")]
    public Image yellowBar;
    [Tooltip("Bar shown when time left <= Red Time")]
    public Image redBar;

    [Header("Game References")]
    [Tooltip("Object that has the GameManager script on it")]
    public GameObject gameManagerObject;

    [Header("Time Settings")]
    [Tooltip("Total countdown time (seconds)")]
    public float totalTime = 30f;
    [Tooltip("Show the yellow bar when this many seconds (or fewer) are left")]
    public float yellowTime = 10f;
    [Tooltip("Show the red bar when this many seconds (or fewer) are left")]
    public float redTime = 5f;
    [Tooltip("Ignore Time.timeScale (keeps counting and animating while the game is paused with timeScale = 0)")]
    public bool useUnscaledTime = false;

    [Header("Bar Settings")]
    [Tooltip("Fill: crops the image from the right (image must have a Source Image). " +
             "Width: shrinks the RectTransform width (good for 9-sliced images)")]
    public ShrinkMode shrinkMode = ShrinkMode.Fill;
    [Tooltip("How fast the bar slides to its new length after AddTime (full bar lengths per second). 0 = jump instantly")]
    public float catchUpSpeed = 1.5f;

    [Header("Effect: Intro (on start / reset)")]
    public IntroEffect introEffect = IntroEffect.FillUp;
    public float introDuration = 0.6f;

    [Header("Effect: Switching Colour")]
    [Tooltip("Played once when the bar turns yellow")]
    public SwitchEffect yellowSwitchEffect = SwitchEffect.Punch;
    [Tooltip("Played once when the bar turns red")]
    public SwitchEffect redSwitchEffect = SwitchEffect.Flash;
    public float switchDuration = 0.4f;

    [Header("Effect: Looping")]
    [Tooltip("Keeps playing while the bar is yellow")]
    public LoopEffect yellowLoopEffect = LoopEffect.None;
    [Tooltip("Keeps playing while the bar is red")]
    public LoopEffect redLoopEffect = LoopEffect.Pulse;
    [Tooltip("Time for one full loop cycle")]
    public float loopDuration = 0.6f;
    [Tooltip("Pulse: height multiplier at the peak")]
    public float pulseScale = 1.2f;
    [Tooltip("Blink: lowest alpha")]
    [Range(0f, 1f)] public float blinkMinAlpha = 0.3f;

    [Header("Effect Strength (pixels)")]
    [Tooltip("Distance used by Bounce")]
    public float moveDistance = 8f;
    [Tooltip("Strength used by Shake effects")]
    public float shakeStrength = 6f;
    [Tooltip("Strength used by Punch (scale amount)")]
    public float punchStrength = 0.2f;

    [Header("Events")]
    [Tooltip("Invoked when the bar turns yellow")]
    public UnityEvent onEnterYellow;
    [Tooltip("Invoked when the bar turns red")]
    public UnityEvent onEnterRed;
    [Tooltip("Invoked when time runs out")]
    public UnityEvent onTimeUp;

    // ===== Runtime state =====

    private readonly BarVisual[] barsByState = new BarVisual[3];
    private readonly List<BarVisual> uniqueBars = new List<BarVisual>();
    private readonly object fillTweenId = new object();

    private float timeLeft;
    private float displayFill = 1f;
    private bool running;
    private BarState state;
    private GameManager gameManager;
    private bool stoppedByGameManager;
    private bool initialized;
    private bool valid;

    public float TimeLeft => timeLeft;
    public bool IsRunning => running;
    public BarState State => state;

    /// <summary>True if the countdown was paused because GameManager.IsFinished became true</summary>
    public bool StoppedByGameManager => stoppedByGameManager;

    float TargetFill => totalTime > 0f ? Mathf.Clamp01(timeLeft / totalTime) : 0f;

    void Start()
    {
        // Only sets up the bar and shows it full. The countdown waits until StartTimer() is called
        Initialize();
    }

    /// <summary>Sets up the bars once. Safe to call many times (also called automatically by StartTimer / ResetTimer)</summary>
    void Initialize()
    {
        if (initialized) return;
        initialized = true;

        if (gameManagerObject != null)
        {
            gameManager = gameManagerObject.GetComponent<GameManager>();
            if (gameManager == null)
                Debug.LogError("[CountdownBar] The Game Manager Object has no GameManager script on it!");
        }

        if (normalBar == null)
        {
            Debug.LogError("[CountdownBar] Please assign at least the Normal Bar!");
            return;
        }

        // A missing yellow / red bar falls back to the previous colour
        BarVisual normal = AddBar(normalBar);
        BarVisual yellow = yellowBar != null ? AddBar(yellowBar) : normal;
        BarVisual red = redBar != null ? AddBar(redBar) : yellow;

        barsByState[(int)BarState.Normal] = normal;
        barsByState[(int)BarState.Yellow] = yellow;
        barsByState[(int)BarState.Red] = red;

        valid = true;
        ResetTimer();
    }

    BarVisual AddBar(Image image)
    {
        foreach (var b in uniqueBars)
            if (b.image == image) return b;

        var bar = new BarVisual(image, shrinkMode);
        uniqueBars.Add(bar);
        return bar;
    }

    void Update()
    {
        if (!valid) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        bool timeUpNow = false;

        if (running)
        {
            // Stop counting as soon as the GameManager says the game is finished
            if (gameManager != null && gameManager.IsFinished)
            {
                stoppedByGameManager = true;
                running = false;
            }
            else
            {
                timeLeft -= dt;
                if (timeLeft <= 0f)
                {
                    timeLeft = 0f;
                    running = false;
                    timeUpNow = true;
                }
            }
        }

        UpdateState(true);
        UpdateDisplay(dt);

        if (timeUpNow)
        {
            onTimeUp?.Invoke();
            if (gameManager != null) gameManager.GameOver();
        }
    }

    void OnDestroy()
    {
        DOTween.Kill(fillTweenId);
        foreach (var b in uniqueBars) b.Kill();
    }

    // ===== Public control methods (call these from other scripts or a UI Button's OnClick) =====

    /// <summary>Start (or resume) the countdown. Does nothing if time is up or GameManager.IsFinished is true</summary>
    public void StartTimer()
    {
        Initialize();
        if (!valid || timeLeft <= 0f) return;
        if (gameManager != null && gameManager.IsFinished) return;
        stoppedByGameManager = false;
        running = true;
    }

    /// <summary>Pause the countdown. Call StartTimer() to continue</summary>
    public void PauseTimer() => running = false;

    /// <summary>Stop the countdown, refill the bar to full time and play the intro effect. Does not start it</summary>
    public void ResetTimer()
    {
        Initialize();
        if (!valid) return;

        running = false;
        stoppedByGameManager = false;
        timeLeft = totalTime;

        DOTween.Kill(fillTweenId);
        SetState(ComputeState(), false);
        displayFill = 1f;
        ApplyFill();
        PlayIntro();
    }

    /// <summary>Reset the bar to full time and start counting right away</summary>
    public void RestartTimer()
    {
        ResetTimer();
        StartTimer();
    }

    /// <summary>Add or subtract time (e.g. time bonus pickups, time penalties). The bar slides to its new length</summary>
    public void AddTime(float seconds)
    {
        timeLeft = Mathf.Clamp(timeLeft + seconds, 0f, totalTime);
        if (valid) UpdateState(true);
    }

    // ===== Core logic =====

    BarState ComputeState()
    {
        if (timeLeft <= redTime) return BarState.Red;
        if (timeLeft <= yellowTime) return BarState.Yellow;
        return BarState.Normal;
    }

    void UpdateState(bool animate)
    {
        BarState newState = ComputeState();
        if (newState == state) return;

        SetState(newState, animate);

        if (newState == BarState.Yellow) onEnterYellow?.Invoke();
        else if (newState == BarState.Red) onEnterRed?.Invoke();
    }

    /// <summary>Show only the bar for this state, then play its switch effect and loop</summary>
    void SetState(BarState newState, bool animate)
    {
        BarVisual previous = barsByState[(int)state];
        state = newState;
        BarVisual active = barsByState[(int)newState];

        foreach (var b in uniqueBars)
        {
            b.Kill();
            b.ResetVisual();
            b.go.SetActive(b == active);
        }
        ApplyFill();

        // Skip the switch effect if the "new" bar is the same object (e.g. no red bar assigned)
        if (animate && active != previous) PlaySwitch(active, newState);
        else StartLoop(active, newState);
    }

    void UpdateDisplay(float dt)
    {
        // While the intro fill-up is playing, it controls the length
        if (DOTween.IsTweening(fillTweenId)) return;

        float target = TargetFill;
        displayFill = catchUpSpeed > 0f
            ? Mathf.MoveTowards(displayFill, target, catchUpSpeed * dt)
            : target;
        ApplyFill();
    }

    void ApplyFill()
    {
        foreach (var b in uniqueBars) b.SetFill(displayFill, shrinkMode);
    }

    // ===== Effects =====

    void PlayIntro()
    {
        BarVisual v = barsByState[(int)state];
        BarState s = state;

        switch (introEffect)
        {
            case IntroEffect.FillUp:
                displayFill = 0f;
                ApplyFill();
                DOTween.To(() => displayFill, x => { displayFill = x; ApplyFill(); }, 1f, introDuration)
                    .SetEase(Ease.OutCubic)
                    .SetId(fillTweenId)
                    .SetUpdate(useUnscaledTime);
                break;

            case IntroEffect.FadeIn:
                {
                    v.Kill();
                    v.ResetVisual();
                    v.alpha.Set(0f);
                    Sequence seq = NewSequence(v.go);
                    seq.Append(FadeTo(v, 1f, introDuration));
                    seq.OnComplete(() => StartLoop(v, s));
                    break;
                }

            case IntroEffect.ScaleIn:
                {
                    v.Kill();
                    v.ResetVisual();
                    v.rt.localScale = new Vector3(v.scale.x, 0f, v.scale.z);
                    Sequence seq = NewSequence(v.go);
                    seq.Append(v.rt.DOScaleY(v.scale.y, introDuration).SetEase(Ease.OutBack));
                    seq.OnComplete(() => StartLoop(v, s));
                    break;
                }
        }
    }

    void PlaySwitch(BarVisual v, BarState s)
    {
        SwitchEffect effect = s == BarState.Yellow ? yellowSwitchEffect
                            : s == BarState.Red ? redSwitchEffect
                            : SwitchEffect.None;

        if (effect == SwitchEffect.None)
        {
            StartLoop(v, s);
            return;
        }

        float d = switchDuration;
        Sequence seq = NewSequence(v.go);

        switch (effect)
        {
            case SwitchEffect.Punch:
                seq.Append(v.rt.DOPunchScale(Vector3.one * punchStrength, d, 8, 0.5f));
                break;

            case SwitchEffect.PopIn:
                v.rt.localScale = new Vector3(v.scale.x, 0f, v.scale.z);
                seq.Append(v.rt.DOScaleY(v.scale.y, d).SetEase(Ease.OutBack));
                break;

            case SwitchEffect.Flash:
                float step = d / 6f;
                for (int i = 0; i < 3; i++)
                {
                    seq.Append(FadeTo(v, 0f, step));
                    seq.Append(FadeTo(v, 1f, step));
                }
                break;

            case SwitchEffect.Shake:
                seq.Append(v.rt.DOShakePosition(d, shakeStrength, 25));
                break;
        }

        seq.OnComplete(() =>
        {
            v.ResetVisual();
            StartLoop(v, s);
        });
    }

    void StartLoop(BarVisual v, BarState s)
    {
        if (s != state || !v.go.activeInHierarchy) return;

        LoopEffect effect = s == BarState.Yellow ? yellowLoopEffect
                          : s == BarState.Red ? redLoopEffect
                          : LoopEffect.None;

        float half = loopDuration * 0.5f;
        Tween t = null;

        switch (effect)
        {
            case LoopEffect.Pulse:
                t = v.rt.DOScaleY(v.scale.y * pulseScale, half).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
                break;

            case LoopEffect.Blink:
                t = FadeTo(v, blinkMinAlpha, half).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
                break;

            case LoopEffect.Shake:
                t = v.rt.DOShakePosition(loopDuration, shakeStrength, 15, 90f, false, false).SetLoops(-1, LoopType.Restart);
                break;

            case LoopEffect.Bounce:
                t = v.rt.DOLocalMoveY(v.pos.y + moveDistance, half).SetEase(Ease.OutQuad).SetLoops(-1, LoopType.Yoyo);
                break;
        }

        if (t != null) t.SetId(v.go).SetUpdate(useUnscaledTime);
    }

    // ===== Helpers =====

    Sequence NewSequence(GameObject id)
    {
        return DOTween.Sequence().SetId(id).SetUpdate(useUnscaledTime);
    }

    Tween FadeTo(BarVisual v, float target, float duration)
    {
        return DOTween.To(() => v.alpha.Factor, x => v.alpha.Set(x), target, duration);
    }

    /// <summary>Moves the pivot without moving the rectangle on screen</summary>
    static void SetPivotKeepRect(RectTransform rt, Vector2 pivot)
    {
        Vector2 size = rt.rect.size;
        Vector2 delta = pivot - rt.pivot;
        rt.pivot = pivot;
        rt.anchoredPosition += new Vector2(delta.x * size.x * rt.localScale.x, delta.y * size.y * rt.localScale.y);
    }

    /// <summary>One bar image plus its original transform values, so effects can always restore it</summary>
    class BarVisual
    {
        public readonly Image image;
        public readonly GameObject go;
        public readonly RectTransform rt;
        public readonly Vector3 pos;
        public readonly Vector3 scale;
        public readonly Quaternion rot;
        public readonly float width;
        public readonly AlphaGroup alpha;

        public BarVisual(Image image, ShrinkMode mode)
        {
            this.image = image;
            go = image.gameObject;
            rt = image.rectTransform;

            if (mode == ShrinkMode.Fill)
            {
                if (image.sprite == null)
                    Debug.LogWarning($"[CountdownBar] '{go.name}' has no Source Image. Fill mode needs a sprite; use Width mode instead.");
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left; // keeps the left side, shrinks from the right
            }
            else
            {
                // Pivot on the left edge so changing the width shrinks it from the right
                SetPivotKeepRect(rt, new Vector2(0f, rt.pivot.y));
                width = rt.rect.width;
            }

            pos = rt.localPosition;
            scale = rt.localScale;
            rot = rt.localRotation;
            alpha = new AlphaGroup(go);
        }

        public void SetFill(float fill, ShrinkMode mode)
        {
            fill = Mathf.Clamp01(fill);
            if (mode == ShrinkMode.Fill) image.fillAmount = fill;
            else rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width * fill);
        }

        public void Kill() => DOTween.Kill(go);

        public void ResetVisual()
        {
            rt.localPosition = pos;
            rt.localScale = scale;
            rt.localRotation = rot;
            alpha.Set(1f);
        }
    }

    /// <summary>Fades a bar and all its children (CanvasGroup if present, otherwise every UI Graphic)</summary>
    class AlphaGroup
    {
        private readonly CanvasGroup canvasGroup;
        private readonly float canvasGroupAlpha;
        private readonly Graphic[] graphics;
        private readonly float[] graphicAlphas;

        public float Factor { get; private set; } = 1f;

        public AlphaGroup(GameObject go)
        {
            canvasGroup = go.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroupAlpha = canvasGroup.alpha;
                return;
            }

            graphics = go.GetComponentsInChildren<Graphic>(true);
            graphicAlphas = new float[graphics.Length];
            for (int i = 0; i < graphics.Length; i++) graphicAlphas[i] = graphics[i].color.a;
        }

        public void Set(float factor)
        {
            Factor = factor;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = canvasGroupAlpha * factor;
                return;
            }

            for (int i = 0; i < graphics.Length; i++)
            {
                Color c = graphics[i].color;
                c.a = graphicAlphas[i] * factor;
                graphics[i].color = c;
            }
        }
    }
}
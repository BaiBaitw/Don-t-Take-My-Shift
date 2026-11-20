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

    /// <summary>Penalty effects can be combined (pick several in the Inspector dropdown)</summary>
    [System.Flags]
    public enum PenaltyEffect
    {
        None = 0,
        Shake = 1 << 0,      // Shake the bar (or the Penalty Shake Target)
        Punch = 1 << 1,      // Quick scale punch
        Blink = 1 << 2,      // Blinks twice
        TintFlash = 1 << 3,  // Bar briefly flashes the Penalty Tint Color
        Trail = 1 << 4,      // The lost section stays visible for a moment, then shrinks away
    }

    /// <summary>Bonus effects for AddBonusTime(); can be combined</summary>
    [System.Flags]
    public enum BonusEffect
    {
        None = 0,
        TintFlash = 1 << 0,    // Bar briefly flashes the Bonus Tint Color
        Punch = 1 << 1,        // Quick scale punch
        GainSection = 1 << 2,  // The gained section appears in Gain Color first, then the bar grows over it
        Blink = 1 << 3,        // Blinks twice
    }

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

    [Header("Penalty (call ApplyPenalty() when the player makes a mistake)")]
    [Tooltip("Seconds removed each time ApplyPenalty() is called")]
    public float penaltySeconds = 3f;
    [Tooltip("Visual effects played on a penalty. You can tick several")]
    public PenaltyEffect penaltyEffects = PenaltyEffect.Shake | PenaltyEffect.TintFlash | PenaltyEffect.Trail;
    [Tooltip("Bar drops to its new length instantly. Off = it slides down at Catch Up Speed")]
    public bool penaltyInstantDrop = true;
    [Tooltip("Duration of the Shake / Punch / Blink / TintFlash effects")]
    public float penaltyEffectDuration = 0.35f;
    [Tooltip("Object shaken by the Shake effect, e.g. the parent of the bars. Empty = only the bar shakes")]
    public RectTransform penaltyShakeTarget;
    [Tooltip("Colour the bar flashes with TintFlash")]
    public Color penaltyTintColor = Color.white;
    [Tooltip("Colour of the lost section shown by Trail")]
    public Color trailColor = new Color(1f, 0.25f, 0.25f, 0.85f);
    [Tooltip("Trail: how long the lost section stays before shrinking")]
    public float trailDelay = 0.3f;
    [Tooltip("Trail: how long the lost section takes to shrink away")]
    public float trailShrinkDuration = 0.4f;

    [Header("Bonus (call AddBonusTime() to add time with effects)")]
    [Tooltip("Visual effects played when time is added with AddBonusTime(). You can tick several")]
    public BonusEffect bonusEffects = BonusEffect.TintFlash | BonusEffect.Punch | BonusEffect.GainSection;
    [Tooltip("Colour the bar flashes with TintFlash")]
    public Color bonusTintColor = new Color(0.35f, 1f, 0.45f, 1f);
    [Tooltip("Colour of the gained section shown by GainSection")]
    public Color gainColor = new Color(0.35f, 1f, 0.45f, 0.9f);
    [Tooltip("Duration of the TintFlash / Punch / Blink bonus effects")]
    public float bonusEffectDuration = 0.4f;
    [Tooltip("GainSection: how long the gained section is shown before the bar grows")]
    public float gainHoldTime = 0.15f;
    [Tooltip("GainSection: how long the bar takes to grow over the gained section")]
    public float gainFillDuration = 0.4f;

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
    [Tooltip("Invoked every time a penalty is applied (e.g. play a sound)")]
    public UnityEvent onPenalty;
    [Tooltip("Invoked when time runs out")]
    public UnityEvent onTimeUp;
    [Tooltip("Invoked every time AddBonusTime() adds time (e.g. play a sound)")]
    public UnityEvent onBonusTime;

    // ===== Runtime state =====

    private readonly BarVisual[] barsByState = new BarVisual[3];
    private readonly List<BarVisual> uniqueBars = new List<BarVisual>();
    private readonly object fillTweenId = new object();
    private readonly object trailTweenId = new object();
    private readonly object tintTweenId = new object();
    private readonly object shakeTweenId = new object();

    private Image gainImage;
    private RectTransform gainRt;
    private float gainWidth;

    private Image trailImage;
    private RectTransform trailRt;
    private float trailWidth;
    private float trailFill;
    private Vector3 shakeTargetBasePos;

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

        CreateTrail();
        if (penaltyShakeTarget != null) shakeTargetBasePos = penaltyShakeTarget.localPosition;

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

        if (timeUpNow) TriggerTimeUp();
    }

    void TriggerTimeUp()
    {
        onTimeUp?.Invoke();
        if (gameManager != null) gameManager.GameOver();
    }

    void OnDestroy()
    {
        DOTween.Kill(fillTweenId);
        DOTween.Kill(trailTweenId);
        DOTween.Kill(tintTweenId);
        DOTween.Kill(shakeTweenId);
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
        StopPenaltyEffects();
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

    /// <summary>Add time and play the bonus effects (e.g. Fever reward)</summary>
    public void AddBonusTime(float seconds)
    {
        Initialize();
        if (!valid || seconds <= 0f || timeLeft <= 0f) return;

        float oldFill = displayFill;
        timeLeft = Mathf.Clamp(timeLeft + seconds, 0f, totalTime);

        DOTween.Kill(fillTweenId); // stop the intro / previous gain fill if still playing
        UpdateState(true);
        PlayBonusEffects(oldFill);
        onBonusTime?.Invoke();
    }

    /// <summary>Remove Penalty Seconds from the bar and play the penalty effects. Call this when the player makes a mistake</summary>
    public void ApplyPenalty() => ApplyPenalty(penaltySeconds);

    /// <summary>Remove a custom number of seconds from the bar and play the penalty effects</summary>
    public void ApplyPenalty(float seconds)
    {
        Initialize();
        if (!valid || seconds <= 0f || timeLeft <= 0f) return;
        if (gameManager != null && gameManager.IsFinished) return;

        float oldFill = displayFill;
        timeLeft = Mathf.Max(0f, timeLeft - seconds);

        DOTween.Kill(fillTweenId); // stop the intro fill-up if it is still playing
        if (penaltyInstantDrop)
        {
            displayFill = TargetFill;
            ApplyFill();
        }

        UpdateState(true);
        PlayPenaltyEffects(oldFill);
        onPenalty?.Invoke();

        // The penalty itself can end the game
        if (timeLeft <= 0f && running)
        {
            running = false;
            TriggerTimeUp();
        }
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

    // ===== Penalty effects =====

    bool HasPenalty(PenaltyEffect e) => (penaltyEffects & e) != 0;

    void PlayPenaltyEffects(float oldFill)
    {
        BarVisual v = barsByState[(int)state];
        BarState s = state;
        float d = penaltyEffectDuration;

        // Effects on the bar itself (these replace the colour-switch effect if the penalty also changed the colour)
        bool shakeBar = HasPenalty(PenaltyEffect.Shake) && penaltyShakeTarget == null;
        bool punch = HasPenalty(PenaltyEffect.Punch);
        bool blink = HasPenalty(PenaltyEffect.Blink);

        if (shakeBar || punch || blink)
        {
            v.Kill();
            v.ResetVisual();

            Sequence seq = NewSequence(v.go);
            if (shakeBar) seq.Insert(0f, v.rt.DOShakePosition(d, shakeStrength, 25));
            if (punch) seq.Insert(0f, v.rt.DOPunchScale(Vector3.one * punchStrength, d, 8, 0.5f));
            if (blink)
            {
                float step = d / 4f;
                seq.Insert(0f, FadeTo(v, 0.2f, step));
                seq.Insert(step, FadeTo(v, 1f, step));
                seq.Insert(step * 2f, FadeTo(v, 0.2f, step));
                seq.Insert(step * 3f, FadeTo(v, 1f, step));
            }
            seq.OnComplete(() =>
            {
                v.ResetVisual();
                StartLoop(v, s);
            });
        }

        // Shake a separate target (e.g. the whole bar frame)
        if (HasPenalty(PenaltyEffect.Shake) && penaltyShakeTarget != null)
        {
            DOTween.Kill(shakeTweenId);
            penaltyShakeTarget.localPosition = shakeTargetBasePos;
            penaltyShakeTarget.DOShakePosition(d, shakeStrength, 25)
                .SetId(shakeTweenId)
                .SetUpdate(useUnscaledTime)
                .OnComplete(() => penaltyShakeTarget.localPosition = shakeTargetBasePos);
        }

        // Flash the bar's colour, keeping its alpha
        if (HasPenalty(PenaltyEffect.TintFlash)) FlashTint(v, penaltyTintColor, d);

        // Show the lost section behind the bar, then shrink it away
        if (HasPenalty(PenaltyEffect.Trail) && trailImage != null)
        {
            if (gainImage != null) { DOTween.Kill(gainImage); gainImage.gameObject.SetActive(false); }
            float start = trailImage.gameObject.activeSelf ? Mathf.Max(trailFill, oldFill) : oldFill;
            DOTween.Kill(trailTweenId);

            trailFill = start;
            SetTrailFill(start);
            trailImage.gameObject.SetActive(true);

            DOTween.Sequence()
                .SetId(trailTweenId)
                .SetUpdate(useUnscaledTime)
                .AppendInterval(trailDelay)
                .Append(DOTween.To(() => trailFill, x => { trailFill = x; SetTrailFill(x); }, TargetFill, trailShrinkDuration)
                    .SetEase(Ease.InQuad))
                .OnComplete(() => trailImage.gameObject.SetActive(false));
        }
    }

    void StopPenaltyEffects()
    {
        DOTween.Kill(trailTweenId);
        DOTween.Kill(tintTweenId);
        DOTween.Kill(shakeTweenId);
        if (trailImage != null) trailImage.gameObject.SetActive(false);
        if (gainImage != null) gainImage.gameObject.SetActive(false);
        if (penaltyShakeTarget != null) penaltyShakeTarget.localPosition = shakeTargetBasePos;
    }

    // ===== Bonus effects =====

    bool HasBonus(BonusEffect e) => (bonusEffects & e) != 0;

    void PlayBonusEffects(float oldFill)
    {
        BarVisual v = barsByState[(int)state];
        BarState s = state;
        float d = bonusEffectDuration;

        bool punch = HasBonus(BonusEffect.Punch);
        bool blink = HasBonus(BonusEffect.Blink);
        if (punch || blink)
        {
            v.Kill();
            v.ResetVisual();

            Sequence seq = NewSequence(v.go);
            if (punch) seq.Insert(0f, v.rt.DOPunchScale(Vector3.one * punchStrength, d, 8, 0.5f));
            if (blink)
            {
                float step = d / 4f;
                seq.Insert(0f, FadeTo(v, 0.4f, step));
                seq.Insert(step, FadeTo(v, 1f, step));
                seq.Insert(step * 2f, FadeTo(v, 0.4f, step));
                seq.Insert(step * 3f, FadeTo(v, 1f, step));
            }
            seq.OnComplete(() =>
            {
                v.ResetVisual();
                StartLoop(v, s);
            });
        }

        if (HasBonus(BonusEffect.TintFlash)) FlashTint(v, bonusTintColor, d);

        // Show the gained section in Gain Color behind the bar, then grow the bar over it
        float target = TargetFill;
        if (HasBonus(BonusEffect.GainSection) && gainImage != null && target > oldFill + 0.001f)
        {
            // A penalty trail would sit on top of the gain section; hide it
            DOTween.Kill(trailTweenId);
            if (trailImage != null) trailImage.gameObject.SetActive(false);

            DOTween.Kill(gainImage);
            gainImage.color = gainColor;
            SetBackFill(gainImage, gainRt, gainWidth, target);
            gainImage.gameObject.SetActive(true);

            displayFill = oldFill;
            ApplyFill();

            // Uses fillTweenId so UpdateDisplay() leaves the length alone while it plays.
            // OnKill also runs if a penalty / reset interrupts it, so the gain image never stays stuck.
            DOTween.Sequence()
                .SetId(fillTweenId)
                .SetUpdate(useUnscaledTime)
                .AppendInterval(gainHoldTime)
                .Append(DOTween.To(() => displayFill, x => { displayFill = x; ApplyFill(); }, target, gainFillDuration)
                    .SetEase(Ease.OutCubic))
                .OnKill(() => { if (gainImage != null) gainImage.gameObject.SetActive(false); });
        }
    }

    /// <summary>Flashes the bar towards a colour and back, keeping its alpha</summary>
    void FlashTint(BarVisual v, Color tint, float d)
    {
        DOTween.Kill(tintTweenId);
        Image img = v.image;
        Color baseColor = v.baseColor;
        float t = 0f;
        DOTween.To(() => t, x =>
        {
            t = x;
            Color c = Color.Lerp(baseColor, tint, x);
            c.a = img.color.a;
            img.color = c;
        }, 1f, d * 0.5f)
            .SetEase(Ease.OutQuad)
            .SetLoops(2, LoopType.Yoyo)
            .SetId(tintTweenId)
            .SetUpdate(useUnscaledTime);
    }

    /// <summary>Creates the trail image used by the Trail penalty effect: a copy of the normal bar's shape, placed behind the bars</summary>
    void CreateTrail()
    {
        trailImage = CreateBackImage("PenaltyTrail", trailColor, out trailRt, out trailWidth);
        gainImage = CreateBackImage("BonusGain", gainColor, out gainRt, out gainWidth);
    }

    /// <summary>Creates a copy of the normal bar's shape, placed behind the bars (used by Trail and GainSection)</summary>
    Image CreateBackImage(string objName, Color color, out RectTransform rt, out float width)
    {
        RectTransform src = normalBar.rectTransform;

        var go = new GameObject(objName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rt = go.GetComponent<RectTransform>();
        rt.SetParent(src.parent, false);
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition;
        rt.sizeDelta = src.sizeDelta;
        rt.localScale = src.localScale;
        rt.localRotation = src.localRotation;
        width = src.rect.width;

        Image img = go.GetComponent<Image>();
        img.sprite = normalBar.sprite;
        img.type = normalBar.type;
        img.fillMethod = normalBar.fillMethod;
        img.fillOrigin = normalBar.fillOrigin;
        img.pixelsPerUnitMultiplier = normalBar.pixelsPerUnitMultiplier;
        img.color = color;
        img.raycastTarget = false;

        // Put it behind every bar that shares the same parent
        int index = src.GetSiblingIndex();
        foreach (var b in uniqueBars)
            if (b.rt.parent == src.parent) index = Mathf.Min(index, b.rt.GetSiblingIndex());
        rt.SetSiblingIndex(index);

        go.SetActive(false);
        return img;
    }

    void SetTrailFill(float fill) => SetBackFill(trailImage, trailRt, trailWidth, fill);

    void SetBackFill(Image img, RectTransform rt, float width, float fill)
    {
        fill = Mathf.Clamp01(fill);
        if (shrinkMode == ShrinkMode.Fill) img.fillAmount = fill;
        else rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width * fill);
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
        public readonly Color baseColor;
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
            baseColor = image.color;
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
            image.color = baseColor;
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
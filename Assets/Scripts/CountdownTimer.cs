using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Block-based countdown bar with selectable DOTween effects.
/// - Duplicates the "normal block" to the right to form a time bar
/// - As time counts down, blocks disappear one by one starting from the rightmost
/// - When the remaining block count drops to or below the warning count,
///   all remaining blocks are swapped to the red "warning block"
/// Works with world-space objects (SpriteRenderer) and UI (Image / any Graphic, or a CanvasGroup).
/// Requires DOTween (free on the Asset Store). No extra DOTween modules are needed.
/// </summary>
public class CountdownBar : MonoBehaviour
{
    // ===== Effect options (shown as dropdowns in the Inspector) =====

    public enum IntroEffect { None, ScaleIn, FadeIn, DropIn }
    public enum DisappearEffect { None, Shrink, Fade, ShrinkAndFade, FallDown, FlyUp, PopAndVanish, SpinOut, ShakeThenVanish }
    public enum BlockLostReaction { None, ShakeBar, PunchLastBlock }
    public enum WarningSwitchEffect { None, Punch, PopIn, FlashIn }
    public enum WarningLoopEffect { None, Pulse, Blink, Shake, Bounce }

    [Header("Block Prefabs / Objects")]
    [Tooltip("Block shown in the normal state")]
    public GameObject normalBlock;
    [Tooltip("Red block shown when time is almost up")]
    public GameObject warningBlock;

    [Header("Time Settings")]
    [Tooltip("Total countdown time (seconds)")]
    public float totalTime = 30f;
    [Tooltip("Total number of blocks in the bar")]
    public int blockCount = 10;
    [Tooltip("Switch to red blocks when this many blocks (or fewer) remain")]
    public int warningBlockCount = 3;
    [Tooltip("Ignore Time.timeScale (keeps counting and animating while the game is paused with timeScale = 0)")]
    public bool useUnscaledTime = false;

    [Header("Layout Settings")]
    [Tooltip("Distance between blocks (center to center). World units for scene objects, pixels for UI")]
    public float spacing = 1.1f;
    [Tooltip("If checked, spacing is calculated from the block size automatically (spacing becomes the extra gap)")]
    public bool autoSpacing = false;

    [Header("Effect: Intro (on start / reset)")]
    public IntroEffect introEffect = IntroEffect.ScaleIn;
    public float introDuration = 0.35f;
    [Tooltip("Delay between each block's intro (creates a wave)")]
    public float introStagger = 0.05f;

    [Header("Effect: Block Disappear")]
    public DisappearEffect disappearEffect = DisappearEffect.PopAndVanish;
    public float disappearDuration = 0.35f;
    public Ease disappearEase = Ease.InBack;

    [Header("Effect: Reaction When a Block Is Lost")]
    public BlockLostReaction blockLostReaction = BlockLostReaction.PunchLastBlock;
    public float reactionDuration = 0.25f;

    [Header("Effect: Switching to Red")]
    public WarningSwitchEffect warningSwitchEffect = WarningSwitchEffect.Punch;
    public float switchDuration = 0.4f;
    [Tooltip("Delay between each block's switch (creates a wave). 0 = all at once")]
    public float switchStagger = 0.06f;

    [Header("Effect: Looping While Red")]
    public WarningLoopEffect warningLoopEffect = WarningLoopEffect.Pulse;
    [Tooltip("Time for one full loop cycle")]
    public float loopDuration = 0.6f;
    [Tooltip("Pulse: scale multiplier at the peak")]
    public float pulseScale = 1.15f;
    [Tooltip("Blink: lowest alpha")]
    [Range(0f, 1f)] public float blinkMinAlpha = 0.3f;

    [Header("Effect Strength")]
    [Tooltip("Distance used by FallDown / FlyUp / DropIn / Bounce. World units for scene objects, pixels for UI")]
    public float moveDistance = 1f;
    [Tooltip("Strength used by shake effects. World units for scene objects, pixels for UI")]
    public float shakeStrength = 0.1f;
    [Tooltip("Strength used by punch effects (scale amount)")]
    public float punchStrength = 0.3f;

    [Header("Events")]
    [Tooltip("Start the countdown automatically when the game starts")]
    public bool startOnAwake = true;
    [Tooltip("Invoked when time runs out")]
    public UnityEvent onTimeUp;

    // ===== Runtime state =====

    private readonly List<Slot> slots = new List<Slot>();
    private float timeLeft;
    private bool running;
    private bool inWarning;
    private int lastVisibleCount = -1;
    private Vector3 barBasePos;

    public float TimeLeft => timeLeft;
    public bool IsRunning => running;

    void Start()
    {
        barBasePos = transform.localPosition;
        BuildBar();
        ResetTimer();
        if (startOnAwake) StartTimer();
    }

    void Update()
    {
        if (!running) return;

        timeLeft -= useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            running = false;
            RefreshBlocks(true);
            onTimeUp?.Invoke();
            return;
        }

        RefreshBlocks(true);
    }

    void OnDestroy()
    {
        DOTween.Kill(this);
        foreach (var s in slots) { s.normal.Kill(); s.warning.Kill(); }
    }

    // ===== Public control methods =====

    public void StartTimer() => running = true;
    public void PauseTimer() => running = false;

    /// <summary>Reset the time, restore all blocks and play the intro effect</summary>
    public void ResetTimer()
    {
        timeLeft = totalTime;
        lastVisibleCount = -1;
        inWarning = false;

        foreach (var s in slots)
        {
            s.normal.Kill(); s.warning.Kill();
            s.normal.ResetVisual(); s.warning.ResetVisual();
            s.normal.go.SetActive(false); s.warning.go.SetActive(false);
            s.shown = false;
        }

        RefreshBlocks(false);
        PlayIntro();
    }

    /// <summary>Add or subtract time (e.g. time bonus pickups, time penalties)</summary>
    public void AddTime(float seconds)
    {
        timeLeft = Mathf.Clamp(timeLeft + seconds, 0f, totalTime);
        RefreshBlocks(true);
    }

    // ===== Building the bar =====

    void BuildBar()
    {
        if (normalBlock == null || warningBlock == null)
        {
            Debug.LogError("[CountdownBar] Please assign both normalBlock and warningBlock!");
            return;
        }

        bool isUI = normalBlock.GetComponent<RectTransform>() != null
                    && GetComponentInParent<Canvas>() != null;

        float step = spacing;
        if (autoSpacing) step = GetBlockWidth(normalBlock, isUI) + spacing;

        for (int i = 0; i < blockCount; i++)
        {
            Vector3 offset = new Vector3(step * i, 0f, 0f);

            GameObject n = Instantiate(normalBlock, transform);
            GameObject w = Instantiate(warningBlock, transform);
            n.name = $"Block_{i}";
            w.name = $"Block_{i}_Red";

            PlaceAt(n, offset, isUI);
            PlaceAt(w, offset, isUI);
            n.SetActive(true);
            w.SetActive(true);

            slots.Add(new Slot
            {
                normal = new BlockVisual(n),
                warning = new BlockVisual(w),
            });
        }

        // If the source blocks are scene objects (not prefabs), hide the originals
        if (normalBlock.scene.IsValid()) normalBlock.SetActive(false);
        if (warningBlock.scene.IsValid()) warningBlock.SetActive(false);
    }

    void PlaceAt(GameObject go, Vector3 offset, bool isUI)
    {
        if (isUI) go.GetComponent<RectTransform>().anchoredPosition = new Vector2(offset.x, 0f);
        else go.transform.localPosition = offset;
    }

    float GetBlockWidth(GameObject prefab, bool isUI)
    {
        if (isUI)
        {
            RectTransform rt = prefab.GetComponent<RectTransform>();
            return rt.rect.width * rt.localScale.x;
        }

        Renderer r = prefab.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            // Bounds may be zero for a prefab that isn't in the scene, so fall back to the sprite size
            SpriteRenderer sr = r as SpriteRenderer;
            if (sr != null && sr.sprite != null)
                return sr.sprite.bounds.size.x * prefab.transform.localScale.x;
            if (r.bounds.size.x > 0f) return r.bounds.size.x;
        }
        return prefab.transform.localScale.x;
    }

    // ===== Core update =====

    /// <summary>Decide how many blocks to show and whether to use red, based on time left</summary>
    void RefreshBlocks(bool animate)
    {
        if (slots.Count == 0) return;

        // Round up so at least one block stays visible until time actually hits 0
        int visible = Mathf.Clamp(Mathf.CeilToInt(timeLeft / totalTime * blockCount), 0, blockCount);
        bool warning = visible > 0 && visible <= warningBlockCount;

        if (visible == lastVisibleCount && warning == inWarning) return; // Nothing changed

        bool warningChanged = warning != inWarning;
        inWarning = warning;
        lastVisibleCount = visible;

        int lost = 0;
        int switchIndex = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            bool show = i < visible; // Counted from the left, so the rightmost blocks disappear first

            if (show)
            {
                if (!s.shown || warningChanged)
                {
                    bool animateSwitch = animate && warning && warningChanged && s.shown;
                    ShowSlot(s, warning, animateSwitch, switchIndex * switchStagger);
                    if (animateSwitch) switchIndex++;
                }
            }
            else if (s.shown)
            {
                HideSlot(s, animate);
                lost++;
            }
        }

        if (animate && lost > 0 && visible > 0)
            PlayBlockLostReaction(slots[visible - 1], warningChanged);
    }

    void ShowSlot(Slot s, bool warning, bool animateSwitch, float delay)
    {
        s.normal.Kill(); s.warning.Kill();
        s.normal.ResetVisual(); s.warning.ResetVisual();
        s.shown = true;

        if (warning && animateSwitch)
        {
            // Keep the normal block until this slot's turn in the wave, then swap
            s.normal.go.SetActive(true);
            s.warning.go.SetActive(false);

            Sequence seq = NewSequence(s.warning.go);
            if (delay > 0f) seq.AppendInterval(delay);
            seq.AppendCallback(() =>
            {
                s.normal.go.SetActive(false);
                s.warning.go.SetActive(true);
            });
            AppendSwitchEffect(seq, s.warning);
            seq.OnComplete(() => StartLoop(s.warning));
        }
        else
        {
            s.normal.go.SetActive(!warning);
            s.warning.go.SetActive(warning);
            if (warning) StartLoop(s.warning);
        }
    }

    void HideSlot(Slot s, bool animate)
    {
        s.shown = false;

        BlockVisual v = s.ActiveVisual;
        BlockVisual other = v == s.warning ? s.normal : s.warning;

        s.normal.Kill(); s.warning.Kill();
        other.ResetVisual();
        other.go.SetActive(false);
        v.ResetVisual();

        if (!animate || disappearEffect == DisappearEffect.None)
        {
            v.go.SetActive(false);
            return;
        }

        Sequence seq = NewSequence(v.go);
        AppendDisappearEffect(seq, v);
        seq.OnComplete(() =>
        {
            v.go.SetActive(false);
            v.ResetVisual();
        });
    }

    // ===== Effects =====

    void PlayIntro()
    {
        if (introEffect == IntroEffect.None) return;

        int k = 0;
        foreach (Slot s in slots)
        {
            if (!s.shown) continue;

            BlockVisual v = s.ActiveVisual;
            bool isWarning = v == s.warning;
            v.Kill();
            v.ResetVisual();

            Sequence seq = NewSequence(v.go);
            seq.AppendInterval(k * introStagger);

            switch (introEffect)
            {
                case IntroEffect.ScaleIn:
                    v.t.localScale = Vector3.zero;
                    seq.Append(v.t.DOScale(v.scale, introDuration).SetEase(Ease.OutBack));
                    break;

                case IntroEffect.FadeIn:
                    v.alpha.Set(0f);
                    seq.Append(FadeTo(v, 1f, introDuration));
                    break;

                case IntroEffect.DropIn:
                    v.t.localPosition = v.pos + Vector3.up * moveDistance;
                    v.alpha.Set(0f);
                    seq.Append(v.t.DOLocalMove(v.pos, introDuration).SetEase(Ease.OutBounce));
                    seq.Join(FadeTo(v, 1f, introDuration * 0.5f));
                    break;
            }

            if (isWarning) seq.OnComplete(() => StartLoop(v));
            k++;
        }
    }

    void AppendDisappearEffect(Sequence seq, BlockVisual v)
    {
        float d = disappearDuration;

        switch (disappearEffect)
        {
            case DisappearEffect.Shrink:
                seq.Append(v.t.DOScale(0f, d).SetEase(disappearEase));
                break;

            case DisappearEffect.Fade:
                seq.Append(FadeTo(v, 0f, d).SetEase(disappearEase));
                break;

            case DisappearEffect.ShrinkAndFade:
                seq.Append(v.t.DOScale(0f, d).SetEase(disappearEase));
                seq.Join(FadeTo(v, 0f, d));
                break;

            case DisappearEffect.FallDown:
                seq.Append(v.t.DOLocalMoveY(v.pos.y - moveDistance, d).SetEase(Ease.InQuad));
                seq.Join(FadeTo(v, 0f, d));
                break;

            case DisappearEffect.FlyUp:
                seq.Append(v.t.DOLocalMoveY(v.pos.y + moveDistance, d).SetEase(Ease.OutQuad));
                seq.Join(FadeTo(v, 0f, d));
                break;

            case DisappearEffect.PopAndVanish:
                seq.Append(v.t.DOScale(v.scale * 1.3f, d * 0.3f).SetEase(Ease.OutQuad));
                seq.Append(v.t.DOScale(0f, d * 0.7f).SetEase(disappearEase));
                break;

            case DisappearEffect.SpinOut:
                seq.Append(v.t.DOLocalRotate(new Vector3(0f, 0f, 360f), d, RotateMode.FastBeyond360));
                seq.Join(v.t.DOScale(0f, d).SetEase(disappearEase));
                break;

            case DisappearEffect.ShakeThenVanish:
                seq.Append(v.t.DOShakePosition(d * 0.6f, shakeStrength, 25));
                seq.Append(v.t.DOScale(0f, d * 0.4f).SetEase(disappearEase));
                break;
        }
    }

    void PlayBlockLostReaction(Slot lastSlot, bool warningJustChanged)
    {
        switch (blockLostReaction)
        {
            case BlockLostReaction.ShakeBar:
                DOTween.Kill(this);
                transform.localPosition = barBasePos;
                transform.DOShakePosition(reactionDuration, shakeStrength, 20)
                    .SetId(this)
                    .SetUpdate(useUnscaledTime)
                    .OnComplete(() => transform.localPosition = barBasePos);
                break;

            case BlockLostReaction.PunchLastBlock:
                // Skip on the frame the bar turns red, so it doesn't fight the switch effect
                if (warningJustChanged) return;

                BlockVisual v = lastSlot.ActiveVisual;
                bool isWarning = v == lastSlot.warning;
                v.Kill();
                v.ResetVisual();

                Sequence seq = NewSequence(v.go);
                seq.Append(v.t.DOPunchScale(Vector3.one * punchStrength, reactionDuration, 8, 0.5f));
                if (isWarning) seq.OnComplete(() => StartLoop(v));
                break;
        }
    }

    void AppendSwitchEffect(Sequence seq, BlockVisual v)
    {
        float d = switchDuration;

        switch (warningSwitchEffect)
        {
            case WarningSwitchEffect.Punch:
                seq.Append(v.t.DOPunchScale(Vector3.one * punchStrength, d, 8, 0.5f));
                break;

            case WarningSwitchEffect.PopIn:
                seq.AppendCallback(() => v.t.localScale = Vector3.zero);
                seq.Append(v.t.DOScale(v.scale, d).SetEase(Ease.OutBack));
                break;

            case WarningSwitchEffect.FlashIn:
                float step = d / 6f;
                for (int i = 0; i < 3; i++)
                {
                    seq.Append(FadeTo(v, 0f, step));
                    seq.Append(FadeTo(v, 1f, step));
                }
                break;
        }
    }

    void StartLoop(BlockVisual v)
    {
        if (!inWarning || !v.go.activeInHierarchy) return;

        float half = loopDuration * 0.5f;
        Tween t = null;

        switch (warningLoopEffect)
        {
            case WarningLoopEffect.Pulse:
                t = v.t.DOScale(v.scale * pulseScale, half).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
                break;

            case WarningLoopEffect.Blink:
                t = FadeTo(v, blinkMinAlpha, half).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
                break;

            case WarningLoopEffect.Shake:
                t = v.t.DOShakePosition(loopDuration, shakeStrength, 15, 90f, false, false).SetLoops(-1, LoopType.Restart);
                break;

            case WarningLoopEffect.Bounce:
                t = v.t.DOLocalMoveY(v.pos.y + moveDistance * 0.3f, half).SetEase(Ease.OutQuad).SetLoops(-1, LoopType.Yoyo);
                break;
        }

        if (t != null) t.SetId(v.go).SetUpdate(useUnscaledTime);
    }

    // ===== Helpers =====

    Sequence NewSequence(GameObject id)
    {
        return DOTween.Sequence().SetId(id).SetUpdate(useUnscaledTime);
    }

    Tween FadeTo(BlockVisual v, float target, float duration)
    {
        return DOTween.To(() => v.alpha.Factor, x => v.alpha.Set(x), target, duration);
    }

    /// <summary>One position in the bar: holds both the normal and the red block</summary>
    class Slot
    {
        public BlockVisual normal;
        public BlockVisual warning;
        public bool shown;

        public BlockVisual ActiveVisual => warning.go.activeSelf ? warning : normal;
    }

    /// <summary>A block instance plus its original transform values, so effects can always restore it</summary>
    class BlockVisual
    {
        public readonly GameObject go;
        public readonly Transform t;
        public readonly Vector3 pos;
        public readonly Vector3 scale;
        public readonly Quaternion rot;
        public readonly AlphaGroup alpha;

        public BlockVisual(GameObject go)
        {
            this.go = go;
            t = go.transform;
            pos = t.localPosition;
            scale = t.localScale;
            rot = t.localRotation;
            alpha = new AlphaGroup(go);
        }

        public void Kill() => DOTween.Kill(go);

        public void ResetVisual()
        {
            t.localPosition = pos;
            t.localScale = scale;
            t.localRotation = rot;
            alpha.Set(1f);
        }
    }

    /// <summary>Fades a block and all its children (CanvasGroup, UI Graphics or SpriteRenderers)</summary>
    class AlphaGroup
    {
        private readonly CanvasGroup canvasGroup;
        private readonly float canvasGroupAlpha;
        private readonly SpriteRenderer[] sprites;
        private readonly float[] spriteAlphas;
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

            sprites = go.GetComponentsInChildren<SpriteRenderer>(true);
            spriteAlphas = new float[sprites.Length];
            for (int i = 0; i < sprites.Length; i++) spriteAlphas[i] = sprites[i].color.a;

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

            for (int i = 0; i < sprites.Length; i++)
            {
                Color c = sprites[i].color;
                c.a = spriteAlphas[i] * factor;
                sprites[i].color = c;
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
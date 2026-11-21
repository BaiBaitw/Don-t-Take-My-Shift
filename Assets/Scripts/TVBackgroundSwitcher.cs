using System.Collections;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Switches between two video background Quads using a "TV turn-off" effect.
/// Sequence: current Quad shrinks ScaleY to ~0 -> shrinks ScaleX to ~0 (a white dot)
///           -> swap to the other Quad -> ScaleX expands back -> ScaleY expands back.
/// The closer the image gets to a thin line, the closer it gets to pure white.
///
/// Auto mode: follows GameState.SupermanSelected (true = Quad B, false = Quad A).
/// It only looks at the CURRENT value, so rapid toggling can never desync it:
///   - If the value flips back while waiting for the video to prepare, the switch is cancelled.
///   - At the white-dot moment it re-checks the value; if it flipped back, it reopens
///     the same Quad instead of swapping.
///   - After every animation it re-checks again and switches if still out of sync.
///
/// Requirement: both Quads must use Custom/TVVideoUnlit (or any shader with a float _White property).
/// </summary>
public class TVBackgroundSwitcher : MonoBehaviour
{
    [Header("Background Quads (each needs VideoPlayer + MeshRenderer)")]
    [SerializeField] private VideoPlayer quadA;
    [SerializeField] private VideoPlayer quadB;

    [Header("Auto Switching")]
    [Tooltip("Follow GameState.SupermanSelected automatically (true = Quad B, false = Quad A)")]
    [SerializeField] private bool followSupermanSelected = true;
    [Tooltip("Only used when auto switching is off: show Quad A first (otherwise Quad B)")]
    [SerializeField] private bool startWithA = true;

    [Header("Animation Durations (seconds)")]
    [SerializeField] private float shrinkYDuration = 0.18f;
    [SerializeField] private float shrinkXDuration = 0.12f;
    [Tooltip("How long to hold on the white dot")]
    [SerializeField] private float holdDuration = 0.05f;
    [SerializeField] private float expandXDuration = 0.12f;
    [SerializeField] private float expandYDuration = 0.18f;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Minimum Scale (relative to original, near 0 but not 0)")]
    [SerializeField, Range(0.001f, 0.2f)] private float minScaleY = 0.01f;
    [SerializeField, Range(0.001f, 0.2f)] private float minScaleX = 0.01f;

    [Header("Whitening")]
    [SerializeField] private string whiteProperty = "_White";
    [Tooltip("Higher = whitens later (only turns white near the line)")]
    [SerializeField, Range(0.5f, 6f)] private float whitenPower = 2f;

    [Header("Wait for next video to be ready before switching (seconds, 0 = don't wait)")]
    [SerializeField] private float prepareTimeout = 2f;

    private VideoPlayer current;
    private VideoPlayer next;
    private Vector3 originalScaleA;
    private Vector3 originalScaleB;
    private Material matA;
    private Material matB;
    private int whiteId;

    public bool IsSwitching { get; private set; }
    public VideoPlayer Current => current;

    private void Awake()
    {
        whiteId = Shader.PropertyToID(whiteProperty);

        originalScaleA = quadA.transform.localScale;
        originalScaleB = quadB.transform.localScale;

        // Use material instances so the two Quads don't affect each other
        matA = quadA.GetComponent<Renderer>().material;
        matB = quadB.GetComponent<Renderer>().material;

        if (!matA.HasProperty(whiteId) || !matB.HasProperty(whiteId))
            Debug.LogWarning($"[TVBackgroundSwitcher] Material has no {whiteProperty} property; whitening will not work. Use Custom/TVVideoUnlit.", this);

        // If using Material Override, make the VideoPlayer write into our material instance
        SetupOverrideTarget(quadA);
        SetupOverrideTarget(quadB);

        // Start directly on the correct Quad (no animation)
        current = followSupermanSelected ? DesiredQuad() : (startWithA ? quadA : quadB);
        next = current == quadA ? quadB : quadA;

        ResetVisuals();
    }

    private void Start()
    {
        if (!current.isPlaying) current.Play();
        next.Pause();
    }

    private void Update()
    {
        if (!followSupermanSelected || IsSwitching) return;

        // Level-based check: just make the screen match the current value
        if (DesiredQuad() != current)
            SwitchBackground();
    }

    private void OnDisable()
    {
        // If disabled mid-switch, stop cleanly so nothing gets stuck shrunk / white / locked
        if (!IsSwitching) return;

        StopAllCoroutines();
        IsSwitching = false;
        next.Pause();
        ResetVisuals();
        // Update() will re-sync with SupermanSelected when re-enabled
    }

    // ---------------- Public API ----------------

    /// <summary>
    /// Switch to the other Quad (keeps its current clip).
    /// Note: in auto mode, a manual switch that disagrees with SupermanSelected will be reverted.
    /// </summary>
    [ContextMenu("Switch Background (Play Mode)")]
    public void SwitchBackground()
    {
        SwitchBackground(null);
    }

    /// <summary>Switch background and optionally assign a new clip to it (null = keep clip).</summary>
    public void SwitchBackground(VideoClip clip)
    {
        if (IsSwitching) return;
        StartCoroutine(SwitchRoutine(clip));
    }

    // ---------------- Main sequence ----------------

    private IEnumerator SwitchRoutine(VideoClip clip)
    {
        IsSwitching = true;

        // 0. Prepare and start the next video in the background (hidden) to avoid a black frame
        if (clip != null && next.source == VideoSource.VideoClip) next.clip = clip;
        yield return PrepareAndPlay(next);

        // Value flipped back while preparing -> cancel, nothing has moved yet
        if (followSupermanSelected && DesiredQuad() == current)
        {
            next.Pause();
            IsSwitching = false;
            yield break;
        }

        Vector3 curScale = OriginalScale(current);

        // 1. Current: shrink ScaleY to ~0 while fading to white
        yield return Animate(shrinkYDuration, t =>
        {
            float y = Mathf.Lerp(1f, minScaleY, t);
            ApplyScale(current, curScale, 1f, y);
            SetWhite(current, WhiteFromY(y));
        });

        // 2. Shrink ScaleX to ~0 (white line -> white dot)
        SetWhite(current, 1f);
        yield return Animate(shrinkXDuration, t =>
        {
            ApplyScale(current, curScale, Mathf.Lerp(1f, minScaleX, t), minScaleY);
        });

        // 3. Decision point: re-check the value at the white dot.
        //    If it flipped back, reopen the same Quad instead of swapping.
        bool swap = !followSupermanSelected || DesiredQuad() == next;
        VideoPlayer shown = swap ? next : current;
        VideoPlayer hidden = swap ? current : next;
        Vector3 shownScale = OriginalScale(shown);

        if (swap)
        {
            ApplyScale(next, shownScale, minScaleX, minScaleY);
            SetWhite(next, 1f);
            SetVisible(current, false);
            SetVisible(next, true);
            ApplyScale(current, curScale, 1f, 1f); // Restore old Quad for next time
            SetWhite(current, 0f);
        }

        if (holdDuration > 0f) yield return Wait(holdDuration);

        // 4. Expand ScaleX back to original (still a white line)
        yield return Animate(expandXDuration, t =>
        {
            ApplyScale(shown, shownScale, Mathf.Lerp(minScaleX, 1f, t), minScaleY);
        });

        // 5. Expand ScaleY back to original while fading out the white
        yield return Animate(expandYDuration, t =>
        {
            float y = Mathf.Lerp(minScaleY, 1f, t);
            ApplyScale(shown, shownScale, 1f, y);
            SetWhite(shown, WhiteFromY(y));
        });

        ApplyScale(shown, shownScale, 1f, 1f);
        SetWhite(shown, 0f);

        // Pause the hidden video to save performance
        hidden.Pause();

        if (swap) (current, next) = (next, current);
        IsSwitching = false;
        // If the value changed during steps 4-5, Update() will start another switch next frame
    }

    // ---------------- Helpers ----------------

    // TODO: replace GameState with the class that actually holds SupermanSelected
    private VideoPlayer DesiredQuad() => MenuManager.SupermanSelected ? quadB : quadA;

    private void ResetVisuals()
    {
        ApplyScale(quadA, originalScaleA, 1f, 1f);
        ApplyScale(quadB, originalScaleB, 1f, 1f);
        SetWhite(quadA, 0f);
        SetWhite(quadB, 0f);
        SetVisible(current, true);
        SetVisible(next, false);
    }

    private IEnumerator PrepareAndPlay(VideoPlayer vp)
    {
        if (prepareTimeout <= 0f)
        {
            vp.Play();
            yield break;
        }

        if (!vp.isPrepared) vp.Prepare();

        float timer = 0f;
        while (!vp.isPrepared && timer < prepareTimeout)
        {
            timer += DeltaTime();
            yield return null;
        }

        vp.Play();

        // Wait for the first frame
        timer = 0f;
        while (vp.frame <= 0 && timer < prepareTimeout)
        {
            timer += DeltaTime();
            yield return null;
        }
    }

    private IEnumerator Animate(float duration, System.Action<float> step)
    {
        if (duration <= 0f)
        {
            step(1f);
            yield break;
        }

        float time = 0f;
        while (time < duration)
        {
            time += DeltaTime();
            step(ease.Evaluate(Mathf.Clamp01(time / duration)));
            yield return null;
        }
        step(1f);
    }

    private IEnumerator Wait(float seconds)
    {
        if (useUnscaledTime) yield return new WaitForSecondsRealtime(seconds);
        else yield return new WaitForSeconds(seconds);
    }

    private float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    /// <summary>Y ratio 1 -> no white; Y ratio = minScaleY (thin line) -> full white.</summary>
    private float WhiteFromY(float yRatio)
    {
        float open = Mathf.InverseLerp(minScaleY, 1f, yRatio); // 1 = fully open, 0 = thin line
        return Mathf.Pow(1f - open, whitenPower);
    }

    private void ApplyScale(VideoPlayer vp, Vector3 baseScale, float xRatio, float yRatio)
    {
        vp.transform.localScale = new Vector3(baseScale.x * xRatio, baseScale.y * yRatio, baseScale.z);
    }

    private Vector3 OriginalScale(VideoPlayer vp) => vp == quadA ? originalScaleA : originalScaleB;

    private void SetWhite(VideoPlayer vp, float value)
    {
        Material m = vp == quadA ? matA : matB;
        if (m != null && m.HasProperty(whiteId)) m.SetFloat(whiteId, value);
    }

    private static void SetVisible(VideoPlayer vp, bool visible)
    {
        // Only toggle the Renderer (not the GameObject) so the VideoPlayer keeps playing
        vp.GetComponent<Renderer>().enabled = visible;
    }

    private static void SetupOverrideTarget(VideoPlayer vp)
    {
        if (vp.renderMode == VideoRenderMode.MaterialOverride && vp.targetMaterialRenderer == null)
            vp.targetMaterialRenderer = vp.GetComponent<Renderer>();
    }
}
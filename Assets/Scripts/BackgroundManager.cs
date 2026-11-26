using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal; // Light2D (URP)

/// <summary>
/// Watches GameManager.FinishedStageCount. Each time it increases by 1,
/// the ground background, the sky and the 2D lights change to the next time of day:
/// Day -> Dusk -> Night. Transition duration is read from GameManager.NextStageDelay.
///
/// Fade method: the new object is turned on fully opaque BEHIND the old object,
/// then the old object (drawn on top) fades from 1 to 0. When it reaches 0 it is
/// turned off. Because the new object is already fully visible underneath,
/// nothing further back shows through during the fade, and the fixed draw order
/// keeps the two objects from flickering.
///
/// Lights: each entry in 'lights' stores a Day / Dusk / Night setting
/// (position, rotation, color, intensity). During a transition the light
/// moves smoothly from its current setting to the next one.
/// </summary>
public class BackgroundManager : MonoBehaviour
{
    [Header("Ground Background (Dusk / Night are optional)")]
    public GameObject background;
    public GameObject backgroundDusk;
    public GameObject backgroundNight;

    [Header("Sky Background (SpriteRenderer + default animation)")]
    public GameObject skyBackground_DayTime;
    public GameObject skyBackgroundDuskTime;
    public GameObject skyBackgroundNightTime;

    [Header("2D Lights (e.g. Freeform Light)")]
    public List<LightTransition> lights = new List<LightTransition>();

    [Header("GameManager Object")]
    public GameObject gameManager;

    [Header("Debug")]
    [Tooltip("Duration used by the Inspector test buttons. Below 0 = use GameManager.NextStageDelay.")]
    public float debugFadeDuration = -1f;
    [Tooltip("Freeze the transition so you can inspect it or scrub it with the slider.")]
    public bool debugPauseFade;

    const int MaxStage = 2; // 0 = Day, 1 = Dusk, 2 = Night
    public static readonly string[] StageNames = { "Day", "Dusk", "Night" };

    GameManager gm;
    int lastCount;
    int stage;

    FadeLayer groundLayer;
    FadeLayer skyLayer;

    // ---- Read-only info for the Inspector ----
    public bool IsReady => groundLayer != null && skyLayer != null;
    public string CurrentStageName => StageNames[stage];

    public bool IsFading
    {
        get
        {
            if (!IsReady) return false;
            if (groundLayer.IsFading || skyLayer.IsFading) return true;
            foreach (var l in lights) if (l != null && l.IsFading) return true;
            return false;
        }
    }

    public float FadeProgress
    {
        get
        {
            if (!IsReady) return 0f;
            if (groundLayer.IsFading) return groundLayer.Progress;
            if (skyLayer.IsFading) return skyLayer.Progress;
            foreach (var l in lights) if (l != null && l.IsFading) return l.Progress;
            return 0f;
        }
    }

    void Start()
    {
        if (gameManager != null)
            gm = gameManager.GetComponent<GameManager>();

        if (gm == null)
        {
            Debug.LogWarning("[BackgroundManager] GameManager component not found. Background transitions disabled.", this);
            enabled = false;
            return;
        }

        lastCount = gm.FinishedStageCount;
        stage = 0;

        groundLayer = new FadeLayer(background, backgroundDusk, backgroundNight);
        skyLayer = new FadeLayer(skyBackground_DayTime, skyBackgroundDuskTime, skyBackgroundNightTime);

        foreach (var l in lights) l?.ApplyImmediate(0);
    }

    void Update()
    {
        int count = gm.FinishedStageCount;
        if (count == lastCount) return;

        // If the count goes down (e.g. game restart), just record it without transitioning.
        if (count < lastCount)
        {
            lastCount = count;
            return;
        }

        int target = Mathf.Min(stage + (count - lastCount), MaxStage);
        lastCount = count;
        if (target == stage) return;

        GoToStage(target, gm.NextStageDelay);
    }

    // Applied in LateUpdate, after Animators have run,
    // so an animation can't override the fade.
    void LateUpdate()
    {
        if (!IsReady || debugPauseFade) return;
        float dt = Time.deltaTime;
        groundLayer.Tick(dt);
        skyLayer.Tick(dt);
        foreach (var l in lights) l?.Tick(dt);
    }

    void GoToStage(int target, float duration)
    {
        stage = target;
        groundLayer.TransitionTo(stage, duration);
        skyLayer.TransitionTo(stage, duration);
        foreach (var l in lights) l?.TransitionTo(stage, duration);
    }

    // ------------------------------------------------------------------
    // Debug API (used by BackgroundManagerEditor buttons, Play Mode only)
    // ------------------------------------------------------------------

    float DebugDuration => debugFadeDuration >= 0f ? debugFadeDuration : (gm != null ? gm.NextStageDelay : 1f);

    [ContextMenu("Debug/Next Stage")]
    public void DebugNextStage()
    {
        if (!IsReady) return;
        if (stage >= MaxStage)
        {
            Debug.Log("[BackgroundManager] Already at Night.", this);
            return;
        }
        GoToStage(stage + 1, DebugDuration);
    }

    public void DebugGoToStage(int target)
    {
        if (!IsReady) return;
        target = Mathf.Clamp(target, 0, MaxStage);
        if (target == stage) return;
        GoToStage(target, DebugDuration);
    }

    [ContextMenu("Debug/Reset To Day")]
    public void DebugResetToDay()
    {
        if (!IsReady) return;
        stage = 0;
        groundLayer.ResetTo(0);
        skyLayer.ResetTo(0);
        foreach (var l in lights) l?.ApplyImmediate(0);
        if (gm != null) lastCount = gm.FinishedStageCount;
    }

    public void DebugSetProgress(float k)
    {
        if (!IsReady) return;
        groundLayer.SetProgress(k);
        skyLayer.SetProgress(k);
        foreach (var l in lights) l?.SetProgress(k);
    }

    [ContextMenu("Debug/Log Renderers")]
    public void DebugLogRenderers()
    {
        if (!IsReady) return;
        var sb = new StringBuilder();
        sb.AppendLine($"[BackgroundManager] Stage = {CurrentStageName}, Fading = {IsFading}, Progress = {FadeProgress:0.00}");
        sb.AppendLine("--- Ground ---");
        groundLayer.AppendInfo(sb);
        sb.AppendLine("--- Sky ---");
        skyLayer.AppendInfo(sb);
        sb.AppendLine("--- Lights ---");
        foreach (var l in lights) l?.AppendInfo(sb);
        Debug.Log(sb.ToString(), this);
    }

    // ==================================================================
    // Lights
    // ==================================================================

    [System.Serializable]
    public class LightStage
    {
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Color color = Color.white;
        public float intensity = 1f;
    }

    [System.Serializable]
    public class LightTransition
    {
        public Light2D light;

        [Tooltip("Move the light between stages.")]
        public bool controlPosition = false;
        [Tooltip("Rotate the light between stages (changes the light angle).")]
        public bool controlRotation = true;
        public bool controlColor = true;
        public bool controlIntensity = true;

        public LightStage day = new LightStage();
        public LightStage dusk = new LightStage();
        public LightStage night = new LightStage();

        [System.NonSerialized] LightStage from;
        [System.NonSerialized] LightStage to;
        [System.NonSerialized] float duration;
        [System.NonSerialized] float elapsed;
        [System.NonSerialized] bool fading;

        public bool IsFading => fading;
        public float Progress => fading && duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;

        public LightStage Get(int index) => index <= 0 ? day : index == 1 ? dusk : night;

        /// <summary>Copy the light's current settings into the given stage.</summary>
        public void CaptureInto(LightStage s)
        {
            if (light == null || s == null) return;
            s.localPosition = light.transform.localPosition;
            s.localEulerAngles = light.transform.localEulerAngles;
            s.color = light.color;
            s.intensity = light.intensity;
        }

        public void ApplyImmediate(int index)
        {
            fading = false;
            if (light == null) return;
            var s = Get(index);
            Blend(s, s, 1f);
        }

        public void TransitionTo(int index, float fadeDuration)
        {
            if (light == null) return;

            // Start from wherever the light is right now (works even if interrupted mid-transition).
            from = new LightStage();
            CaptureInto(from);
            to = Get(index);

            duration = fadeDuration;
            elapsed = 0f;
            fading = duration > 0f;
            if (!fading) Blend(to, to, 1f);
        }

        public void Tick(float deltaTime)
        {
            if (!fading) return;
            elapsed += deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            Blend(from, to, k);
            if (k >= 1f) fading = false;
        }

        public void SetProgress(float k)
        {
            if (!fading || duration <= 0f) return;
            k = Mathf.Clamp01(k);
            elapsed = k * duration;
            Blend(from, to, k);
        }

        void Blend(LightStage a, LightStage b, float k)
        {
            if (light == null) return;
            var t = light.transform;
            if (controlPosition)
                t.localPosition = Vector3.Lerp(a.localPosition, b.localPosition, k);
            if (controlRotation)
                t.localRotation = Quaternion.Slerp(Quaternion.Euler(a.localEulerAngles), Quaternion.Euler(b.localEulerAngles), k);
            if (controlColor)
                light.color = Color.Lerp(a.color, b.color, k);
            if (controlIntensity)
                light.intensity = Mathf.Lerp(a.intensity, b.intensity, k);
        }

        public void AppendInfo(StringBuilder sb)
        {
            if (light == null) { sb.AppendLine("  (empty light)"); return; }
            var t = light.transform;
            sb.AppendLine($"  {light.name}: type={light.lightType}, pos={t.localPosition}, rot={t.localEulerAngles}, " +
                          $"color={light.color}, intensity={light.intensity:0.00}, fading={fading}");
        }
    }

    // ==================================================================
    // Sprite layers (ground / sky)
    // ==================================================================

    /// <summary>
    /// A set of Day / Dusk / Night objects that fade between each other.
    /// Missing objects are skipped and the current object stays visible.
    /// </summary>
    class FadeLayer
    {
        readonly GameObject[] stages;
        readonly Dictionary<GameObject, SpriteRenderer[]> renderers = new Dictionary<GameObject, SpriteRenderer[]>();
        readonly Dictionary<SpriteRenderer, float> baseAlpha = new Dictionary<SpriteRenderer, float>();
        readonly Dictionary<SpriteRenderer, int> baseOrder = new Dictionary<SpriteRenderer, int>();

        GameObject current;
        GameObject fadingFrom;
        GameObject fadingTo;
        float duration;
        float elapsed;
        bool fading;

        public bool IsFading => fading;
        public float Progress => fading && duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;

        public FadeLayer(params GameObject[] stageObjects)
        {
            stages = stageObjects;

            foreach (var obj in stages)
            {
                if (obj == null || renderers.ContainsKey(obj)) continue;
                var srs = obj.GetComponentsInChildren<SpriteRenderer>(true);
                renderers[obj] = srs;
                foreach (var sr in srs)
                {
                    baseAlpha[sr] = sr.color.a;
                    baseOrder[sr] = sr.sortingOrder;
                }
            }

            ResetTo(0);
        }

        public void ResetTo(int index)
        {
            fading = false;
            fadingFrom = null;
            fadingTo = null;

            current = index < stages.Length ? stages[index] : null;
            if (current == null && stages.Length > 0) current = stages[0];

            foreach (var obj in stages)
            {
                if (obj == null) continue;
                bool visible = obj == current;
                RestoreOrder(obj);
                SetAlpha(obj, visible ? 1f : 0f);
                obj.SetActive(visible);
            }
        }

        public void TransitionTo(int index, float fadeDuration)
        {
            if (current == null) return;                      // No Day object in this layer, do nothing.
            if (index < 0 || index >= stages.Length) return;

            GameObject target = stages[index];
            if (target == null || target == current) return; // No object for this stage, keep the current one.

            // Previous transition still running: jump straight to its end state.
            if (fading) Complete();

            fadingFrom = current;
            fadingTo = target;
            current = target;

            // New object appears fully opaque BEHIND the old one,
            // then the old one fades out on top of it.
            fadingTo.SetActive(true);
            SetAlpha(fadingTo, 1f);
            SetAlpha(fadingFrom, 1f);
            RaiseAbove(fadingFrom, fadingTo);

            duration = fadeDuration;
            elapsed = 0f;
            fading = true;

            if (duration <= 0f) Complete();
        }

        public void Tick(float deltaTime)
        {
            if (!fading) return;
            elapsed += deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            SetAlpha(fadingFrom, 1f - k);
            if (k >= 1f) Complete();
        }

        public void SetProgress(float k)
        {
            if (!fading || duration <= 0f) return;
            k = Mathf.Clamp01(k);
            elapsed = k * duration;
            // Don't auto-complete while scrubbing so you can look at k = 1.
            SetAlpha(fadingFrom, 1f - k);
        }

        void Complete()
        {
            if (fadingTo != null)
            {
                fadingTo.SetActive(true);
                SetAlpha(fadingTo, 1f);
            }
            if (fadingFrom != null)
            {
                // Old object is fully transparent now: turn it off and restore its settings.
                SetAlpha(fadingFrom, 0f);
                RestoreOrder(fadingFrom);
                fadingFrom.SetActive(false);
            }
            fading = false;
        }

        // Temporarily shift the 'top' object's sorting orders so that all of its
        // renderers draw just above the 'bottom' object's renderers.
        void RaiseAbove(GameObject top, GameObject bottom)
        {
            if (!renderers.TryGetValue(top, out var topSrs) || topSrs.Length == 0) return;
            if (!renderers.TryGetValue(bottom, out var bottomSrs) || bottomSrs.Length == 0) return;

            int bottomMax = int.MinValue;
            foreach (var sr in bottomSrs)
                if (sr != null) bottomMax = Mathf.Max(bottomMax, baseOrder[sr]);

            int topMin = int.MaxValue;
            foreach (var sr in topSrs)
                if (sr != null) topMin = Mathf.Min(topMin, baseOrder[sr]);

            int offset = bottomMax - topMin + 1;
            if (offset <= 0) return; // Already on top.

            foreach (var sr in topSrs)
                if (sr != null) sr.sortingOrder = baseOrder[sr] + offset;
        }

        void RestoreOrder(GameObject obj)
        {
            if (obj == null || !renderers.TryGetValue(obj, out var srs)) return;
            foreach (var sr in srs)
                if (sr != null) sr.sortingOrder = baseOrder[sr];
        }

        void SetAlpha(GameObject obj, float k)
        {
            if (obj == null || !renderers.TryGetValue(obj, out var srs)) return;
            foreach (var sr in srs)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = baseAlpha[sr] * k;
                sr.color = c;
            }
        }

        public void AppendInfo(StringBuilder sb)
        {
            foreach (var obj in stages)
            {
                if (obj == null) { sb.AppendLine("  (empty)"); continue; }
                string role = obj == fadingFrom && fading ? " [fading OUT]"
                            : obj == fadingTo && fading ? " [fading IN]"
                            : obj == current ? " [current]" : "";
                sb.AppendLine($"  {obj.name} active={obj.activeInHierarchy}{role}");
                if (!renderers.TryGetValue(obj, out var srs)) continue;
                foreach (var sr in srs)
                {
                    if (sr == null) continue;
                    var anim = sr.GetComponentInParent<Animator>();
                    sb.AppendLine($"    - {sr.name}: layer={sr.sortingLayerName}, order={sr.sortingOrder}, alpha={sr.color.a:0.00}, " +
                                  $"z={sr.transform.position.z:0.00}, sprite={(sr.sprite ? sr.sprite.name : "null")}, animator={(anim ? anim.name : "none")}");
                }
            }
        }
    }
}
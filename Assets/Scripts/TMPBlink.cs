using UnityEngine;
using TMPro;

/// <summary>
/// Animates the Vertex Color alpha of a TextMeshPro - Text (UI).
/// Attach to any GameObject with a TextMeshProUGUI component.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TMPBlink : MonoBehaviour
{
    public enum BlinkMode
    {
        RandomDip, // Stays bright, occasionally dims at random (flicker)
        Smooth,    // Smooth fade in/out (sine wave)
        PingPong,  // Linear back and forth
        Hard       // Hard on/off switch
    }

    [Header("General")]
    public BlinkMode mode = BlinkMode.RandomDip;

    [Range(0f, 1f)] public float minAlpha = 0f;
    [Range(0f, 1f)] public float maxAlpha = 1f;

    [Tooltip("Ignore Time.timeScale (keeps animating while the game is paused)")]
    public bool useUnscaledTime = true;

    [Tooltip("Start animating automatically when enabled")]
    public bool playOnEnable = true;

    [Header("Periodic Modes (Smooth / PingPong / Hard)")]
    [Tooltip("Blinks per second")]
    [Min(0.01f)] public float frequency = 1f;

    [Tooltip("Hard mode: fraction of each cycle the text is visible")]
    [Range(0f, 1f)] public float hardOnRatio = 0.5f;

    [Header("Random Dip Mode")]
    [Tooltip("Random wait time (seconds) at full brightness between dips")]
    public Vector2 dipIntervalRange = new Vector2(2f, 6f);

    [Tooltip("Random duration (seconds) of a single dip")]
    public Vector2 dipDurationRange = new Vector2(0.08f, 0.25f);

    [Tooltip("Random alpha reached at the bottom of a dip (clamped to Min/Max Alpha)")]
    public Vector2 dipAlphaRange = new Vector2(0.15f, 0.5f);

    [Tooltip("Chance that a dip is immediately followed by another quick dip")]
    [Range(0f, 1f)] public float doubleFlickerChance = 0.3f;

    private TMP_Text _text;
    private float _originalAlpha;
    private float _timer;
    private bool _isPlaying;

    // Random dip state
    private bool _isDipping;
    private float _waitTimer;
    private float _dipTimer;
    private float _dipDuration;
    private float _dipAlpha;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
        _originalAlpha = _text.color.a;
    }

    private void OnEnable()
    {
        ResetState();
        _isPlaying = playOnEnable;
    }

    private void OnDisable()
    {
        SetAlpha(_originalAlpha);
    }

    private void Update()
    {
        if (!_isPlaying) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (mode == BlinkMode.RandomDip)
        {
            UpdateRandomDip(dt);
            return;
        }

        _timer += dt;
        float t = _timer * frequency; // number of cycles

        float k; // 0..1
        switch (mode)
        {
            case BlinkMode.PingPong:
                k = Mathf.PingPong(t * 2f, 1f);
                break;
            case BlinkMode.Hard:
                k = (t % 1f) < hardOnRatio ? 1f : 0f;
                break;
            default: // Smooth
                k = (Mathf.Sin(t * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) * 0.5f;
                break;
        }

        SetAlpha(Mathf.Lerp(minAlpha, maxAlpha, k));
    }

    private void UpdateRandomDip(float dt)
    {
        if (!_isDipping)
        {
            SetAlpha(maxAlpha);
            _waitTimer -= dt;
            if (_waitTimer <= 0f) BeginDip();
            return;
        }

        _dipTimer += dt;
        float p = Mathf.Clamp01(_dipTimer / _dipDuration);

        // 0 -> 1 -> 0 over the dip duration
        float k = Mathf.Sin(p * Mathf.PI);
        SetAlpha(Mathf.Lerp(maxAlpha, _dipAlpha, k));

        if (p >= 1f)
        {
            _isDipping = false;
            _waitTimer = Random.value < doubleFlickerChance
                ? Random.Range(0.03f, 0.1f)  // quick follow-up flicker
                : RandomInRange(dipIntervalRange);
        }
    }

    private void BeginDip()
    {
        _isDipping = true;
        _dipTimer = 0f;
        _dipDuration = Mathf.Max(0.01f, RandomInRange(dipDurationRange));
        _dipAlpha = Mathf.Clamp(RandomInRange(dipAlphaRange), minAlpha, maxAlpha);
    }

    private void ResetState()
    {
        _timer = 0f;
        _isDipping = false;
        _waitTimer = RandomInRange(dipIntervalRange);
    }

    private static float RandomInRange(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    private void SetAlpha(float a)
    {
        if (_text == null) return;
        Color c = _text.color; // this is the Vertex Color shown in the Inspector
        c.a = a;
        _text.color = c;
    }

    // ===== Public API =====

    public void StartBlink()
    {
        ResetState();
        _isPlaying = true;
    }

    /// <param name="restoreAlpha">Restore the original alpha when stopping</param>
    public void StopBlink(bool restoreAlpha = true)
    {
        _isPlaying = false;
        if (restoreAlpha) SetAlpha(_originalAlpha);
    }
}
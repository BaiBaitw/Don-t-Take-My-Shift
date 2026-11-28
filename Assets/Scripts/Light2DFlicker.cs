using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Flicker effect for Light2D. Attach to any GameObject with a Light2D component.
/// Modes: Smooth (Perlin noise), Random (jumpy), Pulse (sine wave).
/// Optional: radius flicker, color flicker, and random blackouts.
/// </summary>
[RequireComponent(typeof(Light2D))]
[DisallowMultipleComponent]
public class Light2DFlicker : MonoBehaviour
{
    public enum FlickerMode
    {
        Smooth,   // Smooth noise: torches, candles
        Random,   // Random jumps: broken fluorescent lights
        Pulse     // Regular breathing: alarm lights, magic glow
    }

    [Header("Mode")]
    [Tooltip("Smooth = fire, Random = broken light, Pulse = breathing")]
    public FlickerMode mode = FlickerMode.Smooth;

    [Header("Intensity")]
    [Tooltip("Lowest intensity")]
    [Min(0f)] public float minIntensity = 0.6f;
    [Tooltip("Highest intensity")]
    [Min(0f)] public float maxIntensity = 1.2f;

    [Header("Speed")]
    [Tooltip("Flicker speed (frequency for Smooth/Pulse; value changes per second for Random)")]
    [Min(0.01f)] public float speed = 8f;
    [Tooltip("0 = snap to target instantly, higher = softer transitions")]
    [Range(0f, 1f)] public float smoothing = 0.3f;

    [Header("Radius Flicker (Point Light only)")]
    public bool flickerRadius = false;
    [Tooltip("Outer radius variation (plus/minus this value)")]
    [Min(0f)] public float radiusAmount = 0.2f;

    [Header("Color Flicker")]
    public bool flickerColor = false;
    [Tooltip("Color when dim")]
    public Color dimColor = new Color(1f, 0.45f, 0.15f);
    [Tooltip("Color when bright")]
    public Color brightColor = new Color(1f, 0.85f, 0.55f);

    [Header("Blackout (broken light effect)")]
    public bool enableBlackout = false;
    [Tooltip("Chance per second of a blackout occurring (0-1)")]
    [Range(0f, 1f)] public float blackoutChancePerSecond = 0.15f;
    [Tooltip("Blackout duration range in seconds (min, max)")]
    public Vector2 blackoutDuration = new Vector2(0.05f, 0.25f);
    [Tooltip("Intensity during a blackout")]
    [Min(0f)] public float blackoutIntensity = 0f;

    [Header("Other")]
    [Tooltip("Randomize the starting offset so multiple lights don't flicker in sync")]
    public bool randomizeOffset = true;
    [Tooltip("Ignore Time.timeScale (keeps flickering while the game is paused)")]
    public bool useUnscaledTime = false;
    [Tooltip("Restore the original light values when this component is disabled")]
    public bool restoreOnDisable = true;

    private Light2D _light;
    private float _baseOuterRadius;
    private float _baseIntensity;
    private Color _baseColor;

    private float _offset;
    private float _current01;       // Current flicker value in 0-1
    private float _randomTarget01;
    private float _randomTimer;
    private float _blackoutTimer;

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    private float CurrentTime => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void Awake()
    {
        _light = GetComponent<Light2D>();
        _baseIntensity = _light.intensity;
        _baseOuterRadius = _light.pointLightOuterRadius;
        _baseColor = _light.color;
        _offset = randomizeOffset ? Random.Range(0f, 1000f) : 0f;
        _current01 = 0.5f;
        _randomTarget01 = Random.value;
    }

    private void OnDisable()
    {
        if (!restoreOnDisable || _light == null) return;
        _light.intensity = _baseIntensity;
        _light.pointLightOuterRadius = _baseOuterRadius;
        _light.color = _baseColor;
    }

    private void Update()
    {
        float dt = DeltaTime;
        float target01 = GetTarget01(dt);

        // Smoothing: 0 snaps instantly, closer to 1 is slower
        if (smoothing <= 0f)
        {
            _current01 = target01;
        }
        else
        {
            float lerpSpeed = Mathf.Lerp(60f, 2f, smoothing);
            _current01 = Mathf.Lerp(_current01, target01, 1f - Mathf.Exp(-lerpSpeed * dt));
        }

        // Blackout check
        bool isBlackout = UpdateBlackout(dt);

        // Apply intensity
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, _current01);
        _light.intensity = isBlackout ? blackoutIntensity : intensity;

        // Apply radius
        if (flickerRadius)
        {
            float r = _baseOuterRadius + Mathf.Lerp(-radiusAmount, radiusAmount, _current01);
            _light.pointLightOuterRadius = Mathf.Max(0.01f, r);
        }

        // Apply color
        if (flickerColor)
        {
            _light.color = Color.Lerp(dimColor, brightColor, _current01);
        }
    }

    private float GetTarget01(float dt)
    {
        float t = CurrentTime + _offset;

        switch (mode)
        {
            case FlickerMode.Smooth:
                // Two layers of noise blended together look more like real fire
                float n1 = Mathf.PerlinNoise(t * speed * 0.25f, 0f);
                float n2 = Mathf.PerlinNoise(0f, t * speed);
                return Mathf.Clamp01(n1 * 0.6f + n2 * 0.4f);

            case FlickerMode.Random:
                _randomTimer -= dt;
                if (_randomTimer <= 0f)
                {
                    _randomTarget01 = Random.value;
                    _randomTimer = 1f / speed;
                }
                return _randomTarget01;

            case FlickerMode.Pulse:
                return (Mathf.Sin(t * speed) + 1f) * 0.5f;
        }
        return 1f;
    }

    private bool UpdateBlackout(float dt)
    {
        if (!enableBlackout) return false;

        if (_blackoutTimer > 0f)
        {
            _blackoutTimer -= dt;
            return true;
        }

        if (Random.value < blackoutChancePerSecond * dt)
        {
            float minD = Mathf.Min(blackoutDuration.x, blackoutDuration.y);
            float maxD = Mathf.Max(blackoutDuration.x, blackoutDuration.y);
            _blackoutTimer = Random.Range(minD, maxD);
            return true;
        }
        return false;
    }

    private void OnValidate()
    {
        if (maxIntensity < minIntensity) maxIntensity = minIntensity;
    }
}
using System.Collections;
using UnityEngine;

/// <summary>
/// Shakes the camera (scene objects) and a UI root (Screen Space canvas) together,
/// using the same on-screen offset so they stay perfectly in sync.
/// </summary>
[DefaultExecutionOrder(1000)] // Run after other scripts that move the camera
public class ScreenShake : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Camera to shake (scene objects). Uses Camera.main if empty.")]
    public Camera targetCamera;
    [Tooltip("Full-screen RectTransform under the Canvas that contains all UI to shake")]
    public RectTransform uiShakeRoot;

    [Header("Default Settings")]
    [Tooltip("How long the shake lasts (seconds)")]
    public float duration = 0.3f;
    [Tooltip("Max shake distance in screen pixels (same for scene and UI)")]
    public float strength = 15f;
    [Tooltip("How fast it shakes")]
    public float frequency = 25f;
    [Tooltip("Keep shaking even when Time.timeScale is 0")]
    public bool useUnscaledTime = true;

    private Canvas canvas;
    private float timeLeft;
    private float currentDuration;
    private float currentStrength;
    private float seedX, seedY;

    private Vector3 appliedCamOffset;
    private Vector2 appliedUiOffset;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (uiShakeRoot != null)
            canvas = uiShakeRoot.GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        StartCoroutine(RemoveOffsetAfterRender());
    }

    private void OnDisable()
    {
        RemoveOffset();
    }

    // ================= Public API =================

    /// <summary>Shakes using the default settings.</summary>
    public void Shake()
    {
        Shake(duration, strength);
    }

    /// <summary>Shakes with a custom duration and strength (pixels).</summary>
    public void Shake(float shakeDuration, float shakeStrength)
    {
        currentDuration = Mathf.Max(0.01f, shakeDuration);
        currentStrength = shakeStrength;
        timeLeft = currentDuration;
        seedX = Random.value * 100f;
        seedY = Random.value * 100f + 50f;
    }

    // ================= Shake =================

    private void LateUpdate()
    {
        if (timeLeft <= 0f) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;
        timeLeft -= dt;

        float decay = Mathf.Clamp01(timeLeft / currentDuration);

        // Same pixel offset for both scene and UI
        Vector2 pixelOffset = new Vector2(
            Mathf.PerlinNoise(seedX, now * frequency) * 2f - 1f,
            Mathf.PerlinNoise(seedY, now * frequency) * 2f - 1f
        ) * currentStrength * decay;

        // Camera: moving the camera by -X makes the world move by +X on screen
        if (targetCamera != null)
        {
            float worldPerPixel = GetWorldUnitsPerPixel();
            Transform cam = targetCamera.transform;
            appliedCamOffset = (cam.right * -pixelOffset.x + cam.up * -pixelOffset.y) * worldPerPixel;
            cam.position += appliedCamOffset;
        }

        // UI: convert screen pixels to canvas units
        if (uiShakeRoot != null)
        {
            float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
            appliedUiOffset = pixelOffset / scale;
            uiShakeRoot.anchoredPosition += appliedUiOffset;
        }
    }

    // Removes the offset after the frame is rendered,
    // so other scripts always see the original positions.
    private IEnumerator RemoveOffsetAfterRender()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            RemoveOffset();
        }
    }

    private void RemoveOffset()
    {
        if (targetCamera != null && appliedCamOffset != Vector3.zero)
            targetCamera.transform.position -= appliedCamOffset;

        if (uiShakeRoot != null && appliedUiOffset != Vector2.zero)
            uiShakeRoot.anchoredPosition -= appliedUiOffset;

        appliedCamOffset = Vector3.zero;
        appliedUiOffset = Vector2.zero;
    }

    private float GetWorldUnitsPerPixel()
    {
        if (targetCamera.orthographic)
            return (targetCamera.orthographicSize * 2f) / Screen.height;

        // Perspective: use the distance to the z = 0 plane
        float distance = Mathf.Abs(targetCamera.transform.position.z);
        float worldHeight = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return worldHeight / Screen.height;
    }
}
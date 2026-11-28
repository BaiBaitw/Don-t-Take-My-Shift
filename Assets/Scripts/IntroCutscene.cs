using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opening cutscene played when the scene loads:
/// 1. gameUI is placed below its normal position right away (no animation)
/// 2. Cinematic black bars appear at the top and bottom of the screen
/// 3. Camera starts on a zoomed-in opening shot and zooms out to a close-up on the Player's start position
///    (or starts straight on the close-up if Opening Zoom-out is off)
/// 4. Player drops from the sky to its start position and plays a landing effect
/// 5. After a hold time, the camera moves to the Boss's Y position and BossAct() is called on BossManager
/// Call CameraBackToDefault() from another script to end the cutscene:
/// camera returns to its original position/zoom, black bars slide off screen, gameUI slides back up.
/// Requires DOTween (no extra modules needed).
/// </summary>
public class IntroCutscene : MonoBehaviour
{
    public enum LandingEffect { None, Squash, SquashAndStretch, Punch, Jelly }

    [Header("References")]
    [Tooltip("Camera to control. Leave empty to use Camera.main")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("Player Transform. Its position in the scene is the landing (start) position")]
    [SerializeField] private Transform player;

    [Tooltip("Boss GameObject. Hidden when the scene loads, shown when the camera reaches it. A child object under it has the BossManager script (BossAct() is called on it)")]
    [SerializeField] private GameObject boss;

    [Tooltip("Game UI (RectTransform). Placed below its normal position when the scene loads")]
    [SerializeField] private RectTransform gameUI;

    [Tooltip("Play the cutscene automatically when the scene loads. If off, call PlayIntro() from another script")]
    [SerializeField] private bool playOnStart = true;

    [Header("Game UI")]
    [Tooltip("How far below its normal position gameUI is placed at scene load (UI units, negative = down)")]
    [SerializeField] private float gameUIHiddenOffsetY = -300f;

    [Tooltip("Time (seconds) for gameUI to slide back up in CameraBackToDefault()")]
    [SerializeField] private float gameUIReturnDuration = 0.8f;

    [Tooltip("Speed curve for gameUI sliding back up")]
    [SerializeField] private AnimationCurve gameUIReturnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Letterbox (Cinematic Black Bars)")]
    [Tooltip("Canvas to create the black bars in (Screen Space). Its root Canvas is used, so the bars always cover the whole screen and are not affected by gameUI moving. Leave empty to create a separate Canvas automatically")]
    [SerializeField] private Canvas letterboxCanvas;

    [Tooltip("Height of EACH black bar as a fraction of the screen height (0.12 = 12% top + 12% bottom)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float barHeight = 0.12f;

    [Tooltip("Color of the bars")]
    [SerializeField] private Color barColor = Color.black;

    [Tooltip("Only used when Letterbox Canvas is empty: sorting order of the auto-created Canvas (higher = drawn on top of other UI)")]
    [SerializeField] private int barSortingOrder = 100;

    [Tooltip("Time (seconds) for the bars to slide off screen in CameraBackToDefault()")]
    [SerializeField] private float barOutDuration = 0.6f;

    [Tooltip("Speed curve for the bars sliding off screen")]
    [SerializeField] private AnimationCurve barOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Camera Close-up")]
    [Tooltip("Orthographic camera: Orthographic Size during the close-up (smaller = closer)")]
    [SerializeField] private float closeUpSize = 3f;

    [Tooltip("Perspective camera: Field of View during the close-up (smaller = closer)")]
    [SerializeField] private float closeUpFieldOfView = 30f;

    [Header("Opening Zoom-out")]
    [Tooltip("When ON, the camera starts from a zoomed-in shot and zooms out to the close-up above before the Player falls")]
    [SerializeField] private bool enableOpeningZoomOut = true;

    [Tooltip("Orthographic camera: Orthographic Size of the first (zoomed-in) shot. Should be smaller than Close Up Size")]
    [SerializeField] private float openingSize = 1f;

    [Tooltip("Perspective camera: Field of View of the first (zoomed-in) shot. Should be smaller than Close Up Field Of View")]
    [SerializeField] private float openingFieldOfView = 15f;

    [Tooltip("Position of the first shot, as an offset from the Player's start position (see the green gizmo)")]
    [SerializeField] private Vector2 openingOffset = Vector2.zero;

    [Tooltip("Seconds the camera holds the first shot before zooming out")]
    [SerializeField] private float openingHoldTime = 0.5f;

    [Tooltip("Time (seconds) the zoom-out takes")]
    [SerializeField] private float openingZoomDuration = 1.5f;

    [Tooltip("Speed curve of the zoom-out")]
    [SerializeField] private AnimationCurve openingZoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Player Drop")]
    [Tooltip("Wait (seconds) before the Player starts falling (counted after the opening zoom-out, if it's on)")]
    [SerializeField] private float startDelay = 0.5f;

    [Tooltip("How high above the start position the Player starts falling from (see the cyan gizmo in the Scene view)")]
    [SerializeField] private float dropHeight = 10f;

    [Tooltip("Time (seconds) the fall takes")]
    [SerializeField] private float dropDuration = 0.8f;

    [Tooltip("Speed curve of the fall (default speeds up like gravity)")]
    [SerializeField] private AnimationCurve dropCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f));

    [Header("Landing Effect")]
    [Tooltip("Deformation played when the Player lands")]
    [SerializeField] private LandingEffect landingEffect = LandingEffect.SquashAndStretch;

    [Tooltip("Strength of the deformation (0.3 = 30%)")]
    [SerializeField] private float landingStrength = 0.3f;

    [Tooltip("Time (seconds) the landing effect takes")]
    [SerializeField] private float landingDuration = 0.4f;

    [Header("Camera To Boss")]
    [Tooltip("Time (seconds) the camera holds on the Player after landing")]
    [SerializeField] private float holdTime = 2f;

    [Tooltip("Offset added to the Boss's Y position for where the camera stops (negative = lower than the Boss, see the red gizmo line)")]
    [SerializeField] private float bossYOffset = 0f;

    [Tooltip("Time (seconds) for the camera to move to the Boss's Y position")]
    [SerializeField] private float toBossDuration = 1.5f;

    [Tooltip("Speed curve for the camera moving to the Boss")]
    [SerializeField] private AnimationCurve toBossCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Seconds after the camera STARTS moving down before the Boss appears and BossAct() is called (0 = at the same moment the camera starts moving)")]
    [SerializeField] private float bossActDelay = 0f;

    [Header("Camera Back To Default")]
    [Tooltip("Time (seconds) for the camera to return to its original position and zoom")]
    [SerializeField] private float backDuration = 1f;

    [Tooltip("Speed curve for the camera returning")]
    [SerializeField] private AnimationCurve backCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Gizmos")]
    [Tooltip("Show helper lines in the Scene view (sky point, close-up frame, Boss Y line)")]
    [SerializeField] private bool showGizmos = true;

    // ===== Runtime state =====
    private Vector3 defaultCamPos;
    private float defaultOrthoSize;
    private float defaultFieldOfView;
    private Vector2 gameUIDefaultPos;
    private Vector3 playerLandPos;
    private Vector3 playerScale;
    private Rigidbody2D playerRb;
    private bool playerRbWasSimulated;

    private RectTransform letterboxRoot;
    private RectTransform topBar;
    private RectTransform bottomBar;
    private float barProgress;          // 0 = bars fully on screen, 1 = bars fully off screen

    private Coroutine introRoutine;
    private bool playerInAir;

    /// <summary>True while the intro is playing (from scene load until the camera reaches the Boss)</summary>
    public bool IsPlaying => introRoutine != null;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        // Remember all original values once, before anything is changed
        if (targetCamera != null)
        {
            defaultCamPos = targetCamera.transform.position;
            defaultOrthoSize = targetCamera.orthographicSize;
            defaultFieldOfView = targetCamera.fieldOfView;
        }

        if (gameUI != null)
        {
            gameUIDefaultPos = gameUI.anchoredPosition;
        }

        if (player != null)
        {
            playerLandPos = player.position;
            playerScale = player.localScale;
            playerRb = player.GetComponent<Rigidbody2D>();
        }

        // Boss stays hidden until the camera reaches it
        if (boss != null)
        {
            boss.SetActive(false);
        }

        CreateLetterbox();
        SetBarsVisible(false);
    }

    private void Start()
    {
        if (playOnStart) PlayIntro();
    }

    private void OnDestroy()
    {
        DOTween.Kill(this);

        // Bars live in another canvas, so clean them up with this script
        if (letterboxRoot != null) Destroy(letterboxRoot.gameObject);
    }

    // ===== Public control methods =====

    /// <summary>Set up the opening shot and play the intro (called automatically if Play On Start is on)</summary>
    public void PlayIntro()
    {
        StopIntro();
        DOTween.Kill(this);

        // gameUI goes straight to the lower position (no animation)
        if (gameUI != null)
        {
            gameUI.anchoredPosition = gameUIDefaultPos + Vector2.up * gameUIHiddenOffsetY;
        }

        // Black bars fully on screen
        barProgress = 0f;
        SetBarsVisible(true);
        ApplyBars();

        // Camera: start on the zoomed-in opening shot, or straight on the close-up
        if (targetCamera != null)
        {
            if (enableOpeningZoomOut)
            {
                targetCamera.transform.position = OpeningCamPos;
                if (targetCamera.orthographic) targetCamera.orthographicSize = openingSize;
                else targetCamera.fieldOfView = openingFieldOfView;
            }
            else
            {
                targetCamera.transform.position = CloseUpCamPos;
                if (targetCamera.orthographic) targetCamera.orthographicSize = closeUpSize;
                else targetCamera.fieldOfView = closeUpFieldOfView;
            }
        }

        // Player waits in the sky
        if (player != null)
        {
            player.localScale = playerScale;
            player.position = SkyPosition;

            // Stop physics from pulling the Player during the scripted fall
            if (playerRb != null)
            {
                playerRbWasSimulated = playerRb.simulated;
                playerRb.simulated = false;
            }
            playerInAir = true;
        }

        introRoutine = StartCoroutine(IntroRoutine());
    }

    /// <summary>
    /// End the cutscene: camera returns to its original position/zoom,
    /// black bars slide off screen, gameUI slides back up to its original position.
    /// </summary>
    public void CameraBackToDefault()
    {
        // If called while the intro is still playing, finish the Player's fall instantly
        StopIntro();
        DOTween.Kill(this);
        FinishPlayerLanding();

        // Camera back to original position and zoom
        if (targetCamera != null)
        {
            Transform camT = targetCamera.transform;
            camT.DOMove(defaultCamPos, backDuration).SetEase(backCurve).SetId(this);

            if (targetCamera.orthographic)
            {
                DOTween.To(() => targetCamera.orthographicSize, x => targetCamera.orthographicSize = x, defaultOrthoSize, backDuration)
                    .SetEase(backCurve).SetId(this);
            }
            else
            {
                DOTween.To(() => targetCamera.fieldOfView, x => targetCamera.fieldOfView = x, defaultFieldOfView, backDuration)
                    .SetEase(backCurve).SetId(this);
            }
        }

        // Black bars slide up / down off screen
        DOTween.To(() => barProgress, x => { barProgress = x; ApplyBars(); }, 1f, barOutDuration)
            .SetEase(barOutCurve).SetId(this)
            .OnComplete(() => SetBarsVisible(false));

        // gameUI slides back up to its original position
        if (gameUI != null)
        {
            DOTween.To(() => gameUI.anchoredPosition, v => gameUI.anchoredPosition = v, gameUIDefaultPos, gameUIReturnDuration)
                .SetEase(gameUIReturnCurve).SetId(this);
        }
    }

    // ===== Intro sequence =====

    private IEnumerator IntroRoutine()
    {
        // 0. Opening zoom-out: zoomed-in first shot -> close-up on the Player's start position
        if (enableOpeningZoomOut && targetCamera != null)
        {
            if (openingHoldTime > 0f) yield return new WaitForSeconds(openingHoldTime);

            Sequence zoomOut = DOTween.Sequence().SetId(this);
            zoomOut.Append(targetCamera.transform.DOMove(CloseUpCamPos, openingZoomDuration).SetEase(openingZoomCurve));

            if (targetCamera.orthographic)
            {
                zoomOut.Join(DOTween.To(() => targetCamera.orthographicSize, x => targetCamera.orthographicSize = x,
                                        closeUpSize, openingZoomDuration).SetEase(openingZoomCurve));
            }
            else
            {
                zoomOut.Join(DOTween.To(() => targetCamera.fieldOfView, x => targetCamera.fieldOfView = x,
                                        closeUpFieldOfView, openingZoomDuration).SetEase(openingZoomCurve));
            }

            yield return zoomOut.WaitForCompletion();
        }

        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        // 1. Player falls from the sky to the start position (camera stays still)
        if (player != null)
        {
            Tween drop = player.DOMove(playerLandPos, dropDuration).SetEase(dropCurve).SetId(this);
            yield return drop.WaitForCompletion();

            FinishPlayerLanding();

            // 2. Landing deformation (hold time counts from the moment of landing)
            PlayLandingEffect();
        }

        // 3. Camera holds on the Player
        if (holdTime > 0f) yield return new WaitForSeconds(holdTime);

        // 4. Camera moves to the Boss's Y position (X stays the same)
        if (boss != null && targetCamera != null)
        {
            Vector3 target = targetCamera.transform.position;
            target.y = boss.transform.position.y + bossYOffset;

            // 5. Show the Boss and call BossAct() on BossManager (on a child object of boss)
            //    as soon as the camera starts moving (+ optional delay), not after it arrives
            Sequence toBoss = DOTween.Sequence().SetId(this);
            toBoss.Append(targetCamera.transform.DOMove(target, toBossDuration).SetEase(toBossCurve));
            toBoss.InsertCallback(Mathf.Max(0f, bossActDelay), () =>
            {
                boss.SetActive(true);
                boss.BroadcastMessage("BossAct", SendMessageOptions.RequireReceiver);
            });

            yield return toBoss.WaitForCompletion();
        }

        introRoutine = null;
    }

    private void StopIntro()
    {
        if (introRoutine != null)
        {
            StopCoroutine(introRoutine);
            introRoutine = null;
        }
    }

    /// <summary>Snap the Player onto its start position and give physics back</summary>
    private void FinishPlayerLanding()
    {
        if (player == null || !playerInAir) return;

        player.position = playerLandPos;
        player.localScale = playerScale;

        if (playerRb != null)
        {
            playerRb.simulated = playerRbWasSimulated;
        }
        playerInAir = false;
    }

    private void PlayLandingEffect()
    {
        if (player == null || landingEffect == LandingEffect.None) return;

        Vector3 s = playerScale;
        float a = landingStrength;
        float d = landingDuration;

        Sequence seq = DOTween.Sequence().SetId(this);

        switch (landingEffect)
        {
            case LandingEffect.Squash:
                // Flatten, then spring back
                seq.Append(player.DOScale(new Vector3(s.x * (1f + a), s.y * (1f - a), s.z), d * 0.3f).SetEase(Ease.OutQuad));
                seq.Append(player.DOScale(s, d * 0.7f).SetEase(Ease.OutBack));
                break;

            case LandingEffect.SquashAndStretch:
                // Flatten, stretch tall, then settle
                seq.Append(player.DOScale(new Vector3(s.x * (1f + a), s.y * (1f - a), s.z), d * 0.25f).SetEase(Ease.OutQuad));
                seq.Append(player.DOScale(new Vector3(s.x * (1f - a * 0.5f), s.y * (1f + a * 0.5f), s.z), d * 0.3f).SetEase(Ease.OutQuad));
                seq.Append(player.DOScale(s, d * 0.45f).SetEase(Ease.OutBack));
                break;

            case LandingEffect.Punch:
                // Quick wobbly squash
                seq.Append(player.DOPunchScale(new Vector3(s.x * a, -s.y * a, 0f), d, 8, 0.5f));
                break;

            case LandingEffect.Jelly:
                // Jelly-like shaking
                seq.Append(player.DOShakeScale(d, new Vector3(s.x * a, s.y * a, 0f), 12, 90f, true));
                break;
        }

        seq.OnComplete(() => player.localScale = playerScale);
    }

    private Vector3 SkyPosition => playerLandPos + Vector3.up * dropHeight;

    /// <summary>Camera position for the close-up on the Player's start position</summary>
    private Vector3 CloseUpCamPos => new Vector3(playerLandPos.x, playerLandPos.y, defaultCamPos.z);

    /// <summary>Camera position for the zoomed-in opening shot</summary>
    private Vector3 OpeningCamPos => new Vector3(playerLandPos.x + openingOffset.x, playerLandPos.y + openingOffset.y, defaultCamPos.z);

    // ===== Letterbox (black bars) =====

    private void CreateLetterbox()
    {
        Transform parent;

        if (letterboxCanvas != null)
        {
            // Use the ROOT canvas, so the bars always fill the whole screen
            // and don't move with gameUI (or any other moving UI)
            parent = letterboxCanvas.rootCanvas.transform;

            if (letterboxCanvas.rootCanvas.renderMode == RenderMode.WorldSpace)
            {
                Debug.LogWarning("[IntroCutscene] Letterbox Canvas is World Space. Use a Screen Space Canvas so the bars cover the screen.");
            }
        }
        else
        {
            // No canvas assigned: create a separate top-level Canvas (NOT parented to this object)
            GameObject canvasGO = new GameObject("LetterboxCanvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = barSortingOrder;
            parent = canvasGO.transform;
        }

        // Full-screen container that holds both bars
        GameObject rootGO = new GameObject("Letterbox", typeof(RectTransform));
        letterboxRoot = rootGO.GetComponent<RectTransform>();
        letterboxRoot.SetParent(parent, false);
        letterboxRoot.anchorMin = Vector2.zero;
        letterboxRoot.anchorMax = Vector2.one;
        letterboxRoot.offsetMin = letterboxRoot.offsetMax = Vector2.zero;
        letterboxRoot.localScale = Vector3.one;
        letterboxRoot.SetAsLastSibling();   // drawn on top of the other UI in this canvas

        topBar = CreateBar(letterboxRoot, "TopBar");
        bottomBar = CreateBar(letterboxRoot, "BottomBar");
        ApplyBars();
    }

    private RectTransform CreateBar(Transform parent, string barName)
    {
        GameObject go = new GameObject(barName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = barColor;
        img.raycastTarget = false;   // don't block clicks on buttons

        return go.GetComponent<RectTransform>();
    }

    /// <summary>Position the bars using anchors, so the height is always a fraction of the screen</summary>
    private void ApplyBars()
    {
        if (topBar == null || bottomBar == null) return;

        float h = barHeight;
        float shift = h * barProgress;

        topBar.anchorMin = new Vector2(0f, 1f - h + shift);
        topBar.anchorMax = new Vector2(1f, 1f + shift);
        topBar.offsetMin = topBar.offsetMax = Vector2.zero;

        bottomBar.anchorMin = new Vector2(0f, -shift);
        bottomBar.anchorMax = new Vector2(1f, h - shift);
        bottomBar.offsetMin = bottomBar.offsetMax = Vector2.zero;
    }

    private void SetBarsVisible(bool visible)
    {
        if (letterboxRoot != null) letterboxRoot.gameObject.SetActive(visible);
    }

    // ===== Scene view helper lines =====

    private void OnDrawGizmos()
    {
        if (!showGizmos || player == null) return;

        Vector3 land = Application.isPlaying ? playerLandPos : player.position;
        Vector3 sky = land + Vector3.up * dropHeight;

        // Cyan: fall path and sky point
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(land, sky);
        Gizmos.DrawWireSphere(sky, 0.3f);
        Gizmos.DrawLine(sky + Vector3.left * 1.5f, sky + Vector3.right * 1.5f);

        // Yellow: close-up camera frame (orthographic camera only)
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null && cam.orthographic)
        {
            float halfH = closeUpSize;
            float halfW = halfH * cam.aspect;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(new Vector3(land.x, land.y, land.z), new Vector3(halfW * 2f, halfH * 2f, 0f));

            // Area hidden by the black bars inside the close-up frame
            float barH = halfH * 2f * barHeight;
            Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
            Gizmos.DrawCube(new Vector3(land.x, land.y + halfH - barH * 0.5f, land.z), new Vector3(halfW * 2f, barH, 0f));
            Gizmos.DrawCube(new Vector3(land.x, land.y - halfH + barH * 0.5f, land.z), new Vector3(halfW * 2f, barH, 0f));

            // Green: zoomed-in opening shot (before zooming out to the yellow frame)
            if (enableOpeningZoomOut)
            {
                Vector3 o = new Vector3(land.x + openingOffset.x, land.y + openingOffset.y, land.z);
                float oHalfH = openingSize;
                float oHalfW = oHalfH * cam.aspect;
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(o, new Vector3(oHalfW * 2f, oHalfH * 2f, 0f));
                Gizmos.DrawWireSphere(o, 0.15f);
            }
        }

        // Red: the Y the camera moves to (Boss Y + offset)
        if (boss != null)
        {
            Vector3 b = new Vector3(land.x, boss.transform.position.y + bossYOffset, land.z);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(b + Vector3.left * 3f, b + Vector3.right * 3f);
            Gizmos.DrawLine(land, b);

            // Yellow: close-up camera frame at the Boss stop position (orthographic camera only)
            if (cam != null && cam.orthographic)
            {
                float halfH = closeUpSize;
                float halfW = halfH * cam.aspect;
                Gizmos.color = new Color(1f, 0.6f, 0f);
                Gizmos.DrawWireCube(b, new Vector3(halfW * 2f, halfH * 2f, 0f));
            }
        }
    }
}
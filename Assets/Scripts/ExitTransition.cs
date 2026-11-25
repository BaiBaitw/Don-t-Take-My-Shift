using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene exit transition:
/// 1. Camera zooms in on the Player's current position
/// 2. The IrisTransition prefab is spawned on top of the chosen Canvas (at the same moment)
/// 3. After a delay, the next scene is loaded
/// Call StartExit() from another script (or StartExitTo("SceneName") to go to a different scene).
/// Requires DOTween (no extra modules needed).
/// </summary>
public class ExitTransition : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera to control. Leave empty to use Camera.main")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("Player Transform. The camera zooms in on its position at the moment the exit starts")]
    [SerializeField] private Transform player;

    [Tooltip("IrisTransition prefab. Spawned on top of the Canvas below when the exit starts")]
    [SerializeField] private GameObject irisTransition;

    [Tooltip("Canvas to spawn IrisTransition in. It is placed as the last child, so it's drawn on top of the other UI")]
    [SerializeField] private Canvas targetCanvas;

    [Header("Camera Zoom In")]
    [Tooltip("Orthographic camera: Orthographic Size at the end of the zoom (smaller = closer)")]
    [SerializeField] private float zoomSize = 2f;

    [Tooltip("Perspective camera: Field of View at the end of the zoom (smaller = closer)")]
    [SerializeField] private float zoomFieldOfView = 25f;

    [Tooltip("Height offset added to the Player's Y for where the camera ends up (positive = higher, negative = lower, see the magenta gizmo)")]
    [SerializeField] private float zoomYOffset = 0f;

    [Tooltip("Time (seconds) the zoom takes")]
    [SerializeField] private float zoomDuration = 1f;

    [Tooltip("Speed curve of the zoom")]
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Scene Change")]
    [Tooltip("Name of the scene to load (must be added in File > Build Settings)")]
    [SerializeField] private string nextSceneName;

    [Tooltip("Seconds after the exit starts before the next scene is loaded")]
    [SerializeField] private float loadDelay = 2f;

    [Header("Gizmos")]
    [Tooltip("Show the zoom-in camera frame around the Player in the Scene view")]
    [SerializeField] private bool showGizmos = true;

    private bool isExiting = false;

    /// <summary>True once the exit has started</summary>
    public bool IsExiting => isExiting;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void OnDestroy()
    {
        DOTween.Kill(this);
    }

    // ===== Public control methods =====

    /// <summary>Start the exit and load the scene set in Next Scene Name</summary>
    public void StartExit()
    {
        StartExitTo(nextSceneName);
    }

    /// <summary>Start the exit and load the given scene (instead of Next Scene Name)</summary>
    public void StartExitTo(string sceneName)
    {
        if (isExiting) return;   // ignore repeated calls

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[ExitTransition] No scene name set!");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[ExitTransition] Scene \"{sceneName}\" is not in File > Build Settings!");
            return;
        }

        isExiting = true;
        StartCoroutine(ExitRoutine(sceneName));
    }

    // ===== Exit sequence =====

    private IEnumerator ExitRoutine(string sceneName)
    {
        // 1. Camera zooms in on the Player
        ZoomToPlayer();

        // 2. Spawn IrisTransition on top of the Canvas (same moment)
        SpawnIris();

        // 3. Wait, then load the next scene
        if (loadDelay > 0f) yield return new WaitForSeconds(loadDelay);

        SceneManager.LoadScene(sceneName);
    }

    private void ZoomToPlayer()
    {
        if (targetCamera == null) return;

        Transform camT = targetCamera.transform;

        // Stop any other camera movement (e.g. IntroCutscene) so they don't fight
        DOTween.Kill(camT);

        if (player != null)
        {
            Vector3 target = camT.position;
            target.x = player.position.x;
            target.y = player.position.y + zoomYOffset;
            camT.DOMove(target, zoomDuration).SetEase(zoomCurve).SetId(this);
        }

        if (targetCamera.orthographic)
        {
            DOTween.To(() => targetCamera.orthographicSize, x => targetCamera.orthographicSize = x, zoomSize, zoomDuration)
                .SetEase(zoomCurve).SetId(this);
        }
        else
        {
            DOTween.To(() => targetCamera.fieldOfView, x => targetCamera.fieldOfView = x, zoomFieldOfView, zoomDuration)
                .SetEase(zoomCurve).SetId(this);
        }
    }

    private void SpawnIris()
    {
        if (irisTransition == null)
        {
            Debug.LogWarning("[ExitTransition] IrisTransition prefab is not assigned.");
            return;
        }

        if (targetCanvas == null)
        {
            Debug.LogWarning("[ExitTransition] Target Canvas is not assigned.");
            return;
        }

        GameObject iris = Instantiate(irisTransition, targetCanvas.transform, false);
        iris.transform.SetAsLastSibling();   // drawn on top of everything else in this canvas
    }

    // ===== Test =====

    [ContextMenu("Test: Start Exit")]
    private void TestStartExit()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ExitTransition] Enter Play Mode first to test the exit.");
            return;
        }

        StartExit();
    }

    // ===== Scene view helper =====

    private void OnDrawGizmos()
    {
        if (!showGizmos || player == null) return;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || !cam.orthographic) return;

        // Magenta: what the camera sees at the end of the zoom (Player's position + Y offset)
        Vector3 center = player.position + Vector3.up * zoomYOffset;
        float halfH = zoomSize;
        float halfW = halfH * cam.aspect;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(center, new Vector3(halfW * 2f, halfH * 2f, 0f));
        Gizmos.DrawWireSphere(center, 0.15f);   // camera end point
    }
}
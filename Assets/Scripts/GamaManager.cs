using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Header("Debug Settings")]
    [Tooltip("When OFF, the Start Button and Next Button are never shown (they are only used for testing levels)")]
    [SerializeField] private bool debugMode = false;

    [Header("UI Element Settings")]
    [Tooltip("TextMeshProUGUI component displaying progress count")]
    [SerializeField] private TextMeshProUGUI levelText;

    [Tooltip("Button to start the game initially (only shown in debug mode)")]
    [SerializeField] private Button startButton;

    [Tooltip("Button to advance to the next level/stage when finished (only shown in debug mode)")]
    [SerializeField] private Button nextButton;

    [Header("Count & Stage Parameters")]
    [Tooltip("Total count required to complete (formerly totalLevels)")]
    [SerializeField] private int totalCounts = 3;

    [Header("Background Scene Settings")]
    [Tooltip("GameObject with a SpriteRenderer component for the background")]
    [SerializeField] private GameObject backgroundObject;

    [Tooltip("Array of background sprites for different scenes/levels")]
    [SerializeField] private Sprite[] backgroundScenes;

    [Header("External Manager Settings")]
    [Tooltip("GameObject containing the external game manager (e.g., TypeGameManager) with IncreaseDifficulty() and StartGame() functions")]
    [SerializeField] private GameObject typeGameManager;

    [Header("Timer Settings")]
    [Tooltip("GameObject attached with CountdownBar script")]
    [SerializeField] private GameObject timerManager;

    [Header("Player Settings")]
    [Tooltip("GameObject attached with PlayerScript")]
    [SerializeField] private GameObject player;

    [Header("Boss Settings")]
    [Tooltip("Boss GameObject. A child object under it has the BossManager script. BossBubbleChat() is called when the stage is completed")]
    [SerializeField] private GameObject boss;

    [Header("Exit Settings")]
    [Tooltip("Object with the ExitTransition script. StartExit() is called when the stage has been finished Stages To Exit times")]
    [SerializeField] private ExitTransition exitTransition;

    [Tooltip("How many times the stage must be finished before StartExit() is called")]
    [SerializeField] private int stagesToExit = 3;

    [Header("Game Over Transition")]
    [Tooltip("When ON, Game Over plays the transition (camera zoom, Game UI slide down, IrisTransition, scene change). When OFF, none of it happens")]
    [SerializeField] private bool enableGameOverTransition = true;

    [Tooltip("Camera to zoom in on Game Over. Leave empty to use Camera.main")]
    [SerializeField] private Camera gameOverCamera;

    [Tooltip("IrisTransition prefab. Spawned on top of the Canvas below after Iris Delay seconds")]
    [SerializeField] private GameObject irisTransition;

    [Tooltip("Canvas to spawn IrisTransition in (use a Screen Space root Canvas). It is placed as the last child, so it's drawn on top")]
    [SerializeField] private Canvas irisCanvas;

    [Tooltip("Orthographic camera: Orthographic Size at the end of the zoom (smaller = closer)")]
    [SerializeField] private float gameOverZoomSize = 2f;

    [Tooltip("Perspective camera: Field of View at the end of the zoom (smaller = closer)")]
    [SerializeField] private float gameOverZoomFieldOfView = 25f;

    [Tooltip("Horizontal offset added to the Player's X for where the camera ends up (positive = right, negative = left, see the red gizmo)")]
    [SerializeField] private float gameOverZoomXOffset = 0f;

    [Tooltip("Height offset added to the Player's Y for where the camera ends up (positive = higher, see the red gizmo)")]
    [SerializeField] private float gameOverZoomYOffset = 0f;

    [Tooltip("Time (seconds) the zoom takes")]
    [SerializeField] private float gameOverZoomDuration = 1f;

    [Tooltip("Speed curve of the zoom")]
    [SerializeField] private AnimationCurve gameOverZoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Seconds after Game Over starts before IrisTransition is spawned")]
    [SerializeField] private float irisDelay = 1f;

    [Tooltip("Seconds after IrisTransition is spawned before the next scene is loaded")]
    [SerializeField] private float gameOverLoadDelay = 2f;

    [Tooltip("Name of the scene to load after Game Over (must be added in File > Build Settings)")]
    [SerializeField] private string gameOverSceneName;

    [Tooltip("Show the Game Over zoom-in camera frame around the Player in the Scene view")]
    [SerializeField] private bool showGameOverGizmo = true;

    [Header("Game Over: Game UI")]
    [Tooltip("Game UI (RectTransform). Slides down when Game Over starts")]
    [SerializeField] private RectTransform gameUI;

    [Tooltip("Seconds after Game Over starts before Game UI starts moving down")]
    [SerializeField] private float gameUIDropDelay = 0f;

    [Tooltip("How far Game UI moves (UI units, negative = down)")]
    [SerializeField] private float gameUIDropOffsetY = -300f;

    [Tooltip("Time (seconds) Game UI takes to move down")]
    [SerializeField] private float gameUIDropDuration = 0.8f;

    [Tooltip("Speed curve of Game UI moving down")]
    [SerializeField] private AnimationCurve gameUIDropCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("NPC Settings")]
    [Tooltip("NPC objects (each with NpcScript). Each time Next is pressed, a random number of them are picked, RobotSwitch() is called, and they are removed from this list")]
    [SerializeField] private List<GameObject> npcObjects = new List<GameObject>();

    [Tooltip("Minimum number of NPCs picked per Next press")]
    [SerializeField] private int minNpcCount = 1;

    [Tooltip("Maximum number of NPCs picked per Next press (inclusive)")]
    [SerializeField] private int maxNpcCount = 3;

    [Header("Next Button Settings")]
    [Tooltip("Delay (seconds) after pressing Next before the rest of the next-level setup runs (NPCs switch immediately)")]
    [SerializeField] private float nextStageDelay = 1f;

    // Internal states
    private bool isTransitioning = false;
    private bool gameOverTransitionStarted = false;
    private int completedStages = 0;
    private int finishedStageCount = 0;   // how many times "Finished this stage" has happened
    private int currentSceneIndex = 0;
    private SpriteRenderer bgSpriteRenderer;

    /// <summary>
    /// Public property to read whether debug mode is on (Start / Next buttons are shown).
    /// </summary>
    public bool DebugMode => debugMode;

    /// <summary>
    /// Public property to read whether all stages in current session are finished.
    /// </summary>
    public bool IsFinished { get; protected set; } = false;   // protected set: SupermanGameManager

    /// <summary>
    /// Public property to read whether the game is over.
    /// </summary>
    public bool IsGameOver { get; protected set; } = false;   // protected set: SupermanGameManager

    /// <summary>
    /// Public property to read or dynamically update total counts.
    /// </summary>
    public int TotalCounts
    {
        get => totalCounts;
        set
        {
            totalCounts = Mathf.Max(1, value);
            InitializeLevelUI();
        }
    }

    /// <summary>
    /// Public property to read completed stage count.
    /// </summary>
    public int CompletedStages => completedStages;

    /// <summary>
    /// Public property to read how many times the stage has been finished ("Finished this stage").
    /// </summary>
    public int FinishedStageCount => finishedStageCount;

    private void Awake()
    {
        // Try getting SpriteRenderer from backgroundObject
        if (backgroundObject != null)
        {
            bgSpriteRenderer = backgroundObject.GetComponent<SpriteRenderer>();
        }

        // Setup startButton click event
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);

            // Start Button is only shown in debug mode
            if (!debugMode)
            {
                startButton.gameObject.SetActive(false);
            }
        }

        // Setup nextButton click event
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextButtonClicked);

            // Next Button is only shown in debug mode
            if (!debugMode)
            {
                nextButton.gameObject.SetActive(false);
            }
        }
    }

    private void Start()
    {
        InitializeLevelUI();
        UpdateBackground();
    }

    /// <summary>
    /// Called when startButton is clicked. Triggers StartGame() on typeGameManager and StartTimer() on timerManager.
    /// Public, so other scripts can also call it to start the game when debug mode is off.
    /// </summary>
    public void OnStartButtonClicked()
    {
        if (typeGameManager != null)
        {
            typeGameManager.SendMessage("StartGame", SendMessageOptions.DontRequireReceiver);
        }

        // Start timer on CountdownBar script
        if (timerManager != null)
        {
            timerManager.SendMessage("StartTimer", SendMessageOptions.DontRequireReceiver);
        }

        if (startButton != null)
        {
            startButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Initializes level text and hides the Next button.
    /// </summary>
    public void InitializeLevelUI()
    {
        completedStages = 0;
        IsFinished = false;
        IsGameOver = false;

        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(false);
        }

        UpdateTextUI();
    }

    /// <summary>
    /// Updates TextMeshPro text display (Format: CompletedStages/TotalCounts).
    /// </summary>
    private void UpdateTextUI()
    {
        if (levelText != null)
        {
            levelText.text = $"{completedStages}/{totalCounts}";
        }
    }

    /// <summary>
    /// Completes the current stage. When all are completed, pauses timer, logs message,
    /// and shows the Next Button (debug mode only).
    /// </summary>
    public virtual void CompleteCurrentStage()   // virtual: overridden by SupermanGameManager
    {
        if (IsFinished || IsGameOver) return;

        if (completedStages < totalCounts)
        {
            completedStages++;
            UpdateTextUI();

            if (completedStages == totalCounts)
            {
                IsFinished = true;
                finishedStageCount++;
                Debug.Log($"Finished this stage ({finishedStageCount}/{stagesToExit})");

                // Pause timer on CountdownBar script when finishing the stage
                if (timerManager != null)
                {
                    timerManager.SendMessage("PauseTimer", SendMessageOptions.DontRequireReceiver);
                }

                // Call BossBubbleChat() on BossManager (on a child object of boss)
                // Skipped on the finish that triggers the exit
                if (boss != null && finishedStageCount < stagesToExit)
                {
                    boss.BroadcastMessage("BossBubbleChat", SendMessageOptions.RequireReceiver);
                }

                // Finished enough times: leave the scene
                if (finishedStageCount >= stagesToExit)
                {
                    if (exitTransition != null)
                    {
                        exitTransition.StartExit();
                    }
                    else
                    {
                        Debug.LogWarning("[GameManager] Exit Transition is not assigned, can't call StartExit().");
                    }
                    return;   // no Next Button, the scene is ending
                }

                // Show Next Button upon completion (debug mode only)
                if (debugMode && nextButton != null)
                {
                    nextButton.gameObject.SetActive(true);
                }
            }
        }
    }

    /// <summary>
    /// Called when Next Button is pressed.
    /// Public, so other scripts can also call it to go to the next stage when debug mode is off.
    /// Immediately activates random NPCs, then after nextStageDelay seconds runs the rest of the next-level setup.
    /// </summary>
    public void OnNextButtonClicked()
    {
        // Prevent pressing Next again while waiting for the delay
        if (isTransitioning) return;
        isTransitioning = true;

        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(false);
        }

        // Runs immediately (no delay)
        ActivateRandomNpcs();

        // Everything else runs after the delay
        StartCoroutine(NextStageRoutine());
    }

    /// <summary>
    /// Picks a random number (minNpcCount ~ maxNpcCount) of NPCs from npcObjects,
    /// calls RobotSwitch() on their NpcScript, and removes them from the list.
    /// </summary>
    private void ActivateRandomNpcs()
    {
        // Clean out empty slots first
        npcObjects.RemoveAll(npc => npc == null);
        if (npcObjects.Count == 0) return;

        int min = Mathf.Max(0, Mathf.Min(minNpcCount, maxNpcCount));
        int max = Mathf.Max(minNpcCount, maxNpcCount);
        int count = Random.Range(min, max + 1);           // max is inclusive
        count = Mathf.Min(count, npcObjects.Count);       // can't pick more than remain

        for (int i = 0; i < count; i++)
        {
            int index = Random.Range(0, npcObjects.Count);
            GameObject npc = npcObjects[index];
            npcObjects.RemoveAt(index);

            if (npc.GetComponent("NpcScript") == null)
            {
                Debug.LogWarning($"[GameManager] {npc.name} has no NpcScript, skipped.");
                continue;
            }

            npc.SendMessage("RobotSwitch", SendMessageOptions.RequireReceiver);
        }
    }

    /// <summary>
    /// Rest of the Next button logic, run after nextStageDelay seconds.
    /// NOTE: RestartTimer() must be called AFTER InitializeLevelUI(), because CountdownBar.StartTimer()
    /// refuses to start while IsFinished is still true.
    /// </summary>
    private IEnumerator NextStageRoutine()
    {
        if (nextStageDelay > 0f)
        {
            yield return new WaitForSeconds(nextStageDelay);
        }

        // 1. Call IncreaseDifficulty() on typeGameManager
        if (typeGameManager != null)
        {
            typeGameManager.SendMessage("IncreaseDifficulty", SendMessageOptions.DontRequireReceiver);
        }

        // 2. Increase total counts for the next stage
        totalCounts++;

        // 3. Switch to next background scene in array
        if (backgroundScenes != null && backgroundScenes.Length > 0)
        {
            currentSceneIndex = (currentSceneIndex + 1) % backgroundScenes.Length;
            UpdateBackground();
        }

        // 4. Re-initialize UI for the new level (this sets IsFinished back to false)
        InitializeLevelUI();

        // 5. Restart the timer now that IsFinished is false (refill + start counting)
        if (timerManager != null)
        {
            timerManager.SendMessage("RestartTimer", SendMessageOptions.DontRequireReceiver);
        }

        // 6. Directly start the game on typeGameManager
        if (typeGameManager != null)
        {
            typeGameManager.SendMessage("StartGame", SendMessageOptions.DontRequireReceiver);
        }

        isTransitioning = false;
    }

    /// <summary>
    /// Updates the SpriteRenderer with the current background sprite.
    /// </summary>
    private void UpdateBackground()
    {
        if (bgSpriteRenderer != null && backgroundScenes != null && backgroundScenes.Length > 0)
        {
            bgSpriteRenderer.sprite = backgroundScenes[currentSceneIndex];
        }
    }

    /// <summary>
    /// Triggers Game Over state and invokes GameOver() on PlayerScript attached to player GameObject.
    /// </summary>
    public virtual void GameOver()   // virtual: overridden by SupermanGameManager
    {
        if (IsGameOver) return;

        IsGameOver = true;
        Debug.Log("Game Over");

        // Call GameOver() on PlayerScript attached to player GameObject
        if (player != null)
        {
            player.SendMessage("GameOver", SendMessageOptions.DontRequireReceiver);
        }

        // Zoom in on the Player, spawn IrisTransition, then load the Game Over scene
        if (enableGameOverTransition && !gameOverTransitionStarted)
        {
            gameOverTransitionStarted = true;
            StartCoroutine(GameOverTransitionRoutine());
        }
    }

    /// <summary>
    /// Game Over transition: camera zooms in on the Player,
    /// IrisTransition is spawned after irisDelay, the scene changes after gameOverLoadDelay.
    /// </summary>
    private IEnumerator GameOverTransitionRoutine()
    {
        // 1. Camera zooms in on the Player
        GameOverZoomToPlayer();

        // 1b. Game UI slides down (runs alongside, with its own delay)
        DropGameUI();

        // 2. Wait, then spawn IrisTransition on top of the Canvas
        if (irisDelay > 0f) yield return new WaitForSeconds(irisDelay);
        SpawnIris();

        // 3. Wait, then load the next scene
        if (gameOverLoadDelay > 0f) yield return new WaitForSeconds(gameOverLoadDelay);

        if (string.IsNullOrEmpty(gameOverSceneName))
        {
            Debug.LogError("[GameManager] Game Over Scene Name is not set!");
        }
        else if (!Application.CanStreamedLevelBeLoaded(gameOverSceneName))
        {
            Debug.LogError($"[GameManager] Scene \"{gameOverSceneName}\" is not in File > Build Settings!");
        }
        else
        {
            SceneManager.LoadScene(gameOverSceneName);
        }
    }

    private void GameOverZoomToPlayer()
    {
        Camera cam = gameOverCamera != null ? gameOverCamera : Camera.main;
        if (cam == null) return;

        Transform camT = cam.transform;

        // Stop any other camera movement (e.g. IntroCutscene) so they don't fight
        DOTween.Kill(camT);

        if (player != null)
        {
            Vector3 target = camT.position;
            target.x = player.transform.position.x + gameOverZoomXOffset;
            target.y = player.transform.position.y + gameOverZoomYOffset;
            camT.DOMove(target, gameOverZoomDuration).SetEase(gameOverZoomCurve).SetId(this);
        }

        if (cam.orthographic)
        {
            DOTween.To(() => cam.orthographicSize, x => cam.orthographicSize = x, gameOverZoomSize, gameOverZoomDuration)
                .SetEase(gameOverZoomCurve).SetId(this);
        }
        else
        {
            DOTween.To(() => cam.fieldOfView, x => cam.fieldOfView = x, gameOverZoomFieldOfView, gameOverZoomDuration)
                .SetEase(gameOverZoomCurve).SetId(this);
        }
    }

    private void DropGameUI()
    {
        if (gameUI == null) return;

        // Wait for the delay, then move down from wherever Game UI is at that moment
        DOTween.Sequence().SetId(this)
            .AppendInterval(Mathf.Max(0f, gameUIDropDelay))
            .AppendCallback(() =>
            {
                Vector2 target = gameUI.anchoredPosition + Vector2.up * gameUIDropOffsetY;
                DOTween.To(() => gameUI.anchoredPosition, v => gameUI.anchoredPosition = v, target, gameUIDropDuration)
                    .SetEase(gameUIDropCurve).SetId(this);
            });
    }

    private void SpawnIris()
    {
        if (irisTransition == null)
        {
            Debug.LogWarning("[GameManager] IrisTransition prefab is not assigned.");
            return;
        }

        if (irisCanvas == null)
        {
            Debug.LogWarning("[GameManager] Iris Canvas is not assigned.");
            return;
        }

        GameObject iris = Instantiate(irisTransition, irisCanvas.transform, false);
        iris.transform.SetAsLastSibling();   // drawn on top of everything else in this canvas
    }

    private void OnDestroy()
    {
        DOTween.Kill(this);
    }

    private void OnDrawGizmos()
    {
        if (!enableGameOverTransition || !showGameOverGizmo || player == null) return;

        Camera cam = gameOverCamera != null ? gameOverCamera : Camera.main;
        if (cam == null || !cam.orthographic) return;

        // Red: what the camera sees at the end of the Game Over zoom
        Vector3 center = player.transform.position + new Vector3(gameOverZoomXOffset, gameOverZoomYOffset, 0f);
        float halfH = gameOverZoomSize;
        float halfW = halfH * cam.aspect;
        Gizmos.color = new Color(1f, 0.2f, 0.2f);
        Gizmos.DrawWireCube(center, new Vector3(halfW * 2f, halfH * 2f, 0f));
        Gizmos.DrawWireSphere(center, 0.15f);
    }

    [ContextMenu("Test: Complete Stage")]
    private void TestCompleteStage()
    {
        CompleteCurrentStage();
    }

    [ContextMenu("Test: Game Over")]
    private void TestGameOver()
    {
        GameOver();
    }
}
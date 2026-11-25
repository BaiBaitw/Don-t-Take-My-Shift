using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    private int completedStages = 0;
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
                Debug.Log("Finished this stage");

                // Pause timer on CountdownBar script when finishing the stage
                if (timerManager != null)
                {
                    timerManager.SendMessage("PauseTimer", SendMessageOptions.DontRequireReceiver);
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
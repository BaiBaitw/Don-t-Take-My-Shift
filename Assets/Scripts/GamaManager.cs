using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Header("UI Element Settings")]
    [Tooltip("TextMeshProUGUI component displaying progress count")]
    [SerializeField] private TextMeshProUGUI levelText;

    [Tooltip("Parent container transform for spawned icons")]
    [SerializeField] private Transform iconContainer;

    [Tooltip("Button to start the game initially")]
    [SerializeField] private Button startButton;

    [Tooltip("Button to advance to the next level/stage when finished")]
    [SerializeField] private Button nextButton;

    [Header("Icon Prefab Settings")]
    [Tooltip("Prefab for standard state icon (must include Image component)")]
    [SerializeField] private GameObject normalIconPrefab;

    [Tooltip("Sprite representing completed state")]
    [SerializeField] private Sprite completedIconSprite;

    [Header("Count & Stage Parameters")]
    [Tooltip("Total count required to complete (formerly totalLevels)")]
    [SerializeField] private int totalCounts = 3;

    [Tooltip("Horizontal spacing between icons (used if container lacks Layout Group)")]
    [SerializeField] private float iconSpacing = 50f;

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
    private List<Image> spawnedIcons = new List<Image>();
    private SpriteRenderer bgSpriteRenderer;

    /// <summary>
    /// Public property to read whether all stages in current session are finished.
    /// </summary>
    public bool IsFinished { get; private set; } = false;

    /// <summary>
    /// Public property to read whether the game is over.
    /// </summary>
    public bool IsGameOver { get; private set; } = false;

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
        }

        // Setup nextButton click event
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextButtonClicked);
        }
    }

    private void Start()
    {
        InitializeLevelUI();
        UpdateBackground();
    }

    /// <summary>
    /// Called when startButton is clicked. Triggers StartGame() on typeGameManager and StartTimer() on timerManager.
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
    /// Initializes level text, UI icons, and hides the Next button.
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
        SpawnIcons();
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
    /// Spawns icons based on totalCounts.
    /// </summary>
    private void SpawnIcons()
    {
        if (iconContainer == null) return;

        foreach (Transform child in iconContainer)
        {
            Destroy(child.gameObject);
        }
        spawnedIcons.Clear();

        if (normalIconPrefab == null) return;

        for (int i = 0; i < totalCounts; i++)
        {
            GameObject iconObj = Instantiate(normalIconPrefab, iconContainer);

            RectTransform rectTransform = iconObj.GetComponent<RectTransform>();
            if (rectTransform != null && iconContainer.GetComponent<LayoutGroup>() == null)
            {
                rectTransform.anchoredPosition = new Vector2(i * iconSpacing, 0);
            }

            Image img = iconObj.GetComponent<Image>();
            if (img != null)
            {
                spawnedIcons.Add(img);
            }
        }
    }

    /// <summary>
    /// Completes the current stage. When all are completed, displays Next Button, pauses timer, and logs message.
    /// </summary>
    public void CompleteCurrentStage()
    {
        if (IsFinished || IsGameOver) return;

        if (completedStages < totalCounts)
        {
            if (completedStages < spawnedIcons.Count && completedIconSprite != null)
            {
                spawnedIcons[completedStages].sprite = completedIconSprite;
            }

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

                // Show Next Button upon completion
                if (nextButton != null)
                {
                    nextButton.gameObject.SetActive(true);
                }
            }
        }
    }

    /// <summary>
    /// Called when Next Button is pressed.
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
    public void GameOver()
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
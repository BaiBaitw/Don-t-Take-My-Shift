using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Header("UI Element Settings")]
    [Tooltip("TextMeshProUGUI component displaying level progress")]
    [SerializeField] private TextMeshProUGUI levelText;

    [Tooltip("Parent container transform for spawned icons (preferably with Horizontal Layout Group or standard RectTransform)")]
    [SerializeField] private Transform iconContainer;

    [Header("Icon Prefab Settings")]
    [Tooltip("Prefab for standard state icon (must include Image component)")]
    [SerializeField] private GameObject normalIconPrefab;

    [Tooltip("Sprite representing completed state")]
    [SerializeField] private Sprite completedIconSprite;

    [Header("Level Parameters")]
    [Tooltip("Total number of levels")]
    [SerializeField] private int totalLevels = 3;

    [Tooltip("Horizontal spacing between icons (used if container lacks Layout Group)")]
    [SerializeField] private float iconSpacing = 50f;

    // Completed stage count (starts at 0)
    private int completedStages = 0;
    private List<Image> spawnedIcons = new List<Image>();

    /// <summary>
    /// Public property to read whether all stages are finished from external scripts.
    /// </summary>
    public bool IsFinished { get; private set; } = false;

    /// <summary>
    /// Public property to read whether the game is over from external scripts.
    /// </summary>
    public bool IsGameOver { get; private set; } = false;

    /// <summary>
    /// Public property to read completed stage count.
    /// </summary>
    public int CompletedStages => completedStages;

    private void Start()
    {
        InitializeLevelUI();
    }

    /// <summary>
    /// Initializes level text and UI icons to uncompleted status (0 completed).
    /// </summary>
    private void InitializeLevelUI()
    {
        completedStages = 0;
        IsFinished = false;
        IsGameOver = false;
        UpdateTextUI();
        SpawnIcons();
    }

    /// <summary>
    /// Updates TextMeshPro text display (Format: CompletedStages/TotalLevels).
    /// </summary>
    private void UpdateTextUI()
    {
        if (levelText != null)
        {
            levelText.text = $"{completedStages}/{totalLevels}";
        }
    }

    /// <summary>
    /// Spawns all icons in normal state initially (representing 0 levels completed).
    /// </summary>
    private void SpawnIcons()
    {
        // Clear existing child icons
        foreach (Transform child in iconContainer)
        {
            Destroy(child.gameObject);
        }
        spawnedIcons.Clear();

        if (normalIconPrefab == null || iconContainer == null) return;

        for (int i = 0; i < totalLevels; i++)
        {
            GameObject iconObj = Instantiate(normalIconPrefab, iconContainer);

            // Position manually if container lacks a Layout Group component
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
    /// Public method to complete the current active stage.
    /// Advances completedStages, switches the icon to completed sprite, updates text, and logs on final completion.
    /// </summary>
    public void CompleteCurrentStage()
    {
        if (IsFinished || IsGameOver) return;

        if (completedStages < totalLevels)
        {
            // Replace current stage's icon sprite with completed sprite
            if (completedStages < spawnedIcons.Count && completedIconSprite != null)
            {
                spawnedIcons[completedStages].sprite = completedIconSprite;
            }

            // Increment completed count and update text
            completedStages++;
            UpdateTextUI();

            // Check if all levels are completed
            if (completedStages == totalLevels)
            {
                IsFinished = true;
                Debug.Log("Finished this stage");
            }
        }
    }

    /// <summary>
    /// Public method to trigger Game Over state from external scripts.
    /// </summary>
    public void GameOver()
    {
        if (IsGameOver) return;

        IsGameOver = true;
        Debug.Log("Game Over");
    }

    /// <summary>
    /// Adds context menu option in Inspector for testing (Right-click GameManager component -> Test: Complete Stage).
    /// </summary>
    [ContextMenu("Test: Complete Stage")]
    private void TestCompleteStage()
    {
        CompleteCurrentStage();
    }

    /// <summary>
    /// Adds context menu option in Inspector for testing Game Over.
    /// </summary>
    [ContextMenu("Test: Game Over")]
    private void TestGameOver()
    {
        GameOver();
    }
}
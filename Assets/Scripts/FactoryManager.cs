using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class FactoryManager : MonoBehaviour
{
    [Header("Prefabs / Objects")]
    [Tooltip("Prefab (or scene template object) for the left button")]
    public GameObject leftButton;
    [Tooltip("Prefab (or scene template object) for the right button")]
    public GameObject rightButton;
    [Tooltip("Circle shown on the button that should be pressed next")]
    public GameObject circle;
    [Tooltip("Circle shown on buttons that have been completed")]
    public GameObject circleFinished;
    [Tooltip("All buttons are created under this object")]
    public GameObject buttonContainer;
    [Tooltip("Red box shown on a wrong input / when the game is over")]
    public GameObject error_Redbox;

    [Header("GameManager")]
    [Tooltip("Object with the GameManager script; if empty, an object named \"GameManager\" is found automatically")]
    public GameObject gameManagerObject;
    private GameManager gameManager;

    [Header("Mouse Hint")]
    [Tooltip("Shown when the right button should be pressed")]
    public GameObject mouseRight;
    [Tooltip("Shown when the left button should be pressed")]
    public GameObject mouseLeft;
    [Tooltip("Default mouse icon; also shown while error_Redbox is visible")]
    public GameObject mouse;

    [Header("Countdown Bar")]
    [Tooltip("Drag in the countdownBar object (with the CountdownBar script attached)")]
    public CountdownBar countdownBar;

    [Header("Settings")]
    [Tooltip("Number of buttons randomly created per row")]
    public int buttonCount = 4;
    [Tooltip("Distance between buttons (pixels for UI, world units for 2D/3D)")]
    public float buttonSpacing = 150f;
    [Tooltip("Center the row of buttons inside buttonContainer")]
    public bool centerRow = true;
    [Tooltip("How many seconds error_Redbox is shown after a wrong input")]
    public float errorDisplayTime = 0.5f;
    [Tooltip("How many buttons are added each time IncreaseDifficulty() is called")]
    public int difficultyIncrease = 1;

    // ===== Runtime state =====
    private readonly List<GameObject> spawnedButtons = new List<GameObject>();
    private readonly List<bool> buttonIsLeft = new List<bool>();
    private readonly List<GameObject> spawnedFinishedCircles = new List<GameObject>();
    private GameObject currentCircle;
    private int currentIndex = 0;
    private bool isPlaying = false;       // Input is accepted only after StartGame()
    private bool isProcessingRow = false; // Input is locked while waiting for GameManager after a row is completed
    private bool isStopped = false;       // Stops here once IsFinished == true
    private Coroutine errorCoroutine;

    private void Awake()
    {
        if (gameManagerObject == null)
            gameManagerObject = GameObject.Find("GameManager");

        if (gameManagerObject != null)
            gameManager = gameManagerObject.GetComponent<GameManager>();

        if (gameManager == null)
            Debug.LogError("[FactoryManager] GameManager script not found!");

        // If the templates are scene objects, hide them; they are only used for cloning
        HideIfSceneObject(leftButton);
        HideIfSceneObject(rightButton);
        HideIfSceneObject(circle);
        HideIfSceneObject(circleFinished);

        if (error_Redbox != null)
            error_Redbox.SetActive(false);

        // By default only "mouse" is visible
        SetMouseHint(mouse);
    }

    private void LateUpdate()
    {
        UpdateMouseHint();
    }

    private void Update()
    {
        // Game over: keep the red box visible and ignore all input
        if (gameManager != null && gameManager.IsGameOver)
        {
            if (errorCoroutine != null)
            {
                StopCoroutine(errorCoroutine);
                errorCoroutine = null;
            }
            if (error_Redbox != null && !error_Redbox.activeSelf)
                error_Redbox.SetActive(true);
            return;
        }

        if (!isPlaying || isStopped || isProcessingRow) return;
        if (currentIndex >= spawnedButtons.Count) return;

        bool pressedLeft = GetLeftPressed();
        bool pressedRight = GetRightPressed();

        if (pressedLeft == pressedRight) return; // Nothing pressed, or both pressed at once

        bool correct = (pressedLeft && buttonIsLeft[currentIndex]) ||
                       (pressedRight && !buttonIsLeft[currentIndex]);

        if (correct)
            OnCorrect();
        else
            ShowError();
    }

    // ================= Public API =================

    /// <summary>Starts the game: clears old buttons and creates a new row.</summary>
    public void StartGame()
    {
        StopAllCoroutines();
        errorCoroutine = null;
        if (error_Redbox != null) error_Redbox.SetActive(false);

        isPlaying = true;
        isStopped = false;
        isProcessingRow = false;
        GenerateRow();
    }

    /// <summary>Increases difficulty: the next row will have difficultyIncrease more buttons.</summary>
    public void IncreaseDifficulty()
    {
        buttonCount += difficultyIncrease;
    }

    // ================= Spawning / Clearing =================

    private void GenerateRow()
    {
        ClearRow();

        if (buttonContainer == null || leftButton == null || rightButton == null)
        {
            Debug.LogError("[FactoryManager] buttonContainer / leftButton / rightButton is not assigned!");
            return;
        }

        int count = Mathf.Max(1, buttonCount);
        float startX = centerRow ? -(count - 1) * buttonSpacing / 2f : 0f;

        for (int i = 0; i < count; i++)
        {
            bool isLeft = Random.value < 0.5f;
            GameObject prefab = isLeft ? leftButton : rightButton;

            GameObject btn = Instantiate(prefab, buttonContainer.transform, false);
            btn.SetActive(true);
            SetLocalPosition(btn, new Vector2(startX + i * buttonSpacing, 0f));

            spawnedButtons.Add(btn);
            buttonIsLeft.Add(isLeft);
        }

        currentIndex = 0;

        // Show the circle on the first button
        if (circle != null)
        {
            currentCircle = Instantiate(circle);
            currentCircle.SetActive(true);
            AttachTo(currentCircle, spawnedButtons[0]);
        }
    }

    private void ClearRow()
    {
        foreach (var b in spawnedButtons)
            if (b != null) Destroy(b); // circle / circleFinished are children, so they are destroyed too

        foreach (var c in spawnedFinishedCircles)
            if (c != null) Destroy(c);

        if (currentCircle != null) Destroy(currentCircle);

        spawnedButtons.Clear();
        buttonIsLeft.Clear();
        spawnedFinishedCircles.Clear();
        currentCircle = null;
        currentIndex = 0;
    }

    // ================= Game Logic =================

    private void OnCorrect()
    {
        GameObject btn = spawnedButtons[currentIndex];

        // Replace this button's circle with circleFinished
        if (circleFinished != null)
        {
            GameObject done = Instantiate(circleFinished);
            done.SetActive(true);
            AttachTo(done, btn);
            spawnedFinishedCircles.Add(done);
        }

        currentIndex++;

        if (currentIndex < spawnedButtons.Count)
        {
            // Move the circle to the next button
            if (currentCircle != null)
                AttachTo(currentCircle, spawnedButtons[currentIndex]);
        }
        else
        {
            // Row completed
            if (currentCircle != null) currentCircle.SetActive(false);
            StartCoroutine(OnRowCompleted());
        }
    }

    private IEnumerator OnRowCompleted()
    {
        isProcessingRow = true;

        if (gameManager != null)
            gameManager.CompleteCurrentStage();

        // Wait one frame so GameManager can update IsFinished / IsGameOver
        yield return null;

        if (gameManager != null && gameManager.IsFinished)
        {
            // Finished: stop here
            isStopped = true;
            isProcessingRow = false;
            yield break;
        }

        if (gameManager != null && gameManager.IsGameOver)
        {
            isProcessingRow = false;
            yield break;
        }

        // Not finished yet: remove the old buttons and create a new random row
        GenerateRow();
        isProcessingRow = false;
    }

    private void ShowError()
    {
        // Apply the time penalty for a wrong input
        if (countdownBar != null)
            countdownBar.ApplyPenalty();

        if (errorCoroutine != null) StopCoroutine(errorCoroutine);
        errorCoroutine = StartCoroutine(ErrorRoutine());
    }

    private IEnumerator ErrorRoutine()
    {
        if (error_Redbox != null) error_Redbox.SetActive(true);
        yield return new WaitForSeconds(errorDisplayTime);

        // If the game ended during this time, keep the red box visible
        if (error_Redbox != null && !(gameManager != null && gameManager.IsGameOver))
            error_Redbox.SetActive(false);

        errorCoroutine = null;
    }

    // ================= Input =================

    private bool GetLeftPressed()
    {
#if ENABLE_INPUT_SYSTEM
        bool mouse = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool key = Keyboard.current != null && Keyboard.current.leftArrowKey.wasPressedThisFrame;
        return mouse || key;
#else
        return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.LeftArrow);
#endif
    }

    private bool GetRightPressed()
    {
#if ENABLE_INPUT_SYSTEM
        bool mouse = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        bool key = Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame;
        return mouse || key;
#else
        return Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.RightArrow);
#endif
    }

    // ================= Mouse Hint =================

    // Decides which mouse icon to show based on the current state
    private void UpdateMouseHint()
    {
        bool errorShowing = error_Redbox != null && error_Redbox.activeSelf;
        bool hasTarget = isPlaying && !isStopped && !isProcessingRow &&
                         currentIndex < spawnedButtons.Count;

        if (errorShowing || !hasTarget)
            SetMouseHint(mouse);
        else if (buttonIsLeft[currentIndex])
            SetMouseHint(mouseLeft);
        else
            SetMouseHint(mouseRight);
    }

    // Shows only the given icon and hides the other two
    private void SetMouseHint(GameObject target)
    {
        SetActiveIfChanged(mouse, target == mouse);
        SetActiveIfChanged(mouseLeft, target == mouseLeft);
        SetActiveIfChanged(mouseRight, target == mouseRight);
    }

    private void SetActiveIfChanged(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
            obj.SetActive(active);
    }

    // ================= Helpers =================

    // Supports both UI (RectTransform) and regular Transform
    private void SetLocalPosition(GameObject obj, Vector2 pos)
    {
        if (obj.transform is RectTransform rt)
            rt.anchoredPosition = pos;
        else
            obj.transform.localPosition = new Vector3(pos.x, pos.y, obj.transform.localPosition.z);
    }

    // Parents the object under the button and centers it (drawn on top)
    private void AttachTo(GameObject obj, GameObject parent)
    {
        obj.transform.SetParent(parent.transform, false);
        SetLocalPosition(obj, Vector2.zero);
        obj.transform.SetAsLastSibling();
    }

    private void HideIfSceneObject(GameObject obj)
    {
        if (obj != null && obj.scene.IsValid())
            obj.SetActive(false);
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ButtonSuccessEffect
{
    None,     // No effect
    Bounce,   // Jumps up and lands back
    Pop,      // Scales up and back down
    Punch,    // Elastic, wobbly scale
    Squash,   // Squashes and stretches like jelly
    Shake,    // Shakes left and right
    Spin,     // Spins one full turn
    Random    // Picks a random effect each time
}

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

    [Header("Screen Shake")]
    [Tooltip("ScreenShake component; the whole screen shakes on a wrong input")]
    public ScreenShake screenShake;

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

    [Header("Success Effect")]
    [Tooltip("Visual effect played on a button when it is pressed correctly")]
    public ButtonSuccessEffect successEffect = ButtonSuccessEffect.Pop;
    [Tooltip("How long the effect lasts (seconds)")]
    public float effectDuration = 0.25f;
    [Tooltip("Bounce: how high the button jumps (pixels for UI, world units for 2D/3D)")]
    public float bounceHeight = 30f;
    [Tooltip("Pop / Punch / Squash: how much the scale changes (0.3 = 30%)")]
    public float scaleAmount = 0.3f;
    [Tooltip("Shake: how far the button shakes sideways")]
    public float shakeStrength = 10f;

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

        // Play the success effect on this button
        PlaySuccessEffect(btn);

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

        // Let the last button's effect finish before the row is replaced
        if (successEffect != ButtonSuccessEffect.None && effectDuration > 0f)
            yield return new WaitForSeconds(effectDuration);

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

        // Shake the whole screen (scene + UI)
        if (screenShake != null)
            screenShake.Shake();

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

    // ================= Success Effect =================

    private void PlaySuccessEffect(GameObject btn)
    {
        ButtonSuccessEffect effect = successEffect;

        if (effect == ButtonSuccessEffect.Random)
            effect = (ButtonSuccessEffect)UnityEngine.Random.Range(
                (int)ButtonSuccessEffect.Bounce, (int)ButtonSuccessEffect.Random);

        if (effect == ButtonSuccessEffect.None || btn == null) return;

        StartCoroutine(SuccessEffectRoutine(btn.transform, effect));
    }

    private IEnumerator SuccessEffectRoutine(Transform t, ButtonSuccessEffect effect)
    {
        Vector2 basePos = GetLocalPosition(t.gameObject);
        Vector3 baseScale = t.localScale;
        Quaternion baseRot = t.localRotation;
        float duration = Mathf.Max(0.01f, effectDuration);
        float time = 0f;

        while (time < duration)
        {
            if (t == null) yield break; // Button was destroyed (row cleared)

            float p = time / duration;           // 0 -> 1
            float arc = Mathf.Sin(p * Mathf.PI); // 0 -> 1 -> 0
            float decay = 1f - p;                // 1 -> 0

            switch (effect)
            {
                case ButtonSuccessEffect.Bounce:
                    SetLocalPosition(t.gameObject, basePos + Vector2.up * arc * bounceHeight);
                    break;

                case ButtonSuccessEffect.Pop:
                    t.localScale = baseScale * (1f + arc * scaleAmount);
                    break;

                case ButtonSuccessEffect.Punch:
                    t.localScale = baseScale * (1f + Mathf.Sin(p * Mathf.PI * 4f) * scaleAmount * decay);
                    break;

                case ButtonSuccessEffect.Squash:
                    float s = Mathf.Sin(p * Mathf.PI * 2f) * scaleAmount * decay;
                    t.localScale = new Vector3(baseScale.x * (1f + s), baseScale.y * (1f - s), baseScale.z);
                    break;

                case ButtonSuccessEffect.Shake:
                    SetLocalPosition(t.gameObject,
                        basePos + Vector2.right * Mathf.Sin(p * Mathf.PI * 6f) * shakeStrength * decay);
                    break;

                case ButtonSuccessEffect.Spin:
                    float eased = 1f - (1f - p) * (1f - p); // Ease out
                    t.localRotation = baseRot * Quaternion.Euler(0f, 0f, -360f * eased);
                    break;
            }

            time += Time.deltaTime;
            yield return null;
        }

        // Restore the original state
        if (t != null)
        {
            SetLocalPosition(t.gameObject, basePos);
            t.localScale = baseScale;
            t.localRotation = baseRot;
        }
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

    private Vector2 GetLocalPosition(GameObject obj)
    {
        if (obj.transform is RectTransform rt)
            return rt.anchoredPosition;
        return obj.transform.localPosition;
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
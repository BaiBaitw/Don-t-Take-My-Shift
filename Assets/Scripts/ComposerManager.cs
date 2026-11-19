using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ComposerManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Number prefab to spawn (must have a NumberDisplay component)")]
    public GameObject number;

    [Tooltip("Parent object that spawned numbers are placed under")]
    public GameObject noteContainer;

    [Tooltip("Red box shown on a wrong key press (hidden by default)")]
    public GameObject Error_RedBox;

    [Tooltip("Object that has the GameManager script attached")]
    public GameObject gameManager;

    [Header("Keyboard Visuals")]
    [Tooltip("Keyboard background (always visible)")]
    public GameObject keyboard0;
    [Tooltip("Shown while key / button 1 is held")]
    public GameObject keyboard1;
    [Tooltip("Shown while key / button 2 is held")]
    public GameObject keyboard2;
    [Tooltip("Shown while key / button 3 is held")]
    public GameObject keyboard3;
    [Tooltip("Shown while key / button 4 is held")]
    public GameObject keyboard4;
    [Tooltip("Shown while key / button 5 is held")]
    public GameObject keyboard5;

    [Header("On-screen Buttons")]
    public Button button1;
    public Button button2;
    public Button button3;
    public Button button4;
    public Button button5;

    [Tooltip("Button Image color while the mouse is hovering over it")]
    public Color hoverColor = new Color(1f, 0.92f, 0.016f, 0.3f);

    [Header("Sequence Settings")]
    [Tooltip("How many numbers to spawn per sequence")]
    public int numberCount = 4;

    [Tooltip("Horizontal distance between each spawned number")]
    public float spacing = 1.5f;

    [Tooltip("How many numbers IncreaseDifficulty() adds each time it is called")]
    public int difficultyIncreaseAmount = 1;

    [Header("Error Feedback")]
    [Tooltip("How long the red box stays visible (seconds)")]
    public float errorShowTime = 0.5f;

    [Tooltip("ScreenShake component to trigger on a wrong press (optional)")]
    public ScreenShake screenShake;

    [Tooltip("Object that has the CountdownBar script attached")]
    public GameObject countdownBar;

    [Tooltip("Value passed to CountdownBar.ApplyPenalty() on a wrong press")]
    public float penaltyAmount = 1f;

    // Internal state
    private readonly List<NumberDisplay> spawnedNumbers = new List<NumberDisplay>();
    private readonly List<int> answers = new List<int>();
    private int currentIndex = 0;
    private bool isPlaying = false;
    private GameManager gm;
    private CountdownBar countdown;
    private Coroutine errorCoroutine;

    // Index 1-5 used; index 0 unused
    private GameObject[] keyboards;
    private Button[] buttons;
    private readonly int[] holdCounts = new int[6];

    private static readonly KeyCode[] alphaKeys =
        { KeyCode.None, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };
    private static readonly KeyCode[] keypadKeys =
        { KeyCode.None, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5 };

    private void Awake()
    {
        if (Error_RedBox != null)
            Error_RedBox.SetActive(false);

        if (gameManager != null)
            gm = gameManager.GetComponent<GameManager>();

        if (countdownBar != null)
            countdown = countdownBar.GetComponent<CountdownBar>();

        keyboards = new GameObject[] { keyboard0, keyboard1, keyboard2, keyboard3, keyboard4, keyboard5 };
        buttons = new Button[] { null, button1, button2, button3, button4, button5 };

        ResetKeyboardVisuals();
        SetupButtons();
    }

    /// <summary>
    /// Starts the game: clears any existing numbers and spawns a new sequence.
    /// </summary>
    public void StartGame()
    {
        if (gm == null && gameManager != null)
            gm = gameManager.GetComponent<GameManager>();

        ClearNumbers();
        HideErrorImmediately();
        ResetKeyboardVisuals();

        isPlaying = true;
        SpawnNumbers();
    }

    /// <summary>
    /// Increases the number of numbers spawned per sequence.
    /// </summary>
    public void IncreaseDifficulty()
    {
        numberCount += difficultyIncreaseAmount;
    }

    private void Update()
    {
        if (isPlaying && gm != null && gm.IsGameOver)
        {
            HandleGameOver();
            return;
        }

        for (int n = 1; n <= 5; n++)
        {
            if (Input.GetKeyDown(alphaKeys[n]) || Input.GetKeyDown(keypadKeys[n]))
                OnNumberDown(n);

            if (Input.GetKeyUp(alphaKeys[n]) || Input.GetKeyUp(keypadKeys[n]))
                OnNumberUp(n);
        }
    }

    // ---------- Input (shared by keyboard and buttons) ----------

    private void OnNumberDown(int n)
    {
        if (!isPlaying) return;

        if (gm != null && gm.IsGameOver)
        {
            HandleGameOver();
            return;
        }

        holdCounts[n]++;
        SetKeyboardVisual(n, true);

        CheckInput(n);
    }

    private void OnNumberUp(int n)
    {
        if (holdCounts[n] <= 0) return;

        holdCounts[n]--;
        if (holdCounts[n] == 0)
            SetKeyboardVisual(n, false);
    }

    private void SetupButtons()
    {
        for (int n = 1; n <= 5; n++)
        {
            if (buttons[n] == null) continue;

            int value = n; // capture for the closures
            EventTrigger trigger = buttons[n].gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = buttons[n].gameObject.AddComponent<EventTrigger>();

            AddTrigger(trigger, EventTriggerType.PointerDown, () => OnNumberDown(value));
            AddTrigger(trigger, EventTriggerType.PointerUp, () => OnNumberUp(value));

            // Hover highlight on the button's Image
            Image image = buttons[n].GetComponent<Image>();
            if (image != null)
            {
                Color normalColor = image.color;
                AddTrigger(trigger, EventTriggerType.PointerEnter, () => image.color = hoverColor);
                AddTrigger(trigger, EventTriggerType.PointerExit, () => image.color = normalColor);
            }
        }
    }

    private void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    // ---------- Keyboard visuals ----------

    private void SetKeyboardVisual(int n, bool show)
    {
        if (keyboards[n] != null)
            keyboards[n].SetActive(show);
    }

    private void ResetKeyboardVisuals()
    {
        if (keyboard0 != null)
            keyboard0.SetActive(true);

        for (int n = 1; n <= 5; n++)
        {
            holdCounts[n] = 0;
            SetKeyboardVisual(n, false);
        }
    }

    // ---------- Game logic ----------

    private void CheckInput(int pressed)
    {
        if (currentIndex >= answers.Count) return;

        if (pressed == answers[currentIndex])
        {
            // Correct
            NumberDisplay display = spawnedNumbers[currentIndex];
            if (display != null)
                display.PressCurrent();

            currentIndex++;

            if (currentIndex >= answers.Count)
                OnSequenceComplete();
        }
        else
        {
            // Wrong
            ShowError();

            if (screenShake != null)
                screenShake.Shake();

            if (countdown != null)
                countdown.ApplyPenalty(penaltyAmount);
        }
    }

    private void OnSequenceComplete()
    {
        if (gm != null)
            gm.CompleteCurrentStage();

        if (gm != null && gm.IsFinished)
        {
            // Finished: keep the last sequence and stop floating
            isPlaying = false;
            ResetKeyboardVisuals();
            StopAllFloating();
            return;
        }

        // Next sequence
        ClearNumbers();
        SpawnNumbers();
    }

    private void HandleGameOver()
    {
        // Disables input (OnNumberDown returns early while isPlaying is false)
        isPlaying = false;
        ResetKeyboardVisuals();

        // Show the red box permanently (cancel any pending auto-hide)
        if (errorCoroutine != null)
        {
            StopCoroutine(errorCoroutine);
            errorCoroutine = null;
        }
        if (Error_RedBox != null)
            Error_RedBox.SetActive(true);

        StopAllFloating();
    }

    private void StopAllFloating()
    {
        foreach (NumberDisplay display in spawnedNumbers)
        {
            if (display != null)
                display.StopFloating();
        }
    }

    private void SpawnNumbers()
    {
        answers.Clear();
        spawnedNumbers.Clear();
        currentIndex = 0;

        Transform parent = noteContainer.transform;

        for (int i = 0; i < numberCount; i++)
        {
            GameObject obj = Instantiate(number, parent);

            // Lay out to the right (anchoredPosition for UI, localPosition otherwise)
            RectTransform rt = obj.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(i * spacing, 0f);
            else
                obj.transform.localPosition = new Vector3(i * spacing, 0f, 0f);

            int value = Random.Range(1, 6); // 1-5
            answers.Add(value);

            NumberDisplay display = obj.GetComponent<NumberDisplay>();
            if (display != null)
                display.ShowNumber(value);
            else
                Debug.LogWarning("[ComposerManager] NumberDisplay component not found on the number prefab.");

            spawnedNumbers.Add(display);
        }
    }

    private void ClearNumbers()
    {
        // Destroy every spawned number under noteContainer
        if (noteContainer != null)
        {
            for (int i = noteContainer.transform.childCount - 1; i >= 0; i--)
                Destroy(noteContainer.transform.GetChild(i).gameObject);
        }

        spawnedNumbers.Clear();
        answers.Clear();
        currentIndex = 0;
    }

    // ---------- Error feedback ----------

    private void ShowError()
    {
        if (Error_RedBox == null) return;

        if (errorCoroutine != null)
            StopCoroutine(errorCoroutine);

        errorCoroutine = StartCoroutine(ErrorRoutine());
    }

    private IEnumerator ErrorRoutine()
    {
        Error_RedBox.SetActive(true);
        yield return new WaitForSeconds(errorShowTime);
        Error_RedBox.SetActive(false);
        errorCoroutine = null;
    }

    private void HideErrorImmediately()
    {
        if (errorCoroutine != null)
        {
            StopCoroutine(errorCoroutine);
            errorCoroutine = null;
        }
        if (Error_RedBox != null)
            Error_RedBox.SetActive(false);
    }
}
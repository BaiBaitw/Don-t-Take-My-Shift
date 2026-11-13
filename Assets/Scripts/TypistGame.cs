using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TypistGame : MonoBehaviour
{
    [Header("【Prefab & Container Setup】")]
    [Tooltip("Prefab for a single character (requires a TextMeshProUGUI component).")]
    public GameObject letterPrefab;
    [Tooltip("UI container for generated characters (e.g., a Panel with Horizontal Layout Group).")]
    public Transform textContainer;

    [Header("【Length & Probability Settings】")]
    [Range(5, 15)]
    [Tooltip("Minimum character count per line.")]
    public int minLength = 5;
    [Range(5, 15)]
    [Tooltip("Maximum character count per line.")]
    public int maxLength = 15;
    [Range(0f, 1f)]
    [Tooltip("Probability of selecting from the phrase bank (0 = all random, 1 = all phrase bank).")]
    public float phraseProbability = 0.3f;

    [Header("【Text Transparency Settings】")]
    [Range(0f, 1f)]
    [Tooltip("Alpha value for uncompleted letters (e.g., 0.5 = 50% black/transparency).")]
    public float uncompletedAlpha = 0.5f;
    [Tooltip("Invisible placeholder glyph used for space slots so they keep the width of a normal letter.")]
    public char spacePlaceholder = 'n';


    [Header("【Cursor Settings】")]
    [Tooltip("Cursor GameObject displayed during normal typing.")]
    public GameObject normalCursor;
    [Tooltip("Cursor GameObject displayed when an input error occurs.")]
    public GameObject errorCursor;
    [Tooltip("Blink interval for the normal cursor (in seconds).")]
    public float cursorBlinkInterval = 0.5f;
    [Range(0.5f, 1f)]
    [Tooltip("How long the red error state (error background + error cursor) stays visible after a typo (in seconds).")]
    public float errorFlashDuration = 0.5f;

    [Header("【Background Settings】")]
    [Tooltip("Background Image displayed during normal gameplay.")]
    public Image normalBackground;
    [Tooltip("Background Image displayed when an input error occurs.")]
    public Image errorBackground;

    [Header("【UI & External Script References】")]
    [Tooltip("Button to complete the current line/stage.")]
    public Button enterButton;
    [Tooltip("How long EnterButton shows its Pressed Sprite when the keyboard Enter key is pressed (in seconds).")]
    public float enterButtonPressedDuration = 0.15f;
    [Tooltip("GameManager script reference (handles totalLevels and CompleteCurrentStage).")]
    public GameManager gameManager;
    [Tooltip("Player GameObject. Its PlayerScript component is fetched at runtime and PlayWrongAnimation() is called on every typo.")]
    public GameObject player;

    // PlayerScript fetched from the Player GameObject
    private PlayerScript playerScript;

    // Internal state variables
    private string currentTargetText = "";
    private string previousTargetText = "";   // Last line's text, used to avoid repeating it
    private int currentIndex = 0;
    private bool isGameActive = false;
    private bool isLineCompleted = false;
    private bool hasError = false;
    private Coroutine blinkCoroutine;
    private Coroutine errorFlashCoroutine;
    private Coroutine enterButtonVisualCoroutine;
    private Sprite enterButtonNormalSprite;      // EnterButton's original sprite, cached at Start
    private bool isEnterButtonHighlighted = false; // Line completed: waiting for Enter
    private bool isEnterButtonPressing = false;    // Keyboard Enter pressed visual is playing

    // Original parents of the cursors, so they can be moved out before letters are destroyed
    private Transform normalCursorHome;
    private Transform errorCursorHome;

    // Phrase bank dictionary: letter-writing phrases, occasionally AI-related (Length as Key, Array of 10 phrases as Value)
    private Dictionary<int, string[]> aiPhraseBank;

    private readonly string charPool = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ ";

    private void Awake()
    {
        InitializeAIPhraseBank();

        if (normalCursor != null) normalCursorHome = normalCursor.transform.parent;
        if (errorCursor != null) errorCursorHome = errorCursor.transform.parent;

        // Remember EnterButton's normal sprite (done in Awake so it is ready before any StartGame call)
        if (enterButton != null)
        {
            Image enterImg = enterButton.targetGraphic as Image;
            if (enterImg != null) enterButtonNormalSprite = enterImg.sprite;
        }

        // Get the PlayerScript component from the Player GameObject
        if (player != null)
        {
            playerScript = player.GetComponent<PlayerScript>();
            if (playerScript == null)
            {
                Debug.LogWarning("TypistGame: PlayerScript was not found on the Player GameObject.", player);
            }
        }
    }

    private void Start()
    {
        if (enterButton != null)
        {
            enterButton.onClick.AddListener(OnEnterPressed);
        }

        SetBackgroundState(isError: false);
        HideCursors();
    }

    private void Update()
    {
        if (!isGameActive) return;

        // Stop the game immediately when GameManager reports Game Over
        if (gameManager != null && gameManager.IsGameOver)
        {
            StopGameOnGameOver();
            return;
        }

        // When current line is completed, listen for Enter key to advance to next stage
        bool enterKeyDown = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        // Show EnterButton's pressed visual whenever Enter is pressed, even if the line is not finished yet
        if (enterKeyDown)
        {
            PlayEnterButtonPressedVisual();
        }

        if (isLineCompleted)
        {
            // Only advance to the next stage when the current line is completed
            if (enterKeyDown)
            {
                OnEnterPressed();
            }
            return;
        }

        // Enter pressed before the line is finished: count it as a typo (does not advance)
        if (enterKeyDown)
        {
            TriggerTypoError();
        }

        // Handle typing input (including Spacebar)
        if (Input.anyKeyDown)
        {
            foreach (char c in Input.inputString)
            {
                // Filter out Return and Backspace inputs from standard string processing
                if (c == '\b' || c == '\n' || c == '\r') continue;

                CheckInputChar(c);
            }
        }
    }

    // Upper limit for line length (matches the [Range(5, 15)] sliders and the phrase bank)
    private const int MaxAllowedLength = 15;
    // How many characters each IncreaseDifficulty() call adds
    private const int DifficultyStep = 2;

    /// <summary>
    /// Increases difficulty: adds 2 characters to both minLength and maxLength.
    /// Each value stops at 15 once it reaches the limit. Can be called from other scripts.
    /// Takes effect from the next generated line.
    /// </summary>
    public void IncreaseDifficulty()
    {
        minLength = Mathf.Min(minLength + DifficultyStep, MaxAllowedLength);
        maxLength = Mathf.Min(maxLength + DifficultyStep, MaxAllowedLength);

        // Safety: keep minLength from exceeding maxLength
        if (minLength > maxLength) minLength = maxLength;
    }

    /// <summary>
    /// Starts the game loop. Public so it can be called from other scripts or a UI Button's OnClick.
    /// </summary>
    public void StartGame()
    {
        // Do not allow starting again after Game Over
        if (gameManager != null && gameManager.IsGameOver) return;

        isGameActive = true;
        currentIndex = 0;
        GenerateNextLine();
    }

    /// <summary>
    /// Generates the next typing challenge line.
    /// </summary>
    private void GenerateNextLine()
    {
        ClearContainer();
        currentIndex = 0;
        isLineCompleted = false;
        hasError = false;
        SetBackgroundState(isError: false);
        SetEnterButtonHighlighted(false);   // Back to the normal EnterButton look for the new stage

        // Pick a new text that is different from the previous line
        const int maxAttempts = 20;
        string candidate = "";
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            candidate = PickTargetText();
            if (candidate != previousTargetText) break;
        }
        currentTargetText = candidate;
        previousTargetText = currentTargetText;

        // Color definition for uncompleted letters (50% black by default)
        Color uncompletedColor = new Color(0f, 0f, 0f, uncompletedAlpha);

        // Instantiate character Prefabs
        for (int i = 0; i < currentTargetText.Length; i++)
        {
            GameObject letterObj = Instantiate(letterPrefab, textContainer);
            TextMeshProUGUI tmpText = letterObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = currentTargetText[i].ToString();
                tmpText.color = uncompletedColor;
            }

            // Space character: show a fully transparent placeholder glyph so the slot keeps
            // the width of a normal letter but looks like a real blank gap.
            // The expected input is still ' ', so the player presses the Spacebar as before.
            if (currentTargetText[i] == ' ' && tmpText != null)
            {
                tmpText.text = spacePlaceholder.ToString();
                tmpText.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        UpdateCursorPosition();
    }

    /// <summary>
    /// Picks one line of text: from the phrase bank (by phraseProbability) or a random string.
    /// </summary>
    private string PickTargetText()
    {
        int targetLength = Random.Range(minLength, maxLength + 1);

        if (Random.value < phraseProbability && aiPhraseBank.ContainsKey(targetLength))
        {
            string[] bank = aiPhraseBank[targetLength];
            return bank[Random.Range(0, bank.Length)];
        }

        return GenerateRandomString(targetLength);
    }

    /// <summary>
    /// Validates player input character.
    /// </summary>
    private void CheckInputChar(char inputChar)
    {
        if (currentIndex >= currentTargetText.Length) return;

        char expectedChar = currentTargetText[currentIndex];

        if (inputChar == expectedChar)
        {
            // Correct input: reset error background if previously active
            if (hasError)
            {
                StopErrorFlash();
                hasError = false;
                SetBackgroundState(isError: false);
            }

            // Update completed character visual feedback (solid black: 100% alpha)
            SetLetterColor(currentIndex, new Color(0f, 0f, 0f, 1f));
            currentIndex++;

            if (currentIndex >= currentTargetText.Length)
            {
                // Current line completed: highlight EnterButton so the player knows to press Enter
                isLineCompleted = true;
                HideCursors();
                SetEnterButtonHighlighted(true);
            }
            else
            {
                UpdateCursorPosition();
            }
        }
        else
        {
            TriggerTypoError();
        }
    }

    /// <summary>
    /// Shows the red error background and error cursor for errorFlashDuration seconds.
    /// </summary>
    private void TriggerTypoError()
    {
        StopErrorFlash();
        hasError = true;
        SetBackgroundState(isError: true);
        ShowErrorCursor(currentIndex);
        errorFlashCoroutine = StartCoroutine(ErrorFlashRoutine());

        // Play the player's "wrong" animation
        if (playerScript != null)
        {
            playerScript.PlayWrongAnimation();
        }
    }

    /// <summary>
    /// Keeps the error state visible for errorFlashDuration seconds, then returns to normal.
    /// </summary>
    private IEnumerator ErrorFlashRoutine()
    {
        yield return new WaitForSeconds(errorFlashDuration);
        errorFlashCoroutine = null;

        // If the game ended during the flash, Game Over keeps its own red state
        if (!isGameActive) yield break;

        hasError = false;
        SetBackgroundState(isError: false);
        if (!isLineCompleted) UpdateCursorPosition();
    }

    private void StopErrorFlash()
    {
        if (errorFlashCoroutine != null)
        {
            StopCoroutine(errorFlashCoroutine);
            errorFlashCoroutine = null;
        }
    }

    /// <summary>
    /// Makes EnterButton briefly show its Pressed Sprite (Sprite Swap) or Pressed Color (Color Tint)
    /// when the keyboard Enter key is used.
    /// </summary>
    private void PlayEnterButtonPressedVisual()
    {
        if (enterButton == null || !enterButton.interactable) return;

        if (enterButtonVisualCoroutine != null) StopCoroutine(enterButtonVisualCoroutine);
        enterButtonVisualCoroutine = StartCoroutine(EnterButtonPressedRoutine());
    }

    private IEnumerator EnterButtonPressedRoutine()
    {
        isEnterButtonPressing = true;
        ApplyEnterButtonVisual();

        yield return new WaitForSeconds(enterButtonPressedDuration);

        isEnterButtonPressing = false;
        ApplyEnterButtonVisual();   // Returns to Highlighted or Normal, whichever is current
        enterButtonVisualCoroutine = null;
    }

    /// <summary>
    /// Turns EnterButton's Highlighted look on (line completed) or off (new stage / game end).
    /// </summary>
    private void SetEnterButtonHighlighted(bool highlighted)
    {
        isEnterButtonHighlighted = highlighted;
        ApplyEnterButtonVisual();
    }

    /// <summary>
    /// Applies EnterButton's look by priority: Pressed > Highlighted > Normal.
    /// Uses the sprites / colors set on the Button component.
    /// </summary>
    private void ApplyEnterButtonVisual()
    {
        if (enterButton == null || enterButton.targetGraphic == null) return;

        if (enterButton.transition == Selectable.Transition.SpriteSwap)
        {
            Image img = enterButton.targetGraphic as Image;
            if (img == null) return;

            SpriteState ss = enterButton.spriteState;
            if (enterButtonNormalSprite == null) enterButtonNormalSprite = img.sprite;

            Sprite target = enterButtonNormalSprite;
            if (isEnterButtonPressing && ss.pressedSprite != null) target = ss.pressedSprite;
            else if (isEnterButtonHighlighted && ss.highlightedSprite != null) target = ss.highlightedSprite;

            img.sprite = target;
        }
        else if (enterButton.transition == Selectable.Transition.ColorTint)
        {
            ColorBlock cb = enterButton.colors;
            Color target = cb.normalColor;
            if (isEnterButtonPressing) target = cb.pressedColor;
            else if (isEnterButtonHighlighted) target = cb.highlightedColor;

            enterButton.targetGraphic.CrossFadeColor(target * cb.colorMultiplier, isEnterButtonPressing ? 0f : cb.fadeDuration, true, true);
        }
    }

    /// <summary>
    /// Stops the game on Game Over: no more typing or Enter input is accepted.
    /// The current line stays on screen.
    /// </summary>
    private void StopGameOnGameOver()
    {
        isGameActive = false;
        isLineCompleted = false;
        hasError = false;
        StopErrorFlash();
        HideCursors();
        SetEnterButtonHighlighted(false);

        // Switch permanently to the red error background and red error cursor
        SetBackgroundState(isError: true);
        if (textContainer.childCount > 0)
        {
            int cursorIndex = Mathf.Clamp(currentIndex, 0, textContainer.childCount - 1);
            ShowErrorCursor(cursorIndex);
        }
    }

    /// <summary>
    /// Proceeds to the next stage. Called by Enter key or EnterButton.
    /// </summary>
    public void OnEnterPressed()
    {
        if (!isGameActive || !isLineCompleted) return;

        // Ignore the Enter button if Game Over happened earlier in this frame
        if (gameManager != null && gameManager.IsGameOver)
        {
            StopGameOnGameOver();
            return;
        }

        // Call external GameManager function
        if (gameManager != null)
        {
            gameManager.CompleteCurrentStage();
        }

        // Stop here if GameManager reports that all stages are finished
        if (gameManager != null && gameManager.IsFinished)
        {
            isGameActive = false;
            isLineCompleted = false;
            ClearContainer();   // Also hides cursors and removes the last line's letters
            SetEnterButtonHighlighted(false);
            currentTargetText = "";
            currentIndex = 0;
            return;
        }

        // Generate next line
        GenerateNextLine();
    }

    /// <summary>
    /// Updates normal cursor position and blinking logic.
    /// </summary>
    private void UpdateCursorPosition()
    {
        if (errorCursor != null) errorCursor.SetActive(false);

        if (normalCursor != null && currentIndex < textContainer.childCount)
        {
            Transform targetCharTransform = textContainer.GetChild(currentIndex);
            normalCursor.transform.SetParent(targetCharTransform, false);
            normalCursor.transform.localPosition = Vector3.zero;

            if (blinkCoroutine != null) StopCoroutine(blinkCoroutine);
            blinkCoroutine = StartCoroutine(BlinkCursorRoutine());
        }
    }

    /// <summary>
    /// Displays the error cursor at current character position.
    /// </summary>
    private void ShowErrorCursor(int index)
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }

        if (normalCursor != null) normalCursor.SetActive(false);

        if (errorCursor != null && index >= 0 && index < textContainer.childCount)
        {
            Transform targetCharTransform = textContainer.GetChild(index);
            errorCursor.transform.SetParent(targetCharTransform, false);
            errorCursor.transform.localPosition = Vector3.zero;
            errorCursor.SetActive(true);
        }
    }

    /// <summary>
    /// Normal cursor blinking routine.
    /// </summary>
    private IEnumerator BlinkCursorRoutine()
    {
        if (normalCursor == null) yield break;

        while (normalCursor != null)
        {
            normalCursor.SetActive(!normalCursor.activeSelf);
            yield return new WaitForSeconds(cursorBlinkInterval);
        }
    }

    private void HideCursors()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
        if (normalCursor != null) normalCursor.SetActive(false);
        if (errorCursor != null) errorCursor.SetActive(false);
    }

    /// <summary>
    /// Toggles background images between normal and error states.
    /// </summary>
    private void SetBackgroundState(bool isError)
    {
        if (normalBackground != null) normalBackground.gameObject.SetActive(!isError);
        if (errorBackground != null) errorBackground.gameObject.SetActive(isError);
    }

    /// <summary>
    /// Sets text color for a specific instantiated letter Prefab.
    /// </summary>
    private void SetLetterColor(int index, Color color)
    {
        if (index < textContainer.childCount)
        {
            Transform letter = textContainer.GetChild(index);
            bool isSpace = index < currentTargetText.Length && currentTargetText[index] == ' ';

            TextMeshProUGUI tmpText = letter.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
            {
                // Space characters always stay fully transparent (blank gap).
                tmpText.color = isSpace ? new Color(0f, 0f, 0f, 0f) : color;
            }

        }
    }


    /// <summary>
    /// Generates a randomized character sequence including spaces.
    /// </summary>
    private string GenerateRandomString(int length)
    {
        char[] stringChars = new char[length];
        for (int i = 0; i < length; i++)
        {
            stringChars[i] = charPool[Random.Range(0, charPool.Length)];
        }
        return new string(stringChars);
    }

    private void ClearContainer()
    {
        StopErrorFlash();

        // Stop blinking and move the cursors out first, because they are parented to letter objects
        // and would otherwise be destroyed together with them.
        HideCursors();
        if (normalCursor != null) normalCursor.transform.SetParent(normalCursorHome, false);
        if (errorCursor != null) errorCursor.transform.SetParent(errorCursorHome, false);

        // Detach before Destroy: Destroy only happens at the end of the frame, so without detaching,
        // childCount / GetChild would still return the old letters when the new line is built.
        for (int i = textContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = textContainer.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// Initializes the phrase bank: letter-writing phrases with occasional AI-related ones
    /// (Lengths 5 to 15, 10 entries each, exact length matching, letters and spaces only).
    /// </summary>
    private void InitializeAIPhraseBank()
    {
        aiPhraseBank = new Dictionary<int, string[]>()
        {
            // 長度 5
            { 5, new string[] {
                "Hello", "Sorry", "Yours", "Hi Jo", "Hi Ed",
                "Hi Al", "Hi Ma", "Later", "Peace", "Hi AI"
            }},
            // 長度 6
            { 6, new string[] {
                "Thanks", "Cheers", "Warmly", "Fondly", "Hi Amy",
                "Hi Tom", "Hi Mom", "Hi bro", "Hi sis", "Hey AI"
            }},
            // 長度 7
            { 7, new string[] {
                "Regards", "See you", "Bye now", "Hi team", "Hi dear",
                "Love ya", "Miss ya", "Dear Al", "Love Ma", "Dear AI"
            }},
            // 長度 8
            { 8, new string[] {
                "Dear Sir", "Dear Mom", "Dear Dad", "Dear Ann", "Love you",
                "Miss you", "Big news", "Hi folks", "Your son", "AI reply"
            }},
            // 長度 9
            { 9, new string[] {
                "Thank you", "Sincerely", "Take care", "Talk soon", "With love",
                "Dear team", "Good luck", "Good news", "Stay safe", "AI drafts"
            }},
            // 長度 10
            { 10, new string[] {
                "Dear Madam", "Dear Sarah", "Hello team", "I miss you", "I am sorry",
                "Great news", "Good night", "Any update", "Dear diary", "Sent by AI"
            }},
            // 長度 11
            { 11, new string[] {
                "Best wishes", "Warm wishes", "Yours truly", "Many thanks", "Dear friend",
                "Hi everyone", "How are you", "Let me know", "All my love", "Bye for now"
            }},
            // 長度 12
            { 12, new string[] {
                "Best regards", "Kind regards", "Warm regards", "Good morning", "See you soon",
                "Thanks again", "Please reply", "Dear manager", "Running late", "Dear Grandma"
            }},
            // 長度 13
            { 13, new string[] {
                "Keep in touch", "Get well soon", "Forever yours", "Save the date", "Call me later",
                "Dear customer", "Please advise", "Out of office", "AI wrote this", "Drafted by AI"
            }},
            // 長度 14
            { 14, new string[] {
                "Happy birthday", "Dear colleague", "Please confirm", "Quick question", "Welcome aboard",
                "See you Monday", "Lunch tomorrow", "With gratitude", "AI fixed typos", "Dear AI helper"
            }},
            // 長度 15
            { 15, new string[] {
                "Yours sincerely", "Write back soon", "Looking forward", "Have a nice day", "Merry Christmas",
                "You are invited", "Until next time", "Got your letter", "Written with AI", "My AI assistant"
            }}
        };
    }
}
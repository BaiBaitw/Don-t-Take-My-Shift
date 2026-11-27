using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

public class TypistGame : MonoBehaviour
{
    [Header("【Prefab & Container Setup】")]
    [Tooltip("Prefab for a single character (requires a TextMeshProUGUI component).")]
    public GameObject letterPrefab;
    [Tooltip("UI container for generated characters (e.g., a Panel with Horizontal Layout Group).")]
    public Transform textContainer;

    [Header("【Length & Probability Settings】")]
    [Range(5, 20)]
    [Tooltip("Minimum character count per line.")]
    public int minLength = 5;
    [Range(5, 20)]
    [Tooltip("Maximum character count per line.")]
    public int maxLength = 15;
    [Range(0f, 1f)]
    [Tooltip("Probability of selecting from the phrase bank (0 = all random, 1 = all phrase bank).")]
    public float phraseProbability = 0.3f;
    [Min(0)]
    [Tooltip("How many characters each IncreaseDifficulty() call adds to both Min Length and Max Length.")]
    public int difficultyStep = 2;

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
    [Tooltip("How long the red error state (Error Redbox + error cursor) stays visible after a typo (in seconds).")]
    public float errorFlashDuration = 0.5f;

    [Header("【Error Redbox Settings】")]
    [Tooltip("Red box Image shown when an input error occurs (and kept on Game Over). The background itself always stays visible.")]
    [FormerlySerializedAs("errorBackground")]   // Keeps the reference previously assigned to Error Background
    public Image errorRedbox;

    [Header("【UI & External Script References】")]
    [Tooltip("Button to complete the current line/stage.")]
    public Button enterButton;
    [Tooltip("How long EnterButton shows its Pressed Sprite when the keyboard Enter key is pressed (in seconds).")]
    public float enterButtonPressedDuration = 0.15f;
    [Tooltip("GameManager script reference (handles totalLevels and CompleteCurrentStage).")]
    public GameManager gameManager;
    [Tooltip("Player GameObject. Its PlayerScript (or SupermanPlayerScript if PlayerScript is missing) is fetched at runtime and PlayWrongAnimation() is called on every typo.")]
    public GameObject player;
    [Tooltip("ScreenShake script reference. Shake() is called on every typo (uses ScreenShake's Default Settings).")]
    public ScreenShake screenShake;
    [Tooltip("CountdownBar GameObject. Its CountdownBar component is fetched at runtime and ApplyPenalty(countdownPenalty) is called on every typo.")]
    public GameObject countdownBar;
    [Tooltip("Value passed to CountdownBar.ApplyPenalty() on every typo.")]
    public float countdownPenalty = 1f;

    // Player script fetched from the Player GameObject (PlayerScript first, SupermanPlayerScript as fallback)
    private PlayerScript playerScript;
    private SupermanPlayerScript supermanPlayerScript;

    // CountdownBar component fetched from the CountdownBar GameObject
    private CountdownBar countdownBarScript;

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

    // Phrase bank dictionary: letter-writing phrases, occasionally AI-related (Length as Key, Array of 20 phrases as Value)
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

            // If there is no PlayerScript, try SupermanPlayerScript instead
            if (playerScript == null)
            {
                supermanPlayerScript = player.GetComponent<SupermanPlayerScript>();
            }

            if (playerScript == null && supermanPlayerScript == null)
            {
                Debug.LogWarning("TypistGame: Neither PlayerScript nor SupermanPlayerScript was found on the Player GameObject.", player);
            }
        }

        // Get the CountdownBar component from the CountdownBar GameObject
        if (countdownBar != null)
        {
            countdownBarScript = countdownBar.GetComponent<CountdownBar>();
            if (countdownBarScript == null)
            {
                Debug.LogWarning("TypistGame: CountdownBar script was not found on the CountdownBar GameObject.", countdownBar);
            }
        }
    }

    private void Start()
    {
        if (enterButton != null)
        {
            enterButton.onClick.AddListener(OnEnterPressed);
        }

        SetErrorRedboxVisible(false);
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

    // Upper limit for line length (matches the [Range(5, 20)] sliders and the phrase bank)
    private const int MaxAllowedLength = 20;

    /// <summary>
    /// Increases difficulty: adds difficultyStep characters to both minLength and maxLength.
    /// Each value stops at 20 once it reaches the limit. Can be called from other scripts.
    /// Takes effect from the next generated line.
    /// </summary>
    public void IncreaseDifficulty()
    {
        minLength = Mathf.Min(minLength + difficultyStep, MaxAllowedLength);
        maxLength = Mathf.Min(maxLength + difficultyStep, MaxAllowedLength);

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
        SetErrorRedboxVisible(false);
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
    /// Validates player input character (case-insensitive).
    /// </summary>
    private void CheckInputChar(char inputChar)
    {
        if (currentIndex >= currentTargetText.Length) return;

        char expectedChar = currentTargetText[currentIndex];

        // Case-insensitive: "A" and "a" both count as correct, so players never fail on letter case
        if (char.ToLowerInvariant(inputChar) == char.ToLowerInvariant(expectedChar))
        {
            // Correct input: hide Error Redbox if previously shown
            if (hasError)
            {
                StopErrorFlash();
                hasError = false;
                SetErrorRedboxVisible(false);
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
    /// Shows the Error Redbox and error cursor for errorFlashDuration seconds.
    /// </summary>
    private void TriggerTypoError()
    {
        StopErrorFlash();
        hasError = true;
        SetErrorRedboxVisible(true);
        ShowErrorCursor(currentIndex);
        errorFlashCoroutine = StartCoroutine(ErrorFlashRoutine());

        // Play the player's "wrong" animation
        if (playerScript != null)
        {
            playerScript.PlayWrongAnimation();
        }
        else if (supermanPlayerScript != null)
        {
            supermanPlayerScript.PlayWrongAnimation();
        }

        // Shake the camera and UI
        if (screenShake != null)
        {
            screenShake.Shake();
        }

        // Apply the time penalty on the countdown bar
        if (countdownBarScript != null)
        {
            countdownBarScript.ApplyPenalty(countdownPenalty);
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
        SetErrorRedboxVisible(false);
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

        // Keep the Error Redbox and red error cursor shown permanently
        SetErrorRedboxVisible(true);
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
    /// Shows or hides the Error Redbox. The background is not touched and always stays visible.
    /// </summary>
    private void SetErrorRedboxVisible(bool visible)
    {
        if (errorRedbox != null) errorRedbox.gameObject.SetActive(visible);
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
    /// (Lengths 5 to 20, 20 entries each, exact length matching, letters and spaces only).
    /// </summary>
    private void InitializeAIPhraseBank()
    {
        aiPhraseBank = new Dictionary<int, string[]>()
        {
            // 長度 5
            { 5, new string[] {
                "Hello", "Sorry", "Yours", "Hi Jo", "Hi Ed",
                "Hi Bo", "Hi Ma", "Later", "Peace", "Adieu",
                "Hi Mo", "Hi Pa", "Hi Ty", "Howdy", "Truly",
                "Hi Di", "Hi Vi", "Hi ya", "Hi AI", "Yo AI"
            }},
            // 長度 6
            { 6, new string[] {
                "Thanks", "Cheers", "Warmly", "Fondly", "Hi Amy",
                "Hi Tom", "Hi Mom", "Hi bro", "Hi sis", "Hey Ed",
                "Hey Jo", "Hi Ben", "Hi Dad", "Hi Dan", "Hi Eva",
                "Hi Joe", "Hi Ken", "Hi Kim", "Hi Max", "Hey AI"
            }},
            // 長度 7
            { 7, new string[] {
                "Regards", "See you", "Bye now", "Hi team", "Hi dear",
                "Love ya", "Miss ya", "Dear Jo", "Love Ma", "Be safe",
                "Be well", "Bye bye", "Dear Ma", "Go well", "Hey Dad",
                "Hey Mom", "Hey bro", "Hey sis", "Hi Anna", "Dear AI"
            }},
            // 長度 8
            { 8, new string[] {
                "Dear Sir", "Dear Mom", "Dear Dad", "Dear Ann", "Love you",
                "Miss you", "Big news", "Hi folks", "Your son", "Any news",
                "Bad news", "Big hugs", "Dear Amy", "Dear Ben", "Dear Bob",
                "Dear Dan", "Dear Eva", "Dear Joe", "Dear Kim", "AI reply"
            }},
            // 長度 9
            { 9, new string[] {
                "Thank you", "Sincerely", "Take care", "Talk soon", "With love",
                "Dear team", "Good luck", "Good news", "Stay safe", "Cold here",
                "Cordially", "Dear Jack", "Dear John", "Dear Lucy", "Dear Mama",
                "Dear Mary", "Dear Nana", "Dear Papa", "AI drafts", "Hi AI bot"
            }},
            // 長度 10
            { 10, new string[] {
                "Dear Madam", "Dear Sarah", "Hello team", "I miss you", "I am sorry",
                "Great news", "Good night", "Any update", "Dear diary", "Big thanks",
                "Cheers all", "Dear Santa", "Ever yours", "Forgive me", "Hi Grandma",
                "Hi Grandpa", "Hi sweetie", "Sent by AI", "AI read it", "AI says hi"
            }},
            // 長度 11
            { 11, new string[] {
                "Best wishes", "Warm wishes", "Yours truly", "Many thanks", "Dear friend",
                "Hi everyone", "How are you", "Let me know", "All my love", "Bye for now",
                "All is well", "Cheers mate", "Coffee soon", "Dear client", "Dear doctor",
                "Dear editor", "Dear guests", "Dear reader", "AI wrote it", "Dear AI bot"
            }},
            // 長度 12
            { 12, new string[] {
                "Best regards", "Kind regards", "Warm regards", "Good morning", "See you soon",
                "Thanks again", "Please reply", "Dear manager", "Running late", "Dear Grandma",
                "All the best", "Always yours", "Baby is here", "Call at noon", "Call me soon",
                "Dear members", "Dear parents", "Dear partner", "AI reply now", "AI sent this"
            }},
            // 長度 13
            { 13, new string[] {
                "Keep in touch", "Get well soon", "Forever yours", "Save the date", "Call me later",
                "Dear customer", "Please advise", "Out of office", "All good here", "Best from Amy",
                "Bye for today", "Catch up soon", "Dear Dr Green", "Dear Mr Smith", "Dear Ms Jones",
                "Dear landlord", "Dear students", "Dinner at six", "AI wrote this", "Drafted by AI"
            }},
            // 長度 14
            { 14, new string[] {
                "Happy birthday", "Dear colleague", "Please confirm", "Quick question", "Welcome aboard",
                "See you Monday", "Lunch tomorrow", "With gratitude", "Dear Mrs Brown", "Enjoy the show",
                "Enjoy your day", "Happy holidays", "Happy new home", "How are things", "Just saying hi",
                "Love from home", "Meeting at ten", "AI fixed typos", "Dear AI helper", "AI drafts mail"
            }},
            // 長度 15
            { 15, new string[] {
                "Yours sincerely", "Write back soon", "Looking forward", "Have a nice day", "Merry Christmas",
                "You are invited", "Until next time", "Got your letter", "Enjoy the party", "Enjoy your trip",
                "Gentle reminder", "Good luck today", "Hope this helps", "Hope to see you", "How is the baby",
                "How is your mom", "Just a reminder", "Written with AI", "My AI assistant", "AI made a draft"
            }},
            // 長度 16
            { 16, new string[] {
                "Dear Tooth Fairy", "Faithfully yours", "Happy graduation", "Happy retirement", "Have a safe trip",
                "Hope all is well", "Hope you had fun", "How are the kids", "How was the trip", "Human wrote this",
                "Just checking in", "Love from London", "Many many thanks", "Meeting tomorrow", "Please call back",
                "Please see below", "Regards from Tom", "AI drafted reply", "AI fixed my mail", "AI is my pen pal"
            }},
            // 長度 17
            { 17, new string[] {
                "All the very best", "Dear sir or madam", "Friendly reminder", "Good luck at work", "Happy Mothers Day",
                "Happy anniversary", "Happy wedding day", "Have a great trip", "Have a great week", "Have a lovely day",
                "Hope it goes well", "Hope to hear soon", "Hope you are fine", "Hope you are okay", "Hope you are well",
                "Please be on time", "Please check this", "AI does my emails", "AI helps me write", "Dear AI assistant"
            }},
            // 長度 18
            { 18, new string[] {
                "All my love to you", "Best wishes to all", "Dear valued client", "Hope you are happy", "I read your letter",
                "Just to say thanks", "Just wanted to say", "My dear old friend", "Please let me know", "Postcard from Bali",
                "Reminder for today", "Respectfully yours", "Say hi to your mom", "See you after work", "Stay safe and warm",
                "Thanks for the tip", "Thanks for waiting", "With love and hugs", "AI answers my mail", "AI checked grammar"
            }},
            // 長度 19
            { 19, new string[] {
                "Best of luck to you", "Can we talk tonight", "Congrats on the job", "Dear Aunt and Uncle", "Dear Mr and Mrs Lee",
                "Dear hiring manager", "Everyone says hello", "Forever your friend", "Give my love to all", "Greetings from Rome",
                "Have a nice weekend", "I am sorry for that", "I loved your letter", "Please call me back", "See you next Monday",
                "Sorry for the delay", "Thank you for today", "AI made this letter", "AI read your letter", "Reply drafted by AI"
            }},
            // 長度 20
            { 20, new string[] {
                "Best wishes from Tom", "Can you call me back", "Congrats on your win", "Dear valued customer", "Have a happy holiday",
                "Have a wonderful day", "Hope to see you soon", "Hope you feel better", "I am so proud of you", "I miss you very much",
                "I will be there soon", "Kiss the kids for me", "Love you to the moon", "Please write me back", "See you at the party",
                "See you next weekend", "Talk to you tomorrow", "AI sends its regards", "AI writes my letters", "AI wrote this letter"
            }}
        };
    }
}
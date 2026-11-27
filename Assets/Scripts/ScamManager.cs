using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Scam phone-dialing mini game.
/// Call StartGame() from outside to show a random phone number. The player presses the
/// digits / * / # in order, then presses callButton (or Enter) to dial, which calls
/// GameManager.CompleteCurrentStage().
/// </summary>
public class ScamManager : MonoBehaviour
{
    // --------------------------- References ---------------------------
    [Header("Number Buttons")]
    [SerializeField] private Button phoneNumber1;
    [SerializeField] private Button phoneNumber2;
    [SerializeField] private Button phoneNumber3;
    [SerializeField] private Button phoneNumber4;
    [SerializeField] private Button phoneNumber5;
    [SerializeField] private Button phoneNumber6;
    [SerializeField] private Button phoneNumber7;
    [SerializeField] private Button phoneNumber8;
    [SerializeField] private Button phoneNumber9;
    [SerializeField] private Button phoneNumber0;
    [Tooltip("Star key *")]
    [SerializeField] private Button phoneNumberx;
    [Tooltip("Hash key #")]
    [SerializeField] private Button phoneNumberxx;

    [Header("Scene Objects")]
    [Tooltip("Must have a TextMeshPro - Text (UI) component")]
    [SerializeField] private GameObject phoneNumberContainer;
    [Tooltip("Dial button (must have a Button component)")]
    [SerializeField] private GameObject callButton;
    [Tooltip("Error red box, hidden by default")]
    [SerializeField] private GameObject error_Redbox;
    [Tooltip("Must have the GameManager script")]
    [SerializeField] private GameObject gameManagerObject;
    [Tooltip("Must have the CountdownBar script")]
    [SerializeField] private GameObject countdownBar;
    [Tooltip("Must have the ScreenShake script (optional)")]
    [SerializeField] private GameObject screenShakeObject;
    [Tooltip("Must have the playerScript script (falls back to supermanPlayerScript)")]
    [SerializeField] private GameObject Player;

    // --------------------------- Settings ---------------------------
    [Header("Display")]
    [Tooltip("Color applied to correctly entered characters")]
    [SerializeField] private Color correctColor = new Color(0.2f, 0.9f, 0.3f, 1f);
    [Tooltip("How many seconds error_Redbox stays visible after a mistake")]
    [SerializeField] private float errorDisplaySeconds = 1.5f;
    [Tooltip("How many seconds an on-screen button looks pressed when triggered from the keyboard")]
    [SerializeField] private float keyPressVisualSeconds = 0.12f;

    [Header("Question Generation")]
    [Tooltip("Starting minimum number of digits per question (* and # not counted)")]
    [SerializeField] private int minDigits = 8;
    [Tooltip("Starting maximum number of digits per question (* and # not counted)")]
    [SerializeField] private int maxDigits = 15;

    [Header("Difficulty")]
    [Tooltip("How many digits IncreaseDifficulty() adds to the minimum")]
    [SerializeField] private int minDigitsIncrease = 1;
    [Tooltip("How many digits IncreaseDifficulty() adds to the maximum")]
    [SerializeField] private int maxDigitsIncrease = 1;
    [Tooltip("Upper limit for the digit count after increasing difficulty (0 = no limit)")]
    [SerializeField] private int digitsLimit = 0;

    [Header("Rules")]
    [Tooltip("Restart the current number from the beginning after a mistake")]
    [SerializeField] private bool resetProgressOnMistake = false;
    [Tooltip("Make all buttons non-interactable when the game stops (finished or game over). Buttons will show their Disabled color.")]
    [SerializeField] private bool disableButtonsOnStop = false;

    [Header("Mistake Feedback")]
    [Tooltip("Value passed to CountdownBar.ApplyPenalty() on each mistake")]
    [SerializeField] private float mistakePenalty = 1f;
    [Tooltip("Also apply penalty and shake when Call is pressed before the number is complete")]
    [SerializeField] private bool penalizeWrongCall = true;
    [Tooltip("Shake the screen on each mistake")]
    [SerializeField] private bool shakeOnMistake = true;
    [Tooltip("Use the shake duration / strength below instead of ScreenShake's default settings")]
    [SerializeField] private bool overrideShakeSettings = false;
    [Tooltip("Shake duration in seconds (only when Override Shake Settings is on)")]
    [SerializeField] private float mistakeShakeDuration = 0.3f;
    [Tooltip("Shake strength in screen pixels (only when Override Shake Settings is on)")]
    [SerializeField] private float mistakeShakeStrength = 15f;

    // --------------------------- Internal State ---------------------------
    private enum State { Idle, Playing, Finished, GameOver }
    private State state = State.Idle;

    private TMP_Text phoneText;
    private Button callBtn;
    private GameManager gameManager;
    private CountdownBar countdown;
    private ScreenShake screenShake;

    // Player script found on Player, and its PlayWrongAnimation() method
    private Component playerComponent;
    private MethodInfo playWrongAnimationMethod;

    // Script class names to look for on Player, in order (both spellings are tried)
    private static readonly string[] PlayerScriptNames = { "playerScript", "PlayerScript" };
    private static readonly string[] SupermanPlayerScriptNames = { "supermanPlayerScript", "SupermanPlayerScript" };

    private readonly Dictionary<char, Button> keyButtons = new Dictionary<char, Button>();
    private readonly Dictionary<Selectable, Coroutine> pressRoutines = new Dictionary<Selectable, Coroutine>();
    private readonly List<int> keyPositions = new List<int>(); // Indices of pressable characters in the display string
    private readonly StringBuilder sb = new StringBuilder(32);

    private string currentNumber = string.Empty;
    private string lastNumber = string.Empty;
    private int progress; // Number of characters entered correctly so far
    private Coroutine errorRoutine;
    private bool callHighlightApplied;

    // Digit range actually used at runtime (starts from minDigits / maxDigits, raised by IncreaseDifficulty)
    private int currentMinDigits;
    private int currentMaxDigits;

#if ENABLE_INPUT_SYSTEM
    private Keyboard subscribedKeyboard;
#endif

    public bool IsPlaying => state == State.Playing;
    public string CurrentNumber => currentNumber;
    public int CurrentMinDigits => currentMinDigits;
    public int CurrentMaxDigits => currentMaxDigits;

    /// <summary>True when the whole number has been entered and the player should press Call.</summary>
    public bool IsCallReady => state == State.Playing && keyPositions.Count > 0 && progress >= keyPositions.Count;

    // --------------------------- Lifecycle ---------------------------
    private void Awake()
    {
        ResetDifficulty();

        if (phoneNumberContainer != null) phoneText = phoneNumberContainer.GetComponent<TMP_Text>();
        if (callButton != null) callBtn = callButton.GetComponent<Button>();
        if (gameManagerObject != null) gameManager = gameManagerObject.GetComponent<GameManager>();
        if (countdownBar != null) countdown = countdownBar.GetComponent<CountdownBar>();
        if (screenShakeObject != null) screenShake = screenShakeObject.GetComponent<ScreenShake>();
        FindPlayerScript();

        if (phoneText == null) Debug.LogError("[ScamManager] No TextMeshPro - Text (UI) found on phoneNumberContainer", this);
        if (callBtn == null) Debug.LogError("[ScamManager] No Button found on callButton", this);
        else if (callBtn.spriteState.highlightedSprite == null)
            Debug.LogWarning("[ScamManager] callButton has no Highlighted Sprite assigned (Button > Transition: Sprite Swap)", this);
        if (gameManager == null) Debug.LogError("[ScamManager] No GameManager found on gameManagerObject", this);
        if (countdown == null) Debug.LogWarning("[ScamManager] No CountdownBar found on countdownBar; mistakes will not apply a penalty", this);
        if (screenShakeObject != null && screenShake == null) Debug.LogWarning("[ScamManager] No ScreenShake found on screenShakeObject", this);

        if (phoneText != null)
        {
            phoneText.richText = true;
            phoneText.text = string.Empty;
        }
        if (error_Redbox != null) error_Redbox.SetActive(false);

        RegisterKey('1', phoneNumber1);
        RegisterKey('2', phoneNumber2);
        RegisterKey('3', phoneNumber3);
        RegisterKey('4', phoneNumber4);
        RegisterKey('5', phoneNumber5);
        RegisterKey('6', phoneNumber6);
        RegisterKey('7', phoneNumber7);
        RegisterKey('8', phoneNumber8);
        RegisterKey('9', phoneNumber9);
        RegisterKey('0', phoneNumber0);
        RegisterKey('*', phoneNumberx);
        RegisterKey('#', phoneNumberxx);

        if (callBtn != null) callBtn.onClick.AddListener(OnCallButtonClicked);
    }

    /// <summary>
    /// Looks for playerScript on Player. If it is not there, falls back to supermanPlayerScript.
    /// Scripts are found by class name, so this compiles even if one of them does not exist.
    /// </summary>
    private void FindPlayerScript()
    {
        playerComponent = null;
        playWrongAnimationMethod = null;

        if (Player == null)
        {
            Debug.LogWarning("[ScamManager] Player is not assigned; PlayWrongAnimation() will not be called", this);
            return;
        }

        playerComponent = FindComponentByName(Player, PlayerScriptNames);
        if (playerComponent == null)
            playerComponent = FindComponentByName(Player, SupermanPlayerScriptNames);

        if (playerComponent == null)
        {
            Debug.LogWarning("[ScamManager] Neither playerScript nor supermanPlayerScript was found on Player", this);
            return;
        }

        playWrongAnimationMethod = playerComponent.GetType().GetMethod(
            "PlayWrongAnimation",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, System.Type.EmptyTypes, null);

        if (playWrongAnimationMethod == null)
            Debug.LogWarning($"[ScamManager] {playerComponent.GetType().Name} has no PlayWrongAnimation() method", this);
    }

    private static Component FindComponentByName(GameObject target, string[] names)
    {
        foreach (string n in names)
        {
            Component c = target.GetComponent(n);
            if (c != null) return c;
        }
        return null;
    }

    private void PlayPlayerWrongAnimation()
    {
        if (playerComponent == null || playWrongAnimationMethod == null) return;
        playWrongAnimationMethod.Invoke(playerComponent, null);
    }

    private void RegisterKey(char key, Button button)
    {
        if (button == null)
        {
            Debug.LogWarning($"[ScamManager] Button '{key}' is not assigned", this);
            return;
        }
        keyButtons[key] = button;
        button.onClick.AddListener(() => OnKeyButtonClicked(key));
    }

    private void OnDestroy()
    {
        foreach (var b in keyButtons.Values) if (b != null) b.onClick.RemoveAllListeners();
        if (callBtn != null) callBtn.onClick.RemoveListener(OnCallButtonClicked);
#if ENABLE_INPUT_SYSTEM
        UnsubscribeKeyboard();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        UnsubscribeKeyboard();
#endif
    }

    private void OnValidate()
    {
        minDigits = Mathf.Max(4, minDigits);
        maxDigits = Mathf.Max(minDigits, maxDigits);
        minDigitsIncrease = Mathf.Max(0, minDigitsIncrease);
        maxDigitsIncrease = Mathf.Max(0, maxDigitsIncrease);
        digitsLimit = Mathf.Max(0, digitsLimit);
        errorDisplaySeconds = Mathf.Max(0f, errorDisplaySeconds);
        keyPressVisualSeconds = Mathf.Max(0f, keyPressVisualSeconds);
        mistakeShakeDuration = Mathf.Max(0.01f, mistakeShakeDuration);
    }

    private void Update()
    {
        if (state != State.Playing) return;

        // GameManager may set IsGameOver to true at any time
        if (gameManager != null && gameManager.IsGameOver)
        {
            EnterGameOver();
            return;
        }

        PollKeyboard();
    }

    private void LateUpdate()
    {
        // Runs after the EventSystem, so the button's own hover / exit transitions
        // cannot clear the highlight while the number is complete.
        UpdateCallHighlight();
    }

    /// <summary>
    /// While the whole number is entered, show callButton's Highlighted Sprite
    /// (from the Button's Sprite State). A pressed sprite still wins while the button is held.
    /// </summary>
    private void UpdateCallHighlight()
    {
        if (callBtn == null) return;
        Image img = callBtn.image;
        if (img == null) return;

        SpriteState ss = callBtn.spriteState;

        if (IsCallReady && ss.highlightedSprite != null)
        {
            bool showingPressed = ss.pressedSprite != null && img.overrideSprite == ss.pressedSprite;
            if (!showingPressed && img.overrideSprite != ss.highlightedSprite)
                img.overrideSprite = ss.highlightedSprite;
            callHighlightApplied = true;
        }
        else if (callHighlightApplied)
        {
            if (img.overrideSprite == ss.highlightedSprite) img.overrideSprite = null;
            callHighlightApplied = false;
        }
    }

    // --------------------------- Public API ---------------------------
    /// <summary>Starts the game and shows the first random number.</summary>
    public void StartGame()
    {
        if (gameManager == null || phoneText == null)
        {
            Debug.LogError("[ScamManager] References are not fully assigned; cannot start the game", this);
            return;
        }

        StopErrorRoutine();
        if (error_Redbox != null) error_Redbox.SetActive(false);
        SetAllButtonsInteractable(true);

        state = State.Playing;

        if (gameManager.IsGameOver)
        {
            EnterGameOver();
            return;
        }

        NextQuestion();
    }

    /// <summary>
    /// Raises the min / max digit count by minDigitsIncrease / maxDigitsIncrease.
    /// Takes effect from the next generated number.
    /// </summary>
    public void IncreaseDifficulty()
    {
        currentMinDigits += minDigitsIncrease;
        currentMaxDigits += maxDigitsIncrease;

        if (digitsLimit > 0)
        {
            currentMinDigits = Mathf.Min(currentMinDigits, digitsLimit);
            currentMaxDigits = Mathf.Min(currentMaxDigits, digitsLimit);
        }

        currentMinDigits = Mathf.Max(4, currentMinDigits);
        currentMaxDigits = Mathf.Max(currentMinDigits, currentMaxDigits);
    }

    /// <summary>Restores the digit range to the starting minDigits / maxDigits.</summary>
    public void ResetDifficulty()
    {
        currentMinDigits = Mathf.Max(4, minDigits);
        currentMaxDigits = Mathf.Max(currentMinDigits, maxDigits);
    }

    // --------------------------- Input Handling ---------------------------
    private void OnKeyButtonClicked(char key)
    {
        ClearUISelection();
        HandleKey(key, false);
    }

    private void OnCallButtonClicked()
    {
        ClearUISelection();
        HandleCall(false);
    }

    /// <summary>
    /// Deselect after a mouse click so that pressing Enter later is not treated by the
    /// EventSystem as a "Submit" that clicks the last selected button again.
    /// </summary>
    private static void ClearUISelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private bool CanAcceptInput()
    {
        if (state != State.Playing) return false;
        if (gameManager != null && gameManager.IsGameOver)
        {
            EnterGameOver();
            return false;
        }
        return true;
    }

    private void HandleKey(char key, bool fromKeyboard)
    {
        if (!CanAcceptInput()) return;

        if (fromKeyboard && keyButtons.TryGetValue(key, out var btn)) PlayPressVisual(btn);

        // Number is already complete; the player should press Call now
        if (progress >= keyPositions.Count)
        {
            RegisterMistake(false);
            return;
        }

        char expected = currentNumber[keyPositions[progress]];
        if (key == expected)
        {
            progress++;
            RefreshDisplay();
        }
        else
        {
            if (resetProgressOnMistake)
            {
                progress = 0;
                RefreshDisplay();
            }
            RegisterMistake(false);
        }
    }

    private void HandleCall(bool fromKeyboard)
    {
        if (!CanAcceptInput()) return;

        if (fromKeyboard && callBtn != null) PlayPressVisual(callBtn);

        // Pressing Call before the number is complete counts as a mistake
        if (progress < keyPositions.Count)
        {
            RegisterMistake(true);
            return;
        }

        gameManager.CompleteCurrentStage();

        if (gameManager.IsGameOver)
        {
            EnterGameOver();
            return;
        }

        if (gameManager.IsFinished)
        {
            state = State.Finished;
            StopErrorRoutine();
            if (error_Redbox != null) error_Redbox.SetActive(false);
            if (disableButtonsOnStop) SetAllButtonsInteractable(false);
            return;
        }

        NextQuestion();
    }

    private static bool IsKeyChar(char c) => (c >= '0' && c <= '9') || c == '*' || c == '#';

    private void PollKeyboard()
    {
#if ENABLE_INPUT_SYSTEM
        // New Input System: digits / * / # come from the text input event
        // (works for the main row, numpad, Shift+8, Shift+3, and any keyboard layout)
        var kb = Keyboard.current;
        if (kb != subscribedKeyboard)
        {
            UnsubscribeKeyboard();
            if (kb != null)
            {
                kb.onTextInput += OnTextInput;
                subscribedKeyboard = kb;
            }
        }

        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            HandleCall(true);
#else
        // Legacy Input Manager
        string input = Input.inputString;
        for (int i = 0; i < input.Length; i++)
        {
            if (IsKeyChar(input[i])) HandleKey(input[i], true);
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            HandleCall(true);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private void OnTextInput(char c)
    {
        if (state != State.Playing || !isActiveAndEnabled) return;
        if (IsKeyChar(c)) HandleKey(c, true);
    }

    private void UnsubscribeKeyboard()
    {
        if (subscribedKeyboard != null)
        {
            subscribedKeyboard.onTextInput -= OnTextInput;
            subscribedKeyboard = null;
        }
    }
#endif

    // --------------------------- Question Generation ---------------------------
    private void NextQuestion()
    {
        // Clear the previous number
        phoneText.text = string.Empty;
        keyPositions.Clear();
        progress = 0;

        // Generate a new number that differs from the previous one
        string number;
        int guard = 0;
        do
        {
            number = GenerateNumber();
            guard++;
        } while (number == lastNumber && guard < 50);

        currentNumber = number;
        lastNumber = number;

        for (int i = 0; i < currentNumber.Length; i++)
        {
            if (IsKeyChar(currentNumber[i])) keyPositions.Add(i);
        }

        RefreshDisplay();
    }

    /// <summary>
    /// Generates a random phone number, e.g. *113-5532-*525, *0912-345-678*, **42-8810-3376.
    /// Digits are split into 2-4 groups (2-5 digits each), separated by "-" (no spaces).
    /// * is randomly inserted at the start, after a "-", or at the end (# is not used in questions).
    /// "-" is formatting only and does not need to be pressed.
    /// </summary>
    private string GenerateNumber()
    {
        int digitCount = Random.Range(currentMinDigits, currentMaxDigits + 1);

        // Decide group count; each group holds 2-5 digits
        int groupMin = Mathf.Max(2, Mathf.CeilToInt(digitCount / 5f));
        int groupMax = Mathf.Max(groupMin, Mathf.Min(4, digitCount / 2));
        int groupCount = Random.Range(groupMin, groupMax + 1);

        int[] sizes = new int[groupCount];
        for (int i = 0; i < groupCount; i++) sizes[i] = 2;
        int extra = digitCount - groupCount * 2;
        while (extra > 0)
        {
            int idx = Random.Range(0, groupCount);
            if (sizes[idx] < 5)
            {
                sizes[idx]++;
                extra--;
            }
        }

        sb.Clear();

        // Leading symbol (50% chance), doubled 20% of the time
        if (Random.value < 0.5f)
        {
            char sym = RandomSymbol();
            sb.Append(sym);
            if (Random.value < 0.2f) sb.Append(sym);
        }

        for (int g = 0; g < groupCount; g++)
        {
            if (g > 0)
            {
                sb.Append('-');
                // A symbol may follow a "-", e.g. "-*525"
                if (Random.value < 0.2f) sb.Append(RandomSymbol());
            }

            for (int d = 0; d < sizes[g]; d++)
                sb.Append((char)('0' + Random.Range(0, 10)));
        }

        // Trailing symbol (25% chance)
        if (Random.value < 0.25f) sb.Append(RandomSymbol());

        return sb.ToString();
    }

    // Only * is used in questions. The # button still exists, but pressing it counts as a mistake.
    private static char RandomSymbol() => '*';

    // --------------------------- Display ---------------------------
    private void RefreshDisplay()
    {
        if (phoneText == null) return;

        // Color everything before the next character the player must press.
        // This way formatting characters ("-") turn green as soon as
        // the player reaches them, so they are never mistaken for keys to press.
        int end = progress < keyPositions.Count
            ? keyPositions[progress] - 1        // up to (not including) the next key
            : currentNumber.Length - 1;         // number complete: color everything

        if (end < 0)
        {
            phoneText.text = currentNumber;
            return;
        }

        string hex = ColorUtility.ToHtmlStringRGBA(correctColor);

        sb.Clear();
        sb.Append("<color=#").Append(hex).Append('>');
        sb.Append(currentNumber, 0, end + 1);
        sb.Append("</color>");
        sb.Append(currentNumber, end + 1, currentNumber.Length - end - 1);
        phoneText.text = sb.ToString();
    }

    /// <summary>
    /// Handles a mistake: shows error_Redbox, plays the player's wrong animation,
    /// applies the countdown penalty and shakes the screen.
    /// </summary>
    private void RegisterMistake(bool isWrongCall)
    {
        ShowError();
        PlayPlayerWrongAnimation();

        if (isWrongCall && !penalizeWrongCall) return;

        if (countdown != null) countdown.ApplyPenalty(mistakePenalty);

        if (shakeOnMistake && screenShake != null)
        {
            if (overrideShakeSettings) screenShake.Shake(mistakeShakeDuration, mistakeShakeStrength);
            else screenShake.Shake();
        }
    }

    private void ShowError()
    {
        if (error_Redbox == null) return;
        StopErrorRoutine();
        errorRoutine = StartCoroutine(ErrorRoutine());
    }

    private IEnumerator ErrorRoutine()
    {
        error_Redbox.SetActive(true);
        yield return new WaitForSecondsRealtime(errorDisplaySeconds);
        // Keep it visible if the game is over
        if (state != State.GameOver) error_Redbox.SetActive(false);
        errorRoutine = null;
    }

    private void StopErrorRoutine()
    {
        if (errorRoutine != null)
        {
            StopCoroutine(errorRoutine);
            errorRoutine = null;
        }
    }

    private void EnterGameOver()
    {
        state = State.GameOver;
        StopErrorRoutine();
        if (error_Redbox != null) error_Redbox.SetActive(true);
        if (disableButtonsOnStop) SetAllButtonsInteractable(false);
    }

    private void SetAllButtonsInteractable(bool value)
    {
        foreach (var b in keyButtons.Values) if (b != null) b.interactable = value;
        if (callBtn != null) callBtn.interactable = value;
    }

    // ------------------- Pressed visual for keyboard input -------------------
    private void PlayPressVisual(Selectable target)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return;

        if (pressRoutines.TryGetValue(target, out var running) && running != null)
            StopCoroutine(running);

        pressRoutines[target] = StartCoroutine(PressVisualRoutine(target));
    }

    private IEnumerator PressVisualRoutine(Selectable target)
    {
        ApplyVisualState(target, true);
        yield return new WaitForSecondsRealtime(keyPressVisualSeconds);
        ApplyVisualState(target, false);
        pressRoutines.Remove(target);
    }

    /// <summary>Switches between pressed and normal state using the button's own Transition setting (Color Tint / Sprite Swap / Animation).</summary>
    private static void ApplyVisualState(Selectable s, bool pressed)
    {
        if (s == null) return;

        switch (s.transition)
        {
            case Selectable.Transition.ColorTint:
                if (s.targetGraphic != null)
                {
                    ColorBlock cb = s.colors;
                    Color c = (pressed ? cb.pressedColor : cb.normalColor) * cb.colorMultiplier;
                    s.targetGraphic.CrossFadeColor(c, pressed ? 0f : cb.fadeDuration, true, true);
                }
                break;

            case Selectable.Transition.SpriteSwap:
                if (s.image != null)
                    s.image.overrideSprite = pressed ? s.spriteState.pressedSprite : null;
                break;

            case Selectable.Transition.Animation:
                Animator anim = s.animator;
                if (anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null)
                {
                    AnimationTriggers t = s.animationTriggers;
                    anim.ResetTrigger(t.normalTrigger);
                    anim.ResetTrigger(t.highlightedTrigger);
                    anim.ResetTrigger(t.pressedTrigger);
                    anim.ResetTrigger(t.selectedTrigger);
                    anim.ResetTrigger(t.disabledTrigger);
                    anim.SetTrigger(pressed ? t.pressedTrigger : t.normalTrigger);
                }
                break;
        }
    }
}
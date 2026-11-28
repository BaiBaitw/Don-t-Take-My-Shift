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
    [Tooltip("Generate numbers that follow real phone formats from different countries (falls back to random numbers when no real format fits the digit range)")]
    [SerializeField] private bool useRealPhoneFormats = true;
    [Tooltip("Chance (0-1) that the \"-\" is shown. A question never has more than one.")]
    [Range(0f, 1f)]
    [SerializeField] private float dashChance = 0.7f;
    [Tooltip("Chance (0-1) that a real-format number starts with a star code such as *67 (adds 2 digits)")]
    [Range(0f, 1f)]
    [SerializeField] private float starCodeChance = 0.25f;

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
    private readonly List<int> formatCandidates = new List<int>();

    // --------------------------- Real Phone Formats ---------------------------
    /// <summary>
    /// A real phone number layout: fixed leading digits (one is picked at random),
    /// total digit count, and where the single "-" goes.
    /// </summary>
    private readonly struct PhoneFormat
    {
        public readonly int TotalDigits;
        public readonly int DashAfter;      // number of digits before the "-"
        public readonly string[] Prefixes;  // fixed leading digits, one is picked at random

        public PhoneFormat(int totalDigits, int dashAfter, params string[] prefixes)
        {
            TotalDigits = totalDigits;
            DashAfter = dashAfter;
            Prefixes = prefixes;
        }
    }

    // Based on real domestic and international dialing formats.
    // Real numbers often use 2 separators; these are shortened to a single "-".
    // ITU E.164 limits a phone number to 15 digits, so nothing here is longer.
    private static readonly PhoneFormat[] RealFormats =
    {
        // ---- 8 digits ----
        new PhoneFormat( 8, 4, "2", "3", "5", "6", "9"),                 // Hong Kong            9123-4567
        new PhoneFormat( 8, 4, "6", "8", "9"),                           // Singapore            9123-4567
        new PhoneFormat( 8, 4, "6"),                                     // Macau                6612-3456

        // ---- 9 digits ----
        new PhoneFormat( 9, 2, "03", "05", "06", "07"),                  // Taiwan landline      07-1234567
        new PhoneFormat( 9, 5, "9"),                                     // Brazil mobile        91234-5678

        // ---- 10 digits ----
        new PhoneFormat(10, 4, "09"),                                    // Taiwan mobile        0912-345678
        new PhoneFormat(10, 2, "02", "04"),                              // Taiwan landline      02-23456789
        new PhoneFormat(10, 2, "03", "06"),                              // Japan landline       03-12345678
        new PhoneFormat(10, 3, "06", "08", "09"),                        // Thailand mobile      081-2345678
        new PhoneFormat(10, 4, "03", "07", "08", "09"),                  // Vietnam mobile       0912-345678
        new PhoneFormat(10, 3, "012", "013", "016", "017", "019"),       // Malaysia mobile      012-3456789
        new PhoneFormat(10, 5, "6", "7", "8", "9"),                      // India mobile         98765-43210
        new PhoneFormat(10, 4, "04"),                                    // Australia mobile     0412-345678
        new PhoneFormat(10, 2, "06", "07"),                              // France mobile        06-12345678
        new PhoneFormat(10, 3, "2", "3", "4", "5", "6", "7", "8", "9"),  // USA / Canada         212-5551234
        new PhoneFormat(10, 2, "33", "55", "81"),                        // Mexico               55-12345678

        // ---- 11 digits ----
        new PhoneFormat(11, 3, "070", "080", "090"),                     // Japan mobile         090-12345678
        new PhoneFormat(11, 3, "010"),                                   // Korea mobile         010-12345678
        new PhoneFormat(11, 3, "13", "15", "17", "18", "19"),            // China mobile         138-12345678
        new PhoneFormat(11, 3, "010", "020", "021"),                     // China landline       021-12345678
        new PhoneFormat(11, 4, "09"),                                    // Philippines mobile   0917-1234567
        new PhoneFormat(11, 3, "011"),                                   // Malaysia mobile      011-12345678
        new PhoneFormat(11, 5, "07"),                                    // UK mobile            07123-456789
        new PhoneFormat(11, 3, "020"),                                   // UK London            020-12345678

        // ---- 12 digits ----
        new PhoneFormat(12, 4, "081", "082", "085", "087", "089"),       // Indonesia mobile     0812-34567890
        new PhoneFormat(12, 4, "015", "016", "017"),                     // Germany mobile       0151-23456789

        // ---- 13-15 digits: international calls dialed from Taiwan (IDD 002 + country code) ----
        new PhoneFormat(13, 5, "002658", "002659"),                      // to Singapore         00265-91234567
        new PhoneFormat(14, 6, "0028525", "0028526", "0028529"),         // to Hong Kong         002852-91234567
        new PhoneFormat(14, 4, "0021"),                                  // to USA               0021-2125551234
        new PhoneFormat(14, 5, "002614"),                                // to Australia mobile  00261-412345678
        new PhoneFormat(14, 5, "002601"),                                // to Malaysia mobile   00260-123456789
        new PhoneFormat(15, 5, "0028170", "0028180", "0028190"),         // to Japan mobile      00281-9012345678
        new PhoneFormat(15, 5, "0028210"),                               // to Korea mobile      00282-1012345678
        new PhoneFormat(15, 5, "002447"),                                // to UK mobile         00244-7123456789
    };

    // North American star codes: *67 hides caller ID, *82 shows it (each adds 2 digits)
    private static readonly string[] StarCodes = { "*67", "*82" };
    private const int StarCodeDigits = 2;

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
    /// Generates the next question. Uses a real phone format when one fits the current
    /// digit range; otherwise falls back to a random number. Never more than one "-".
    /// </summary>
    private string GenerateNumber()
    {
        if (useRealPhoneFormats)
        {
            string real = GenerateRealNumber();
            if (real != null) return real;
        }
        return GenerateRandomNumber();
    }

    /// <summary>
    /// Picks a real phone format whose digit count is within the current range,
    /// e.g. 0912-345678, 02-23456789, 98765-43210, *67-2125551234, 00281-9012345678.
    /// Returns null when no real format fits (e.g. after difficulty goes past 15 digits).
    /// </summary>
    private string GenerateRealNumber()
    {
        bool withStar = Random.value < starCodeChance;
        if (!CollectFormats(withStar))
        {
            withStar = !withStar;
            if (!CollectFormats(withStar)) return null;
        }

        PhoneFormat f = RealFormats[formatCandidates[Random.Range(0, formatCandidates.Count)]];
        string prefix = f.Prefixes[Random.Range(0, f.Prefixes.Length)];
        bool showDash = Random.value < dashChance;

        sb.Clear();

        // With a star code, the "-" goes after the code and the number itself stays in one block
        if (withStar)
        {
            sb.Append(StarCodes[Random.Range(0, StarCodes.Length)]);
            if (showDash) sb.Append('-');
        }

        int dashAt = (!withStar && showDash) ? f.DashAfter : -1;

        for (int i = 0; i < f.TotalDigits; i++)
        {
            if (i == dashAt) sb.Append('-');

            char c;
            if (i < prefix.Length) c = prefix[i];
            else if (i == prefix.Length) c = (char)('0' + Random.Range(1, 10)); // first free digit is never 0
            else c = (char)('0' + Random.Range(0, 10));
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Fills formatCandidates with formats that fit the current digit range.</summary>
    private bool CollectFormats(bool withStar)
    {
        formatCandidates.Clear();
        int extra = withStar ? StarCodeDigits : 0;

        for (int i = 0; i < RealFormats.Length; i++)
        {
            int digits = RealFormats[i].TotalDigits + extra;
            if (digits >= currentMinDigits && digits <= currentMaxDigits)
                formatCandidates.Add(i);
        }
        return formatCandidates.Count > 0;
    }

    /// <summary>
    /// Fallback: random number, e.g. *0912-345678, 113553*2525, **4288-103376*.
    /// The digits are either one block, or split into two blocks by a single "-"
    /// (each block has at least 2 digits). A question never has more than one "-".
    /// * is randomly inserted at the start, right after the "-", or at the end (# is not used in questions).
    /// </summary>
    private string GenerateRandomNumber()
    {
        int digitCount = Random.Range(currentMinDigits, currentMaxDigits + 1);

        // Split position: 0 = no "-", otherwise number of digits before the "-"
        int splitAt = 0;
        if (digitCount >= 4 && Random.value < dashChance)
            splitAt = Random.Range(2, digitCount - 1); // both sides get at least 2 digits

        sb.Clear();

        // Leading symbol (50% chance), doubled 20% of the time
        if (Random.value < 0.5f)
        {
            char sym = RandomSymbol();
            sb.Append(sym);
            if (Random.value < 0.2f) sb.Append(sym);
        }

        for (int d = 0; d < digitCount; d++)
        {
            if (splitAt > 0 && d == splitAt)
            {
                sb.Append('-');
                // A symbol may follow the "-", e.g. "-*525"
                if (Random.value < 0.2f) sb.Append(RandomSymbol());
            }
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
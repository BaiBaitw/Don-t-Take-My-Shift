using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Code-ordering mini game.
/// 1. Call StartGame() from outside: a random question (within the current difficulty range)
///    is shown in questionContainer, formatted as code (max 3 lines).
/// 2. The question is split into tokens of 1~7 characters. Each token becomes a button
///    (option1~option7 by length), placed in optionContainer in random order.
/// 3. The player must click the tokens in code order (judged on mouse release).
///    Correct: the button is removed, remaining buttons slide left, and an answer object
///    (answer1~answer7 by length) is added to answerContainer.
///    Wrong: error_Redbox is shown for a few seconds, CountdownBar.ApplyPenalty() is called
///    and the screen shakes (ScreenShake).
/// 4. When the question is complete: GameManager.CompleteCurrentStage() is called and all
///    spawned objects are cleared.
///    GameManager.IsFinished == true  -> stop, keep the existing objects.
///    GameManager.IsGameOver == true  -> no button can be clicked, error_Redbox stays visible.
/// 5. Call IncreaseDifficulty() from outside to raise the min/max number of options.
/// </summary>
public class EngineerManager : MonoBehaviour
{
    // ------------------------- Prefabs (by character count 1~7) -------------------------
    [Header("Answer Prefabs (by character count 1~7)")]
    public GameObject answer1;
    public GameObject answer2;
    public GameObject answer3;
    public GameObject answer4;
    public GameObject answer5;
    public GameObject answer6;
    public GameObject answer7;

    [Header("Option Prefabs (buttons, by character count 1~7)")]
    public GameObject option1;
    public GameObject option2;
    public GameObject option3;
    public GameObject option4;
    public GameObject option5;
    public GameObject option6;
    public GameObject option7;

    // ------------------------- Scene references -------------------------
    [Header("Containers")]
    [Tooltip("Must have a TextMeshPro - Text (UI) component")]
    public GameObject questionContainer;
    public GameObject optionContainer;
    public GameObject answerContainer;

    [Header("Game Manager")]
    public GameManager gameManager;

    [Header("Error")]
    public GameObject error_Redbox;

    [Header("Wrong Click Feedback")]
    [Tooltip("Object with the CountdownBar script")]
    public CountdownBar countdownBar;
    [Tooltip("Value passed to countdownBar.ApplyPenalty() on a wrong click")]
    public float penaltyAmount = 1f;
    [Tooltip("Object with the ScreenShake script (optional)")]
    public ScreenShake screenShake;
    [Tooltip("Shake duration on a wrong click (seconds). 0 = use ScreenShake's default")]
    public float errorShakeDuration = 0f;
    [Tooltip("Shake strength on a wrong click (pixels). 0 = use ScreenShake's default")]
    public float errorShakeStrength = 0f;

    // ------------------------- Difficulty -------------------------
    [Header("Difficulty (number of options per question)")]
    [Tooltip("Minimum number of options a question can have")]
    public int minOptionCount = 4;
    [Tooltip("Maximum number of options a question can have")]
    public int maxOptionCount = 7;
    [Tooltip("How much minOptionCount increases on each IncreaseDifficulty() call")]
    public int minIncreaseStep = 1;
    [Tooltip("How much maxOptionCount increases on each IncreaseDifficulty() call")]
    public int maxIncreaseStep = 1;

    // ------------------------- Layout -------------------------
    [Header("Layout")]
    [Tooltip("Spacing between option buttons")]
    public float optionSpacing = 10f;
    [Tooltip("Spacing between answer objects")]
    public float answerSpacing = 10f;
    [Tooltip("Slide speed of the option buttons when filling a gap (pixels/sec). 0 = instant")]
    public float optionSlideSpeed = 1500f;

    [Header("Error Display")]
    [Tooltip("Seconds error_Redbox stays visible after a wrong click")]
    public float errorDisplayTime = 1.5f;

    [Header("Token Colors (each token gets one of these at random)")]
    public Color textColor1 = new Color(0.30f, 0.85f, 1.00f); // cyan
    public Color textColor2 = new Color(1.00f, 0.85f, 0.25f); // yellow
    public Color textColor3 = new Color(1.00f, 0.45f, 0.70f); // pink
    public Color textColor4 = new Color(0.45f, 1.00f, 0.50f); // green

    [Header("Debug")]
    [Tooltip("Log every click (clicked token vs expected token) to the Console")]
    public bool logClicks = true;

    [Header("Question Display")]
    [Tooltip("Number of spaces per indent level")]
    public int indentSpaces = 4;

    // ------------------------- Question bank -------------------------
    // 110 questions, uppercase, max 35 characters each, max 3 lines when displayed, 3~10 options each.
    // Tokens are separated by a single space; every token is 1~7 characters.
    // A standalone "/" marks a line break (it is not a token). Indentation is automatic.
    private static readonly string[] QuestionBank =
    {
        "WHILE (X) X-- ;",
        "INT X = 5 ;",
        "IF (OK) RUN () ;",
        "STR S = \"HI\" ;",
        "WHILE (I<9) I++ ;",
        "DO X++ ; WHILE (X<5) ;",
        "IF (X) { X=0 ; }",
        "WHILE (ON) { WAIT () ; }",
        "IF (X>3) { / PRINT (\"OK\") ; / }",
        "IF (A==B) { / X=0 ; / }",
        "IF (HP<=0) { / DIE () ; / }",
        "IF (!OK) { / RETURN ; / }",
        "IF (A&&B) { / WIN () ; / }",
        "WHILE (X<5) { / X++ ; / }",
        "WHILE (HP>0) { / HIT () ; / }",
        "WHILE (TRUE) { / BREAK ; / }",
        "FOR (;;) { / RUN () ; / }",
        "DO { / X++ ; / } WHILE (X<9) ;",
        "DO { / READ () ; / } WHILE (!EOF) ;",
        "IF (X<0) { / X = -X ; / }",
        "X = 0 ;",
        "WHILE (N>1) { / N = N/2 ; / }",
        "FOR (I=0; I<3; I++) { / PRINT (I) ; / }",
        "INT N = A + B ;",
        "FOR (I=0; I<N; I++) { / SUM++ ; / }",
        "SWITCH (X) { / CASE 1: Y=5 ; BREAK ; / }",
        "SWITCH (C) { / CASE 2: C=1 ; BREAK ; / }",
        "SWITCH (K) { / DEFAULT : K=0 ; / }",
        "DO { / N = N-1 ; / } WHILE (N>0) ;",
        "BOOL OK = TRUE ;",
        "WHILE (X<9) { / X = X + 1 ; / }",
        "IF (A) { / IF (B) C () ; / }",
        "FOR (;;) { / IF (X) BREAK ; / }",
        "WHILE (X) { / IF (Y) X=0 ; / }",
        "SWITCH (N) { / CASE 0: N=1 ; BREAK ; / }",
        "IF (A>B) M=A ; / ELSE M=B ;",
        "IF (X) X=0 ; / ELSE X=1 ;",
        "IF (OK) GO () ; / ELSE END () ;",
        "FOR (;;) { / IF (X>9) { BREAK ; } / }",
        "DO { / I+=3 ; / } WHILE (I<30) ;",
        "FOR (I=0; I<3; I++) { / S += I ; / }",
        "FOR (J=0; J<N; J++) { / A[J] = 0 ; / }",
        "WHILE (X>0) { / X-- ; PRINT (X) ; / }",
        "WHILE (A<B) { / A = A * 2 ; / }",
        "IF (X>0) Y=1 ; / ELSE Y=0 ;",
        "IF (A>B) M = A ; / ELSE M++ ;",
        "IF (S<60) F () ; / ELSE P () ;",
        "WHILE (I<N) { / S += I ; I++ ; / }",
        "WHILE (Q) { / X += Q ; Q-- ; / }",
        "DO { / X *= 2 ; / } WHILE (X<99) ;",

        "PRINT (\"HI\") ;",
        "RETURN 0 ;",
        "Y = X ;",
        "X += 2 ;",
        "N -= 1 ;",
        "A = B ;",
        "INT I = 0 ;",
        "FLOAT F = 1.5 ;",
        "CHAR C = 'A' ;",
        "BOOL B = FALSE ;",
        "X = Y * 2 ;",
        "S = A + B ;",
        "HP = HP - 1 ;",
        "INT A = 10 ;",
        "RETURN A + B ;",
        "IF (X) Y++ ;",
        "WHILE (!OK) TRY () ;",
        "IF (A>B) SWAP () ;",
        "DO Y++ ; WHILE (Y<3) ;",
        "INT X = 5 ; / X++ ;",
        "IF (X==0) { / RESET () ; / }",
        "IF (HP<50) { / HEAL () ; / }",
        "WHILE (N<9) { / N++ ; / }",
        "WHILE (!DONE) { / WORK () ; / }",
        "FOR (I=0; I<2; I++) { / JUMP () ; / }",
        "FOR (I=0; I<8; I++) { / A++ ; / }",
        "DO { / Y-- ; / } WHILE (Y>0) ;",
        "SWITCH (S) { / CASE 5: S=0 ; BREAK ; / }",
        "SWITCH (T) { / DEFAULT : T++ ; / }",
        "SWITCH (A) { / CASE 7: A++ ; BREAK ; / }",
        "IF (X!=Y) { / X = Y ; / }",
        "IF (A<0) { / A = 0 ; / }",
        "WHILE (T>0) { / T = T-1 ; / }",
        "IF (OK) { / SCORE++ ; / }",
        "IF (X>Y) { / MAX = X ; / }",
        "INT A = 1 ; / A *= 3 ;",
        "X = 1 ; / Y = X + 2 ;",
        "INT S = 0 ; / S += 9 ;",
        "INT X = 3 ; / IF (X) GO () ;",
        "IF (X>5) Y = 1 ; / ELSE Y++ ;",
        "IF (HP) RUN () ; / ELSE DIE () ;",
        "IF (A==1) B () ; / ELSE C () ;",
        "IF (A>9) A = 9 ; / ELSE A++ ;",
        "WHILE (I<5) { / S += I ; I++ ; / }",
        "WHILE (Y<20) { / Y = Y + 4 ; / }",
        "FOR (I=0; I<9; I++) { / T += 2 ; / }",
        "FOR (N=1; N<5; N++) { / P *= N ; / }",
        "DO { / S += 5 ; / } WHILE (S<50) ;",
        "DO { / HP += 1 ; / } WHILE (HP<9) ;",
        "IF (X) { / A=1 ; B=2 ; / }",

        "RETURN X ;",
        "PRINT (X) ;",
        "JUMP () ;",
        "SHOOT (2) ;",
        "INT X ;",
        "FLOAT Y ;",
        "WAIT (3) ;",
        "BOOL B ;",
        "X = 9 ;",
        "Y *= 3 ;",
    };

    private const int MaxTokenLength = 7;
    private const int MaxQuestionLength = 35;
    private const int MaxQuestionLines = 3;
    private const string LineBreakMarker = "/";

    // A "(" token after these keywords gets a space, e.g. IF (X>3).
    // After anything else it does not, e.g. PRINT("OK").
    private static readonly HashSet<string> SpaceBeforeParenKeywords =
        new HashSet<string> { "IF", "WHILE", "FOR", "SWITCH" };

    // ------------------------- Runtime state -------------------------
    private class OptionEntry
    {
        public GameObject go;
        public RectTransform rect;
        public Button button;
        public string token;
        public Vector2 targetPos;
    }

    private GameObject[] answerPrefabs;
    private GameObject[] optionPrefabs;
    private TMP_Text questionText;

    private readonly List<OptionEntry> options = new List<OptionEntry>();
    private readonly List<RectTransform> answers = new List<RectTransform>();
    private List<string> currentTokens = new List<string>();
    // Same token text = same color, so the question, the button and the answer always match.
    private readonly Dictionary<string, Color> tokenColors = new Dictionary<string, Color>();
    private int nextTokenIndex;
    private int lastQuestionIndex = -1;
    private int roundId;

    private bool isPlaying;
    private bool gameOverApplied;
    private Coroutine errorRoutine;

    private bool GmIsFinished => gameManager != null && gameManager.IsFinished;
    private bool GmIsGameOver => gameManager != null && gameManager.IsGameOver;

    // ------------------------- Unity lifecycle -------------------------
    private void Awake()
    {
        answerPrefabs = new[] { answer1, answer2, answer3, answer4, answer5, answer6, answer7 };
        optionPrefabs = new[] { option1, option2, option3, option4, option5, option6, option7 };

        if (questionContainer != null)
            questionText = questionContainer.GetComponent<TMP_Text>();
        if (questionText == null)
            Debug.LogWarning("[EngineerManager] No TextMeshPro - Text (UI) found on questionContainer.");

        if (error_Redbox != null)
        {
            MakeNonBlocking(error_Redbox);
            error_Redbox.SetActive(false);
        }

        ValidateQuestionBank();
    }

    private void OnValidate()
    {
        minOptionCount = Mathf.Max(1, minOptionCount);
        maxOptionCount = Mathf.Max(minOptionCount, maxOptionCount);
    }

    private void Update()
    {
        // Game over: lock all buttons and keep error_Redbox visible.
        if (GmIsGameOver)
        {
            if (!gameOverApplied) ApplyGameOver();
            if (error_Redbox != null && !error_Redbox.activeSelf) error_Redbox.SetActive(true);
            return;
        }

        // Finished: stop all actions, keep existing objects.
        if (GmIsFinished)
        {
            isPlaying = false;
            return;
        }

        UpdateOptionSlide();
    }

    // ------------------------- Public API -------------------------
    /// <summary>Picks a random question within the difficulty range and builds the options.</summary>
    public void StartGame()
    {
        if (GmIsGameOver || GmIsFinished) return;

        roundId++;
        gameOverApplied = false;
        StopError();
        ClearAll();

        int index = PickQuestionIndex();
        lastQuestionIndex = index;

        currentTokens = Tokenize(QuestionBank[index]);
        nextTokenIndex = 0;
        AssignTokenColors(currentTokens);

        if (questionText != null)
        {
            questionText.richText = true;
            questionText.text = FormatCode(QuestionBank[index]);
        }

        foreach (string token in ShuffledTokens(currentTokens))
            CreateOption(token);

        LayoutOptions(true);
        isPlaying = true;
    }

    /// <summary>Raises the min and max number of options by the configured steps.</summary>
    public void IncreaseDifficulty()
    {
        minOptionCount = Mathf.Max(1, minOptionCount + minIncreaseStep);
        maxOptionCount = Mathf.Max(minOptionCount, maxOptionCount + maxIncreaseStep);
    }

    // ------------------------- Question selection -------------------------
    private int PickQuestionIndex()
    {
        int min = Mathf.Max(1, minOptionCount);
        int max = Mathf.Max(min, maxOptionCount);

        var candidates = new List<int>();
        for (int i = 0; i < QuestionBank.Length; i++)
        {
            int count = Tokenize(QuestionBank[i]).Count;
            if (count >= min && count <= max) candidates.Add(i);
        }

        // Nothing in range: fall back to the questions closest to the range.
        if (candidates.Count == 0)
        {
            int bestDistance = int.MaxValue;
            for (int i = 0; i < QuestionBank.Length; i++)
            {
                int count = Tokenize(QuestionBank[i]).Count;
                int distance = count < min ? min - count : count - max;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    candidates.Clear();
                }
                if (distance == bestDistance) candidates.Add(i);
            }
            Debug.Log($"[EngineerManager] No question has {min}~{max} options. Using the closest ones instead.");
        }

        // Avoid repeating the previous question when possible.
        if (candidates.Count > 1) candidates.Remove(lastQuestionIndex);

        return candidates[Random.Range(0, candidates.Count)];
    }

    // ------------------------- Click handling -------------------------
    // Any button can be clicked at any time, wherever it is in the row.
    // A click is correct when the button's text equals the NEXT token of the question
    // (the question is read from its beginning: IF -> (X>3) -> { -> PRINT -> ...).
    private void OnOptionClicked(OptionEntry entry)
    {
        // Button.onClick already fires on mouse release (while still over the button).
        if (!isPlaying || GmIsGameOver || GmIsFinished) return;
        if (!options.Contains(entry)) return;
        if (nextTokenIndex >= currentTokens.Count) return;

        string expected = currentTokens[nextTokenIndex];
        bool correct = entry.token == expected;

        if (logClicks)
            Debug.Log($"[EngineerManager] Clicked \"{entry.token}\", expected \"{expected}\" -> {(correct ? "CORRECT" : "WRONG")}");

        // Compared by text, so identical tokens (e.g. two ";") are interchangeable.
        if (correct)
        {
            options.Remove(entry);
            Destroy(entry.go);

            CreateAnswer(entry.token);
            nextTokenIndex++;
            LayoutOptions(false);

            if (nextTokenIndex >= currentTokens.Count)
                OnQuestionComplete();
        }
        else
        {
            OnWrongClick();
        }
    }

    private void OnWrongClick()
    {
        ShowError();

        if (countdownBar != null)
            countdownBar.ApplyPenalty(penaltyAmount);

        if (screenShake != null)
        {
            if (errorShakeDuration > 0f || errorShakeStrength > 0f)
            {
                float d = errorShakeDuration > 0f ? errorShakeDuration : screenShake.duration;
                float st = errorShakeStrength > 0f ? errorShakeStrength : screenShake.strength;
                screenShake.Shake(d, st);
            }
            else
            {
                screenShake.Shake();
            }
        }
    }

    private void OnQuestionComplete()
    {
        isPlaying = false;
        int finishedRound = roundId;

        if (gameManager != null)
            gameManager.CompleteCurrentStage();
        else
            Debug.LogWarning("[EngineerManager] GameManager is not assigned.");

        // All stages done: stop and keep the existing objects.
        if (GmIsFinished) return;
        // Game over is handled in Update (lock buttons + red box).
        if (GmIsGameOver) return;

        // Not finished yet: clear everything and start a new random question
        // (PickQuestionIndex never repeats the previous one).
        // If CompleteCurrentStage() already called StartGame() itself, don't start another one.
        if (roundId == finishedRound)
            StartGame(); // StartGame() clears all existing objects first
    }

    // ------------------------- Colors -------------------------
    private void AssignTokenColors(List<string> tokens)
    {
        Color[] palette = { textColor1, textColor2, textColor3, textColor4 };
        tokenColors.Clear();
        foreach (string t in tokens)
            if (!tokenColors.ContainsKey(t))
                tokenColors[t] = palette[Random.Range(0, palette.Length)];
    }

    private Color GetTokenColor(string token)
    {
        return tokenColors.TryGetValue(token, out Color c) ? c : Color.white;
    }

    /// <summary>Wraps a token in a TMP color tag. &lt;noparse&gt; keeps "X&lt;3" from being read as a tag.</summary>
    private string ColorizeToken(string token)
    {
        string hex = ColorUtility.ToHtmlStringRGBA(GetTokenColor(token));
        return "<color=#" + hex + "><noparse>" + token + "</noparse></color>";
    }

    // ------------------------- Error display -------------------------
    private void ShowError()
    {
        if (error_Redbox == null) return;
        StopError();
        errorRoutine = StartCoroutine(ErrorRoutine());
    }

    private IEnumerator ErrorRoutine()
    {
        error_Redbox.SetActive(true);
        yield return new WaitForSecondsRealtime(errorDisplayTime);
        if (!GmIsGameOver) error_Redbox.SetActive(false);
        errorRoutine = null;
    }

    private void StopError()
    {
        if (errorRoutine != null)
        {
            StopCoroutine(errorRoutine);
            errorRoutine = null;
        }
        if (error_Redbox != null && !GmIsGameOver)
            error_Redbox.SetActive(false);
    }

    private void ApplyGameOver()
    {
        gameOverApplied = true;
        isPlaying = false;

        if (errorRoutine != null)
        {
            StopCoroutine(errorRoutine);
            errorRoutine = null;
        }
        if (error_Redbox != null) error_Redbox.SetActive(true);

        foreach (OptionEntry o in options)
            if (o.button != null) o.button.interactable = false;
    }

    // ------------------------- Spawning -------------------------
    private void CreateOption(string token)
    {
        GameObject prefab = GetPrefab(optionPrefabs, token, "option");
        if (prefab == null) return;

        GameObject go = Instantiate(prefab, optionContainer.transform, false);
        go.name = "option_" + token;
        SetLabel(go, token, GetTokenColor(token));

        var entry = new OptionEntry
        {
            go = go,
            rect = PrepareRect(go),
            button = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true),
            token = token
        };

        if (entry.button != null)
        {
            entry.button.onClick.RemoveAllListeners();
            entry.button.onClick.AddListener(() => OnOptionClicked(entry));
        }
        else
        {
            Debug.LogWarning($"[EngineerManager] No Button component found on {prefab.name}.");
        }

        options.Add(entry);
    }

    private void CreateAnswer(string token)
    {
        GameObject prefab = GetPrefab(answerPrefabs, token, "answer");
        if (prefab == null) return;

        GameObject go = Instantiate(prefab, answerContainer.transform, false);
        go.name = "answer_" + token;
        SetLabel(go, token, GetTokenColor(token));

        answers.Add(PrepareRect(go));
        LayoutAnswers();
    }

    private GameObject GetPrefab(GameObject[] prefabs, string token, string label)
    {
        int len = Mathf.Clamp(token.Length, 1, MaxTokenLength);
        GameObject prefab = prefabs[len - 1];
        if (prefab == null)
            Debug.LogError($"[EngineerManager] Prefab {label}{len} is not assigned (token: {token}).");
        return prefab;
    }

    private static void SetLabel(GameObject go, string token, Color color)
    {
        TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        label.text = "<noparse>" + token + "</noparse>";
        label.color = color;
        label.raycastTarget = false; // the label should never steal clicks from the button
    }

    /// <summary>
    /// Keeps error_Redbox visible but lets clicks pass through it,
    /// so it never blocks the option buttons underneath.
    /// </summary>
    private static void MakeNonBlocking(GameObject go)
    {
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null) group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    /// <summary>Anchors the object to the container's middle-left, keeping its size and pivot.</summary>
    private static RectTransform PrepareRect(GameObject go)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        Vector2 size = rt.rect.size;
        // Fallback so buttons never stack on top of each other when the prefab reports 0 width.
        if (size.x <= 0f) size.x = Mathf.Max(LayoutUtility.GetPreferredWidth(rt), 50f);
        if (size.y <= 0f) size.y = Mathf.Max(LayoutUtility.GetPreferredHeight(rt), 50f);
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.sizeDelta = size;
        return rt;
    }

    // ------------------------- Layout -------------------------
    private void LayoutOptions(bool instant)
    {
        float x = 0f;
        foreach (OptionEntry o in options)
        {
            float w = o.rect.rect.width * o.rect.localScale.x;
            o.targetPos = new Vector2(x + w * o.rect.pivot.x, 0f);
            x += w + optionSpacing;

            if (instant || optionSlideSpeed <= 0f)
                o.rect.anchoredPosition = o.targetPos;
        }
    }

    private void UpdateOptionSlide()
    {
        if (optionSlideSpeed <= 0f) return;
        float step = optionSlideSpeed * Time.unscaledDeltaTime;
        foreach (OptionEntry o in options)
        {
            if (o.rect == null) continue;
            o.rect.anchoredPosition = Vector2.MoveTowards(o.rect.anchoredPosition, o.targetPos, step);
        }
    }

    private void LayoutAnswers()
    {
        float x = 0f;
        foreach (RectTransform rt in answers)
        {
            float w = rt.rect.width * rt.localScale.x;
            rt.anchoredPosition = new Vector2(x + w * rt.pivot.x, 0f);
            x += w + answerSpacing;
        }
    }

    // ------------------------- Clearing -------------------------
    private void ClearAll()
    {
        options.Clear();
        answers.Clear();

        if (questionText != null) questionText.text = string.Empty;

        DestroyChildren(questionContainer);
        DestroyChildren(optionContainer);
        DestroyChildren(answerContainer);
    }

    private static void DestroyChildren(GameObject parent)
    {
        if (parent == null) return;
        Transform t = parent.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            Destroy(t.GetChild(i).gameObject);
    }

    // ------------------------- Question processing -------------------------
    /// <summary>Splits a question into tokens (line break markers are skipped).</summary>
    private static List<string> Tokenize(string question)
    {
        var list = new List<string>();
        foreach (string s in question.Split(' '))
            if (!string.IsNullOrEmpty(s) && s != LineBreakMarker) list.Add(s);
        return list;
    }

    private static List<string> ShuffledTokens(List<string> tokens)
    {
        var result = new List<string>(tokens);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }
            if (!SameOrder(result, tokens)) break; // try not to land on the correct order
        }
        return result;
    }

    private static bool SameOrder(List<string> a, List<string> b)
    {
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static bool NeedSpace(string prev, string cur)
    {
        if (prev == null) return false;
        if (cur == ";" || cur == ":") return false;
        if (cur.StartsWith("(") && !SpaceBeforeParenKeywords.Contains(prev)) return false; // PRINT("OK")
        return true;
    }

    /// <summary>
    /// Builds the code text shown in questionContainer.
    /// "/" starts a new line; indentation follows the brace depth
    /// (a line starting with "}" is indented one level less).
    /// </summary>
    private string FormatCode(string question)
    {
        List<string> lines = BuildLines(question, indentSpaces, ColorizeToken);
        return string.Join("\n", lines);
    }

    private static List<string> BuildLines(string question, int spacesPerIndent,
                                           System.Func<string, string> wrapToken = null)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        string pad = new string(' ', Mathf.Max(0, spacesPerIndent));
        int depth = 0;
        string prev = null;

        foreach (string tok in question.Split(' '))
        {
            if (string.IsNullOrEmpty(tok)) continue;

            if (tok == LineBreakMarker)
            {
                lines.Add(line.ToString());
                line.Clear();
                prev = null;
                continue;
            }

            if (line.Length == 0)
            {
                int indent = Mathf.Max(0, depth - (tok == "}" ? 1 : 0));
                for (int k = 0; k < indent; k++) line.Append(pad);
            }
            else if (NeedSpace(prev, tok))
            {
                line.Append(' ');
            }
            line.Append(wrapToken != null ? wrapToken(tok) : tok);

            if (tok == "{") depth++;
            else if (tok == "}") depth--;
            prev = tok;
        }

        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    private static string CompactText(List<string> tokens)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0 && NeedSpace(tokens[i - 1], tokens[i])) sb.Append(' ');
            sb.Append(tokens[i]);
        }
        return sb.ToString();
    }

    private static void ValidateQuestionBank()
    {
        foreach (string q in QuestionBank)
        {
            List<string> tokens = Tokenize(q);
            string compact = CompactText(tokens);

            if (compact.Length > MaxQuestionLength)
                Debug.LogWarning($"[EngineerManager] Question longer than {MaxQuestionLength} characters: {compact}");

            if (BuildLines(q, 0).Count > MaxQuestionLines)
                Debug.LogWarning($"[EngineerManager] Question has more than {MaxQuestionLines} lines: {compact}");

            foreach (string t in tokens)
                if (t.Length > MaxTokenLength)
                    Debug.LogWarning($"[EngineerManager] Token longer than {MaxTokenLength} characters: {t} (question: {compact})");
        }
    }
}
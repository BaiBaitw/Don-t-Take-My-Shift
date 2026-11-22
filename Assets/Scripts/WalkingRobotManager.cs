// WalkingRobotManager.cs
// Spawns walkingRobot prefabs into robotContainer based on how many levels are cleared (MenuManager progress),
// and makes them walk around a circle (like walking on a rotating planet).
//
// Setup:
// 1. Add this component to any object in the menu scene.
// 2. Assign walkingRobotPrefab, robotContainer (e.g. the Earth image) and the object that has MenuManager.
// 3. Set "robotCountByProgress": element 0 = robots when 0 levels cleared, element 1 = 1 cleared, ...
// 4. Adjust circle radius / center and the "no spawn" line in the Scene view (Gizmos).
//    (Turn on the Gizmos button in the Game view to see the lines there too.)

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class WalkingRobotManager : MonoBehaviour
{
    public enum ProgressSource
    {
        [Tooltip("Levels cleared (counts immediately after returning from a level)")]
        ClearedLevels,
        [Tooltip("Only counts after the menu's unlock sequence has played (robots appear after the animation)")]
        RevealedLevels
    }

    // ------------------------- References -------------------------
    [Header("References")]
    [Tooltip("walkingRobot prefab")]
    public GameObject walkingRobotPrefab;
    [Tooltip("Parent for the robots; the circle is drawn in this object's local space")]
    public Transform robotContainer;
    [Tooltip("Object with the MenuManager script (used for the number of levels)")]
    public MenuManager menuManager;
    [Tooltip("PlayerWalk object (its SpriteRenderer is used for front/back sorting)")]
    public GameObject playerWalk;

    // ------------------------- Sorting vs PlayerWalk -------------------------
    [Header("Sorting vs PlayerWalk")]
    [Tooltip("Chance (0..1) that a robot is drawn in front of PlayerWalk; the rest are drawn behind")]
    [Range(0f, 1f)] public float inFrontOfPlayerChance = 0.5f;
    [Tooltip("How many Order in Layer steps above / below PlayerWalk")]
    public int sortingOffset = 1;
    [Tooltip("Keep following PlayerWalk's Sorting Layer / Order if it changes at runtime (checked every watchInterval)")]
    public bool followPlayerSorting = true;

    // ------------------------- Robot count -------------------------
    [Header("Robot Count")]
    [Tooltip("How progress is measured")]
    public ProgressSource progressSource = ProgressSource.ClearedLevels;
    [Tooltip("Element i = number of robots when i levels are cleared.\nIf progress is past the end of the list, the last element is used.")]
    public int[] robotCountByProgress = { 0, 1, 2, 3, 5, 7, 9, 12 };
    [Tooltip("Keep checking progress while running (adds / removes robots live, e.g. when using MenuManager's test tools)")]
    public bool watchProgress = true;
    [Tooltip("Seconds between progress checks")]
    public float watchInterval = 0.5f;

    // ------------------------- Circle -------------------------
    [Header("Circle (local space of robotContainer)")]
    [Tooltip("Circle center, relative to robotContainer's pivot")]
    public Vector2 circleCenter = Vector2.zero;
    [Tooltip("Circle radius")]
    public float circleRadius = 300f;
    [Tooltip("Extra distance from the circle for the robot's pivot (e.g. half the robot height if its pivot is at the center)")]
    public float surfaceOffset = 0f;
    [Tooltip("Random +/- radius per robot so they don't all stand on exactly the same line")]
    public float radiusJitter = 0f;

    // ------------------------- Movement -------------------------
    [Header("Movement")]
    [Tooltip("Minimum walking speed (units per second along the circle)")]
    public float minSpeed = 20f;
    [Tooltip("Maximum walking speed (units per second along the circle)")]
    public float maxSpeed = 60f;
    [Tooltip("Chance (0..1) that a robot moves counter-clockwise (planet rotation)")]
    [Range(0f, 1f)] public float counterClockwiseChance = 0.3f;
    [Tooltip("Rotate robots so their feet point to the circle center")]
    public bool alignToSurface = true;
    [Tooltip("Mirror the robot horizontally (negative scale X) based on its walking direction.\n" +
             "WARNING: shaders that cull back faces (e.g. Unlit/Transparent) make mirrored robots invisible. " +
             "Only turn on with a shader that has Cull Off (e.g. Sprites/Default).")]
    public bool flipByDirection = false;
    [Tooltip("Does the robot artwork face right? (turn off if it faces left)")]
    public bool spriteFacesRight = true;
    [Tooltip("Ignore Time.timeScale")]
    public bool useUnscaledTime = true;
    [Tooltip("On: the counter-clockwise share always matches counterClockwiseChance (e.g. 0.3 with 10 robots = exactly 3).\nOff: each robot rolls the dice on its own, so with few robots they can all end up clockwise.")]
    public bool guaranteeDirectionRatio = true;
    [Tooltip("Turn on if robotContainer itself rotates (spinning Earth).\nOn: speeds / directions are as seen on screen, so the container's spin doesn't drag every robot the same way.\nOff: speeds are relative to the container, and its spin is added on top.")]
    public bool cancelContainerRotation = false;

    // ------------------------- Spawn area -------------------------
    [Header("Spawn Area")]
    [Tooltip("Robots never SPAWN above this line (they can still walk there).\nRelative to the radius: 1 = top of circle, 0 = center, -1 = bottom.")]
    [Range(-1f, 1f)] public float noSpawnAboveHeight = 0.3f;
    [Tooltip("Try to keep at least this many degrees between newly spawned robots")]
    public float minSpawnSpacing = 12f;

    // ------------------------- Gizmos -------------------------
    [Header("Gizmos (virtual lines)")]
    public bool showGizmos = true;
    public Color circleColor = new Color(0.2f, 0.9f, 1f, 1f);
    public Color noSpawnArcColor = new Color(1f, 0.3f, 0.3f, 1f);
    public Color noSpawnLineColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("How far the no-spawn line extends past the circle (in radius)")]
    public float lineExtend = 0.25f;
    public int gizmoSegments = 128;

    // ------------------------- Runtime -------------------------
    class Robot
    {
        public Transform t;
        public float angle;        // degrees, 0 = right, 90 = top
        public float speed;        // units per second along the circle
        public bool clockwise;
        public float radiusOffset;
        public Vector3 baseScale;
        // sorting
        public bool inFront;
        public SortingGroup sortingGroup;
        public Renderer[] renderers;
        public int[] baseOrders;
        public int minOrder, maxOrder;
    }

    readonly List<Robot> robots = new List<Robot>();
    float watchTimer;
    int lastTarget = -1;
    SpriteRenderer playerRenderer;
    int lastPlayerLayer = int.MinValue, lastPlayerOrder = int.MinValue;
    bool warnedNoRenderer;
    float lastContainerAngle;
    bool hasContainerAngle;

    /// <summary>Number of robots walking clockwise / counter-clockwise.</summary>
    public int ClockwiseCount { get { int n = 0; foreach (var r in robots) if (r.clockwise) n++; return n; } }
    public int CounterClockwiseCount => robots.Count - ClockwiseCount;

    float Dt => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    Transform Container => robotContainer != null ? robotContainer : transform;

    /// <summary>Number of robots currently on the circle.</summary>
    public int RobotCount => robots.Count;

    // ------------------------- Lifecycle -------------------------
    void Reset()
    {
#if UNITY_2023_1_OR_NEWER
        menuManager = FindFirstObjectByType<MenuManager>();
#else
        menuManager = FindObjectOfType<MenuManager>();
#endif
        playerWalk = GameObject.Find("PlayerWalk");
    }

    void OnValidate()
    {
        if (maxSpeed < minSpeed) maxSpeed = minSpeed;
        if (minSpeed < 0f) minSpeed = 0f;
        if (circleRadius < 0.01f) circleRadius = 0.01f;
        if (gizmoSegments < 16) gizmoSegments = 16;
        if (watchInterval < 0.05f) watchInterval = 0.05f;
    }

    void Start()
    {
        if (walkingRobotPrefab == null)
            Debug.LogWarning("[WalkingRobotManager] No walkingRobotPrefab assigned.", this);
        if (robotContainer == null)
            Debug.LogWarning("[WalkingRobotManager] No robotContainer assigned; using this object instead.", this);

        if (playerWalk != null)
        {
            playerRenderer = playerWalk.GetComponent<SpriteRenderer>();
            if (playerRenderer == null) playerRenderer = playerWalk.GetComponentInChildren<SpriteRenderer>(true);
            if (playerRenderer == null)
                Debug.LogWarning("[WalkingRobotManager] PlayerWalk has no SpriteRenderer; front/back sorting is disabled.", this);
        }
        else
        {
            Debug.LogWarning("[WalkingRobotManager] No PlayerWalk assigned; front/back sorting is disabled.", this);
        }

        Sync();
        CachePlayerSorting();
    }

    void Update()
    {
        float dt = Dt;

        if (watchProgress)
        {
            watchTimer += dt;
            if (watchTimer >= watchInterval)
            {
                watchTimer = 0f;
                Sync();
            }
        }

        if (followPlayerSorting && playerRenderer != null &&
            (playerRenderer.sortingLayerID != lastPlayerLayer || playerRenderer.sortingOrder != lastPlayerOrder))
        {
            CachePlayerSorting();
            foreach (var r in robots) if (r.t != null) ApplySorting(r);
        }

        // How much the container turned this frame (only used when cancelling its rotation)
        float containerSpin = 0f;
        float containerAngle = Container.eulerAngles.z;
        if (cancelContainerRotation && hasContainerAngle)
            containerSpin = Mathf.DeltaAngle(lastContainerAngle, containerAngle);
        lastContainerAngle = containerAngle;
        hasContainerAngle = true;

        for (int i = 0; i < robots.Count; i++)
        {
            var r = robots[i];
            if (r.t == null) continue;
            float radius = RobotRadius(r);
            float degPerSec = r.speed / radius * Mathf.Rad2Deg;
            r.angle += (r.clockwise ? -degPerSec : degPerSec) * dt - containerSpin;
            r.angle = Mathf.Repeat(r.angle, 360f);
            ApplyTransform(r);
        }
    }

    // ------------------------- Progress -------------------------
    /// <summary>How many levels count as cleared, according to progressSource.</summary>
    public int GetProgress()
    {
        int p;
        if (progressSource == ProgressSource.ClearedLevels)
            p = MenuManager.UnlockedCount - 1;
        else
            p = PlayerPrefs.GetInt(MenuManager.KeyRevealed, 0) - 1;

        int levelCount = (menuManager != null && menuManager.levels != null) ? menuManager.levels.Length : int.MaxValue;
        return Mathf.Clamp(p, 0, levelCount);
    }

    /// <summary>Robot count for the current progress.</summary>
    public int GetTargetCount()
    {
        if (robotCountByProgress == null || robotCountByProgress.Length == 0) return 0;
        int idx = Mathf.Clamp(GetProgress(), 0, robotCountByProgress.Length - 1);
        return Mathf.Max(0, robotCountByProgress[idx]);
    }

    /// <summary>Adds / removes robots so the count matches the current progress.</summary>
    public void Sync()
    {
        robots.RemoveAll(r => r.t == null);
        int target = GetTargetCount();
        if (target == lastTarget && target == robots.Count) return;
        lastTarget = target;

        while (robots.Count < target)
        {
            if (!SpawnRobot()) break;
        }
        while (robots.Count > target)
        {
            var r = robots[robots.Count - 1];
            robots.RemoveAt(robots.Count - 1);
            if (r.t != null) Destroy(r.t.gameObject);
        }
    }

    [ContextMenu("Respawn All Robots")]
    public void RespawnAll()
    {
        foreach (var r in robots) if (r.t != null) Destroy(r.t.gameObject);
        robots.Clear();
        lastTarget = -1;
        Sync();
    }

    // ------------------------- Spawning -------------------------
    bool SpawnRobot()
    {
        if (walkingRobotPrefab == null) return false;

        GameObject go = Instantiate(walkingRobotPrefab, Container, false);
        go.name = $"{walkingRobotPrefab.name}_{robots.Count + 1}";

        var r = new Robot
        {
            t = go.transform,
            angle = PickSpawnAngle(),
            speed = Random.Range(minSpeed, maxSpeed),
            clockwise = PickClockwise(),
            radiusOffset = radiusJitter != 0f ? Random.Range(-radiusJitter, radiusJitter) : 0f,
            baseScale = go.transform.localScale,
            inFront = Random.value < inFrontOfPlayerChance
        };
        SetupSorting(r);
        robots.Add(r);
        ApplyTransform(r);
        ApplySorting(r);
        return true;
    }

    bool PickClockwise()
    {
        if (!guaranteeDirectionRatio) return Random.value >= counterClockwiseChance;

        // Keep the counter-clockwise count at round(total * chance)
        int total = robots.Count + 1;
        int wantCcw = Mathf.RoundToInt(total * counterClockwiseChance);
        if (counterClockwiseChance > 0f && counterClockwiseChance < 1f && total >= 2) wantCcw = Mathf.Clamp(wantCcw, 1, total - 1);
        return CounterClockwiseCount >= wantCcw;
    }

    // ------------------------- Sorting -------------------------
    void CachePlayerSorting()
    {
        if (playerRenderer == null) return;
        lastPlayerLayer = playerRenderer.sortingLayerID;
        lastPlayerOrder = playerRenderer.sortingOrder;
    }

    void SetupSorting(Robot r)
    {
        // A SortingGroup on the prefab root sorts the whole robot as one unit
        r.sortingGroup = r.t.GetComponent<SortingGroup>();
        r.renderers = r.t.GetComponentsInChildren<Renderer>(true);
        r.baseOrders = new int[r.renderers.Length];
        r.minOrder = int.MaxValue;
        r.maxOrder = int.MinValue;
        for (int i = 0; i < r.renderers.Length; i++)
        {
            int o = r.renderers[i].sortingOrder;
            r.baseOrders[i] = o;
            r.minOrder = Mathf.Min(r.minOrder, o);
            r.maxOrder = Mathf.Max(r.maxOrder, o);
        }

        if (r.sortingGroup == null && r.renderers.Length == 0 && !warnedNoRenderer)
        {
            warnedNoRenderer = true;
            Debug.LogWarning("[WalkingRobotManager] walkingRobotPrefab has no Renderer / SortingGroup " +
                             "(is it a UI Image?); it can't be sorted against PlayerWalk's SpriteRenderer.", this);
        }
    }

    void ApplySorting(Robot r)
    {
        if (playerRenderer == null) return;
        int layer = playerRenderer.sortingLayerID;
        int playerOrder = playerRenderer.sortingOrder;
        int step = Mathf.Max(1, sortingOffset);

        if (r.sortingGroup != null)
        {
            r.sortingGroup.sortingLayerID = layer;
            r.sortingGroup.sortingOrder = r.inFront ? playerOrder + step : playerOrder - step;
            return;
        }

        // No SortingGroup: shift every renderer, keeping the robot's internal order (e.g. arm over body)
        for (int i = 0; i < r.renderers.Length; i++)
        {
            var rend = r.renderers[i];
            if (rend == null) continue;
            rend.sortingLayerID = layer;
            rend.sortingOrder = r.inFront
                ? playerOrder + step + (r.baseOrders[i] - r.minOrder)    // lowest part sits just above player
                : playerOrder - step - (r.maxOrder - r.baseOrders[i]);   // highest part sits just below player
        }
    }

    /// <summary>Is the robot at this index drawn in front of PlayerWalk?</summary>
    public bool IsRobotInFront(int index) => index >= 0 && index < robots.Count && robots[index].inFront;

    // Spawn range: every angle whose height (sin) is at or below noSpawnAboveHeight.
    // Goes from the left crossing point, through the bottom, to the right crossing point.
    void GetSpawnRange(out float from, out float to)
    {
        float a = Mathf.Asin(Mathf.Clamp(noSpawnAboveHeight, -1f, 1f)) * Mathf.Rad2Deg;
        from = 180f - a;
        to = 360f + a;
    }

    float PickSpawnAngle()
    {
        GetSpawnRange(out float from, out float to);
        float best = Random.Range(from, to);
        if (minSpawnSpacing <= 0f || robots.Count == 0) return best;

        float bestGap = -1f;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float candidate = Random.Range(from, to);
            float gap = float.MaxValue;
            foreach (var r in robots)
                gap = Mathf.Min(gap, Mathf.Abs(Mathf.DeltaAngle(candidate, r.angle)));
            if (gap >= minSpawnSpacing) return candidate;
            if (gap > bestGap) { bestGap = gap; best = candidate; }
        }
        return best; // couldn't satisfy the spacing; use the most spread-out try
    }

    // ------------------------- Transform -------------------------
    float RobotRadius(Robot r) => Mathf.Max(0.01f, circleRadius + surfaceOffset + r.radiusOffset);

    void ApplyTransform(Robot r)
    {
        float rad = r.angle * Mathf.Deg2Rad;
        Vector2 p = circleCenter + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * RobotRadius(r);
        r.t.localPosition = new Vector3(p.x, p.y, r.t.localPosition.z);

        r.t.localRotation = alignToSurface ? Quaternion.Euler(0f, 0f, r.angle - 90f) : Quaternion.identity;

        if (flipByDirection)
        {
            // With alignToSurface, the robot's local "right" points in the clockwise direction.
            bool faceRight = r.clockwise;
            if (!alignToSurface)
            {
                // Without rotation, use the actual screen direction of movement.
                float vx = r.clockwise ? Mathf.Sin(rad) : -Mathf.Sin(rad);
                faceRight = vx >= 0f;
            }
            if (!spriteFacesRight) faceRight = !faceRight;
            Vector3 s = r.baseScale;
            s.x = Mathf.Abs(s.x) * (faceRight ? 1f : -1f);
            r.t.localScale = s;
        }
        else
        {
            r.t.localScale = r.baseScale;
        }
    }

    // ------------------------- Gizmos -------------------------
    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        Transform c = Container;
        int seg = Mathf.Max(16, gizmoSegments);
        float h = Mathf.Clamp(noSpawnAboveHeight, -1f, 1f);

        // Circle: normal color where robots may spawn, red where they may not
        for (int i = 0; i < seg; i++)
        {
            float a0 = (float)i / seg * Mathf.PI * 2f;
            float a1 = (float)(i + 1) / seg * Mathf.PI * 2f;
            float mid = (a0 + a1) * 0.5f;
            Gizmos.color = Mathf.Sin(mid) > h ? noSpawnArcColor : circleColor;
            Gizmos.DrawLine(LocalToWorld(c, a0, circleRadius), LocalToWorld(c, a1, circleRadius));
        }

        // No-spawn line (horizontal)
        float y = h * circleRadius;
        float halfX = circleRadius * (1f + Mathf.Max(0f, lineExtend));
        Vector3 l = c.TransformPoint(new Vector3(circleCenter.x - halfX, circleCenter.y + y, 0f));
        Vector3 rgt = c.TransformPoint(new Vector3(circleCenter.x + halfX, circleCenter.y + y, 0f));
        Gizmos.color = noSpawnLineColor;
        Gizmos.DrawLine(l, rgt);

        // Center cross
        float cs = circleRadius * 0.05f;
        Gizmos.color = circleColor;
        Gizmos.DrawLine(c.TransformPoint(circleCenter + new Vector2(-cs, 0f)), c.TransformPoint(circleCenter + new Vector2(cs, 0f)));
        Gizmos.DrawLine(c.TransformPoint(circleCenter + new Vector2(0f, -cs)), c.TransformPoint(circleCenter + new Vector2(0f, cs)));

#if UNITY_EDITOR
        var style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = noSpawnLineColor;
        Handles.Label(rgt, " No spawn above", style);
        if (Application.isPlaying)
        {
            style.normal.textColor = circleColor;
            Handles.Label(c.TransformPoint(circleCenter), $" Robots: {robots.Count}  CW {ClockwiseCount} / CCW {CounterClockwiseCount}  (progress {GetProgress()})", style);
        }
#endif
    }

    Vector3 LocalToWorld(Transform c, float angleRad, float radius)
    {
        Vector2 p = circleCenter + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;
        return c.TransformPoint(new Vector3(p.x, p.y, 0f));
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerScript : MonoBehaviour
{
    public enum RobotEntrySide
    {
        Random,      // Randomly picks Left or Right
        Left,
        Right,
        MiddleLeft,  // Comes up from below on the left, then moves sideways
        MiddleRight  // Comes up from below on the right, then moves sideways
    }

    public Animator myAnimator;

    [Header("Scene Mode")]
    [Tooltip("When true, game over also spawns the guard.\n" +
             "The robot waits for the player to start walking left before it moves sideways.\n" +
             "The Lose animation is never used.")]
    public bool scamScene = false;

    // ======================================================================
    // Robot
    // ======================================================================

    [Header("Robot Settings")]
    [Tooltip("Robot prefab (must have RobotScript on itself or a child).")]
    public GameObject robot;

    [Tooltip("Where the robot enters from.\n" +
             "Random picks Left or Right each time.\n" +
             "Middle-Left / Middle-Right use the Robot Middle Path Settings below.")]
    public RobotEntrySide robotEntrySide = RobotEntrySide.MiddleRight;

    [Tooltip("Minimum random delay (seconds) before the robot spawns after RobotSwitch is called.")]
    public float robotSpawnMinDelay = 0f;

    [Tooltip("Maximum random delay (seconds) before the robot spawns after RobotSwitch is called.")]
    public float robotSpawnMaxDelay = 0f;

    [Tooltip("Average robot speed when moving left / right (units per second).\n" +
             "The speed change within the move is controlled by Robot Side Curve.")]
    public float robotMoveSpeed = 3f;

    [Tooltip("Left / Right: horizontal distance from the player where the robot spawns.")]
    public float robotSpawnDistance = 8f;

    [Tooltip("Offset from the player's original position where the robot stops (0 = exactly at the player).")]
    public Vector3 robotTargetOffset = Vector3.zero;

    [Tooltip("Check this if the robot prefab's walk animation faces RIGHT by default.\n" +
             "The robot is flipped horizontally so it always faces the direction it walks.\n" +
             "If the robot looks backwards, toggle this.")]
    public bool robotFacesRightByDefault = true;

    [Tooltip("Animator bool parameter set to true while the robot moves upward (bottom to top).")]
    public string backWalkParameter = "BackWalk";

    [Tooltip("Spawn the robot / guard under the same parent as the player in the Hierarchy.\n" +
             "If the player has no parent, they are spawned at the scene root.")]
    public bool spawnUnderPlayerParent = true;

    [Header("Robot Up Movement")]
    [Tooltip("Seconds the robot takes to move from the bottom point to the corner.")]
    public float robotUpDuration = 1f;

    [Tooltip("Progress curve for the upward movement.\n" +
             "X = time (0 to 1), Y = distance covered (0 = bottom, 1 = corner).\n" +
             "Steeper = faster. Values above 1 overshoot the corner, then come back.")]
    public AnimationCurve robotUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Robot Side Movement")]
    [Tooltip("Progress curve for the left / right movement.\n" +
             "X = time (0 to 1), Y = distance covered (0 = start, 1 = target).\n" +
             "Steeper = faster. Values above 1 overshoot the target, then come back.\n" +
             "The total time is distance / Robot Move Speed.")]
    public AnimationCurve robotSideCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Robot Middle Path Settings")]
    [Tooltip("Middle-Left / Middle-Right: horizontal distance of the robot's path.")]
    public float robotMiddleSideDistance = 10f;

    [Tooltip("Middle-Left / Middle-Right: vertical distance of the robot's path.")]
    public float robotMiddleDownDistance = 3f;

    [Header("Sorting Settings")]
    [Tooltip("Copy the player's Sorting Layer and Order in Layer to the spawned robot / guard.")]
    public bool matchPlayerSorting = true;

    [Tooltip("Added to the player's Order in Layer for the robot (1 = in front of the player, -1 = behind).")]
    public int robotSortingOrderOffset = 0;

    [Tooltip("Added to the player's Order in Layer for the guard (1 = in front of the player, -1 = behind).")]
    public int guardSortingOrderOffset = 0;

    [Header("Player Exit Settings (Normal Scene)")]
    [Tooltip("Seconds after the robot spawns before the player starts walking.")]
    public float playerLeaveDelay = 2f;

    [Tooltip("How far the player walks to the left before the Lose animation starts.")]
    public float playerWalkDistance = 1.5f;

    [Tooltip("Player walk speed (units per second).")]
    public float playerWalkSpeed = 2f;

    [Tooltip("Check this if the player's walk animation faces RIGHT by default.\n" +
             "The player is flipped horizontally so it faces left while walking.\n" +
             "If the player looks backwards, toggle this.")]
    public bool playerFacesRightByDefault = true;

    [Tooltip("Keep the player flipped when the Lose animation plays.\n" +
             "Unchecked = flip back to the original facing before Lose.")]
    public bool keepFlippedOnLose = false;

    // ======================================================================
    // Guard (scam scene)
    // ======================================================================

    [Header("Guard Settings (Scam Scene)")]
    [Tooltip("Guard prefab.")]
    public GameObject guard;

    [Tooltip("Seconds after game over before the guard spawns.")]
    public float guardSpawnDelay = 0f;

    [Tooltip("Horizontal distance to the LEFT of the player where the guard's path is.")]
    public float guardSideDistance = 4f;

    [Tooltip("How far below the player the guard spawns.")]
    public float guardSpawnDownDistance = 3f;

    [Tooltip("Guard movement speed (units per second).")]
    public float guardMoveSpeed = 3f;

    [Tooltip("Seconds the guard waits at the corner before moving right.")]
    public float guardPauseAtCorner = 0f;

    [Tooltip("The guard is removed when it is this close to the player.")]
    public float guardTouchDistance = 0.5f;

    [Tooltip("Check this if the guard prefab faces RIGHT by default.\n" +
             "The guard is flipped so it faces right (toward the player).\n" +
             "If the guard looks backwards, toggle this.")]
    public bool guardFacesRightByDefault = true;

    [Tooltip("Freeze the player's animation completely until the guard touches the player.")]
    public bool freezePlayerAnimationBeforeGuard = false;

    [Header("Player Exit Settings (Scam Scene)")]
    [Tooltip("Seconds after the guard is removed before the player starts walking.")]
    public float scamPlayerStartDelay = 0.5f;

    [Tooltip("How far the player walks to the left.")]
    public float scamPlayerLeftDistance = 2f;

    [Tooltip("Player speed when walking left (units per second).")]
    public float scamPlayerLeftSpeed = 2f;

    [Tooltip("Seconds the player waits between walking left and walking down.")]
    public float scamPlayerPauseBeforeDown = 0.3f;

    [Tooltip("How far the player walks down.")]
    public float scamPlayerDownDistance = 3f;

    [Tooltip("Player speed when walking down (units per second).")]
    public float scamPlayerDownSpeed = 2f;

    [Header("Gizmos")]
    public bool showGizmos = true;

    // ======================================================================

    private bool gameOverStarted = false;
    private bool robotSwitchStarted = false;
    private bool playerLeaving = false;
    private bool playerStartedWalkingLeft = false;

    // Player position when game over starts (so guides and targets don't move)
    private Vector3 originPos;
    private bool hasOrigin = false;
    private RobotEntrySide chosenEntry;

    /// <summary>True once GameOver() has been executed. It can only run once.</summary>
    public bool HasGameOver => gameOverStarted;

    // ---------- Path points ----------
    private Vector3 Origin => hasOrigin ? originPos : transform.position;

    // Robot / normal scene
    private Vector3 RobotTargetPos => Origin + robotTargetOffset;
    private Vector3 PlayerWalkTarget => Origin + Vector3.left * playerWalkDistance;

    // Scam scene
    private Vector3 GuardCornerPos => Origin + Vector3.left * guardSideDistance;
    private Vector3 GuardSpawnPos => GuardCornerPos + Vector3.down * guardSpawnDownDistance;
    private Vector3 ScamPlayerLeftPos => Origin + Vector3.left * scamPlayerLeftDistance;
    private Vector3 ScamPlayerDownPos => ScamPlayerLeftPos + Vector3.down * scamPlayerDownDistance;

    private void Awake()
    {
        // Only auto-assign if not set in the Inspector
        if (myAnimator == null)
            myAnimator = GetComponent<Animator>();
    }

    public void PlayWrongAnimation()
    {
        myAnimator.SetTrigger("Wrong");
    }

    public void GameOver()
    {
        if (gameOverStarted)
        {
            Debug.LogWarning($"[{name}] PlayerScript: GameOver can only be executed once.", this);
            return;
        }

        gameOverStarted = true;
        LockOrigin();

        if (scamScene)
        {
            if (guard == null)
            {
                Debug.LogWarning($"[{name}] PlayerScript: guard prefab is not assigned. " +
                                 "The player will not walk, so the robot will keep waiting.", this);
            }
            else
            {
                StartCoroutine(ScamSceneRoutine());
            }
        }

        // The robot comes in for both scenes
        RobotSwitch();
    }

    private void LockOrigin()
    {
        if (hasOrigin) return;
        originPos = transform.position;
        hasOrigin = true;
    }

    // ======================================================================
    // Scam scene: guard sequence
    // ======================================================================

    private IEnumerator ScamSceneRoutine()
    {
        // Optionally freeze the player's animation until the guard arrives
        float originalAnimatorSpeed = myAnimator != null ? myAnimator.speed : 1f;
        if (freezePlayerAnimationBeforeGuard && myAnimator != null)
        {
            myAnimator.speed = 0f;
        }

        if (guardSpawnDelay > 0f)
        {
            yield return new WaitForSeconds(guardSpawnDelay);
        }

        // Spawn the guard below and to the left of the player
        Transform spawnParent = spawnUnderPlayerParent ? transform.parent : null;
        GameObject newGuard = Instantiate(guard, GuardSpawnPos, guard.transform.rotation, spawnParent);

        if (spawnParent != null)
        {
            newGuard.transform.SetSiblingIndex(transform.GetSiblingIndex() + 1);
        }

        // The guard walks right, so flip it if it faces left by default
        if (!guardFacesRightByDefault)
        {
            Vector3 scale = newGuard.transform.localScale;
            scale.x = -scale.x;
            newGuard.transform.localScale = scale;
        }

        if (matchPlayerSorting)
        {
            ApplyPlayerSorting(newGuard, guardSortingOrderOffset);
        }

        Transform guardTransform = newGuard.transform;

        // Step 1: guard moves up to the player's height
        yield return MoveTransformTo(guardTransform, GuardCornerPos, guardMoveSpeed);

        if (guardPauseAtCorner > 0f)
        {
            yield return new WaitForSeconds(guardPauseAtCorner);
        }

        // Step 2: guard moves right until it touches the player
        while (guardTransform != null &&
               Vector3.Distance(guardTransform.position, Origin) > guardTouchDistance)
        {
            guardTransform.position = Vector3.MoveTowards(
                guardTransform.position, Origin, guardMoveSpeed * Time.deltaTime);
            yield return null;
        }

        // Step 3: remove the guard
        if (newGuard != null)
        {
            Destroy(newGuard);
        }

        if (freezePlayerAnimationBeforeGuard && myAnimator != null)
        {
            myAnimator.speed = originalAnimatorSpeed;
        }

        if (scamPlayerStartDelay > 0f)
        {
            yield return new WaitForSeconds(scamPlayerStartDelay);
        }

        // Step 4: player walks left (Walk) - this also releases the waiting robot
        Vector3 originalScale = transform.localScale;
        FlipPlayerToFaceLeft();

        playerStartedWalkingLeft = true;
        SetAnimatorBool(myAnimator, "Walk", true);
        yield return MoveTransformTo(transform, ScamPlayerLeftPos, scamPlayerLeftSpeed);
        SetAnimatorBool(myAnimator, "Walk", false);

        // Face front again for the down walk
        transform.localScale = originalScale;

        if (scamPlayerPauseBeforeDown > 0f)
        {
            yield return new WaitForSeconds(scamPlayerPauseBeforeDown);
        }

        // Step 5: player walks down (FrontWalk)
        SetAnimatorBool(myAnimator, "FrontWalk", true);
        yield return MoveTransformTo(transform, ScamPlayerDownPos, scamPlayerDownSpeed);
        SetAnimatorBool(myAnimator, "FrontWalk", false);
    }

    // ======================================================================
    // Robot sequence
    // ======================================================================

    /// <summary>
    /// Spawns a robot on the chosen side and moves it to the player's position.
    /// Normal scene: after playerLeaveDelay seconds the player walks left, then plays Lose.
    /// Scam scene: the robot waits before moving sideways until the player starts walking left.
    /// Can only be executed once.
    /// </summary>
    public void RobotSwitch()
    {
        if (robotSwitchStarted)
        {
            Debug.LogWarning($"[{name}] PlayerScript: RobotSwitch can only be executed once.", this);
            return;
        }

        if (robot == null)
        {
            Debug.LogWarning($"[{name}] PlayerScript: robot prefab is not assigned.", this);
            return;
        }

        robotSwitchStarted = true;
        LockOrigin();

        chosenEntry = robotEntrySide;
        if (chosenEntry == RobotEntrySide.Random)
        {
            chosenEntry = Random.value < 0.5f ? RobotEntrySide.Left : RobotEntrySide.Right;
        }
        GetSideInfo(chosenEntry, out float side, out bool isMiddle);

        // Random delay before the robot spawns
        float minDelay = Mathf.Min(robotSpawnMinDelay, robotSpawnMaxDelay);
        float maxDelay = Mathf.Max(robotSpawnMinDelay, robotSpawnMaxDelay);
        float delay = Random.Range(minDelay, maxDelay);

        StartCoroutine(RobotSwitchRoutine(side, isMiddle, delay));
    }

    private IEnumerator RobotSwitchRoutine(float side, bool isMiddle, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        List<Vector3> robotPath = BuildRobotPath(RobotTargetPos, side, isMiddle);

        Transform spawnParent = spawnUnderPlayerParent ? transform.parent : null;
        GameObject newRobot = Instantiate(robot, robotPath[0], robot.transform.rotation, spawnParent);

        if (spawnParent != null)
        {
            newRobot.transform.SetSiblingIndex(transform.GetSiblingIndex() + 1);
        }

        // Entering from the left = walking right (+1), entering from the right = walking left (-1).
        float walkDirection = -side;
        float defaultFacing = robotFacesRightByDefault ? 1f : -1f;
        if (walkDirection != defaultFacing)
        {
            Vector3 scale = newRobot.transform.localScale;
            scale.x = -scale.x;
            newRobot.transform.localScale = scale;
        }

        if (matchPlayerSorting)
        {
            ApplyPlayerSorting(newRobot, robotSortingOrderOffset);
        }

        RobotScript robotScript = newRobot.GetComponentInChildren<RobotScript>(true);
        if (robotScript == null)
        {
            Debug.LogWarning($"[{name}] The spawned robot has no RobotScript on itself or its children.", newRobot);
        }

        Animator robotAnimator = newRobot.GetComponentInChildren<Animator>(true);

        // Normal scene: the player leaves on a timer.
        // Scam scene: the player leaves after the guard, so no timer here.
        if (!scamScene)
        {
            StartCoroutine(PlayerLeaveTimer());
        }

        yield return MoveRobotRoutine(newRobot.transform, robotScript, robotAnimator, robotPath);
    }

    private IEnumerator MoveRobotRoutine(Transform robotTransform, RobotScript robotScript,
                                         Animator robotAnimator, List<Vector3> path)
    {
        bool isWalking = false;

        for (int i = 1; i < path.Count; i++)
        {
            if (robotTransform == null)
            {
                break;
            }

            Vector3 waypoint = path[i];
            Vector3 segment = waypoint - path[i - 1];
            bool movingUp = segment.y > 0f && Mathf.Abs(segment.y) > Mathf.Abs(segment.x);

            if (movingUp)
            {
                // Moving bottom to top: BackWalk only, using the up speed curve
                if (isWalking && robotScript != null)
                {
                    robotScript.StopWalking();
                }
                isWalking = false;
                SetAnimatorBool(robotAnimator, backWalkParameter, true);

                yield return MoveWithCurve(robotTransform, waypoint, robotUpDuration, robotUpCurve);
            }
            else
            {
                SetAnimatorBool(robotAnimator, backWalkParameter, false);

                // Scam scene: wait here until the player starts walking left
                if (scamScene && !playerStartedWalkingLeft)
                {
                    if (isWalking && robotScript != null)
                    {
                        robotScript.StopWalking();
                    }
                    isWalking = false;

                    while (!playerStartedWalkingLeft && robotTransform != null)
                    {
                        yield return null;
                    }
                }

                if (robotTransform == null)
                {
                    break;
                }

                // Moving left / right: Walk animation
                if (!isWalking && robotScript != null)
                {
                    robotScript.Walk();
                }
                isWalking = true;

                // Moving left / right using the side speed curve
                float sideDistance = Vector3.Distance(robotTransform.position, waypoint);
                float sideDuration = robotMoveSpeed > 0f ? sideDistance / robotMoveSpeed : 0f;
                yield return MoveWithCurve(robotTransform, waypoint, sideDuration, robotSideCurve);
            }
        }

        SetAnimatorBool(robotAnimator, backWalkParameter, false);

        if (isWalking && robotScript != null)
        {
            robotScript.StopWalking();
        }
    }

    private IEnumerator PlayerLeaveTimer()
    {
        yield return new WaitForSeconds(playerLeaveDelay);
        StartLeaving();
    }

    private void StartLeaving()
    {
        if (playerLeaving) return;
        playerLeaving = true;

        StartCoroutine(PlayerLeaveRoutine());
    }

    private IEnumerator PlayerLeaveRoutine()
    {
        Vector3 originalScale = transform.localScale;
        FlipPlayerToFaceLeft();

        // Step 1: walk left a short distance
        playerStartedWalkingLeft = true;
        SetAnimatorBool(myAnimator, "Walk", true);
        yield return MoveTransformTo(transform, PlayerWalkTarget, playerWalkSpeed);
        SetAnimatorBool(myAnimator, "Walk", false);

        // Step 2: play the Lose animation and stay in place
        if (!keepFlippedOnLose)
        {
            transform.localScale = originalScale;
        }

        SetAnimatorBool(myAnimator, "Lose", true);
    }

    // ======================================================================
    // Shared helpers
    // ======================================================================

    // Constant-speed movement
    private IEnumerator MoveTransformTo(Transform t, Vector3 target, float speed)
    {
        while (t != null && (t.position - target).sqrMagnitude > 0.0001f)
        {
            t.position = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
            yield return null;
        }

        if (t != null)
        {
            t.position = target;
        }
    }

    // Curve-based movement: the curve maps time (0-1) to progress (0-1)
    private IEnumerator MoveWithCurve(Transform t, Vector3 target, float duration, AnimationCurve curve)
    {
        if (t == null)
        {
            yield break;
        }

        // Fall back to constant speed if the curve or duration is not usable
        if (duration <= 0f || curve == null || curve.length == 0)
        {
            yield return MoveTransformTo(t, target, robotMoveSpeed);
            yield break;
        }

        Vector3 start = t.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (t == null)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            float progress = curve.Evaluate(Mathf.Clamp01(elapsed / duration));
            t.position = Vector3.LerpUnclamped(start, target, progress);
            yield return null;
        }

        if (t != null)
        {
            t.position = target;
        }
    }

    // Flips the player so its walk animation faces left
    private void FlipPlayerToFaceLeft()
    {
        if (playerFacesRightByDefault)
        {
            Vector3 scale = transform.localScale;
            scale.x = -scale.x;
            transform.localScale = scale;
        }
    }

    // Sets a bool parameter only if it exists, to avoid Animator warnings
    private void SetAnimatorBool(Animator animator, string parameter, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(parameter))
        {
            return;
        }

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == parameter && p.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(parameter, value);
                return;
            }
        }
    }

    // ---------- Sorting helpers ----------

    // Reads the player's sorting (SortingGroup first, then its own Renderer, then a child Renderer).
    private bool TryGetPlayerSorting(out int sortingLayerID, out int sortingOrder)
    {
        SortingGroup group = GetComponent<SortingGroup>();
        if (group != null)
        {
            sortingLayerID = group.sortingLayerID;
            sortingOrder = group.sortingOrder;
            return true;
        }

        Renderer playerRenderer = GetComponent<Renderer>();
        if (playerRenderer == null)
        {
            playerRenderer = GetComponentInChildren<Renderer>();
        }

        if (playerRenderer != null)
        {
            sortingLayerID = playerRenderer.sortingLayerID;
            sortingOrder = playerRenderer.sortingOrder;
            return true;
        }

        sortingLayerID = 0;
        sortingOrder = 0;
        return false;
    }

    // Applies the player's sorting to another object.
    // If the object has a SortingGroup, only that is changed.
    // Otherwise every Renderer is changed, keeping their relative order to each other.
    private void ApplyPlayerSorting(GameObject target, int orderOffset)
    {
        if (!TryGetPlayerSorting(out int layerID, out int order))
        {
            Debug.LogWarning($"[{name}] PlayerScript: no Renderer or SortingGroup found on the player, sorting not copied.", this);
            return;
        }

        order += orderOffset;

        SortingGroup targetGroup = target.GetComponent<SortingGroup>();
        if (targetGroup != null)
        {
            targetGroup.sortingLayerID = layerID;
            targetGroup.sortingOrder = order;
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        int baseOrder = int.MaxValue;
        foreach (Renderer r in renderers)
        {
            baseOrder = Mathf.Min(baseOrder, r.sortingOrder);
        }

        foreach (Renderer r in renderers)
        {
            int relativeOrder = r.sortingOrder - baseOrder;
            r.sortingLayerID = layerID;
            r.sortingOrder = order + relativeOrder;
        }
    }

    // ---------- Path helpers ----------

    // side: -1 = left, +1 = right
    private static void GetSideInfo(RobotEntrySide entry, out float side, out bool isMiddle)
    {
        side = (entry == RobotEntrySide.Left || entry == RobotEntrySide.MiddleLeft) ? -1f : 1f;
        isMiddle = entry == RobotEntrySide.MiddleLeft || entry == RobotEntrySide.MiddleRight;
    }

    // Robot path: [spawn, (corner), target]
    private List<Vector3> BuildRobotPath(Vector3 target, float side, bool isMiddle)
    {
        List<Vector3> path = new List<Vector3>();

        if (isMiddle)
        {
            Vector3 corner = target + new Vector3(side * robotMiddleSideDistance, 0f, 0f);
            path.Add(corner + Vector3.down * robotMiddleDownDistance);
            path.Add(corner);
        }
        else
        {
            path.Add(target + new Vector3(side * robotSpawnDistance, 0f, 0f));
        }

        path.Add(target);
        return path;
    }

    // ======================================================================
    // Guide lines in the Scene view
    // ======================================================================

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        DrawRobotGizmos();

        if (scamScene)
        {
            DrawScamGizmos();
        }
        else
        {
            DrawNormalPlayerGizmos();
        }
    }

    private void DrawRobotGizmos()
    {
        if (hasOrigin)
        {
            DrawRobotPathGizmos(chosenEntry);
        }
        else if (robotEntrySide == RobotEntrySide.Random)
        {
            DrawRobotPathGizmos(RobotEntrySide.Left);
            DrawRobotPathGizmos(RobotEntrySide.Right);
        }
        else
        {
            DrawRobotPathGizmos(robotEntrySide);
        }
    }

    private void DrawScamGizmos()
    {
        Vector3 origin = Origin;

        // Guard path (green): spawn -> up -> right to the player
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(GuardSpawnPos, 0.2f);
        Gizmos.DrawLine(GuardSpawnPos, GuardCornerPos);
        Gizmos.DrawWireSphere(GuardCornerPos, 0.15f);
        Gizmos.DrawLine(GuardCornerPos, origin);
        DrawArrowHead(origin, Vector3.right);

        // Guard touch range (red)
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(origin, guardTouchDistance);

        // Player path: walk left (yellow) -> walk down (orange)
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, ScamPlayerLeftPos);
        Gizmos.DrawWireSphere(ScamPlayerLeftPos, 0.15f);

        Gizmos.color = new Color(1f, 0.55f, 0f);
        Gizmos.DrawLine(ScamPlayerLeftPos, ScamPlayerDownPos);
        Gizmos.DrawWireSphere(ScamPlayerDownPos, 0.2f);
        DrawArrowHead(ScamPlayerDownPos, Vector3.down);

#if UNITY_EDITOR
        Handles.Label(GuardSpawnPos, "Guard Spawn");
        Handles.Label(GuardCornerPos + Vector3.up * 0.3f, "Guard Turn");
        Handles.Label(origin + Vector3.right * guardTouchDistance, "Touch Range");
        Handles.Label(ScamPlayerLeftPos + Vector3.up * 0.4f, "Player Walk (Walk)");
        Handles.Label(ScamPlayerDownPos, "Player End (FrontWalk)");
#endif
    }

    private void DrawNormalPlayerGizmos()
    {
        // Player path (yellow): walk left -> Lose position
        Vector3 walkTarget = PlayerWalkTarget;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(Origin, walkTarget);
        Gizmos.DrawWireSphere(walkTarget, 0.2f);
        DrawArrowHead(walkTarget, Vector3.left);

#if UNITY_EDITOR
        Handles.Label(walkTarget + Vector3.up * 0.4f, "Player Lose");
#endif
    }

    private void DrawRobotPathGizmos(RobotEntrySide entry)
    {
        GetSideInfo(entry, out float side, out bool isMiddle);
        List<Vector3> robotPath = BuildRobotPath(RobotTargetPos, side, isMiddle);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(robotPath[0], 0.2f);
        for (int i = 0; i < robotPath.Count - 1; i++)
        {
            Gizmos.DrawLine(robotPath[i], robotPath[i + 1]);
        }
        Gizmos.DrawWireSphere(RobotTargetPos, 0.25f);

#if UNITY_EDITOR
        Handles.Label(robotPath[0], $"Robot Spawn ({entry})");
        Handles.Label(RobotTargetPos + Vector3.down * 0.4f, "Robot Target");

        // Scam scene: mark where the robot waits for the player
        if (scamScene)
        {
            Vector3 waitPoint = robotPath[robotPath.Count - 2];
            Handles.Label(waitPoint + Vector3.up * 0.4f, "Robot Waits Here");
        }
#endif
    }

    private void DrawArrowHead(Vector3 tip, Vector3 dir)
    {
        if (dir == Vector3.zero) return;
        Vector3 right = Quaternion.Euler(0, 0, 150) * dir * 0.4f;
        Vector3 left = Quaternion.Euler(0, 0, -150) * dir * 0.4f;
        Gizmos.DrawLine(tip, tip + right);
        Gizmos.DrawLine(tip, tip + left);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(PlayerScript))]
public class PlayerScriptEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);

        PlayerScript player = (PlayerScript)target;

        using (new EditorGUI.DisabledScope(!Application.isPlaying || player.HasGameOver))
        {
            if (GUILayout.Button("Test Game Over", GUILayout.Height(30)))
            {
                player.GameOver();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test button.", MessageType.Info);
        }
        else if (player.HasGameOver)
        {
            EditorGUILayout.HelpBox("Game Over has already been executed (it can only run once).", MessageType.Info);
        }
    }
}
#endif
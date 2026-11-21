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

    [Tooltip("Robot movement speed (units per second).")]
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

    [Tooltip("Spawn the robot under the same parent as the player in the Hierarchy.\n" +
             "If the player has no parent, the robot is spawned at the scene root.")]
    public bool spawnUnderPlayerParent = true;

    [Header("Robot Middle Path Settings")]
    [Tooltip("Middle-Left / Middle-Right: horizontal distance of the robot's path.")]
    public float robotMiddleSideDistance = 10f;

    [Tooltip("Middle-Left / Middle-Right: vertical distance of the robot's path.")]
    public float robotMiddleDownDistance = 3f;

    [Header("Robot Sorting Settings")]
    [Tooltip("Copy the player's Sorting Layer and Order in Layer to the spawned robot.")]
    public bool matchPlayerSorting = true;

    [Tooltip("Added to the player's Order in Layer (e.g. 1 = robot draws in front of the player, -1 = behind).")]
    public int robotSortingOrderOffset = 0;

    [Header("Player Exit Settings")]
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

    [Header("Gizmos")]
    public bool showGizmos = true;

    private bool robotSwitchStarted = false;
    private bool playerLeaving = false;

    // Player position when RobotSwitch starts (so guides and targets don't move)
    private Vector3 originPos;
    private bool hasOrigin = false;
    private RobotEntrySide chosenEntry;

    /// <summary>True once RobotSwitch() has been executed. It can only run once.</summary>
    public bool HasSwitched => robotSwitchStarted;

    // ---------- Path points ----------
    private Vector3 Origin => hasOrigin ? originPos : transform.position;
    private Vector3 RobotTargetPos => Origin + robotTargetOffset;
    private Vector3 PlayerWalkTarget => Origin + Vector3.left * playerWalkDistance;

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
        RobotSwitch();
    }

    /// <summary>
    /// Spawns a robot on the chosen side and moves it to the player's position.
    /// After playerLeaveDelay seconds the player walks left, then plays the Lose animation.
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

        // Lock the original player position
        originPos = transform.position;
        hasOrigin = true;

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
        // Wait the random spawn delay
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        List<Vector3> robotPath = BuildRobotPath(RobotTargetPos, side, isMiddle);

        // Spawn at the same Hierarchy path as the player (same parent), keeping the world position
        Transform spawnParent = spawnUnderPlayerParent ? transform.parent : null;
        GameObject newRobot = Instantiate(robot, robotPath[0], robot.transform.rotation, spawnParent);

        // Place the robot right after the player among its siblings
        if (spawnParent != null)
        {
            newRobot.transform.SetSiblingIndex(transform.GetSiblingIndex() + 1);
        }

        // Flip the robot so its walk animation faces the walking direction.
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
            ApplyPlayerSorting(newRobot);
        }

        // RobotScript and Animator may live on a child object of the prefab
        RobotScript robotScript = newRobot.GetComponentInChildren<RobotScript>(true);
        if (robotScript == null)
        {
            Debug.LogWarning($"[{name}] The spawned robot has no RobotScript on itself or its children.", newRobot);
        }

        Animator robotAnimator = newRobot.GetComponentInChildren<Animator>(true);

        // Start the timer for the player to leave
        StartCoroutine(PlayerLeaveTimer());

        yield return MoveRobotRoutine(newRobot.transform, robotScript, robotAnimator, robotPath);
    }

    private IEnumerator MoveRobotRoutine(Transform robotTransform, RobotScript robotScript,
                                         Animator robotAnimator, List<Vector3> path)
    {
        bool isWalking = false;

        // path[0] is the spawn position, so start moving toward path[1]
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 waypoint = path[i];

            Vector3 segment = waypoint - path[i - 1];
            bool movingUp = segment.y > 0f && Mathf.Abs(segment.y) > Mathf.Abs(segment.x);

            if (movingUp)
            {
                // Moving bottom to top: only BackWalk, no Walk animation
                if (isWalking && robotScript != null)
                {
                    robotScript.StopWalking();
                }
                isWalking = false;
                SetBackWalk(robotAnimator, true);
            }
            else
            {
                // Moving left / right: Walk animation only
                SetBackWalk(robotAnimator, false);
                if (!isWalking && robotScript != null)
                {
                    robotScript.Walk();
                }
                isWalking = true;
            }

            while (robotTransform != null &&
                   (robotTransform.position - waypoint).sqrMagnitude > 0.0001f)
            {
                robotTransform.position = Vector3.MoveTowards(
                    robotTransform.position, waypoint, robotMoveSpeed * Time.deltaTime);
                yield return null;
            }

            if (robotTransform == null)
            {
                break;
            }
            robotTransform.position = waypoint;
        }

        // Robot has arrived
        SetBackWalk(robotAnimator, false);

        if (isWalking && robotScript != null)
        {
            robotScript.StopWalking();
        }
    }

    // ---------- Player exit ----------

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

        // Flip the player so the walk animation faces left (walking direction = -1)
        float defaultFacing = playerFacesRightByDefault ? 1f : -1f;
        if (defaultFacing != -1f)
        {
            Vector3 scale = transform.localScale;
            scale.x = -scale.x;
            transform.localScale = scale;
        }

        // Step 1: walk left a short distance
        myAnimator.SetBool("Walk", true);

        Vector3 walkTarget = PlayerWalkTarget;
        while ((transform.position - walkTarget).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, walkTarget, playerWalkSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = walkTarget;

        myAnimator.SetBool("Walk", false);

        // Step 2: play the Lose animation and stay in place
        if (!keepFlippedOnLose)
        {
            transform.localScale = originalScale;
        }

        myAnimator.SetBool("Lose", true);
    }

    // ---------- Animation helpers ----------

    private void SetBackWalk(Animator animator, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(backWalkParameter))
        {
            return;
        }

        // Only set the parameter if it exists, to avoid Animator warnings
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == backWalkParameter && p.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(backWalkParameter, value);
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

    // Applies the player's sorting to the robot.
    // If the robot has a SortingGroup, only that is changed.
    // Otherwise every Renderer is changed, keeping their relative order to each other.
    private void ApplyPlayerSorting(GameObject robotObject)
    {
        if (!TryGetPlayerSorting(out int layerID, out int order))
        {
            Debug.LogWarning($"[{name}] PlayerScript: no Renderer or SortingGroup found on the player, sorting not copied.", this);
            return;
        }

        order += robotSortingOrderOffset;

        SortingGroup robotGroup = robotObject.GetComponent<SortingGroup>();
        if (robotGroup != null)
        {
            robotGroup.sortingLayerID = layerID;
            robotGroup.sortingOrder = order;
            return;
        }

        Renderer[] renderers = robotObject.GetComponentsInChildren<Renderer>(true);
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
    // Middle paths: come up from below, then move sideways to the player.
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

    // ---------- Guide lines in the Scene view ----------

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        // Robot path(s) (cyan)
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

        // Player path (yellow): walk left -> Lose position
        Vector3 origin = Origin;
        Vector3 walkTarget = PlayerWalkTarget;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, walkTarget);
        Gizmos.DrawWireSphere(walkTarget, 0.2f);
        DrawArrowHead(walkTarget, Vector3.left);

#if UNITY_EDITOR
        Handles.Label(RobotTargetPos + Vector3.down * 0.4f, "Robot Target");
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

        using (new EditorGUI.DisabledScope(!Application.isPlaying || player.HasSwitched))
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
        else if (player.HasSwitched)
        {
            EditorGUILayout.HelpBox("Game Over has already been executed (it can only run once).", MessageType.Info);
        }
    }
}
#endif
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NpcScript : MonoBehaviour
{
    public enum RobotEntrySide
    {
        Random,      // Randomly picks Left or Right
        Left,
        Right,
        MiddleLeft,  // Side + vertical path on the left
        MiddleRight  // Side + vertical path on the right
    }

    [Header("Bobbing Settings")]
    [Tooltip("Vertical distance to offset between Position A and Position B.")]
    public float moveOffset = 0.2f;

    [Header("Random Timing Settings")]
    [Tooltip("Minimum time to stay at current position (in seconds).")]
    public float minInterval = 0.2f;

    [Tooltip("Maximum time to stay at current position (in seconds).")]
    public float maxInterval = 1.2f;

    [Header("Robot Settings")]
    [Tooltip("Robot prefab (must have RobotScript attached).")]
    public GameObject robot;

    [Tooltip("Where the robot enters from (the NPC exits toward the same side).\n" +
             "Random picks Left or Right each time.\n" +
             "Middle-Left / Middle-Right use the Middle Entry/Exit Settings below.")]
    public RobotEntrySide robotEntrySide = RobotEntrySide.Random;

    [Tooltip("Minimum random delay (seconds) before the robot spawns after RobotSwitch is called.")]
    public float robotSpawnMinDelay = 0f;

    [Tooltip("Maximum random delay (seconds) before the robot spawns after RobotSwitch is called.")]
    public float robotSpawnMaxDelay = 2f;

    [Tooltip("Robot movement speed (units per second).")]
    public float robotMoveSpeed = 3f;

    [Tooltip("Left / Right: horizontal distance from the NPC where the robot spawns.")]
    public float robotSpawnDistance = 8f;

    [Tooltip("Offset from the NPC position where the robot stops (0 = exactly at the NPC).")]
    public Vector3 robotTargetOffset = Vector3.zero;

    [Header("Robot Sorting Settings")]
    [Tooltip("Copy this NPC's Sorting Layer and Order in Layer to the spawned robot.")]
    public bool matchNpcSorting = true;

    [Tooltip("Added to the NPC's Order in Layer (e.g. 1 = robot draws in front of the NPC, -1 = behind).")]
    public int robotSortingOrderOffset = 0;

    [Header("NPC Exit Settings")]
    [Tooltip("When the robot is within this distance of its stop position, the NPC starts moving out.")]
    public float npcExitTriggerDistance = 3f;

    [Tooltip("NPC movement speed when exiting (units per second).")]
    public float npcExitSpeed = 3f;

    [Tooltip("Left / Right: how far the NPC moves horizontally when exiting.")]
    public float npcExitDistance = 8f;

    [Header("Middle Entry/Exit Settings")]
    [Tooltip("Middle-Left / Middle-Right: horizontal distance of the robot's path.")]
    public float robotMiddleSideDistance = 4f;

    [Tooltip("Middle-Left / Middle-Right: vertical distance of the robot's path.")]
    public float robotMiddleDownDistance = 4f;

    [Tooltip("Middle-Left / Middle-Right: how far the NPC moves sideways before moving down.")]
    public float npcMiddleSideDistance = 4f;

    [Tooltip("Middle-Left / Middle-Right: how far the NPC moves down after moving sideways.")]
    public float npcMiddleDownDistance = 4f;

    private Vector3 posA;
    private Vector3 posB;

    private Coroutine bobbingRoutine;
    private bool hasSwitched;

    /// <summary>True once RobotSwitch() has been executed. It can only run once.</summary>
    public bool HasSwitched => hasSwitched;

    private void Start()
    {
        // Define exact Position A (bottom) and Position B (top)
        posA = transform.localPosition;
        posB = posA + new Vector3(0, moveOffset, 0);

        bobbingRoutine = StartCoroutine(SteppyBobbingRoutine());
    }

    private IEnumerator SteppyBobbingRoutine()
    {
        bool isAtB = false;

        while (true)
        {
            // Instantly snap to the other position without any transition/lerping
            transform.localPosition = isAtB ? posA : posB;
            isAtB = !isAtB;

            // Wait for a random duration before snapping back
            float randomWait = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(randomWait);
        }
    }

    /// <summary>
    /// Spawns a robot on the chosen side and moves it to this NPC's position.
    /// When the robot gets close enough, the NPC moves out toward the side the robot came from.
    /// Can only be executed once.
    /// </summary>
    public void RobotSwitch()
    {
        // RobotSwitch can only be executed once
        if (hasSwitched)
        {
            Debug.LogWarning($"[{name}] NpcScript: RobotSwitch can only be executed once.", this);
            return;
        }

        if (robot == null)
        {
            Debug.LogWarning($"[{name}] NpcScript: robot prefab is not assigned.", this);
            return;
        }

        hasSwitched = true;

        RobotEntrySide entry = robotEntrySide;
        if (entry == RobotEntrySide.Random)
        {
            entry = Random.value < 0.5f ? RobotEntrySide.Left : RobotEntrySide.Right;
        }
        GetSideInfo(entry, out float side, out bool isMiddle);

        // Random delay before the robot spawns
        float minDelay = Mathf.Min(robotSpawnMinDelay, robotSpawnMaxDelay);
        float maxDelay = Mathf.Max(robotSpawnMinDelay, robotSpawnMaxDelay);
        float delay = Random.Range(minDelay, maxDelay);

        StartCoroutine(RobotSwitchRoutine(side, isMiddle, delay));
    }

    private IEnumerator RobotSwitchRoutine(float side, bool isMiddle, float delay)
    {
        // Wait the random spawn delay (the NPC keeps bobbing while waiting)
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        Vector3 target = GetNpcBaseWorldPosition() + robotTargetOffset;
        List<Vector3> robotPath = BuildRobotPath(target, side, isMiddle);

        GameObject newRobot = Instantiate(robot, robotPath[0], robot.transform.rotation);

        // Flip the robot horizontally when it enters from the left side
        if (side < 0f)
        {
            Vector3 scale = newRobot.transform.localScale;
            scale.x = -Mathf.Abs(scale.x);
            newRobot.transform.localScale = scale;
        }

        if (matchNpcSorting)
        {
            ApplyNpcSorting(newRobot);
        }

        RobotScript robotScript = newRobot.GetComponent<RobotScript>();
        if (robotScript == null)
        {
            Debug.LogWarning($"[{name}] The spawned robot has no RobotScript attached.", newRobot);
        }

        yield return MoveRobotRoutine(newRobot.transform, robotScript, robotPath, target, side, isMiddle);
    }

    private IEnumerator MoveRobotRoutine(Transform robotTransform, RobotScript robotScript,
                                         List<Vector3> path, Vector3 target, float side, bool isMiddle)
    {
        if (robotScript != null)
        {
            robotScript.Walk();
        }

        bool npcExitStarted = false;

        // path[0] is the spawn position, so start moving toward path[1]
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 waypoint = path[i];

            while (robotTransform != null &&
                   (robotTransform.position - waypoint).sqrMagnitude > 0.0001f)
            {
                robotTransform.position = Vector3.MoveTowards(
                    robotTransform.position, waypoint, robotMoveSpeed * Time.deltaTime);

                // Start the NPC exit once the robot is close enough to its stop position
                if (!npcExitStarted &&
                    Vector3.Distance(robotTransform.position, target) <= npcExitTriggerDistance)
                {
                    npcExitStarted = true;
                    StartCoroutine(NpcExitRoutine(side, isMiddle));
                }

                yield return null;
            }

            if (robotTransform == null)
            {
                break;
            }
            robotTransform.position = waypoint;
        }

        if (robotScript != null)
        {
            robotScript.StopWalking();
        }

        // Safety: make sure the NPC exits even if the trigger distance was never reached
        if (!npcExitStarted)
        {
            StartCoroutine(NpcExitRoutine(side, isMiddle));
        }
    }

    private IEnumerator NpcExitRoutine(float side, bool isMiddle)
    {
        // Stop bobbing and snap back to the bottom position before moving out
        if (bobbingRoutine != null)
        {
            StopCoroutine(bobbingRoutine);
            bobbingRoutine = null;
        }
        transform.localPosition = posA;

        List<Vector3> exitPath = BuildNpcExitPath(transform.position, side, isMiddle);

        foreach (Vector3 waypoint in exitPath)
        {
            while ((transform.position - waypoint).sqrMagnitude > 0.0001f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, waypoint, npcExitSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = waypoint;
        }
    }

    // ---------- Sorting helpers ----------

    // Reads the NPC's sorting (SortingGroup first, then its own Renderer, then a child Renderer).
    private bool TryGetNpcSorting(out int sortingLayerID, out int sortingOrder)
    {
        SortingGroup group = GetComponent<SortingGroup>();
        if (group != null)
        {
            sortingLayerID = group.sortingLayerID;
            sortingOrder = group.sortingOrder;
            return true;
        }

        Renderer npcRenderer = GetComponent<Renderer>();
        if (npcRenderer == null)
        {
            npcRenderer = GetComponentInChildren<Renderer>();
        }

        if (npcRenderer != null)
        {
            sortingLayerID = npcRenderer.sortingLayerID;
            sortingOrder = npcRenderer.sortingOrder;
            return true;
        }

        sortingLayerID = 0;
        sortingOrder = 0;
        return false;
    }

    // Applies the NPC's sorting to the robot.
    // If the robot has a SortingGroup, only that is changed.
    // Otherwise every Renderer is changed, keeping their relative order to each other.
    private void ApplyNpcSorting(GameObject robotObject)
    {
        if (!TryGetNpcSorting(out int layerID, out int order))
        {
            Debug.LogWarning($"[{name}] NpcScript: no Renderer or SortingGroup found on the NPC, sorting not copied.", this);
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
    // Middle paths are the reverse of the NPC's exit: come up from below, then move sideways to the NPC.
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

    // NPC exit path (waypoints after the start position)
    // Middle paths: move sideways first, then move down.
    private List<Vector3> BuildNpcExitPath(Vector3 start, float side, bool isMiddle)
    {
        List<Vector3> path = new List<Vector3>();

        if (isMiddle)
        {
            Vector3 corner = start + new Vector3(side * npcMiddleSideDistance, 0f, 0f);
            path.Add(corner);
            path.Add(corner + Vector3.down * npcMiddleDownDistance);
        }
        else
        {
            path.Add(start + new Vector3(side * npcExitDistance, 0f, 0f));
        }

        return path;
    }

    // The NPC bobs up and down, so use the bottom position (Position A) as the target
    // to avoid the robot stopping at the raised height.
    private Vector3 GetNpcBaseWorldPosition()
    {
        if (!Application.isPlaying)
        {
            return transform.position;
        }
        return transform.parent != null ? transform.parent.TransformPoint(posA) : posA;
    }

    // ---------- Scene view preview ----------

    // Draws the robot path (cyan) and NPC exit path (yellow) when this object is selected.
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && hasSwitched)
        {
            return;
        }

        if (robotEntrySide == RobotEntrySide.Random)
        {
            DrawPathGizmos(RobotEntrySide.Left);
            DrawPathGizmos(RobotEntrySide.Right);
        }
        else
        {
            DrawPathGizmos(robotEntrySide);
        }
    }

    private void DrawPathGizmos(RobotEntrySide entry)
    {
        GetSideInfo(entry, out float side, out bool isMiddle);
        Vector3 npcPos = GetNpcBaseWorldPosition();

        Gizmos.color = Color.cyan;
        List<Vector3> robotPath = BuildRobotPath(npcPos + robotTargetOffset, side, isMiddle);
        Gizmos.DrawWireSphere(robotPath[0], 0.2f);
        for (int i = 0; i < robotPath.Count - 1; i++)
        {
            Gizmos.DrawLine(robotPath[i], robotPath[i + 1]);
        }

        Gizmos.color = Color.yellow;
        List<Vector3> exitPath = BuildNpcExitPath(npcPos, side, isMiddle);
        Vector3 prev = npcPos;
        foreach (Vector3 waypoint in exitPath)
        {
            Gizmos.DrawLine(prev, waypoint);
            prev = waypoint;
        }
        Gizmos.DrawWireSphere(prev, 0.2f);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(NpcScript))]
public class NpcScriptEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);

        NpcScript npc = (NpcScript)target;

        using (new EditorGUI.DisabledScope(!Application.isPlaying || npc.HasSwitched))
        {
            if (GUILayout.Button("Test RobotSwitch", GUILayout.Height(30)))
            {
                npc.RobotSwitch();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the test button.", MessageType.Info);
        }
        else if (npc.HasSwitched)
        {
            EditorGUILayout.HelpBox("RobotSwitch has already been executed (it can only run once).", MessageType.Info);
        }
    }
}
#endif
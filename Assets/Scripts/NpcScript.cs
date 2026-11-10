using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NpcScript : MonoBehaviour
{
    public enum RobotEntrySide
    {
        Random,
        Left,
        Right
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

    [Tooltip("Which side the robot enters from. Random picks left or right each time.")]
    public RobotEntrySide robotEntrySide = RobotEntrySide.Random;

    [Tooltip("Robot movement speed (units per second).")]
    public float robotMoveSpeed = 3f;

    [Tooltip("Horizontal distance from the NPC where the robot spawns.")]
    public float robotSpawnDistance = 8f;

    [Tooltip("Offset from the NPC position where the robot stops (0 = exactly at the NPC).")]
    public Vector3 robotTargetOffset = Vector3.zero;

    [Header("NPC Exit Settings")]
    [Tooltip("When the robot is within this distance of its stop position, the NPC starts moving out.")]
    public float npcExitTriggerDistance = 3f;

    [Tooltip("NPC movement speed when exiting (units per second).")]
    public float npcExitSpeed = 3f;

    [Tooltip("How far the NPC moves horizontally when exiting.")]
    public float npcExitDistance = 8f;

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
    /// Spawns a robot on the chosen side (left, right, or random) and moves it to this NPC's position.
    /// When the robot gets close enough, the NPC moves out toward the side the robot came from.
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

        Vector3 target = GetNpcBaseWorldPosition() + robotTargetOffset;

        // Left = -1, Right = +1
        float side;
        switch (robotEntrySide)
        {
            case RobotEntrySide.Left:
                side = -1f;
                break;
            case RobotEntrySide.Right:
                side = 1f;
                break;
            default:
                side = Random.value < 0.5f ? -1f : 1f;
                break;
        }
        Vector3 spawnPos = target + new Vector3(side * robotSpawnDistance, 0f, 0f);

        GameObject newRobot = Instantiate(robot, spawnPos, robot.transform.rotation);

        // Flip the robot horizontally when it enters from the left
        if (side < 0f)
        {
            Vector3 scale = newRobot.transform.localScale;
            scale.x = -Mathf.Abs(scale.x);
            newRobot.transform.localScale = scale;
        }

        RobotScript robotScript = newRobot.GetComponent<RobotScript>();
        if (robotScript == null)
        {
            Debug.LogWarning($"[{name}] The spawned robot has no RobotScript attached.", newRobot);
        }

        StartCoroutine(MoveRobotRoutine(newRobot.transform, robotScript, target, side));
    }

    private IEnumerator MoveRobotRoutine(Transform robotTransform, RobotScript robotScript, Vector3 target, float side)
    {
        if (robotScript != null)
        {
            robotScript.Walk();
        }

        bool npcExitStarted = false;

        while (robotTransform != null &&
               (robotTransform.position - target).sqrMagnitude > 0.0001f)
        {
            robotTransform.position = Vector3.MoveTowards(
                robotTransform.position, target, robotMoveSpeed * Time.deltaTime);

            // Start the NPC exit once the robot is close enough
            if (!npcExitStarted &&
                Vector3.Distance(robotTransform.position, target) <= npcExitTriggerDistance)
            {
                npcExitStarted = true;
                StartCoroutine(NpcExitRoutine(side));
            }

            yield return null;
        }

        if (robotTransform != null)
        {
            robotTransform.position = target;
        }

        if (robotScript != null)
        {
            robotScript.StopWalking();
        }

        // Safety: make sure the NPC exits even if the trigger distance was never reached
        if (!npcExitStarted)
        {
            StartCoroutine(NpcExitRoutine(side));
        }
    }

    private IEnumerator NpcExitRoutine(float side)
    {
        // Stop bobbing and snap back to the bottom position before moving out
        if (bobbingRoutine != null)
        {
            StopCoroutine(bobbingRoutine);
            bobbingRoutine = null;
        }
        transform.localPosition = posA;

        // Move toward the side the robot came from
        Vector3 exitTarget = transform.position + new Vector3(side * npcExitDistance, 0f, 0f);

        while ((transform.position - exitTarget).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, exitTarget, npcExitSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = exitTarget;
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
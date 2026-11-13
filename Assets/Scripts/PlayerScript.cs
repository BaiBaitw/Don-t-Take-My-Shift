using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerScript : MonoBehaviour
{
    public Animator myAnimator;

    [Header("Robot Settings")]
    public GameObject robot;                    // Robot prefab (with RobotScript attached)
    public float robotSpawnDistance = 10f;      // How far to the right of the player the robot spawns
    public float robotSpawnBelowDistance = 3f;  // How far below the player the robot spawns
    public float robotMoveSpeed = 3f;           // Robot move speed (units per second)

    [Header("Player Exit Settings")]
    public float playerLeaveDelay = 2f;         // Seconds after the robot starts walking in before the player leaves
    public float playerRiseDistance = 2f;       // How far the player moves up before leaving
    public float playerRiseSpeed = 2f;          // Player move speed when moving up (units per second)
    public float exitSpeed = 2f;                // Player move speed when leaving (units per second)
    public Vector3 exitDirection = Vector3.right;  // Direction the player leaves in

    [Header("Gizmos")]
    public bool showGizmos = true;
    public float exitArrowLength = 3f;          // Length of the exit direction guide line

    private bool robotSwitchStarted = false;
    private bool playerLeaving = false;
    private GameObject spawnedRobot;
    private RobotScript robotScript;

    // Player position when RobotSwitch starts (so guides and targets don't move)
    private Vector3 originPos;
    private bool hasOrigin = false;

    // ---------- Path points ----------
    private Vector3 Origin => hasOrigin ? originPos : transform.position;
    private Vector3 RobotSpawnPos => Origin + Vector3.right * robotSpawnDistance + Vector3.down * robotSpawnBelowDistance;
    private Vector3 RobotRisePos => Origin + Vector3.right * robotSpawnDistance;
    private Vector3 RobotTargetPos => Origin;
    private Vector3 PlayerRisePos => Origin + Vector3.up * playerRiseDistance;

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

    public void RobotSwitch()
    {
        // Prevent spawning more than one robot
        if (robotSwitchStarted) return;
        robotSwitchStarted = true;

        StartCoroutine(RobotSwitchRoutine());
    }

    private IEnumerator RobotSwitchRoutine()
    {
        if (robot == null)
        {
            Debug.LogWarning("PlayerScript: Robot prefab is not assigned.", this);
            yield break;
        }

        // Lock the original player position
        originPos = transform.position;
        hasOrigin = true;

        // Spawn the robot below and to the right of the player
        spawnedRobot = Instantiate(robot, RobotSpawnPos, robot.transform.rotation);

        robotScript = spawnedRobot.GetComponent<RobotScript>();
        if (robotScript == null)
            Debug.LogWarning("PlayerScript: Spawned robot has no RobotScript.", spawnedRobot);

        // Robot starts walking
        if (robotScript != null)
            robotScript.Walk();

        // Start the timer for the player to leave
        StartCoroutine(PlayerLeaveTimer());

        // Step 1: robot moves up to the player's height
        yield return MoveRobotTo(RobotRisePos);

        // Step 2: robot moves left to the player's original position
        yield return MoveRobotTo(RobotTargetPos);

        // Robot has arrived
        if (robotScript != null)
            robotScript.StopWalking();
    }

    private IEnumerator MoveRobotTo(Vector3 target)
    {
        while (Vector3.Distance(spawnedRobot.transform.position, target) > 0.01f)
        {
            spawnedRobot.transform.position = Vector3.MoveTowards(
                spawnedRobot.transform.position,
                target,
                robotMoveSpeed * Time.deltaTime);
            yield return null;
        }

        spawnedRobot.transform.position = target;
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

        myAnimator.SetBool("Lose", true);
        StartCoroutine(PlayerLeaveRoutine());
    }

    private IEnumerator PlayerLeaveRoutine()
    {
        // Step 1: player moves up
        Vector3 riseTarget = PlayerRisePos;
        while (Vector3.Distance(transform.position, riseTarget) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                riseTarget,
                playerRiseSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = riseTarget;

        // Step 2: player keeps walking out
        while (true)
        {
            transform.position += exitDirection.normalized * exitSpeed * Time.deltaTime;
            yield return null;
        }
    }

    // ---------- Guide lines in the Scene view ----------
    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Vector3 origin = Origin;

        // Robot path (cyan): spawn -> up -> left to player's original position
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(RobotSpawnPos, 0.2f);
        Gizmos.DrawLine(RobotSpawnPos, RobotRisePos);
        Gizmos.DrawWireSphere(RobotRisePos, 0.15f);
        Gizmos.DrawLine(RobotRisePos, RobotTargetPos);
        Gizmos.DrawWireSphere(RobotTargetPos, 0.25f);

        // Player path (yellow): up -> exit direction
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, PlayerRisePos);
        Gizmos.DrawWireSphere(PlayerRisePos, 0.2f);
        Vector3 exitEnd = PlayerRisePos + exitDirection.normalized * exitArrowLength;
        Gizmos.DrawLine(PlayerRisePos, exitEnd);
        DrawArrowHead(exitEnd, exitDirection.normalized);

#if UNITY_EDITOR
        Handles.Label(RobotSpawnPos, "Robot Spawn");
        Handles.Label(RobotRisePos, "Robot Turn");
        Handles.Label(RobotTargetPos + Vector3.down * 0.4f, "Robot Target");
        Handles.Label(PlayerRisePos, "Player Rise");
        Handles.Label(exitEnd, "Player Exit");
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

        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("Test Game Over"))
            player.GameOver();
        GUI.enabled = true;

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play Mode to use the test buttons.", MessageType.Info);
    }
}
#endif
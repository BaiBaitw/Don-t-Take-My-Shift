using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerScript : MonoBehaviour
{
    public Animator myAnimator;

    [Header("Robot Settings")]
    public GameObject robot;                  // Robot prefab (with RobotScript attached)
    public float robotSpawnDistance = 10f;    // How far to the right of the player the robot spawns
    public float robotMoveSpeed = 3f;         // Robot move speed (units per second)
    public float robotStopDistance = 1.5f;    // Robot stops when this close to the player's original position

    [Header("Player Exit Settings")]
    public float playerLeaveDistance = 4f;    // Player starts leaving when the robot is this close
    public float exitSpeed = 2f;              // Player move speed when leaving (units per second)
    public Vector3 exitDirection = Vector3.right;  // Direction the player leaves in

    private bool robotSwitchStarted = false;
    private bool playerLeaving = false;
    private GameObject spawnedRobot;
    private RobotScript robotScript;

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

        // Spawn the robot to the right of the player
        Vector3 spawnPos = transform.position + Vector3.right * robotSpawnDistance;
        spawnedRobot = Instantiate(robot, spawnPos, robot.transform.rotation);

        robotScript = spawnedRobot.GetComponent<RobotScript>();
        if (robotScript == null)
            Debug.LogWarning("PlayerScript: Spawned robot has no RobotScript.", spawnedRobot);

        // Robot starts walking
        if (robotScript != null)
            robotScript.Walk();

        // Robot target is based on the player's original position
        Vector3 targetPos = transform.position + Vector3.right * robotStopDistance;

        // Move the robot until it reaches the target
        while (Vector3.Distance(spawnedRobot.transform.position, targetPos) > 0.01f)
        {
            spawnedRobot.transform.position = Vector3.MoveTowards(
                spawnedRobot.transform.position,
                targetPos,
                robotMoveSpeed * Time.deltaTime);

            // Player starts leaving when the robot gets close enough
            if (!playerLeaving &&
                Vector3.Distance(spawnedRobot.transform.position, transform.position) <= playerLeaveDistance)
            {
                StartLeaving();
            }

            if (playerLeaving)
                MovePlayer();

            yield return null;
        }

        // Robot has arrived
        if (robotScript != null)
            robotScript.StopWalking();

        // In case the leave distance was never reached
        if (!playerLeaving)
            StartLeaving();

        // Player keeps walking out
        while (true)
        {
            MovePlayer();
            yield return null;
        }
    }

    private void StartLeaving()
    {
        playerLeaving = true;
        myAnimator.SetBool("Lose", true);
    }

    private void MovePlayer()
    {
        transform.position += exitDirection.normalized * exitSpeed * Time.deltaTime;
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
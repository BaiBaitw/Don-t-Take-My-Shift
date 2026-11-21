using UnityEngine;

public class RobotScript : MonoBehaviour
{
    public Animator myAnimator;

    [Header("Countdown Bar")]
    public string timerManagerName = "TimerManager";   // Name of the object to search for in the scene

    private CountdownBar countdownBar;
    private bool lastWorkState = false;

    private static readonly int WalkHash = Animator.StringToHash("Walk");
    private static readonly int WorkHash = Animator.StringToHash("Work");

    private void Start()
    {
        myAnimator = GetComponent<Animator>();
        FindCountdownBar();
    }

    private void FindCountdownBar()
    {
        // Searches the whole scene by name, regardless of hierarchy path
        GameObject timerManager = GameObject.Find(timerManagerName);

        if (timerManager != null)
        {
            countdownBar = timerManager.GetComponent<CountdownBar>();
        }

        if (countdownBar == null)
        {
            Debug.LogWarning($"{name}: Could not find '{timerManagerName}' with a CountdownBar component in the scene.");
        }
    }

    private void Update()
    {
        // Retry if TimerManager wasn't ready when this robot spawned
        if (countdownBar == null)
        {
            FindCountdownBar();
            if (countdownBar == null) return;
        }

        bool isRunning = countdownBar.IsRunning;

        // Only update the Animator when the state changes
        if (isRunning != lastWorkState)
        {
            myAnimator.SetBool(WorkHash, isRunning);
            lastWorkState = isRunning;
        }
    }

    public void Walk()
    {
        myAnimator.SetBool(WalkHash, true);
    }

    public void StopWalking()
    {
        myAnimator.SetBool(WalkHash, false);
    }
}
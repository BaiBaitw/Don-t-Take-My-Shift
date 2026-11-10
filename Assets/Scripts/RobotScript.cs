using UnityEngine;

public class RobotScript : MonoBehaviour
{
    public Animator myAnimator;

    private void Start()
    {
        myAnimator = GetComponent<Animator>();
    }

    public void Walk() { 
        myAnimator.SetBool("Walk", true);
    }

    public void StopWalking() { 
        myAnimator.SetBool("Walk", false);
    }
}

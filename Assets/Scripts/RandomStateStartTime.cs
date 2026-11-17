using UnityEngine;

public class RandomStateStartTime : StateMachineBehaviour
{
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (stateInfo.normalizedTime < 0.1f)
        {
            animator.Play(stateInfo.fullPathHash, layerIndex, Random.Range(0f, 1f));
        }
    }
}
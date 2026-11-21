using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SupermanPlayerScript : MonoBehaviour
{
    public Animator myAnimator;

    private bool isGameOver = false;

    /// <summary>True once GameOver() has been executed. It can only run once.</summary>
    public bool IsGameOver => isGameOver;

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
        // GameOver can only be executed once
        if (isGameOver) return;
        isGameOver = true;

        // The player stays in place. Add any game over effects here.
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SupermanPlayerScript))]
public class SupermanPlayerScriptEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);

        SupermanPlayerScript player = (SupermanPlayerScript)target;

        using (new EditorGUI.DisabledScope(!Application.isPlaying || player.IsGameOver))
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
        else if (player.IsGameOver)
        {
            EditorGUILayout.HelpBox("Game Over has already been executed (it can only run once).", MessageType.Info);
        }
    }
}
#endif
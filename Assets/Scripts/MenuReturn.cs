// MenuReturn.cs
// Attach to an object in a level scene so Button OnClick (or any UnityEvent) can call these from the Inspector.
using UnityEngine;

public class MenuReturn : MonoBehaviour
{
    [Tooltip("Which level this scene is (1-based). 0 = use the level last launched from the menu")]
    public int levelNumber = 0;

    /// <summary>Go back to the menu without clearing the level.</summary>
    public void BackToMenu()
    {
        MenuManager.ReturnToMenu();
    }

    /// <summary>Level cleared: unlock the next level and go back to the menu.</summary>
    public void CompleteAndBackToMenu()
    {
        if (levelNumber > 0) MenuManager.CompleteLevelAndReturn(levelNumber);
        else MenuManager.CompleteCurrentLevelAndReturn();
    }
}
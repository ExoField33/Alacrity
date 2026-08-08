namespace AlacrityTerraria.Runtime;

/// <summary>
/// Edge-detects only state that is observed through the version-locked main update hook. The first
/// update establishes a baseline; startup never fabricates lifecycle transitions for plugins.
/// </summary>
internal sealed class ClientPresentationStateTracker
{
    private bool initialized;
    private bool gameMenu;
    private bool chatInput;

    internal void Update(bool currentGameMenu, bool currentChatInput, out bool gameMenuChanged, out bool chatInputChanged)
    {
        if (!initialized)
        {
            initialized = true;
            gameMenu = currentGameMenu;
            chatInput = currentChatInput;
            gameMenuChanged = false;
            chatInputChanged = false;
            return;
        }

        gameMenuChanged = gameMenu != currentGameMenu;
        chatInputChanged = chatInput != currentChatInput;
        gameMenu = currentGameMenu;
        chatInput = currentChatInput;
    }
}

/// <summary>
/// Implement this interface on any object the player can interact with
/// by looking at it and pressing E.
/// </summary>
public interface IInteractable
{
    /// <summary>Executes the interaction logic.</summary>
    void Interact();

    /// <summary>Returns the hint text shown to the player (e.g. "Open Door [E]").</summary>
    string GetInteractText();
}

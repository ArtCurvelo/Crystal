namespace CrystalExcavator.Player;

/// <summary>Objetos que respondem à tecla de interação contextual (E).</summary>
public interface IInteractable
{
    string GetPrompt();
    void Interact(PlayerController player);
}

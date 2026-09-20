// ReSharper disable UnassignedField.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
namespace HornetInHallownest.Save;

// Global binds for hornet's actions. Null key: use HK's equivalent. Null controller button: no button.
public sealed class InputSettings {
    public string? MoveLeft;
    public string? MoveRight;
    public string? MoveUp;
    public string? MoveDown;

    public string? Jump;
    public string? Attack;
    public string? Dash;
    public string? Harpoon; // CDash
    public string? Bind; // Focus
    public string? Tool; // Quick Cast
    public string? Needolin; // Dream Nail
    public string? OpenInventory;
    public string? Taunt = "V";
    public string? TauntController = "RightStickButton";
    public string? OpenTools = "L";
    public string? OpenToolsController;
    public string? SwitchHero = "F5"; // toggle Knight <-> Hornet
}

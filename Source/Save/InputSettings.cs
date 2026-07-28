// ReSharper disable UnassignedField.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
namespace HornetInHallownest.Save;

// Global bindings for hornets actions. Null means use HK equivalent.
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
    public string? OpenTools = "L";
    public string? SwitchHero = "F5"; // toggle Knight <-> Hornet
}

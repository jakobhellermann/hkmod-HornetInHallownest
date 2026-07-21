// ReSharper disable UnassignedField.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
namespace HornetPlayer.HornetInHallownest.Save;

// Global bindings for hornets actions. Null means use HK equivalent.
public sealed class InputSettings {
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
    public string? SwitchHero = "Tab"; // toggle Knight <-> Hornet
}

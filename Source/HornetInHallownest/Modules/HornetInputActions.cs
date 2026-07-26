using InControl;

namespace HornetInHallownest.HornetInHallownest.Modules;

// InControl action set holding hornets actions.
// Automatically updated by HK's InputManager.UpdatePlayerActionSets since constructor attaches it.
public sealed class HornetInputActions : PlayerActionSet {
    public readonly PlayerAction[] Slots;

    public HornetInputActions(int count) {
        Slots = new PlayerAction[count];
        for (var i = 0; i < count; i++) Slots[i] = CreatePlayerAction($"HornetInput{i}");
    }
}

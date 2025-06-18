using System.Reflection;
using Modding;

namespace HornetPlayer;

public class HornetPlayerMod : Mod, ITogglableMod {
    public static HornetPlayerMod? LoadedInstance { get; private set; }

    public override string GetVersion() {
        return Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    public override void Initialize() {
        if (LoadedInstance != null) return;
        LoadedInstance = this;
    }

    public void Unload() {
        LoadedInstance = null;
    }
}

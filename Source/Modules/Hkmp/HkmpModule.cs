#if HK1578
extern alias Silksong;
using System;
using System.Runtime.CompilerServices;
using HornetInHallownest.Core;
using Hkmp.Api.Client;
using Modding;

namespace HornetInHallownest.Modules.Hkmp;

// Shows Hornet to other HKMP players running this mod. Inert unless the HKMP mod is loaded.
public sealed class HkmpModule : ModuleBase {
#pragma warning disable CA1859
    private IHkmpLink? link;
#pragma warning restore CA1859

    public override string Id => "hkmp";

    public override void Initialize() {
        if (ModHooks.GetMod("HKMP") != null) RegisterAddon();
    }

    // Kept out of Initialize: compiling a method that touches HKMP types loads HKMP.dll.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RegisterAddon() => ClientAddon.RegisterAddon(new HornetHkmpAddon(api => link = new HkmpLink(api)));

    public override void HornetActiveUpdate(Silksong::HeroController hero) => link?.HornetActiveUpdate(hero);

    public override void HornetToggled(bool active) => link?.HornetToggled(active);

    protected override void OnDeinitialize() => link?.Dispose();
}

internal interface IHkmpLink : IDisposable {
    void HornetActiveUpdate(Silksong::HeroController hero);
    void HornetToggled(bool active);
}
#endif

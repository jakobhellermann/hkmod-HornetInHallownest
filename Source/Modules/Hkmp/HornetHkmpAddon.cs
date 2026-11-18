#if HK1578
using System;
using Hkmp.Api.Client;

namespace HornetInHallownest.Modules.Hkmp;

// HKMP addon, showing hornet when both sides have the mod.
internal sealed class HornetHkmpAddon(Action<IClientApi> onInitialize) : ClientAddon {
    protected override string Name => "HornetInHallownest";
    protected override string Version => "1";
    public override bool NeedsNetwork => false; // support public servers.

    public override void Initialize(IClientApi clientApi) => onInitialize(clientApi);
}
#endif

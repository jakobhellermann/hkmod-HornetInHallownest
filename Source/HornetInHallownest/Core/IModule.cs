namespace HornetPlayer.HornetInHallownest.Core;

public interface IModule {
    string Id { get; }
    void Initialize();
    void Deinitialize();
}

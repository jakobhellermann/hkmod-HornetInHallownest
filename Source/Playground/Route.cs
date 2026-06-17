namespace HornetPlayer.Playground;

// A sync handler returns a DevResponse for full control, or any object to be JSON-serialized. An async handler runs as
// a coroutine (for work spanning frames, e.g. WaitForEndOfFrame before a screenshot) and calls `respond` when done.
internal class Route(string method, string path, RouteHandler? sync, AsyncRouteHandler? async) {
    public string Method { get; } = method;
    public string Path { get; } = path;
    public RouteHandler? Sync { get; } = sync;
    public AsyncRouteHandler? Async { get; } = async;
}

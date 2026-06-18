using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HornetPlayer.Playground; // Log

namespace HornetPlayer.DevServer;

// Returns a DevResponse for full control, or any object which is serialized to JSON. Runs on the main thread.
public delegate object? RouteHandler(DevRequest request);

// For work spanning multiple frames (e.g. WaitForEndOfFrame before a screenshot). Runs as a coroutine; call respond when done.
public delegate IEnumerator AsyncRouteHandler(DevRequest request, Action<object?> respond);

// A lean, framework-agnostic HTTP debug server. Unlike DevUtils' DevServer (BepInEx-coupled, OpenAPI, per-plugin
// prefixing) this is a single flat route table owned by the one mod. Pump Update() from a MonoBehaviour each frame.
public static class DebugServer {
    private static readonly Dictionary<string, Route> routes = new();
    private static HttpServer? server;

    public static bool IsRunning => server != null;

    public static void MapGet(string path, RouteHandler handler) => Add("GET", path, handler, null);
    public static void MapGet(string path, AsyncRouteHandler handler) => Add("GET", path, null, handler);
    public static void MapPost(string path, RouteHandler handler) => Add("POST", path, handler, null);
    public static void MapPost(string path, AsyncRouteHandler handler) => Add("POST", path, null, handler);
    public static void Map(string method, string path, RouteHandler handler) => Add(method, path, handler, null);

    private static void Add(string method, string path, RouteHandler? sync, AsyncRouteHandler? async) {
        var normalized = Normalize(path);
        var key = Key(method, normalized);
        routes[key] = new Route(method.ToUpperInvariant(), normalized, sync, async);
        // Log.Info($"[DebugServer] mapped {key}");
    }

    public static void Start(MonoBehaviour host, int port) {
        if (server != null) return;

        Map("GET", "/routes", _ => routes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        try {
            server = new HttpServer(host, port, Resolve);
            Log.Info($"DebugServer listening on http://localhost:{port}/");
        } catch (Exception e) {
            Log.Error($"DebugServer failed to start on port {port}: {e}");
        }
    }

    public static void Update() => server?.Update();

    public static void Stop() {
        server?.Dispose();
        server = null;
        routes.Clear();
    }

    private static Route? Resolve(string method, string path) => routes.GetValueOrDefault(Key(method, path));

    private static string Key(string method, string path) => method.ToUpperInvariant() + " " + Normalize(path);

    private static string Normalize(string path) {
        if (string.IsNullOrEmpty(path)) return "/";
        if (!path.StartsWith("/")) path = "/" + path;
        if (path.Length > 1 && path.EndsWith("/")) path = path.TrimEnd('/');
        return path;
    }
}

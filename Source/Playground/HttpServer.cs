using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HornetPlayer.Playground;

// Requests arrive on a background thread; handlers must run on the Unity main thread. Each request is queued and the
// listener thread blocks on a TaskCompletionSource until Update() (main thread) runs the handler.
internal class HttpServer : IDisposable {
    private readonly MonoBehaviour host;
    private readonly HttpListener listener;
    private readonly ConcurrentQueue<PendingRequest> pending = new();
    private readonly Func<string, string, Route?> resolve;
    private volatile bool running;

    public HttpServer(MonoBehaviour host, int port, Func<string, string, Route?> resolve) {
        this.host = host;
        this.resolve = resolve;
        listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        running = true;
        var thread = new Thread(Serve) { IsBackground = true, Name = "HornetPlayerDebugServer" };
        thread.Start();
    }

    public void Dispose() {
        running = false;
        listener.Stop();
        listener.Close();
    }

    private void Serve() {
        while (running) {
            HttpListenerContext ctx;
            try {
                ctx = listener.GetContext();
            } catch (HttpListenerException) {
                break;
            } catch (ObjectDisposedException) {
                break;
            }

            try {
                HandleContext(ctx);
            } catch (Exception e) {
                Log.Error(e);
                TryClose(ctx);
            }
        }
    }

    private void HandleContext(HttpListenerContext ctx) {
        var method = ctx.Request.HttpMethod;
        var path = Normalize(ctx.Request.Url?.AbsolutePath ?? "/");
        var route = resolve(method, path);

        if (route == null) {
            Log.Info($"[DebugServer] {method} {path} -> 404");
            WriteResponse(ctx, DevResponse.Json(new { error = "no such route", method, path }, 404));
            return;
        }

        string? body = null;
        if (ctx.Request.HasEntityBody) {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            body = reader.ReadToEnd();
        }

        var req = new DevRequest(path, method, ctx.Request.QueryString, body);
        var tcs = new TaskCompletionSource<DevResponse>();
        pending.Enqueue(new PendingRequest(route, req, tcs));

        DevResponse response;
        try {
            response = tcs.Task.GetAwaiter().GetResult();
        } catch (Exception e) {
            response = DevResponse.Json(new { error = e.Message, type = e.GetType().Name }, 500);
        }

        Log.Info($"[DebugServer] {method} {path} -> {response.StatusCode}");
        WriteResponse(ctx, response);
    }

    private static string Normalize(string path) {
        if (string.IsNullOrEmpty(path)) return "/";
        if (path.Length > 1 && path.EndsWith("/")) path = path.TrimEnd('/');
        return path;
    }

    private static void WriteResponse(HttpListenerContext ctx, DevResponse response) {
        ctx.Response.StatusCode = response.StatusCode;
        ctx.Response.ContentType = response.ContentType;
        ctx.Response.ContentLength64 = response.Body.Length;
        ctx.Response.OutputStream.Write(response.Body, 0, response.Body.Length);
        ctx.Response.Close();
    }

    private static void TryClose(HttpListenerContext ctx) {
        try {
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
        } catch {
            // ignore — connection may already be gone
        }
    }

    public void Update() {
        while (pending.TryDequeue(out var item))
            try {
                if (item.Route.Async != null)
                    host.StartCoroutine(RunAsync(item));
                else
                    item.Tcs.SetResult(DevResponse.From(item.Route.Sync!(item.Request)));
            } catch (Exception e) {
                item.Tcs.SetException(e);
            }
    }

    private static IEnumerator RunAsync(PendingRequest item) {
        object? captured = null;
        var responded = false;

        IEnumerator inner;
        try {
            inner = item.Route.Async!(item.Request, result => {
                captured = result;
                responded = true;
            });
        } catch (Exception e) {
            item.Tcs.SetException(e);
            yield break;
        }

        while (true) {
            bool moved;
            try {
                moved = inner.MoveNext();
            } catch (Exception e) {
                item.Tcs.SetException(e);
                yield break;
            }

            if (!moved) break;
            yield return inner.Current;
        }

        item.Tcs.SetResult(responded ? DevResponse.From(captured) : DevResponse.Json(new { ok = true }));
    }

    private readonly struct PendingRequest(Route route, DevRequest request, TaskCompletionSource<DevResponse> tcs) {
        public Route Route { get; } = route;
        public DevRequest Request { get; } = request;
        public TaskCompletionSource<DevResponse> Tcs { get; } = tcs;
    }
}

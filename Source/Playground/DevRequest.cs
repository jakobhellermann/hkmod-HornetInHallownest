using System.Collections.Specialized;

namespace HornetPlayer.Playground;

public class DevRequest(string path, string httpMethod, NameValueCollection query, string? body) {
    public string Path { get; } = path;
    public string HttpMethod { get; } = httpMethod;
    public NameValueCollection Query { get; } = query;
    public string? Body { get; } = body;

    public string? this[string key] => Query[key];
}

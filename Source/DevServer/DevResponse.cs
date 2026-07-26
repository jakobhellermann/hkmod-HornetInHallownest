using System.Text;
using Newtonsoft.Json;

namespace HornetInHallownest.DevServer;

public class DevResponse(byte[] body, string contentType, int statusCode = 200) {
    private static readonly JsonSerializerSettings jsonSettings = new() {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include
    };

    public byte[] Body { get; } = body;
    public string ContentType { get; } = contentType;
    public int StatusCode { get; } = statusCode;

    public static DevResponse Json(object? value, int statusCode = 200) {
        var json = JsonConvert.SerializeObject(value, jsonSettings);
        return new DevResponse(Encoding.UTF8.GetBytes(json), "application/json", statusCode);
    }

    public static DevResponse Text(string text, int statusCode = 200) {
        return new DevResponse(Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", statusCode);
    }

    public static DevResponse Bytes(byte[] data, string contentType, int statusCode = 200) {
        return new DevResponse(data, contentType, statusCode);
    }

    internal static DevResponse From(object? result) {
        return result as DevResponse ?? Json(result);
    }
}

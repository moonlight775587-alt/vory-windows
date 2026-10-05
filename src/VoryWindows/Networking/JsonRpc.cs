using System;
using Newtonsoft.Json.Linq;

namespace VoryWindows.Networking
{
    /// <summary>JSON-RPC 2.0 frame helpers mirroring the Hermes gateway wire protocol.</summary>
    public static class JsonRpc
    {
        public static string BuildRequest(int id, string method, JObject parameters)
        {
            var frame = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new JObject()
            };
            return frame.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildResponse(object id, JObject result)
        {
            var frame = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id == null ? JValue.CreateNull() : JToken.FromObject(id),
                ["result"] = result ?? new JObject()
            };
            return frame.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildErrorResponse(object id, int code, string message)
        {
            var frame = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id == null ? JValue.CreateNull() : JToken.FromObject(id),
                ["error"] = new JObject { ["code"] = code, ["message"] = message ?? "" }
            };
            return frame.ToString(Newtonsoft.Json.Formatting.None);
        }

        public enum FrameKind { Response, EventNotification, ServerRequest, Unknown }

        public static FrameKind Classify(JObject frame)
        {
            if (frame == null) return FrameKind.Unknown;
            var method = frame["method"]?.ToString();
            if (!string.IsNullOrEmpty(method))
            {
                return method == "event" ? FrameKind.EventNotification : FrameKind.ServerRequest;
            }
            if (frame["id"] != null && (frame["result"] != null || frame["error"] != null))
                return FrameKind.Response;
            return FrameKind.Unknown;
        }

        public static bool IsMethodNotFound(JObject errorFrame)
        {
            var code = errorFrame?["error"]?["code"];
            return code != null && code.Type == JTokenType.Integer && code.Value<int>() == -32601;
        }

        public static string ErrorMessage(JObject errorFrame)
        {
            return errorFrame?["error"]?["message"]?.ToString() ?? "Unknown error";
        }
    }

    public class JsonRpcException : Exception
    {
        public int Code { get; }
        public bool IsMethodNotFound { get { return Code == -32601; } }

        public JsonRpcException(int code, string message) : base(message)
        {
            Code = code;
        }
    }
}

using System.Text;
using System.Text.Json.Nodes;

const string handshake = "GORIDIS-PROC-PLUGIN 1";
var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

if (await input.ReadLineAsync() != handshake)
{
    return 2;
}

await output.WriteLineAsync(handshake);

string? line;
while ((line = await input.ReadLineAsync()) is not null)
{
    JsonObject request;
    try
    {
        request = JsonNode.Parse(line)!.AsObject();
    }
    catch
    {
        await output.WriteLineAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 0,
            ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "parse error" },
        }.ToJsonString());
        continue;
    }

    var id = request["id"]?.DeepClone() ?? 0;
    var method = request["method"]?.GetValue<string>();
    var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
    switch (method)
    {
        case "echo":
            var text = request["params"]?["text"]?.GetValue<string>() ?? string.Empty;
            response["result"] = new JsonObject { ["text"] = $"echo:{text}" };
            break;
        case "shutdown":
            response["result"] = new JsonObject();
            await output.WriteLineAsync(response.ToJsonString());
            return 0;
        default:
            response["error"] = new JsonObject { ["code"] = -32601, ["message"] = "unknown method" };
            break;
    }

    await output.WriteLineAsync(response.ToJsonString());
}

return 0;

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

    if (request["result"] is not null || request["error"] is not null)
    {
        continue; // response to a plugin -> host call
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
        case "crash":
            return 3;
        case "callhost":
            await output.WriteLineAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 900,
                ["method"] = "host/unsupported",
            }.ToJsonString());
            var rejection = await input.ReadLineAsync();
            await Console.Error.WriteLineAsync(rejection ?? "no rejection");
            response["result"] = new JsonObject { ["deferred"] = true };
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

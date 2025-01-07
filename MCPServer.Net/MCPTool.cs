using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCPServer;
public class MCPTool : MCPBase
{
    public JsonObject InputSchema { get; }
    private readonly Func<Dictionary<string, JsonElement>, CancellationToken, Task<MCPToolResult>> executeAction;

    public MCPTool(
        string name,
        string description,
        JsonObject inputSchema,
        Func<Dictionary<string, JsonElement>, CancellationToken, Task<MCPToolResult>> executeAction)
        : base(name, description)
    {
        InputSchema = inputSchema ?? throw new ArgumentNullException(nameof(inputSchema));
        this.executeAction = executeAction ?? throw new ArgumentNullException(nameof(executeAction));
    }

    public Task<MCPToolResult> ExecuteAsync(Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default)
    {
        return executeAction(arguments, cancellationToken);
    }


}
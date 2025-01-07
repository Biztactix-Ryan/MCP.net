using System.Text.Json.Nodes;
using System.Text.Json;

namespace MCPServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Create and initialize the server
        var server = new MCPServer(
            name: "TestMCPServer",
            description: "Test server implementation with calculator, file system, and greeting functionality",
            version: "1.0"
        );
        server.EnableLogging(".\\mcp-server.log");

        await server.InitializeAsync();

        try
        {
            // Add calculator tool
            server.AddTool(new CalculatorTool());

            // Add config file resource
            var configResource = new FileSystemResource(
                uri: "file://config.json",
                filePath: "R:\\mcp-definition.json",
                name: "Configuration File",
                description: "Application configuration settings",
                mimeType: "application/json"
            );
            server.AddResource(configResource);

            // Add greeting prompt
            server.AddPrompt(new GreetingPrompt());

            // Start listening for requests
            await server.ListenAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Server error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}

// Calculator tool implementation
public class CalculatorTool : MCPTool
{
    private static readonly JsonObject schema = JsonSerializer.Deserialize<JsonObject>("""
    {
        "type": "object",
        "properties": {
            "operation": {
                "type": "string",
                "enum": ["add", "subtract", "multiply", "divide"],
                "description": "The arithmetic operation to perform"
            },
            "a": {
                "type": "number",
                "description": "First operand"
            },
            "b": {
                "type": "number",
                "description": "Second operand"
            }
        },
        "required": ["operation", "a", "b"]
    }
    """);

    public CalculatorTool() : base(
        name: "calculator",
        description: "Performs basic arithmetic operations",
        inputSchema: schema,
        executeAction: ExecuteCalculatorAsync)
    {
    }

    private static async Task<MCPToolResult> ExecuteCalculatorAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var operation = arguments["operation"].GetString();
        var a = arguments["a"].GetDouble();
        var b = arguments["b"].GetDouble();

        double result = operation switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" when b != 0 => a / b,
            "divide" => throw new DivideByZeroException("Cannot divide by zero"),
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };

        return MCPToolResult.Success(new[] { MCPContent.sText(result.ToString()) });
    }
}

// File system resource implementation
public class FileSystemResource : MCPResource
{
    private readonly FileSystemWatcher watcher;

    public FileSystemResource(
        string uri,
        string filePath,
        string name,
        string description,
        string mimeType) : base(
            name: name,
            description: description,
            uri: uri,
            mimeType: mimeType,
            readContentAction: ct => File.ReadAllTextAsync(filePath, ct))
    {
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        var filename = Path.GetFileName(filePath);

        watcher = new FileSystemWatcher(directory, filename)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        watcher.Changed += (s, e) => OnChanged(DateTime.UtcNow);
        watcher.EnableRaisingEvents = true;
    }
}

// Greeting prompt implementation
public class GreetingPrompt : MCPPrompt
{
    private static readonly IReadOnlyList<MCPPromptArgument> arguments = new[]
    {
        new MCPPromptArgument("name", "The name to greet"),
        new MCPPromptArgument("language", "The language to use (en, es, fr)", required: false)
    };

    public GreetingPrompt() : base(
        name: "greeting",
        description: "Generates a greeting message",
        arguments: arguments,
        getMessagesAction: GetGreetingMessagesAsync)
    {
    }

    private static Task<IReadOnlyList<MCPMessage>> GetGreetingMessagesAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        var name = arguments["name"];
        var language = arguments.GetValueOrDefault("language", "en");

        var greeting = language switch
        {
            "es" => $"¡Hola, {name}!",
            "fr" => $"Bonjour, {name}!",
            _ => $"Hello, {name}!"
        };

        var messages = new[]
        {
            new MCPMessage("assistant", MCPContent.sText(greeting))
        };

        return Task.FromResult<IReadOnlyList<MCPMessage>>(messages);
    }
}
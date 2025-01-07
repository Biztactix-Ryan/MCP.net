using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPServer;

public class MCPServer
{
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly JsonSerializerOptions jsonOptions;
    private StreamWriter logWriter;
    private bool isLoggingEnabled;
    private readonly ConcurrentDictionary<string, MCPTool> tools = new();
    private readonly ConcurrentDictionary<string, MCPResource> resources = new();
    private readonly ConcurrentDictionary<string, MCPPrompt> prompts = new();
    private readonly ConcurrentDictionary<string, DateTime> subscribedResources = new();
    private bool isInitialized = false;
    private string protocolVersion = "2024-11-05";
    public event EventHandler<InitializationEventArgs> Initializing;
    private bool isInitializing = false;
    // Server properties
    public string Name { get; }
    public string Description { get; }
    public string Version { get; }

    public void EnableLogging(string logPath)
    {
        try
        {
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Open log file with append mode
            logWriter = new StreamWriter(logPath, true);
            isLoggingEnabled = true;
            LogMessage("Logging initialized");
        }
        catch (Exception ex)
        {
            error.WriteLine($"Failed to initialize logging: {ex.Message}");
        }
    }

    private void LogMessage(string message, bool isError = false)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] {(isError ? "ERROR" : "INFO ")} - {message}";

        // Write to error console
        error.WriteLine(logMessage);

        // Write to log file if enabled
        if (isLoggingEnabled && logWriter != null)
        {
            try
            {
                logWriter.WriteLine(logMessage);
                logWriter.Flush();  // Ensure it's written immediately
            }
            catch (Exception ex)
            {
                error.WriteLine($"Failed to write to log: {ex.Message}");
            }
        }
    }

    public MCPServer(string name, string description, string version,
        TextReader input = null, TextWriter output = null, TextWriter error = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Version = version ?? throw new ArgumentNullException(nameof(version));

        this.input = input ?? Console.In;
        this.output = output ?? Console.Out;
        this.error = error ?? Console.Error;
        this.jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Add this line
        };
    }

    public async Task InitializeAsync()
    {
        // Just set up initial state - don't send any messages yet
        isInitialized = true;
        error.WriteLine($"MCP server '{Name}' ready to handle initialization request...");
    }

    private async Task HandleInitializeAsync(string id)
    {
        if (isInitializing) return;
        isInitializing = true;

        try
        {
            // First send quick response
            var capabilities = new
            {
                resources = new { subscribe = true, listChanged = true },
                tools = new { listChanged = true },
                prompts = new { listChanged = true }
            };

            await SendResponseAsync(id, new
            {
                protocolVersion = this.protocolVersion,
                serverInfo = new
                {
                    name = this.Name,
                    description = this.Description,
                    version = this.Version
                },
                capabilities = capabilities
            });

            // Raise event for subscribers to handle initialization
            Initializing?.Invoke(this, new InitializationEventArgs(id));

            isInitialized = true;
            error.WriteLine($"MCP server '{Name}' initialized...");
        }
        catch (Exception ex)
        {
            error.WriteLine($"Initialization error: {ex.Message}");
            throw;
        }
        finally
        {
            isInitializing = false;
        }
    }

    public void ServerReady()
    {
        SendNotificationAsync("server/ready", new { }).Wait();
    }

    public void AddTool(MCPTool tool)
    {

        if (!tools.TryAdd(tool.Name, tool))
            throw new ArgumentException($"Tool with name '{tool.Name}' already exists");

        error.WriteLine($"Tool '{tool.Name}' registered successfully");
    }

    public void AddResource(MCPResource resource)
    {
        if (!isInitialized)
            throw new InvalidOperationException("Server must be initialized before adding resources");

        if (!resources.TryAdd(resource.Uri, resource))
            throw new ArgumentException($"Resource with URI '{resource.Uri}' already exists");

        resource.Changed += OnResourceChanged;
        error.WriteLine($"Resource '{resource.Uri}' registered successfully");
    }

    public void AddPrompt(MCPPrompt prompt)
    {
        if (!isInitialized)
            throw new InvalidOperationException("Server must be initialized before adding prompts");

        if (!prompts.TryAdd(prompt.Name, prompt))
            throw new ArgumentException($"Prompt with name '{prompt.Name}' already exists");

        error.WriteLine($"Prompt '{prompt.Name}' registered successfully");
    }

    public async Task ListenAsync(CancellationToken cancellationToken = default)
    {
        if (!isInitialized)
            throw new InvalidOperationException("Server must be initialized before starting to listen");

        error.WriteLine($"MCP server '{Name}' listening for requests...");

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line == null) break;

            try
            {
                await HandleMessageAsync(line, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                error.WriteLine($"Error handling message: {ex}");
                await SendErrorAsync(null, -32603, $"Internal error: {ex.Message}");
            }
        }
    }

    private async Task HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
        LogMessage($"Received message: {message}");

        var jsonDoc = JsonDocument.Parse(message);
        var root = jsonDoc.RootElement;

        if (root.TryGetProperty("id", out var idElement) &&
            root.TryGetProperty("method", out var methodElement))
        {
            // Get ID as either string or number
            string id = idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : idElement.GetRawText();
            var method = methodElement.GetString();

            if (!isInitialized && method != "initialize")
            {
                await SendErrorAsync(id, -32600, "Server not initialized");
                return;
            }

            await HandleRequestAsync(id, method, root, cancellationToken);
        }
        else if (root.TryGetProperty("method", out var notifMethodElement))
        {
            var method = notifMethodElement.GetString();
            await HandleNotificationAsync(method, root, cancellationToken);
        }
    }

    private async Task HandleRequestAsync(string id, string method, JsonElement root, CancellationToken cancellationToken)
    {
        try
        {
            switch (method)
            {
                case "initialize":
                    await HandleInitializeAsync(id);
                    break;

                case "resources/list":
                    await HandleListResourcesAsync(id);
                    break;

                case "resources/read":
                    await HandleReadResourceAsync(id, root.GetProperty("params"), cancellationToken);
                    break;

                case "resources/subscribe":
                    await HandleResourceSubscribeAsync(id, root.GetProperty("params"));
                    break;

                case "resources/unsubscribe":
                    await HandleResourceUnsubscribeAsync(id, root.GetProperty("params"));
                    break;

                case "tools/list":
                    await HandleListToolsAsync(id);
                    break;

                case "tools/call":
                    await HandleToolCallAsync(id, root.GetProperty("params"), cancellationToken);
                    break;

                case "prompts/list":
                    await HandleListPromptsAsync(id);
                    break;

                case "prompts/get":
                    await HandleGetPromptAsync(id, root.GetProperty("params"), cancellationToken);
                    break;

                case "ping":
                    await SendResponseAsync(id, new { });
                    break;

                default:
                    await SendErrorAsync(id, -32601, $"Method not found: {method}");
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendErrorAsync(id, -32603, $"Error handling {method}: {ex.Message}");
        }
    }

    private async Task HandleListResourcesAsync(string id)
    {
        var resourcesList = resources.Values.Select(r => new
        {
            uri = r.Uri,
            name = r.Name,
            description = r.Description,
            mimeType = r.MimeType
        });

        await SendResponseAsync(id, new { resources = resourcesList });
    }

    private async Task HandleReadResourceAsync(string id, JsonElement paramsElement, CancellationToken cancellationToken)
    {
        var uri = paramsElement.GetProperty("uri").GetString();
        if (!resources.TryGetValue(uri, out var resource))
        {
            await SendErrorAsync(id, -32602, $"Resource not found: {uri}");
            return;
        }

        var content = await resource.ReadContentAsync(cancellationToken);
        var contents = new[]
        {
            new { uri, text = content, mimeType = resource.MimeType }
        };

        await SendResponseAsync(id, new { contents });
    }

    private async Task HandleResourceSubscribeAsync(string id, JsonElement paramsElement)
    {
        var uri = paramsElement.GetProperty("uri").GetString();
        if (!resources.ContainsKey(uri))
        {
            await SendErrorAsync(id, -32602, $"Resource not found: {uri}");
            return;
        }

        subscribedResources[uri] = DateTime.UtcNow;
        await SendResponseAsync(id, new { });
    }

    private async Task HandleResourceUnsubscribeAsync(string id, JsonElement paramsElement)
    {
        var uri = paramsElement.GetProperty("uri").GetString();
        subscribedResources.TryRemove(uri, out _);
        await SendResponseAsync(id, new { });
    }

    private async Task HandleListToolsAsync(string id)
    {
        var toolsList = tools.Values.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            inputSchema = t.InputSchema
        });

        await SendResponseAsync(id, new { tools = toolsList });
    }

    private async Task HandleToolCallAsync(string id, JsonElement paramsElement, CancellationToken cancellationToken)
    {
        var toolName = paramsElement.GetProperty("name").GetString();
        if (!tools.TryGetValue(toolName, out var tool))
        {
            await SendErrorAsync(id, -32602, $"Tool not found: {toolName}");
            return;
        }

        var arguments = paramsElement.GetProperty("arguments")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value);

        var result = await tool.ExecuteAsync(arguments, cancellationToken);
        await SendResponseAsync(id, new
        {
            content = result.Content.Select(c => new { type = c.Type, text = c.Text, mimeType = c.MimeType }),
            isError = result.IsError
        });
    }

    private async Task HandleListPromptsAsync(string id)
    {
        var promptsList = prompts.Values.Select(p => new
        {
            name = p.Name,
            description = p.Description,
            arguments = p.Arguments.Select(a => new
            {
                name = a.Name,
                description = a.Description,
                required = a.Required
            })
        });

        await SendResponseAsync(id, new { prompts = promptsList });
    }

    private async Task HandleGetPromptAsync(string id, JsonElement paramsElement, CancellationToken cancellationToken)
    {
        var promptName = paramsElement.GetProperty("name").GetString();
        if (!prompts.TryGetValue(promptName, out var prompt))
        {
            await SendErrorAsync(id, -32602, $"Prompt not found: {promptName}");
            return;
        }

        var arguments = paramsElement.TryGetProperty("arguments", out var argsElement) ?
            argsElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()) :
            new Dictionary<string, string>();

        var messages = await prompt.GetMessagesAsync(arguments, cancellationToken);
        await SendResponseAsync(id, new
        {
            messages = messages.Select(m => new
            {
                role = m.Role,
                content = new { type = m.Content.Type, text = m.Content.Text }
            })
        });
    }

    private async Task HandleNotificationAsync(string method, JsonElement root, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialized":
                error.WriteLine("Client initialization complete");
                break;

            case "$/cancelRequest":
                // Handle cancellation if needed
                if (root.TryGetProperty("params", out var paramsElement) &&
                    paramsElement.TryGetProperty("id", out var requestId))
                {
                    error.WriteLine($"Request cancelled: {requestId}");
                }
                break;

            default:
                error.WriteLine($"Received notification: {method}");
                break;
        }
    }

    private async void OnResourceChanged(object sender, ResourceChangedEventArgs e)
    {
        if (subscribedResources.ContainsKey(e.Uri))
        {
            await SendNotificationAsync("resources/changed", new
            {
                uri = e.Uri,
                timestamp = e.Timestamp
            });
        }
    }

    private async Task SendResponseAsync(string id, object result)
    {
        // Try parse id as number if it's numeric
        var parsedId = id;
        if (long.TryParse(id, out var numericId))
        {
            // If the id was originally a number, send it as a number
            var response = new
            {
                jsonrpc = "2.0",
                id = numericId,
                result = result ?? new { }
            };
            await SendMessageAsync(response);
        }
        else
        {
            // Otherwise send it as a string
            var response = new
            {
                jsonrpc = "2.0",
                id = id ?? "null",
                result = result ?? new { }
            };
            await SendMessageAsync(response);
        }
    }

    private async Task SendErrorAsync(string id, int code, string message, object data = null)
    {
        // Try parse id as number if it's numeric
        var parsedId = id;
        if (long.TryParse(id, out var numericId))
        {
            // If the id was originally a number, send it as a number
            var response = new
            {
                jsonrpc = "2.0",
                id = numericId,
                error = new
                {
                    code,
                    message,
                    data = data ?? new { }
                }
            };
            await SendMessageAsync(response);
        }
        else
        {
            // Otherwise send it as a string
            var response = new
            {
                jsonrpc = "2.0",
                id = id ?? "null",
                error = new
                {
                    code,
                    message,
                    data = data ?? new { }
                }
            };
            await SendMessageAsync(response);
        }
    }

    private async Task SendNotificationAsync(string method, object parameters)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        await SendMessageAsync(notification);
    }

    private async Task SendMessageAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, jsonOptions);
        LogMessage($"Sending message: {json}");
        await output.WriteLineAsync(json);
        await output.FlushAsync();
    }

    public void Dispose()
    {
        logWriter?.Dispose();
    }
}
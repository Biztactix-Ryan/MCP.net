
public class InitializationEventArgs : EventArgs
{
    public string RequestId { get; }
    public InitializationEventArgs(string requestId)
    {
        RequestId = requestId;
    }
}
public class ResourceChangedEventArgs : EventArgs
{
    public string Uri { get; }
    public DateTime Timestamp { get; }

    public ResourceChangedEventArgs(string uri, DateTime timestamp)
    {
        Uri = uri;
        Timestamp = timestamp;
    }
}

public class MCPToolResult
{
    public IReadOnlyList<MCPContent> Content { get; }
    public bool IsError { get; }

    public MCPToolResult(IReadOnlyList<MCPContent> content, bool isError = false)
    {
        Content = content;
        IsError = isError;
    }

    public static MCPToolResult Success(IReadOnlyList<MCPContent> content) => new(content, false);
    public static MCPToolResult Error(string errorMessage) => new(new[] { MCPContent.sText(errorMessage) }, true);
}

public class MCPContent
{
    public string Type { get; }
    public string Text { get; }
    public string MimeType { get; }

    private MCPContent(string type, string text, string mimeType = null)
    {
        Type = type;
        Text = text;
        MimeType = mimeType;
    }

    public static MCPContent sText(string text) => new("text", text);
    public static MCPContent File(string text, string mimeType) => new("file", text, mimeType);
}

public class MCPPromptArgument
{
    public string Name { get; }
    public string Description { get; }
    public bool Required { get; }

    public MCPPromptArgument(string name, string description, bool required = true)
    {
        Name = name;
        Description = description;
        Required = required;
    }
}

public class MCPMessage
{
    public string Role { get; }
    public MCPContent Content { get; }

    public MCPMessage(string role, MCPContent content)
    {
        Role = role;
        Content = content;
    }
}
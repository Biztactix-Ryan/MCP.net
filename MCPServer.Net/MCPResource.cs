using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCPServer;
public class MCPResource : MCPBase
{
    public string Uri { get; }
    public string MimeType { get; }
    private readonly Func<CancellationToken, Task<string>> readContentAction;

    public event EventHandler<ResourceChangedEventArgs> Changed;

    public MCPResource(
        string name,
        string description,
        string uri,
        string mimeType,
        Func<CancellationToken, Task<string>> readContentAction)
        : base(name, description)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
        this.readContentAction = readContentAction ?? throw new ArgumentNullException(nameof(readContentAction));
    }

    public Task<string> ReadContentAsync(CancellationToken cancellationToken = default)
    {
        return readContentAction(cancellationToken);
    }

    protected virtual void OnChanged(DateTime timestamp)
    {
        Changed?.Invoke(this, new ResourceChangedEventArgs(Uri, timestamp));
    }



}

// MCPPrompt.cs

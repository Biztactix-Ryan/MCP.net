using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MCPServer;
public class MCPPrompt : MCPBase
{
    public IReadOnlyList<MCPPromptArgument> Arguments { get; }
    private readonly Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<IReadOnlyList<MCPMessage>>> getMessagesAction;

    public MCPPrompt(
        string name,
        string description,
        IReadOnlyList<MCPPromptArgument> arguments,
        Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<IReadOnlyList<MCPMessage>>> getMessagesAction)
        : base(name, description)
    {
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        this.getMessagesAction = getMessagesAction ?? throw new ArgumentNullException(nameof(getMessagesAction));
    }

    public Task<IReadOnlyList<MCPMessage>> GetMessagesAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        return getMessagesAction(arguments, cancellationToken);
    }
}


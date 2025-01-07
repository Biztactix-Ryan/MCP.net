using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Text.Json;
using System.Xml.Linq;
using DotNetMCP;

namespace MCPServer;

public class DotnetMCP
{

    private static DotnetMCP server;
    private readonly MCPServer mcpServer;
    private readonly MSBuildWorkspace workspace;
    private Solution solution;
    private MCPTools tools;
    private readonly string solutionPath;
    private Dictionary<string, string> options;
    private bool isInitialized;


    public DotnetMCP(Dictionary<string, string> options)
    {
        this.options = options;
        this.solutionPath = options["solution"];
        this.mcpServer = new MCPServer("MCP.Net", "MCP Server for .NET development", "1.0.0");


        var properties = new Dictionary<string, string>
        {
            { "RestorePackagesConfig", "true" },
            { "RestoreIgnoreFailedSources", "true" },
            { "RestoreNoCache", "true" }
        };

        workspace = MSBuildWorkspace.Create(properties);
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var parser = new CommandLineParser();
            parser.AddOption("--solution", "Path to the solution file", required: true);
            parser.AddOption("--log", "Log file path", required: false);

            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("DotnetMCP - .NET Development MCP Server");
                Console.WriteLine("\nUsage:");
                Console.WriteLine("  DotnetMCP --solution <path> [--log <path>]\n");
                Console.WriteLine("Options:");
                Console.WriteLine("  --solution <path>     Required. Path to the .sln file");
                Console.WriteLine("  --log <path>          Optional. Log file path");
                Console.WriteLine("  --help, -h            Show this help message");
                return 1;
            }
            var options = parser.Parse(args);
            server = new DotnetMCP(options);

            await server.InitializeAsync();

            await server.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public async Task InitializeAsync()
    {

        if (options.ContainsKey("log"))
        {
            mcpServer.EnableLogging(options["log"]);
        }

        mcpServer.Initializing += OnServerInitializing;
        await mcpServer.InitializeAsync();

        isInitialized = true;
        // error.WriteLine($"MCP server 'DotNet' ready to handle initialization request...");
    }

    private async void OnServerInitializing(object sender, InitializationEventArgs e)
    {
        try
        {
            LoadSolution();
            InitializeTools();
            InitializeResources();
            mcpServer.ServerReady();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during initialization: {ex.Message}");
        }
    }

    private void LoadSolution()
    {

        // Create workspace with custom properties to handle package restore


        // Register workspace failed event handler
        workspace.WorkspaceFailed += (s, e) =>
        {
            if (e.Diagnostic.ToString().Contains("NuGet"))
            {
                Console.Error.WriteLine($"Attempting NuGet restore for: {e.Diagnostic.Message}");
                // Try to restore packages
                RestoreNuGetPackagesAsync().Wait();
            }
            Console.Error.WriteLine($"Workspace error: {e.Diagnostic.Message}");
        };

        // Try to restore packages before loading solution
        RestoreNuGetPackagesAsync().Wait();

        // Load the solution
        try
        {
            solution = workspace.OpenSolutionAsync(solutionPath).GetAwaiter().GetResult();
            tools = new MCPTools(solution);
            Console.Error.WriteLine($"Solution loaded successfully: {Path.GetFileName(solutionPath)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load solution: {ex.Message}");
            throw;
        }

    }

    private class CommandLineParser
    {
        private readonly Dictionary<string, (string description, string defaultValue, bool required)> options = new();

        public void AddOption(string name, string description, string defaultValue = null, bool required = false)
        {
            options[name] = (description, defaultValue, required);
        }

        public Dictionary<string, string> Parse(string[] args)
        {
            var result = new Dictionary<string, string>();

            // Add default values
            foreach (var (option, (_, defaultValue, _)) in options)
            {
                if (defaultValue != null)
                {
                    result[option.TrimStart('-')] = defaultValue;
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    var option = args[i];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        result[option.TrimStart('-')] = args[i + 1];
                        i++;
                    }
                }
            }

            // Check required options
            var missingOptions = options
                .Where(o => o.Value.required && !result.ContainsKey(o.Key.TrimStart('-')))
                .Select(o => o.Key)
                .ToList();

            if (missingOptions.Any())
            {
                throw new ArgumentException(
                    $"Missing required options: {string.Join(", ", missingOptions)}");
            }

            return result;
        }
    }

    private void EnableLogging(string logPath)
    {
        mcpServer.EnableLogging(logPath);
    }

    private string GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text)),
            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
            DestructorDeclarationSyntax dtor => "~" + dtor.Identifier.Text,
            EventDeclarationSyntax evt => evt.Identifier.Text,
            IndexerDeclarationSyntax indexer => "this[]",
            DelegateDeclarationSyntax del => del.Identifier.Text,
            EnumMemberDeclarationSyntax enumMember => enumMember.Identifier.Text,
            _ => member.ToString()
        };
    }


    private async Task RestoreNuGetPackagesAsync()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore \"{solutionPath}\" --force",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return;

            // Create a cancellation token with 30 second timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var processTask = Task.Run(() =>
            {
                process.WaitForExit();
                return (
                    output: process.StandardOutput.ReadToEnd(),
                    error: process.StandardError.ReadToEnd(),
                    exitCode: process.ExitCode
                );
            });

            try
            {
                var (output, error, exitCode) = await processTask.WaitAsync(cts.Token);

                if (exitCode != 0)
                {
                    Console.Error.WriteLine($"NuGet restore failed: {error}");
                }
                else
                {
                    Console.Error.WriteLine("NuGet packages restored successfully");
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true); // Kill process tree
                    }
                }
                catch { }
                Console.Error.WriteLine("NuGet restore timed out after 30 seconds");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to restore NuGet packages: {ex.Message}");
        }


    }

    private void InitializeResources()
    {
        // Resource for project structure
        foreach (var project in solution.Projects)
        {
            var projectResource = new MCPResource(
                $"project_{project.Name}",
                $"Project structure for {project.Name}",
                $"project/{project.Name}/structure",
                "application/json",
                async (cancellationToken) =>
                {
                    var structure = await GetProjectStructureAsync(project, cancellationToken);
                    return System.Text.Json.JsonSerializer.Serialize(structure, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
            );
            mcpServer.AddResource(projectResource);

            // Resources for each source file
            foreach (var document in project.Documents)
            {
                var docResource = new MCPResource(
                    $"file_{document.FilePath.GetHashCode()}",
                    $"Source code for {document.FilePath}",
                    $"file/{document.FilePath.GetHashCode()}/content",
                    "text/plain",
                    async (cancellationToken) => (await document.GetTextAsync(cancellationToken)).ToString()
                );
                mcpServer.AddResource(docResource);
            }
        }
    }

    private void InitializeTools()
    {
        // Tool for getting project list
       mcpServer.AddTool(tools.GetDependencyGraphTool());
        mcpServer.AddTool(tools.GetExtensionMethodsTool());
        mcpServer.AddTool(tools.GetClassStructureTool());
        mcpServer.AddTool(tools.GetMethodStructureTool());
        mcpServer.AddTool(tools.GetMethodUsagesTool());
        mcpServer.AddTool(tools.GetQuickStartTool());
        mcpServer.AddTool(tools.GetListClassesTool());
        mcpServer.AddTool(tools.GetReadFileTool());
        mcpServer.AddTool(tools.GetEnhancedQuickStartTool());
    }

    private async Task<Dictionary<string, object>> GetProjectStructureAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        var structure = new Dictionary<string, object>
        {
            ["name"] = project.Name,
            ["path"] = project.FilePath,
            ["documents"] = await Task.WhenAll(project.Documents.Select(async doc =>
            {
                var syntaxTree = await doc.GetSyntaxTreeAsync(cancellationToken);
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => c.Identifier.Text);

                return new
                {
                    path = doc.FilePath,
                    name = doc.Name,
                    resourceUri = $"file/{doc.FilePath.GetHashCode()}/content",
                    classes = classes.ToList()
                };
            })),
            ["references"] = project.MetadataReferences.Select(r => r.Display).ToList()
        };

        return structure;
    }

    private async Task<MCPToolResult> GetProjectsAsync(Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var projects = solution.Projects.Select(p => new
        {
            name = p.Name,
            path = p.FilePath,
            documentCount = p.Documents.Count(),
            resourceUri = $"project/{p.Name}/structure"
        });

        return MCPToolResult.Success(new[]
        {
            MCPContent.sText(System.Text.Json.JsonSerializer.Serialize(projects, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }))
        });
    }

    private async Task<MCPToolResult> GetClassStructureAsync(Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var projectName = arguments["projectName"].GetString();
            var filePath = arguments["filePath"].GetString();

            var project = solution.Projects.FirstOrDefault(p => p.Name == projectName);
            if (project == null)
                return MCPToolResult.Error($"Project '{projectName}' not found");

            var document = project.Documents.FirstOrDefault(d => d.FilePath == filePath);
            if (document == null)
                return MCPToolResult.Error($"File '{filePath}' not found in project '{projectName}'");

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var classStructures = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Select(type =>
                {
                    var symbol = semanticModel.GetDeclaredSymbol(type);
                    return new
                    {
                        name = type.Identifier.Text,
                        kind = type.Kind().ToString(),
                        modifiers = type.Modifiers.ToString(),
                        baseTypes = type.BaseList?.Types.Select(t => t.ToString()),
                        members = type.Members.Select(m =>
                        {
                            var memberSymbol = semanticModel.GetDeclaredSymbol(m);
                            var memberName = GetMemberName(m);
                            return new
                            {
                                name = memberName,
                                kind = m.Kind().ToString(),
                                modifiers = m.Modifiers.ToString(),
                                type = memberSymbol?.GetType().Name,
                                location = m.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                            };
                        })
                    };
                });

            return MCPToolResult.Success(new[]
            {
                MCPContent.sText(System.Text.Json.JsonSerializer.Serialize(classStructures, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                }))
            });
        }
        catch (Exception ex)
        {
            return MCPToolResult.Error($"Failed to get class structure: {ex.Message}");
        }
    }

    private async Task<MCPToolResult> GetSymbolInfoAsync(Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var projectName = arguments["projectName"].GetString();
            var filePath = arguments["filePath"].GetString();
            var line = arguments["line"].GetInt32() - 1; // Convert to 0-based
            var column = arguments["column"].GetInt32() - 1;

            var project = solution.Projects.FirstOrDefault(p => p.Name == projectName);
            if (project == null)
                return MCPToolResult.Error($"Project '{projectName}' not found");

            var document = project.Documents.FirstOrDefault(d => d.FilePath == filePath);
            if (document == null)
                return MCPToolResult.Error($"File '{filePath}' not found in project '{projectName}'");

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            var position = syntaxTree.GetText().Lines[line].Start + column;

            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, workspace, cancellationToken);

            if (symbol == null)
                return MCPToolResult.Error("No symbol found at specified position");

            var symbolInfo = new
            {
                name = symbol.Name,
                kind = symbol.Kind,
                containingNamespace = symbol.ContainingNamespace?.ToString(),
                containingType = symbol.ContainingType?.ToString(),
                locations = symbol.Locations.Select(loc => new
                {
                    path = loc.SourceTree?.FilePath,
                    startLine = loc.GetLineSpan().StartLinePosition.Line + 1,
                    startColumn = loc.GetLineSpan().StartLinePosition.Character + 1,
                    endLine = loc.GetLineSpan().EndLinePosition.Line + 1,
                    endColumn = loc.GetLineSpan().EndLinePosition.Character + 1
                }),
                documentation = symbol.GetDocumentationCommentXml()
            };

            return MCPToolResult.Success(new[]
            {
                MCPContent.sText(System.Text.Json.JsonSerializer.Serialize(symbolInfo, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                }))
            });
        }
        catch (Exception ex)
        {
            return MCPToolResult.Error($"Failed to get symbol info: {ex.Message}");
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await mcpServer.InitializeAsync();
        await mcpServer.ListenAsync(cancellationToken);
    }
}
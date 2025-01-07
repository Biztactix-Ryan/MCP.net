using MCPServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DotNetMCP;
public class MCPTools
{
    private readonly Solution solution;
    private readonly DotNetMCP.Services.DotNet.CodeStructureAnalyzer structureAnalyzer;
    private readonly DotNetMCP.Services.DotNet.ExtensionMethodAnalyzer extensionAnalyzer;
    private readonly CodeAnalysisExtensions codeAnalyzer;

    public MCPTools(Solution solution)
    {
        this.solution = solution;
        this.structureAnalyzer = new DotNetMCP.Services.DotNet.CodeStructureAnalyzer();
        this.extensionAnalyzer = new DotNetMCP.Services.DotNet.ExtensionMethodAnalyzer();
        this.codeAnalyzer = new CodeAnalysisExtensions();
    }

    public MCPTool GetListProjectsTool()
    {
        return new MCPTool(
            "listProjects",
            "Get a list of all projects in the solution",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),  // No properties needed, but explicitly define the schema
                ["required"] = new JsonArray()
            }, // No parameters needed
            async (args, ct) =>
            {
                try
                {
                    var projects = solution.Projects.Select(p => new
                    {
                        name = p.Name,
                        path = p.FilePath,
                        language = p.Language,
                        documentCount = p.Documents.Count(),
                        hasTests = p.Name.Contains("Test", StringComparison.OrdinalIgnoreCase),
                        targetFramework = p.OutputFilePath?.Split("\\bin\\")?[0]?.Split('\\')?.LastOrDefault()
                    });

                    return MCPToolResult.Success(new[]
                    {
                    MCPContent.sText(JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetReadFileTool()
    {
        return new MCPTool(
            "readFile",
            "Read the contents of a specific file in the solution",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Path to the file to read"
                    }
                },
                ["required"] = new JsonArray { "filePath" }
            },
            async (args, ct) =>
            {
                try
                {
                    var filePath = args["filePath"].GetString();
                    var project = solution.Projects.FirstOrDefault(p =>
                        p.Documents.Any(d => d.FilePath == filePath));

                    if (project == null)
                    {
                        return MCPToolResult.Error($"File not found in solution: {filePath}");
                    }

                    var document = project.Documents.FirstOrDefault(d => d.FilePath == filePath);
                    if (document == null)
                    {
                        return MCPToolResult.Error($"Document not found: {filePath}");
                    }

                    var text = await document.GetTextAsync(ct);
                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(text.ToString())
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetListClassesTool()
    {
        return new MCPTool(
            "listClasses",
            "Get a list of all classes in a specific project",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["projectName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the project to analyze"
                    }
                },
                ["required"] = new JsonArray { "projectName" }
            },
            async (args, ct) =>
            {
                try
                {
                    var projectName = args["projectName"].GetString();
                    var project = solution.Projects.FirstOrDefault(p => p.Name == projectName);
                    if (project == null) throw new ArgumentException($"Project '{projectName}' not found");

                    var compilation = await project.GetCompilationAsync(ct);
                    var classes = new List<object>();

                    foreach (var document in project.Documents)
                    {
                        var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                        var root = await syntaxTree.GetRootAsync(ct);
                        var semanticModel = compilation.GetSemanticModel(syntaxTree);

                        var classNodes = root.DescendantNodes()
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();

                        foreach (var classNode in classNodes)
                        {
                            var symbol = semanticModel.GetDeclaredSymbol(classNode) as INamedTypeSymbol;
                            classes.Add(new
                            {
                                name = classNode.Identifier.Text,
                                fullName = symbol.ToDisplayString(),
                                filePath = document.FilePath,
                                isPublic = classNode.Modifiers.Any(m => m.Text == "public"),
                                isStatic = classNode.Modifiers.Any(m => m.Text == "static"),
                                methodCount = classNode.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Count(),
                                baseType = symbol?.BaseType?.ToDisplayString(),
                                interfaces = symbol?.Interfaces.Select(i => i.ToDisplayString())
                            });
                        }
                    }

                    return MCPToolResult.Success(new[]
                    {
                    MCPContent.sText(JsonSerializer.Serialize(classes, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetFindEntryPointsTool()
    {
        return new MCPTool(
            "findEntryPoints",
            "Find all potential entry points (Main methods) in the solution",
            new JsonObject(), // No parameters needed
            async (args, ct) =>
            {
                try
                {
                    var entryPoints = new List<object>();

                    foreach (var project in solution.Projects)
                    {
                        var compilation = await project.GetCompilationAsync(ct);

                        foreach (var document in project.Documents)
                        {
                            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                            var root = await syntaxTree.GetRootAsync(ct);
                            var semanticModel = compilation.GetSemanticModel(syntaxTree);

                            var mainMethods = root.DescendantNodes()
                                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                                .Where(m => m.Identifier.Text == "Main");

                            foreach (var method in mainMethods)
                            {
                                var classNode = method.Ancestors()
                                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                                    .FirstOrDefault();

                                if (classNode != null)
                                {
                                    var symbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol ;
                                    entryPoints.Add(new
                                    {
                                        projectName = project.Name,
                                        className = classNode.Identifier.Text,
                                        filePath = document.FilePath,
                                        isStatic = method.Modifiers.Any(m => m.Text == "static"),
                                        isAsync = method.Modifiers.Any(m => m.Text == "async"),
                                        returnType = symbol.ReturnType.ToDisplayString(),
                                        parameters = method.ParameterList.Parameters.Select(p =>
                                            semanticModel.GetDeclaredSymbol(p)?.ToDisplayString())
                                    });
                                }
                            }
                        }
                    }

                    return MCPToolResult.Success(new[]
                    {
                    MCPContent.sText(JsonSerializer.Serialize(entryPoints, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }
    public MCPTool GetQuickStartTool()
{
    return new MCPTool(
        "quickstart",
        "Get a comprehensive overview of the solution structure and key components",
       new JsonObject
       {
           ["type"] = "object",
           ["properties"] = new JsonObject(),  // No properties needed, but explicitly define the schema
           ["required"] = new JsonArray()
       }, // No parameters needed
        async (args, ct) =>
        {
            try
            {
                var overview = new
                {
                    solution = new
                    {
                        projectCount = solution.Projects.Count(),
                        projects = await Task.WhenAll(solution.Projects.Select(async project =>
                        {
                            var compilation = await project.GetCompilationAsync(ct);
                            var entryPoints = new List<string>();
                            var publicClasses = new List<object>();
                            
                            foreach (var document in project.Documents)
                            {
                                var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                                var root = await syntaxTree.GetRootAsync(ct);
                                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                                // Find entry points (Main methods)
                                var mainMethods = root.DescendantNodes()
                                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                                    .Where(m => m.Identifier.Text == "Main");

                                foreach (var main in mainMethods)
                                {
                                    var containingClass = main.Ancestors()
                                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                                        .FirstOrDefault();
                                    if (containingClass != null)
                                    {
                                        entryPoints.Add(containingClass.Identifier.Text);
                                    }
                                }

                                // Find significant public classes (with methods)
                                var classNodes = root.DescendantNodes()
                                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                                    .Where(c => c.Modifiers.Any(m => m.Text == "public"));

                                foreach (var classNode in classNodes)
                                {
                                    var methodCount = classNode.Members
                                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                                        .Count();
                                    
                                    if (methodCount > 0) // Only include classes with methods
                                    {
                                        var symbol = semanticModel.GetDeclaredSymbol(classNode) as INamedTypeSymbol;
                                        if (symbol != null)
                                        {
                                            publicClasses.Add(new
                                            {
                                                name = classNode.Identifier.Text,
                                                methodCount = methodCount,
                                                hasInterface = symbol.Interfaces.Any(),
                                                @namespace = symbol.ContainingNamespace.ToDisplayString()
                                            });
                                        }
                                    }
                                }
                            }

                            return new
                            {
                                name = project.Name,
                                documentCount = project.Documents.Count(),
                                isTestProject = project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase),
                                entryPoints = entryPoints.Distinct(),
                                significantPublicClasses = publicClasses.OrderByDescending(c => ((dynamic)c).methodCount),
                                language = project.Language,
                                hasPackageReferences = project.MetadataReferences.Any(r => r.Display.Contains("packages"))
                            };
                        }))
                    },
                    keyFindings = new
                    {
                        hasTests = solution.Projects.Any(p => p.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)),
                        primaryLanguage = solution.Projects
                            .GroupBy(p => p.Language)
                            .OrderByDescending(g => g.Count())
                            .First().Key,
                        estimatedComplexity = solution.Projects.Sum(p => p.Documents.Count()) switch
                        {
                            < 10 => "Small",
                            < 50 => "Medium",
                            < 100 => "Large",
                            _ => "Very Large"
                        }
                    }
                };

                return MCPToolResult.Success(new[]
                {
                    MCPContent.sText(JsonSerializer.Serialize(overview, new JsonSerializerOptions { WriteIndented = true }))
                });
            }
            catch (Exception ex)
            {
                return MCPToolResult.Error(ex.Message);
            }
        }
    );
}

    public MCPTool GetEnhancedQuickStartTool()
    {
        return new MCPTool(
            "enhancedQuickstart",
            "Get a comprehensive overview of the solution structure with detailed file and class information",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["required"] = new JsonArray()
            },
            async (args, ct) =>
            {
                try
                {
                    var structuralAnalysis = new StringBuilder();
                    var jsonAnalysis = new List<object>();

                    foreach (var project in solution.Projects)
                    {
                        structuralAnalysis.AppendLine($"Project: {project.Name}");
                        structuralAnalysis.AppendLine($"Language: {project.Language}");
                        structuralAnalysis.AppendLine($"Document Count: {project.Documents.Count()}");
                        structuralAnalysis.AppendLine();

                        var projectInfo = new
                        {
                            name = project.Name,
                            files = new List<object>()
                        };

                        foreach (var document in project.Documents)
                        {
                            structuralAnalysis.AppendLine($"File: {Path.GetFileName(document.FilePath)}");
                            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                            var root = await syntaxTree.GetRootAsync(ct);
                            var compilation = await project.GetCompilationAsync(ct);
                            var semanticModel = compilation.GetSemanticModel(syntaxTree);

                            var fileClasses = new List<object>();
                            var classNodes = root.DescendantNodes()
                                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();

                            foreach (var classNode in classNodes)
                            {
                                var symbol = semanticModel.GetDeclaredSymbol(classNode) as INamedTypeSymbol;
                                if (symbol == null) continue;

                                structuralAnalysis.AppendLine($"  Class: {classNode.Identifier.Text}");

                                var methods = classNode.Members
                                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                                    .Select(m => new
                                    {
                                        name = m.Identifier.Text,
                                        returnType = m.ReturnType.ToString(),
                                        isPublic = m.Modifiers.Any(mod => mod.Text == "public"),
                                        isAsync = m.Modifiers.Any(mod => mod.Text == "async"),
                                        parameters = m.ParameterList.Parameters.Select(p =>
                                            $"{p.Type} {p.Identifier.Text}").ToList()
                                    }).ToList();

                                foreach (var method in methods)
                                {
                                    structuralAnalysis.AppendLine($"    Method: {method.name}");
                                    structuralAnalysis.AppendLine($"      Return Type: {method.returnType}");
                                    if (method.parameters.Any())
                                    {
                                        structuralAnalysis.AppendLine($"      Parameters: {string.Join(", ", method.parameters)}");
                                    }
                                }

                                fileClasses.Add(new
                                {
                                    name = classNode.Identifier.Text,
                                    fullName = symbol.ToDisplayString(),
                                    methods = methods,
                                    isPublic = classNode.Modifiers.Any(m => m.Text == "public"),
                                    isStatic = classNode.Modifiers.Any(m => m.Text == "static"),
                                    baseType = symbol.BaseType?.ToDisplayString(),
                                    interfaces = symbol.Interfaces.Select(i => i.ToDisplayString()).ToList()
                                });
                            }

                            projectInfo.files.Add(new
                            {
                                name = Path.GetFileName(document.FilePath),
                                path = document.FilePath,
                                classes = fileClasses
                            });

                            structuralAnalysis.AppendLine();
                        }

                        jsonAnalysis.Add(projectInfo);
                    }

                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(structuralAnalysis.ToString()),
                        MCPContent.File(
                            JsonSerializer.Serialize(jsonAnalysis, new JsonSerializerOptions { WriteIndented = true }),
                            "application/json"
                        )
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetFindReferencesTool()
    {
        return new MCPTool(
            "findReferences",
            "Find all references to a specific type or member",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["typeName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified type name"
                    },
                    ["memberName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional member name to find references to",
                        ["required"] = false
                    }
                },
                ["required"] = new JsonArray { "typeName" }
            },
            async (args, ct) =>
            {
                try
                {
                    var typeName = args["typeName"].GetString();
                    var memberName = args.TryGetValue("memberName", out var member) ? member.GetString() : null;

                    var references = new List<object>();
                    foreach (var project in solution.Projects)
                    {
                        var compilation = await project.GetCompilationAsync(ct);
                        var typeSymbol = compilation.GetTypeByMetadataName(typeName);

                        if (typeSymbol == null) continue;

                        ISymbol targetSymbol = typeSymbol;
                        if (memberName != null)
                        {
                            targetSymbol = typeSymbol.GetMembers(memberName).FirstOrDefault();
                            if (targetSymbol == null) continue;
                        }

                        var referencingLocations = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, ct);

                        foreach (var reference in referencingLocations)
                        {
                            foreach (var location in reference.Locations)
                            {
                                var lineSpan = location.Location.GetLineSpan();
                                references.Add(new
                                {
                                    filePath = location.Document.FilePath,
                                    line = lineSpan.StartLinePosition.Line + 1,
                                    column = lineSpan.StartLinePosition.Character + 1,
                                    projectName = location.Document.Project.Name
                                });
                            }
                        }
                    }

                    return MCPToolResult.Success(new[]
                    {
                    MCPContent.sText(JsonSerializer.Serialize(references, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetClassStructureTool()
    {
        return new MCPTool(
            "getClassStructure",
            "Get detailed class structure for a specific class",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["className"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified class name"
                    }
                },
                ["required"] = new JsonArray { "className" }
            },
            async (args, ct) =>
            {
                try
                {
                    var className = args["className"].GetString();
                    var structure = await structureAnalyzer.GetClassStructure(className, solution);
                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(JsonSerializer.Serialize(structure, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetMethodStructureTool()
    {
        return new MCPTool(
            "getMethodStructure",
            "Get detailed method structure and analysis",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["className"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified class name"
                    },
                    ["methodName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Method name"
                    }
                },
                ["required"] = new JsonArray { "className", "methodName" }
            },
            async (args, ct) =>
            {
                try
                {
                    var className = args["className"].GetString();
                    var methodName = args["methodName"].GetString();
                    var structure = await structureAnalyzer.GetMethodStructure(className, methodName, solution);
                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(JsonSerializer.Serialize(structure, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetMethodUsagesTool()
    {
        return new MCPTool(
            "getMethodUsages",
            "Find all usages of a specific method",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["className"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified class name"
                    },
                    ["methodName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Method name"
                    }
                },
                ["required"] = new JsonArray { "className", "methodName" }
            },
            async (args, ct) =>
            {
                try
                {
                    var className = args["className"].GetString();
                    var methodName = args["methodName"].GetString();
                    var usages = await structureAnalyzer.FindMethodUsages(className, methodName, solution);
                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(JsonSerializer.Serialize(usages, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetExtensionMethodsTool()
    {
        return new MCPTool(
            "getExtensionMethods",
            "Get all extension methods for a specific type",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["typeName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified type name"
                    }
                },
                ["required"] = new JsonArray { "typeName" }
            },
            async (args, ct) =>
            {
                try
                {
                    await extensionAnalyzer.AnalyzeExtensionMethods(solution);
                    var typeName = args["typeName"].GetString();
                    var groups = extensionAnalyzer.GetExtensionGroups();

                    if (!groups.TryGetValue(typeName, out var group))
                    {
                        return MCPToolResult.Success(new[] { MCPContent.sText("[]") });
                    }

                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(JsonSerializer.Serialize(group, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }

    public MCPTool GetDependencyGraphTool()
    {
        return new MCPTool(
            "getDependencyGraph",
            "Get dependency graph for a specific class",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["className"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified class name"
                    }
                },
                ["required"] = new JsonArray { "className" }
            },
            async (args, ct) =>
            {
                try
                {
                    var className = args["className"].GetString();
                    var project = solution.Projects.FirstOrDefault();
                    if (project == null) throw new Exception("No project found in solution");

                    var compilation = await project.GetCompilationAsync(ct);
                    var classType = compilation.GetTypeByMetadataName(className);
                    if (classType == null) throw new Exception($"Class {className} not found");

                    var syntaxTree = await classType.DeclaringSyntaxReferences.First().GetSyntaxAsync(ct) as Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax;
                    if (syntaxTree == null) throw new Exception($"Could not find class declaration for {className}");

                    var semanticModel = compilation.GetSemanticModel(syntaxTree.SyntaxTree);
                    var classDeclaration = syntaxTree;

                    var graph = await codeAnalyzer.AnalyzeClassDependencies(semanticModel, classDeclaration);

                    return MCPToolResult.Success(new[]
                    {
                        MCPContent.sText(JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true }))
                    });
                }
                catch (Exception ex)
                {
                    return MCPToolResult.Error(ex.Message);
                }
            }
        );
    }
}
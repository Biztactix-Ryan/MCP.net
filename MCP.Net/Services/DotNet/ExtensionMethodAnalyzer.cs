using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using static DotNetMCP.Services.DotNet.ExtensionMethodAnalyzer;

namespace DotNetMCP.Services.DotNet;

public interface IExtensionMethodAnalyzer
{
    Task AnalyzeExtensionMethods(Solution solution);
    IReadOnlyDictionary<string, ExtensionGroup> GetExtensionGroups();
}

public class ExtensionMethodAnalyzer : IExtensionMethodAnalyzer
{
    private readonly Dictionary<string, ExtensionGroup> _extensionGroups = new();

    public class ExtensionInfo
    {
        public string ExtensionMethod { get; set; }
        public string ExtendedType { get; set; }
        public string DefiningClass { get; set; }
        public string Namespace { get; set; }
        public string ReturnType { get; set; }
        public List<ExtensionParameterInfo> Parameters { get; set; } = new();
        public string Documentation { get; set; }
        public string SourceFile { get; set; }
        public bool IsAsync { get; set; }
        public List<string> UsedBy { get; set; } = new();
    }

    public class ExtensionParameterInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool HasDefaultValue { get; set; }
    }

    public class ExtensionGroup
    {
        public string ExtendedType { get; set; }
        public List<ExtensionInfo> Extensions { get; set; } = new();
        public Dictionary<string, List<string>> CategoryMap { get; set; } = new();
    }

    public ExtensionMethodAnalyzer()
    {
        
    }

    public IReadOnlyDictionary<string, ExtensionGroup> GetExtensionGroups() => _extensionGroups;

    public async Task AnalyzeExtensionMethods(Solution solution)
    {
        try
        {
            _extensionGroups.Clear();
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var document in project.Documents)
                {
                    await AnalyzeDocument(document, compilation);
                }
            }

            // Analyze usage patterns after collecting all extensions
            await AnalyzeExtensionUsage(solution);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, "Error analyzing extension methods");
            throw;
        }
    }

    private async Task AnalyzeDocument(Document document, Compilation compilation)
    {
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null) return;

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = await syntaxTree.GetRootAsync();

        // Find all static classes
        var staticClasses = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));

        foreach (var staticClass in staticClasses)
        {
            await AnalyzeStaticClass(staticClass, semanticModel, document);
        }
    }

    private async Task AnalyzeStaticClass(
        ClassDeclarationSyntax staticClass,
        SemanticModel semanticModel,
        Document document)
    {
        var methods = staticClass.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(m => m.ParameterList.Parameters.Count > 0 &&
                       m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.StaticKeyword)) &&
                       m.ParameterList.Parameters[0].Modifiers
                           .Any(mod => mod.IsKind(SyntaxKind.ThisKeyword)));

        foreach (var method in methods)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(method);
            if (methodSymbol == null) continue;

            var firstParam = method.ParameterList.Parameters[0];
            var firstParamType = semanticModel.GetTypeInfo(firstParam.Type).Type;
            if (firstParamType == null) continue;

            var extendedType = firstParamType.ToDisplayString();

            var extensionInfo = new ExtensionInfo
            {
                ExtensionMethod = method.Identifier.Text,
                ExtendedType = extendedType,
                DefiningClass = staticClass.Identifier.Text,
                Namespace = GetNamespace(staticClass),
                ReturnType = methodSymbol.ReturnType.ToDisplayString(),
                IsAsync = methodSymbol.IsAsync,
                SourceFile = document.FilePath,
                Documentation = methodSymbol.GetDocumentationCommentXml(),
                Parameters = method.ParameterList.Parameters
                    .Skip(1) // Skip this parameter
                    .Select(p => new ExtensionParameterInfo
                    {
                        Name = p.Identifier.Text,
                        Type = semanticModel.GetTypeInfo(p.Type).Type?.ToDisplayString() ?? p.Type.ToString(),
                        HasDefaultValue = p.Default != null
                    }).ToList()
            };

            AddToExtensionGroup(extensionInfo);
            CategorizeExtension(extensionInfo, methodSymbol);
        }
    }

    private void AddToExtensionGroup(ExtensionInfo extension)
    {
        if (!_extensionGroups.TryGetValue(extension.ExtendedType, out var group))
        {
            group = new ExtensionGroup { ExtendedType = extension.ExtendedType };
            _extensionGroups[extension.ExtendedType] = group;
        }

        group.Extensions.Add(extension);
    }

    private void CategorizeExtension(ExtensionInfo extension, IMethodSymbol methodSymbol)
    {
        var group = _extensionGroups[extension.ExtendedType];

        // Categorize by return type
        var returnCategory = "Returns" + extension.ReturnType.Split('.').Last();
        AddToCategory(group, returnCategory, extension.ExtensionMethod);

        // Categorize by common patterns
        if (extension.IsAsync)
        {
            AddToCategory(group, "AsyncOperations", extension.ExtensionMethod);
        }

        if (extension.ExtensionMethod.StartsWith("To", StringComparison.Ordinal))
        {
            AddToCategory(group, "Conversions", extension.ExtensionMethod);
        }

        if (extension.ExtensionMethod.StartsWith("With", StringComparison.Ordinal))
        {
            AddToCategory(group, "Builders", extension.ExtensionMethod);
        }

        // Categorize by parameter count
        var paramCategory = extension.Parameters.Count switch
        {
            0 => "NoParameters",
            1 => "SingleParameter",
            _ => "MultipleParameters"
        };
        AddToCategory(group, paramCategory, extension.ExtensionMethod);
    }

    private void AddToCategory(ExtensionGroup group, string category, string methodName)
    {
        if (!group.CategoryMap.TryGetValue(category, out var methods))
        {
            methods = new List<string>();
            group.CategoryMap[category] = methods;
        }
        if (!methods.Contains(methodName))
        {
            methods.Add(methodName);
        }
    }

    private async Task AnalyzeExtensionUsage(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var document in project.Documents)
            {
                await AnalyzeDocumentUsage(document, compilation);
            }
        }
    }

    private async Task AnalyzeDocumentUsage(Document document, Compilation compilation)
    {
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null) return;

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = await syntaxTree.GetRootAsync();

        var invocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol ||
                methodSymbol.ReducedFrom == null) continue;

            var containingType = methodSymbol.ReceiverType.ToDisplayString();
            var methodName = methodSymbol.Name;

            if (_extensionGroups.TryGetValue(containingType, out var group))
            {
                var extension = group.Extensions
                    .FirstOrDefault(e => e.ExtensionMethod == methodName);

                if (extension != null)
                {
                    var containingMethod = invocation.Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault();

                    if (containingMethod != null)
                    {
                        var methodSymbolInfo = semanticModel.GetDeclaredSymbol(containingMethod);
                        if (methodSymbolInfo?.ContainingType != null)
                        {
                            var usage = $"{methodSymbolInfo.ContainingType.Name}.{containingMethod.Identifier.Text}";
                            if (!extension.UsedBy.Contains(usage))
                            {
                                extension.UsedBy.Add(usage);
                            }
                        }
                    }
                }
            }
        }
    }

    private string GetNamespace(ClassDeclarationSyntax classDeclaration)
    {
        var namespaceDeclaration = classDeclaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return namespaceDeclaration?.Name.ToString() ?? string.Empty;
    }
}
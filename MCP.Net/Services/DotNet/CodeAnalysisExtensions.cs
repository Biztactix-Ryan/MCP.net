using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class CodeAnalysisExtensions
{
    public class CallGraph
    {
        public string MethodId { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public List<CallNode> Calls { get; set; } = new();
        public Dictionary<string, string> Variables { get; set; } = new();
    }

    public class CallNode
    {
        public string TargetMethod { get; set; }
        public string TargetClass { get; set; }
        public int LineNumber { get; set; }
        public List<string> Parameters { get; set; } = new();
        public List<CallNode> SubsequentCalls { get; set; } = new();
        public bool IsAsync { get; set; }
        public string ReturnType { get; set; }
    }

    public class DependencyGraph
    {
        public string ClassId { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public List<string> DependedOnBy { get; set; } = new();
        public List<ServiceDependency> ServiceDependencies { get; set; } = new();
    }

    public class ServiceDependency
    {
        public string ServiceType { get; set; }
        public string ImplementationType { get; set; }
        public string Lifetime { get; set; } // Singleton, Scoped, Transient
        public List<string> UsedByMethods { get; set; } = new();
    }

    public async Task<CallGraph> AnalyzeMethodFlow(
         SemanticModel semanticModel,
         MethodDeclarationSyntax methodSyntax,
         CancellationToken cancellationToken = default)
    {
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax);
        var callGraph = new CallGraph
        {
            MethodId = methodSymbol.ToDisplayString(),
            ClassName = methodSymbol.ContainingType.Name,
            MethodName = methodSymbol.Name
        };

        // Analyze method body for method calls
        var methodCalls = methodSyntax.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var call in methodCalls)
        {
            var callSymbol = semanticModel.GetSymbolInfo(call).Symbol as IMethodSymbol;
            if (callSymbol != null)
            {
                var node = new CallNode
                {
                    TargetMethod = callSymbol.Name,
                    TargetClass = callSymbol.ContainingType.Name,
                    LineNumber = call.GetLocation().GetLineSpan().StartLinePosition.Line,
                    IsAsync = callSymbol.IsAsync,
                    ReturnType = callSymbol.ReturnType.ToDisplayString()
                };

                // Analyze parameters
                var paramList = call.ArgumentList.Arguments
                    .Select(arg => semanticModel.GetSymbolInfo(arg.Expression).Symbol?.ToDisplayString())
                    .Where(s => s != null)
                    .ToList();
                node.Parameters.AddRange(paramList);

                callGraph.Calls.Add(node);
            }
        }

        // Track variable declarations and usage
        var variables = methodSyntax.DescendantNodes()
            .OfType<VariableDeclarationSyntax>();

        foreach (var variable in variables)
        {
            foreach (var declarator in variable.Variables)
            {
                var variableSymbol = semanticModel.GetDeclaredSymbol(declarator);
                if (variableSymbol != null)
                {
                    callGraph.Variables[declarator.Identifier.Text] =
                        variable.Type.ToFullString();
                }
            }
        }

        return callGraph;
    }

    public async Task<DependencyGraph> AnalyzeClassDependencies(
       SemanticModel semanticModel,
       ClassDeclarationSyntax classSyntax)
    {
        var classSymbol = semanticModel.GetDeclaredSymbol(classSyntax) as INamedTypeSymbol;
        if (classSymbol == null) return new DependencyGraph();

        var graph = new DependencyGraph
        {
            ClassId = classSymbol.ToDisplayString(),
            Dependencies = new List<string>(),
            DependedOnBy = new List<string>(),
            ServiceDependencies = new List<ServiceDependency>()
        };

        // Analyze constructor dependencies
        var constructors = classSyntax.DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>();

        foreach (var ctor in constructors)
        {
            foreach (var parameter in ctor.ParameterList.Parameters)
            {
                var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter) as IParameterSymbol;
                if (parameterSymbol?.Type != null)
                {
                    var serviceType = parameterSymbol.Type.ToDisplayString();
                    graph.ServiceDependencies.Add(new ServiceDependency
                    {
                        ServiceType = serviceType,
                        ImplementationType = await GetImplementationType(semanticModel, parameter.Type),
                        Lifetime = GetServiceLifetime(parameterSymbol.Type),
                        UsedByMethods = new List<string>()
                    });
                }
            }
        }

        // Find classes that depend on this class using compilation
        var compilation = semanticModel.Compilation;
        await FindDependentTypes(compilation, classSymbol, graph);

        return graph;
    }

    private async Task FindDependentTypes(Compilation compilation, INamedTypeSymbol classSymbol, DependencyGraph graph)
    {
        // Get all source files in the compilation
        var syntaxTrees = compilation.SyntaxTrees;

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = await tree.GetRootAsync();

            // Find all class declarations
            var classDeclarations = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>();

            foreach (var classDeclaration in classDeclarations)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (typeSymbol != null && await HasDependencyOn(typeSymbol, classSymbol))
                {
                    graph.DependedOnBy.Add(typeSymbol.ToDisplayString());
                }
            }
        }
    }

    private string GetServiceLifetime(ITypeSymbol type)
    {
        // Check for common DI lifetime attributes
        var attributes = type.GetAttributes();
        if (attributes.Any(a => a.AttributeClass?.Name == "SingletonAttribute")) return "Singleton";
        if (attributes.Any(a => a.AttributeClass?.Name == "ScopedAttribute")) return "Scoped";
        if (attributes.Any(a => a.AttributeClass?.Name == "TransientAttribute")) return "Transient";
        return "Unknown";
    }

    private async Task<string> GetImplementationType(SemanticModel semanticModel, TypeSyntax type)
    {
        var typeInfo = semanticModel.GetTypeInfo(type);
        var typeSymbol = typeInfo.Type;
        if (typeSymbol?.TypeKind == TypeKind.Interface)
        {
            var compilation = semanticModel.Compilation;
            var implementations = new List<string>();

            // Search through all syntax trees for implementations
            foreach (var tree in compilation.SyntaxTrees)
            {
                var treeModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();

                var classDeclarations = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>();

                foreach (var classDeclaration in classDeclarations)
                {
                    var declaredSymbol = treeModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                    if (declaredSymbol != null &&
                        declaredSymbol.AllInterfaces.Contains(typeSymbol))
                    {
                        implementations.Add(declaredSymbol.Name);
                    }
                }
            }

            return string.Join(", ", implementations);
        }
        return typeSymbol?.Name ?? "Unknown";
    }


    private async Task<bool> HasDependencyOn(INamedTypeSymbol type, INamedTypeSymbol targetType)
    {
        if (type == null || targetType == null) return false;

        // Check if it's the same type
        if (SymbolEqualityComparer.Default.Equals(type, targetType))
            return false; // Skip self-references

        // Check constructor parameters
        foreach (var ctor in type.Constructors)
        {
            if (ctor.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, targetType)))
                return true;
        }

        // Check field and property types
        var members = type.GetMembers()
            .Where(m => m.Kind == SymbolKind.Field || m.Kind == SymbolKind.Property);

        foreach (var member in members)
        {
            ITypeSymbol memberType = null;
            if (member is IFieldSymbol field)
                memberType = field.Type;
            else if (member is IPropertySymbol property)
                memberType = property.Type;

            if (memberType != null && SymbolEqualityComparer.Default.Equals(memberType, targetType))
                return true;
        }

        // Check base type and interfaces
        if (SymbolEqualityComparer.Default.Equals(type.BaseType, targetType))
            return true;

        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, targetType));
    }
}
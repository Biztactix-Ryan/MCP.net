using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DotNetMCP.Services.DotNet;

public class CodeStructureAnalyzer : ICodeStructureAnalyzer
{

    public CodeStructureAnalyzer()
    {
       
    }

    public async Task<ClassStructure> GetClassStructure(string className, Solution solution)
    {
        try
        {
            var classSymbol = await FindClassSymbol(className, solution);
            if (classSymbol == null)
            {
                throw new ArgumentException($"Class '{className}' not found");
            }

            return new ClassStructure
            {
                ClassName = classSymbol.Name,
                Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
                FilePath = classSymbol.Locations.FirstOrDefault()?.GetLineSpan().Path,
                BaseTypes = GetBaseTypes(classSymbol),
                Interfaces = GetImplementedInterfaces(classSymbol),
                Methods = await GetMethodsInfo(classSymbol),
                Properties = GetPropertiesInfo(classSymbol),
                Fields = GetFieldsInfo(classSymbol),
                Attributes = GetAttributesInfo(classSymbol),
                Accessibility = GetAccessibilityLevel(classSymbol.DeclaredAccessibility),
                IsStatic = classSymbol.IsStatic,
                IsAbstract = classSymbol.IsAbstract,
                Documentation = classSymbol.GetDocumentationCommentXml()
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, $"Error analyzing class structure for {className}");
            throw;
        }
    }

    public async Task<MethodStructure> GetMethodStructure(string className, string methodName, Solution solution)
    {
        try
        {
            var classSymbol = await FindClassSymbol(className, solution);
            if (classSymbol == null)
            {
                throw new ArgumentException($"Class '{className}' not found");
            }

            var methodSymbol = classSymbol.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault();

            if (methodSymbol == null)
            {
                throw new ArgumentException($"Method '{methodName}' not found in class '{className}'");
            }

            return new MethodStructure
            {
                Name = methodSymbol.Name,
                ReturnType = methodSymbol.ReturnType.ToDisplayString(),
                Parameters = GetParametersInfo(methodSymbol),
                Accessibility = GetAccessibilityLevel(methodSymbol.DeclaredAccessibility),
                IsStatic = methodSymbol.IsStatic,
                IsAsync = methodSymbol.IsAsync,
                IsVirtual = methodSymbol.IsVirtual,
                IsOverride = methodSymbol.IsOverride,
                Modifiers = GetMethodModifiers(methodSymbol),
                Documentation = methodSymbol.GetDocumentationCommentXml(),
                TypeParameters = methodSymbol.TypeParameters.Select(tp => tp.Name),
                ConstraintClauses = GetTypeParameterConstraints(methodSymbol)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, $"Error analyzing method structure for {className}.{methodName}");
            throw;
        }
    }

    public async Task<IEnumerable<string>> FindClassUsages(string className, Solution solution)
    {
        try
        {
            var classSymbol = await FindClassSymbol(className, solution);
            if (classSymbol == null)
            {
                throw new ArgumentException($"Class '{className}' not found");
            }

            var references = await SymbolFinder.FindReferencesAsync(classSymbol, solution);
            return references
                .SelectMany(r => r.Locations)
                .Select(location => location.Document.FilePath)
                .Distinct();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, $"Error finding usages for class {className}");
            throw;
        }
    }

    public async Task<IEnumerable<MethodUsage>> FindMethodUsages(string className, string methodName, Solution solution)
    {
        try
        {
            var classSymbol = await FindClassSymbol(className, solution);
            if (classSymbol == null)
            {
                throw new ArgumentException($"Class '{className}' not found");
            }

            var methodSymbol = classSymbol.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault();

            if (methodSymbol == null)
            {
                throw new ArgumentException($"Method '{methodName}' not found in class '{className}'");
            }

            var references = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);
            var usages = new List<MethodUsage>();

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var syntaxTree = await location.Document.GetSyntaxTreeAsync();
                    var semanticModel = await location.Document.GetSemanticModelAsync();
                    var node = await syntaxTree.GetRootAsync();
                    var invocation = node.FindNode(location.Location.SourceSpan)
                        .AncestorsAndSelf()
                        .OfType<InvocationExpressionSyntax>()
                        .FirstOrDefault();

                    if (invocation != null)
                    {
                        var containingMethod = invocation.Ancestors()
                            .OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault();
                        var containingClass = invocation.Ancestors()
                            .OfType<ClassDeclarationSyntax>()
                            .FirstOrDefault();

                        usages.Add(new MethodUsage
                        {
                            CallingClass = containingClass?.Identifier.Text ?? "Unknown",
                            CallingMethod = containingMethod?.Identifier.Text ?? "Unknown",
                            FilePath = location.Document.FilePath,
                            LineNumber = location.Location.GetLineSpan().StartLinePosition.Line + 1,
                            Arguments = GetInvocationArguments(invocation, semanticModel)
                        });
                    }
                }
            }

            return usages;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, $"Error finding usages for method {className}.{methodName}");
            throw;
        }
    }

    public async Task<ClassHierarchy> GetClassHierarchy(string className, Solution solution)
    {
        try
        {
            var classSymbol = await FindClassSymbol(className, solution);
            if (classSymbol == null)
            {
                throw new ArgumentException($"Class '{className}' not found");
            }

            var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(classSymbol, solution);

            return new ClassHierarchy
            {
                ClassName = classSymbol.Name,
                BaseClasses = GetBaseClassHierarchy(classSymbol),
                DerivedClasses = derivedTypes.Select(t => t.ToDisplayString()),
                ImplementedInterfaces = GetDetailedInterfaceImplementations(classSymbol)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, $"Error analyzing class hierarchy for {className}");
            throw;
        }
    }

    private async Task<INamedTypeSymbol> FindClassSymbol(string className, Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            var classSymbol = compilation.GetTypeByMetadataName(className);
            if (classSymbol != null)
            {
                return classSymbol;
            }
        }
        return null;
    }

    private IEnumerable<string> GetBaseTypes(INamedTypeSymbol classSymbol)
    {
        var baseTypes = new List<string>();
        var currentType = classSymbol.BaseType;
        while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(currentType.ToDisplayString());
            currentType = currentType.BaseType;
        }
        return baseTypes;
    }

    private IEnumerable<string> GetImplementedInterfaces(INamedTypeSymbol classSymbol)
    {
        return classSymbol.AllInterfaces.Select(i => i.ToDisplayString());
    }

    private async Task<IEnumerable<MethodInfo>> GetMethodsInfo(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => !m.IsImplicitlyDeclared)
            .Select(m => new MethodInfo
            {
                Name = m.Name,
                ReturnType = m.ReturnType.ToDisplayString(),
                Parameters = GetParametersInfo(m),
                Accessibility = GetAccessibilityLevel(m.DeclaredAccessibility)
            });
    }

    private IEnumerable<PropertyInfo> GetPropertiesInfo(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(p => new PropertyInfo
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(),
                Accessibility = GetAccessibilityLevel(p.DeclaredAccessibility),
                HasGetter = p.GetMethod != null,
                HasSetter = p.SetMethod != null
            });
    }

    private IEnumerable<FieldInfo> GetFieldsInfo(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsImplicitlyDeclared)
            .Select(f => new FieldInfo
            {
                Name = f.Name,
                Type = f.Type.ToDisplayString(),
                Accessibility = GetAccessibilityLevel(f.DeclaredAccessibility),
                IsReadOnly = f.IsReadOnly
            });
    }

    private IEnumerable<AttributeInfo> GetAttributesInfo(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes().Select(a => new AttributeInfo
        {
            Name = a.AttributeClass.Name,
            Arguments = a.ConstructorArguments
                .Select((arg, index) => new KeyValuePair<string, TypedConstant>($"ctor{index}", arg))
                .Concat(a.NamedArguments.Select(na => new KeyValuePair<string, TypedConstant>(na.Key, na.Value)))
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToCSharpString()
                )
        });
    }

    private IEnumerable<ParameterInfo> GetParametersInfo(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Parameters.Select(p => new ParameterInfo
        {
            Name = p.Name,
            Type = p.Type.ToDisplayString(),
            HasDefaultValue = p.HasExplicitDefaultValue,
            DefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
        });
    }

    private IEnumerable<string> GetMethodModifiers(IMethodSymbol methodSymbol)
    {
        var modifiers = new List<string>();

        if (methodSymbol.IsAbstract) modifiers.Add("abstract");
        if (methodSymbol.IsVirtual) modifiers.Add("virtual");
        if (methodSymbol.IsOverride) modifiers.Add("override");
        if (methodSymbol.IsStatic) modifiers.Add("static");
        if (methodSymbol.IsAsync) modifiers.Add("async");
        if (methodSymbol.IsExtern) modifiers.Add("extern");
        if (methodSymbol.IsSealed) modifiers.Add("sealed");

        return modifiers;
    }

    private IEnumerable<string> GetTypeParameterConstraints(IMethodSymbol methodSymbol)
    {
        var constraints = new List<string>();

        foreach (var typeParameter in methodSymbol.TypeParameters)
        {
            var constraintClauses = new List<string>();

            if (typeParameter.HasReferenceTypeConstraint)
            {
                constraintClauses.Add("class");
            }
            else if (typeParameter.HasValueTypeConstraint)
            {
                constraintClauses.Add("struct");
            }

            if (typeParameter.HasConstructorConstraint)
            {
                constraintClauses.Add("new()");
            }

            var typeConstraints = typeParameter.ConstraintTypes.Select(t => t.ToDisplayString());
            constraintClauses.AddRange(typeConstraints);

            if (constraintClauses.Any())
            {
                constraints.Add($"where {typeParameter.Name} : {string.Join(", ", constraintClauses)}");
            }
        }

        return constraints;
    }

    private IEnumerable<string> GetInvocationArguments(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        return invocation.ArgumentList.Arguments
            .Select(arg =>
            {
                var symbol = semanticModel.GetSymbolInfo(arg.Expression).Symbol;
                return symbol?.ToDisplayString() ?? arg.ToString();
            });
    }

    private IEnumerable<string> GetBaseClassHierarchy(INamedTypeSymbol classSymbol)
    {
        var hierarchy = new List<string>();
        var currentType = classSymbol.BaseType;

        while (currentType != null && !currentType.IsObjectType())
        {
            hierarchy.Add(currentType.ToDisplayString());
            currentType = currentType.BaseType;
        }

        return hierarchy;
    }

    private IEnumerable<InterfaceImplementation> GetDetailedInterfaceImplementations(INamedTypeSymbol classSymbol)
    {
        return classSymbol.AllInterfaces.Select(interfaceType =>
        {
            var interfaceImpl = new InterfaceImplementation
            {
                InterfaceName = interfaceType.ToDisplayString()
            };

            var implementedMembers = new List<string>();
            foreach (var member in interfaceType.GetMembers())
            {
                var memberImpl = classSymbol.FindImplementationForInterfaceMember(member);
                if (memberImpl != null)
                {
                    implementedMembers.Add(member.Name);
                }
            }

            interfaceImpl.ImplementedMembers = implementedMembers;
            return interfaceImpl;
        });
    }

    private AccessibilityLevel GetAccessibilityLevel(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => AccessibilityLevel.Public,
            Accessibility.Private => AccessibilityLevel.Private,
            Accessibility.Protected => AccessibilityLevel.Protected,
            Accessibility.Internal => AccessibilityLevel.Internal,
            Accessibility.ProtectedOrInternal => AccessibilityLevel.ProtectedInternal,
            Accessibility.ProtectedAndInternal => AccessibilityLevel.PrivateProtected,
            _ => AccessibilityLevel.Private
        };
    }
}

public static class RoslynExtensions
{
    public static bool IsObjectType(this INamedTypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_Object;
    }
}

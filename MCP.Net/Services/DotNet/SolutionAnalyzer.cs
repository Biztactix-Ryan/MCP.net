using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetMCP.Services.DotNet;
public class SolutionAnalyzer
{
    public async Task<SolutionAnalysis> AnalyzeSolution(string solutionPath)
    {
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        var analysis = new SolutionAnalysis
        {
            SolutionPath = solutionPath,
            Projects = new List<ProjectAnalysis>()
        };

        foreach (var project in solution.Projects)
        {
            var projectAnalysis = await AnalyzeProject(project);
            analysis.Projects.Add(projectAnalysis);
        }

        return analysis;
    }

    private async Task<ProjectAnalysis> AnalyzeProject(Project project)
    {
        var compilation = await project.GetCompilationAsync();
        var projectAnalysis = new ProjectAnalysis
        {
            Name = project.Name,
            FilePath = project.FilePath,
            Classes = new List<ClassAnalysis>()
        };

        foreach (var document in project.Documents)
        {
            var syntaxTree = await document.GetSyntaxTreeAsync();
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await document.GetSyntaxRootAsync();

            var classes = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();

            foreach (var classNode in classes)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classNode) as INamedTypeSymbol;
                if (classSymbol != null)
                {
                    projectAnalysis.Classes.Add(AnalyzeClass(classSymbol, classNode, semanticModel));
                }
            }
        }

        return projectAnalysis;
    }

    private ClassAnalysis AnalyzeClass(
        INamedTypeSymbol classSymbol,
        Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classNode,
        SemanticModel semanticModel)
    {
        var classAnalysis = new ClassAnalysis
        {
            Name = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            IsStatic = classSymbol.IsStatic,
            IsAbstract = classSymbol.IsAbstract,
            Accessibility = classSymbol.DeclaredAccessibility.ToString(),
            BaseType = classSymbol.BaseType?.ToDisplayString(),
            Interfaces = classSymbol.Interfaces.Select(i => i.ToDisplayString()).ToList(),
            Methods = new List<MethodAnalysis>(),
            Properties = new List<PropertyAnalysis>(),
            Fields = new List<FieldAnalysis>()
        };

        // Analyze methods
        foreach (var member in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (!member.IsImplicitlyDeclared)
            {
                classAnalysis.Methods.Add(new MethodAnalysis
                {
                    Name = member.Name,
                    ReturnType = member.ReturnType.ToDisplayString(),
                    IsStatic = member.IsStatic,
                    IsAsync = member.IsAsync,
                    IsVirtual = member.IsVirtual,
                    IsOverride = member.IsOverride,
                    Accessibility = member.DeclaredAccessibility.ToString(),
                    Parameters = member.Parameters.Select(p => new ParameterAnalysis
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(),
                        HasDefaultValue = p.HasExplicitDefaultValue
                    }).ToList()
                });
            }
        }

        // Analyze properties
        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            classAnalysis.Properties.Add(new PropertyAnalysis
            {
                Name = member.Name,
                Type = member.Type.ToDisplayString(),
                HasGetter = member.GetMethod != null,
                HasSetter = member.SetMethod != null,
                Accessibility = member.DeclaredAccessibility.ToString()
            });
        }

        // Analyze fields
        foreach (var member in classSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.IsImplicitlyDeclared)
            {
                classAnalysis.Fields.Add(new FieldAnalysis
                {
                    Name = member.Name,
                    Type = member.Type.ToDisplayString(),
                    IsReadOnly = member.IsReadOnly,
                    Accessibility = member.DeclaredAccessibility.ToString()
                });
            }
        }

        return classAnalysis;
    }
}

public class SolutionAnalysis
{
    public string SolutionPath { get; set; }
    public List<ProjectAnalysis> Projects { get; set; }
}

public class ProjectAnalysis
{
    public string Name { get; set; }
    public string FilePath { get; set; }
    public List<ClassAnalysis> Classes { get; set; }
}

public class ClassAnalysis
{
    public string Name { get; set; }
    public string Namespace { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public string Accessibility { get; set; }
    public string BaseType { get; set; }
    public List<string> Interfaces { get; set; }
    public List<MethodAnalysis> Methods { get; set; }
    public List<PropertyAnalysis> Properties { get; set; }
    public List<FieldAnalysis> Fields { get; set; }
}

public class MethodAnalysis
{
    public string Name { get; set; }
    public string ReturnType { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public string Accessibility { get; set; }
    public List<ParameterAnalysis> Parameters { get; set; }
}

public class PropertyAnalysis
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
    public string Accessibility { get; set; }
}

public class FieldAnalysis
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsReadOnly { get; set; }
    public string Accessibility { get; set; }
}

public class ParameterAnalysis
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool HasDefaultValue { get; set; }
}
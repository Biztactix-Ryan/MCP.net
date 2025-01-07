using Microsoft.CodeAnalysis;

namespace DotNetMCP.Services.DotNet;

public interface ICodeStructureAnalyzer
{
    Task<ClassStructure> GetClassStructure(string className, Solution solution);
    Task<MethodStructure> GetMethodStructure(string className, string methodName, Solution solution);
    Task<IEnumerable<string>> FindClassUsages(string className, Solution solution);
    Task<IEnumerable<MethodUsage>> FindMethodUsages(string className, string methodName, Solution solution);
    Task<ClassHierarchy> GetClassHierarchy(string className, Solution solution);
}

public class ClassStructure
{
    public string ClassName { get; set; }
    public string Namespace { get; set; }
    public string FilePath { get; set; }
    public IEnumerable<string> BaseTypes { get; set; }
    public IEnumerable<string> Interfaces { get; set; }
    public IEnumerable<MethodInfo> Methods { get; set; }
    public IEnumerable<PropertyInfo> Properties { get; set; }
    public IEnumerable<FieldInfo> Fields { get; set; }
    public IEnumerable<AttributeInfo> Attributes { get; set; }
    public AccessibilityLevel Accessibility { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public string Documentation { get; set; }
}

public class MethodStructure
{
    public string Name { get; set; }
    public string ReturnType { get; set; }
    public IEnumerable<ParameterInfo> Parameters { get; set; }
    public AccessibilityLevel Accessibility { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public IEnumerable<string> Modifiers { get; set; }
    public string Documentation { get; set; }
    public IEnumerable<string> TypeParameters { get; set; }
    public IEnumerable<string> ConstraintClauses { get; set; }
}

public class MethodUsage
{
    public string CallingClass { get; set; }
    public string CallingMethod { get; set; }
    public string FilePath { get; set; }
    public int LineNumber { get; set; }
    public IEnumerable<string> Arguments { get; set; }
}

public class ClassHierarchy
{
    public string ClassName { get; set; }
    public IEnumerable<string> BaseClasses { get; set; }
    public IEnumerable<string> DerivedClasses { get; set; }
    public IEnumerable<InterfaceImplementation> ImplementedInterfaces { get; set; }
}

public class MethodInfo
{
    public string Name { get; set; }
    public string ReturnType { get; set; }
    public IEnumerable<ParameterInfo> Parameters { get; set; }
    public AccessibilityLevel Accessibility { get; set; }
}

public class PropertyInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public AccessibilityLevel Accessibility { get; set; }
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
}

public class FieldInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public AccessibilityLevel Accessibility { get; set; }
    public bool IsReadOnly { get; set; }
}

public class ParameterInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool HasDefaultValue { get; set; }
    public string DefaultValue { get; set; }
}

public class AttributeInfo
{
    public string Name { get; set; }
    public IDictionary<string, string> Arguments { get; set; }
}

public class InterfaceImplementation
{
    public string InterfaceName { get; set; }
    public IEnumerable<string> ImplementedMembers { get; set; }
}

public enum AccessibilityLevel
{
    Public,
    Private,
    Protected,
    Internal,
    ProtectedInternal,
    PrivateProtected
}

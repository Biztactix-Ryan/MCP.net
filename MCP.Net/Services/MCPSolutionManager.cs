using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Threading;
using DotNetMCP.Services.DotNet;
using Microsoft.CodeAnalysis.MSBuild;
using System.Diagnostics;

public class MCPSolutionManager : IDisposable
{
    public class SolutionContext : IDisposable
    {
        public Solution Solution { get; set; }
        public DateTime LastAccessed { get; set; }
        public string SolutionPath { get; set; }
        public ICodeStructureAnalyzer Analyzer { get; set; }
        public IExtensionMethodAnalyzer ExtensionAnalyzer { get; set; }
        public Dictionary<string, object> AnalysisCache { get; set; } = new();
        public MSBuildWorkspace Workspace { get; set; }

        public void Dispose()
        {
            Workspace?.Dispose();
            if (Analyzer is IDisposable disposableAnalyzer)
            {
                disposableAnalyzer.Dispose();
            }
            if (ExtensionAnalyzer is IDisposable disposableExtensionAnalyzer)
            {
                disposableExtensionAnalyzer.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }

    private readonly Dictionary<string, SolutionContext> _activeSolutions = new();

    private readonly SemaphoreSlim _lock = new(1, 1);

    private bool _disposed;

    public MCPSolutionManager(        )
    {

    }

    public async Task<string> InitializeSolution(string solutionPath)
    {
        if (string.IsNullOrEmpty(solutionPath))
        {
            throw new ArgumentException("Solution path cannot be empty", nameof(solutionPath));
        }

        await _lock.WaitAsync();
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            //Debug.WriteLine($"Initializing solution: {solutionPath} with session {sessionId}");

            var workspace = MSBuildWorkspace.Create();
            workspace.WorkspaceFailed += (sender, args) =>
            {
                Debug.WriteLine($"Workspace error: {args.Diagnostic.Message}");
            };

            var solution = await workspace.OpenSolutionAsync(solutionPath);

            var context = new SolutionContext
            {
                Solution = solution,
                LastAccessed = DateTime.UtcNow,
                SolutionPath = solutionPath,
                Workspace = workspace,
                Analyzer = new CodeStructureAnalyzer(),
                ExtensionAnalyzer = new ExtensionMethodAnalyzer()
            };

            // Initialize analyzers
            await context.ExtensionAnalyzer.AnalyzeExtensionMethods(solution);

            _activeSolutions[sessionId] = context;

            return sessionId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex, $"Failed to initialize solution: {solutionPath}");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SolutionContext> GetContext(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));
        }

        await _lock.WaitAsync();
        try
        {
            if (!_activeSolutions.TryGetValue(sessionId, out var context))
            {
                throw new KeyNotFoundException($"Session {sessionId} not found. Solution needs to be initialized first.");
            }

            context.LastAccessed = DateTime.UtcNow;
            return context;
        }
        finally
        {
            _lock.Release();
        }
    }



    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {

            _lock.Dispose();

            foreach (var context in _activeSolutions.Values)
            {
                context.Dispose();
            }
            _activeSolutions.Clear();
        }

        _disposed = true;
    }
}
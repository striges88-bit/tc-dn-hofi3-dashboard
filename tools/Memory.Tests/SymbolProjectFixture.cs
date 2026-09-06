using CryptoIndicatorApp.Memory;
namespace CryptoIndicatorApp.Memory.Tests;

internal sealed class SymbolProjectFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "memory-symbol-identity", Guid.NewGuid().ToString("N"));

    public SymbolProjectFixture() => Directory.CreateDirectory(Root);

    public void Write(string path, string text)
    {
        var target = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text);
    }

    public Task<ProjectMemorySnapshot> Snapshot() => new ProjectMemoryIndexer(Root).BuildSnapshotAsync();

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

namespace XnaFiddle.Tests;

public class LibraryRegistryTests
{
    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void Register_AddsPluginToRegistry()
    {
        var registry = new LibraryRegistry();
        var plugin = new FakePlugin("TestLib");

        registry.Register(plugin);

        Assert.Contains(plugin, registry.Plugins);
    }

    [Fact]
    public void Register_MultiplePlugins_AllPresent()
    {
        var registry = new LibraryRegistry();
        var a = new FakePlugin("A");
        var b = new FakePlugin("B");

        registry.Register(a);
        registry.Register(b);

        Assert.Equal(2, registry.Plugins.Count);
        Assert.Contains(a, registry.Plugins);
        Assert.Contains(b, registry.Plugins);
    }

    // ── RunAllCleanups ───────────────────────────────────────────────────────

    [Fact]
    public void RunAllCleanups_CallsEachPlugin()
    {
        var registry = new LibraryRegistry();
        var a = new FakePlugin("A");
        var b = new FakePlugin("B");
        registry.Register(a);
        registry.Register(b);

        registry.RunAllCleanups();

        Assert.Equal(1, a.CleanUpCallCount);
        Assert.Equal(1, b.CleanUpCallCount);
    }

    [Fact]
    public void RunAllCleanups_EmptyRegistry_IsNoOp()
    {
        var registry = new LibraryRegistry();

        // Should not throw
        registry.RunAllCleanups();
    }

    [Fact]
    public void RunAllCleanups_CalledTwice_CallsEachPluginTwice()
    {
        var registry = new LibraryRegistry();
        var plugin = new FakePlugin("A");
        registry.Register(plugin);

        registry.RunAllCleanups();
        registry.RunAllCleanups();

        Assert.Equal(2, plugin.CleanUpCallCount);
    }

    [Fact]
    public void RunAllCleanups_OnePluginThrows_OtherStillCalled()
    {
        var registry = new LibraryRegistry();
        var thrower = new FakePlugin("Thrower", throwOnCleanUp: true);
        var healthy = new FakePlugin("Healthy");
        registry.Register(thrower);
        registry.Register(healthy);

        // Should not throw — failures are caught per-plugin
        registry.RunAllCleanups();

        Assert.Equal(1, thrower.CleanUpCallCount);
        Assert.Equal(1, healthy.CleanUpCallCount);
    }

    [Fact]
    public void RunAllCleanups_AllPluginsThrow_DoesNotThrow()
    {
        var registry = new LibraryRegistry();
        registry.Register(new FakePlugin("A", throwOnCleanUp: true));
        registry.Register(new FakePlugin("B", throwOnCleanUp: true));

        // Should not throw even when every plugin fails
        registry.RunAllCleanups();
    }

    // ── Plugin metadata ──────────────────────────────────────────────────────

    [Fact]
    public void Plugins_ReturnsReadOnlyView()
    {
        var registry = new LibraryRegistry();
        registry.Register(new FakePlugin("A"));

        var plugins = registry.Plugins;

        // Should be read-only — callers can't mutate the registry
        Assert.IsAssignableFrom<IReadOnlyList<ILibraryPlugin>>(plugins);
    }

    // ── Test helper ──────────────────────────────────────────────────────────

    private class FakePlugin : ILibraryPlugin
    {
        private readonly bool _throwOnCleanUp;

        public FakePlugin(string name, bool throwOnCleanUp = false)
        {
            Name = name;
            _throwOnCleanUp = throwOnCleanUp;
        }

        public string Name { get; }
        public string[] RequiredAssemblies => [];
        public (string Label, string[] AssemblyNames) VersionInfo => (Name, []);
        public int CleanUpCallCount { get; private set; }

        public void CleanUp()
        {
            CleanUpCallCount++;
            if (_throwOnCleanUp)
                throw new InvalidOperationException($"{Name} cleanup failed");
        }
    }
}

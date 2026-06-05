using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using XnaFiddle;
using XnaFiddle.Plugins;

namespace XnaFiddle.Tests;

public class CompilationServiceTests
{
    [Fact]
    public async Task GetMetadataReferencesAsync_LoadsFlatRedBallAnimationChainAssembly()
    {
        var requestedAssemblies = new List<string>();
        var registry = new LibraryRegistry();
        registry.Register(new FlatRedBallAnimationChainPlugin());

        var service = new CompilationService(new TestNavigationManager(), registry);

        var (references, failedAssemblies, versionInfo) = await service.GetMetadataReferencesAsync(
            resolveMetadataReferenceAsync: (assemblyName, _) =>
            {
                requestedAssemblies.Add(assemblyName);
                MetadataReference reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
                return Task.FromResult(reference);
            });

        Assert.Contains("AnimationChain.KNI", requestedAssemblies);
        Assert.DoesNotContain("AnimationChain.KNI", failedAssemblies);
        Assert.NotEmpty(references);
        Assert.Contains("FlatRedBall.AnimationChain", versionInfo);
    }

    [Fact]
    public async Task GetMetadataReferencesAsync_RequestsAnimationChainReferencedAssemblies()
    {
        var requestedAssemblies = new List<string>();
        var registry = new LibraryRegistry();
        registry.Register(new FlatRedBallAnimationChainPlugin());

        Assembly animationChainAssembly = Assembly.Load("AnimationChain.KNI");
        var referencedAssemblyNames = animationChainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToArray();

        Assert.NotEmpty(referencedAssemblyNames);

        var service = new CompilationService(new TestNavigationManager(), registry);

        await service.GetMetadataReferencesAsync(
            resolveMetadataReferenceAsync: (assemblyName, _) =>
            {
                requestedAssemblies.Add(assemblyName);
                MetadataReference reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
                return Task.FromResult(reference);
            });

        Assert.Contains("AnimationChain.KNI", requestedAssemblies);
        foreach (string referencedAssemblyName in referencedAssemblyNames)
            Assert.Contains(referencedAssemblyName, requestedAssemblies);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}

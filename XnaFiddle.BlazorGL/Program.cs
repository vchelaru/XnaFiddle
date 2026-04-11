using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using XnaFiddle.Plugins;

namespace XnaFiddle
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            // Touch one type from each optional package so Blazor WASM loads their
            // assemblies eagerly. Without this, the assemblies stay unloaded until a
            // game actually uses them, causing Roslyn to fail when compiling shared
            // #code= links that reference Gum, Shapes, or Extended.
            _ = typeof(MonoGameGum.GumService);          // Gum.KNI
            _ = typeof(Apos.Shapes.ShapeBatch);           // Apos.Shapes.KNI
            _ = typeof(MonoGame.Extended.OrthographicCamera); // KNI.Extended

            // Force-load StbTrueType rasterizer so KernSmith can discover it at runtime.
            // Without this, Blazor WASM won't load the assembly and the backend stays unregistered.
            RuntimeHelpers.RunClassConstructor(
                typeof(KernSmith.Rasterizers.StbTrueType.StbTrueTypeRasterizer).TypeHandle);

            // Register all library plugins. Each plugin declares its assembly names,
            // version info, and cleanup logic — adding a new library only requires
            // writing a plugin class and registering it here.
            var libraryRegistry = new LibraryRegistry();
            libraryRegistry.Register(new GameWindowPlugin());
            libraryRegistry.Register(new GumPlugin());
            libraryRegistry.Register(new MlemPlugin());
            libraryRegistry.Register(new AposShapesPlugin());
            libraryRegistry.Register(new FontStashSharpPlugin());
            libraryRegistry.Register(new MonoGameExtendedPlugin());
            libraryRegistry.Register(new AetherPhysicsPlugin());
            libraryRegistry.Register(new KernSmithPlugin());

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.Services.AddScoped(sp => new HttpClient()
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });
            builder.Services.AddSingleton(libraryRegistry);
            builder.Services.AddSingleton<CompilationService>();

            await builder.Build().RunAsync();
        }
    }
}

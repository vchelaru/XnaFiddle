using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace XnaFiddle
{
    internal class Program
    {
        public static NavigationManager NavigationManager { get; set; }

        private static async Task Main(string[] args)
        {
            // Touch one type from each optional package so Blazor WASM loads their
            // assemblies eagerly. Without this, the assemblies stay unloaded until a
            // game actually uses them, causing Roslyn to fail when compiling shared
            // #code= links that reference Gum, Shapes, or Extended.
            _ = typeof(MonoGameGum.GumService);          // Gum.KNI
            _ = typeof(Apos.Shapes.ShapeBatch);           // Apos.Shapes.KNI
            _ = typeof(MonoGame.Extended.OrthographicCamera); // KNI.Extended

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.Services.AddScoped(sp => new HttpClient()
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });

            NavigationManager = builder.Services.BuildServiceProvider().GetRequiredService<NavigationManager>();

            await builder.Build().RunAsync();
        }
    }
}

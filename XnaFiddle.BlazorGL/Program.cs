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

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TranslationService.Client;
using TranslationService.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// La Api hospeda este SPA, así que BaseAddress es el propio origen: sin CORS y sin ninguna
// URL de backend que configurar por entorno.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<ITranslationApiClient, TranslationApiClient>();
builder.Services.AddSingleton(new PollingOptions());

await builder.Build().RunAsync();

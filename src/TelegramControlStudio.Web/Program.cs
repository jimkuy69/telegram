using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Web.Components;
using TelegramControlStudio.Web.Services;

var studio = await StudioHost.StartAsync();

var contentRoot = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
    ? AppContext.BaseDirectory
    : Directory.GetCurrentDirectory();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
});
builder.Services.AddSingleton(studio);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<WorkspaceState>();

var app = builder.Build();
app.Lifetime.ApplicationStopped.Register(() => studio.DisposeAsync().AsTask().GetAwaiter().GetResult());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

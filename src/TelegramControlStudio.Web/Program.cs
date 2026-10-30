using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
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
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "tcs_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<TelegramBotApiClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHostedService<BotPollingService>();
if (OperatingSystem.IsWindows())
    builder.Services.AddHostedService<WebSendRunnerService>();

var app = builder.Build();
app.Lifetime.ApplicationStopped.Register(() => studio.DisposeAsync().AsTask().GetAwaiter().GetResult());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    if (IsPublicPath(path))
    {
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect($"/login?next={Uri.EscapeDataString(context.Request.Path + context.Request.QueryString)}");
        return;
    }

    var isOwner = context.User.IsInRole("Owner");
    if (!isOwner && IsOwnerPath(path))
    {
        context.Response.Redirect("/me");
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Text("ok"));

app.MapPost("/setup/bot", async (HttpContext context, StudioHost host) =>
{
    await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var token = form["token"].ToString().Trim();

    if (await BotSecrets.GetTokenAsync(host.DatabasePath, context.RequestAborted) is not null)
        return Results.Redirect("/login?error=already");

    if (token.Length < 20 || !token.Contains(':'))
        return Results.Redirect("/login?error=token");

    TelegramControlStudio.Infrastructure.Telegram.BotIdentity identity;
    try
    {
        identity = await context.RequestServices.GetRequiredService<TelegramBotApiClient>().GetMeAsync(token, context.RequestAborted);
    }
    catch (Exception)
    {
        return Results.Redirect("/login?error=botapi");
    }

    await BotSecrets.SaveTokenAsync(host.DatabasePath, token, context.RequestAborted);
    var settings = new AppSettingsRepository(host.DatabasePath);
    await settings.SetAsync("bot_username", identity.Username, context.RequestAborted);
    await settings.SetAsync("bot_id", identity.Id.ToString(), context.RequestAborted);
    return Results.Redirect("/login?setup=ok");
});

app.MapGet("/auth/telegram", async (HttpContext context, StudioHost host) =>
{
    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in context.Request.Query)
        if (!string.IsNullOrEmpty(pair.Value.ToString()))
            fields[pair.Key] = pair.Value.ToString();

    var token = await BotSecrets.GetTokenAsync(host.DatabasePath, context.RequestAborted);
    if (string.IsNullOrWhiteSpace(token) || !TelegramLoginVerifier.Verify(fields, token, DateTimeOffset.UtcNow))
        return Results.Redirect("/login?error=verify");
    if (!long.TryParse(fields.GetValueOrDefault("id"), out var telegramUserId) || telegramUserId <= 0)
        return Results.Redirect("/login?error=verify");

    var firstName = fields.GetValueOrDefault("first_name", string.Empty);
    var lastName = fields.GetValueOrDefault("last_name", string.Empty);
    var username = fields.GetValueOrDefault("username");
    var now = DateTimeOffset.UtcNow;
    var users = new AppUserRepository(host.DatabasePath);
    var user = await users.GetAsync(telegramUserId, context.RequestAborted);

    if (user is null)
    {
        if (await users.CountAsync(context.RequestAborted) == 0)
        {
            user = new AppUser(telegramUserId, firstName, lastName, username, AppUserRole.Owner, false, now, now);
            await users.UpsertAsync(user, context.RequestAborted);
        }
        else if (context.Request.Cookies["tcs_invite"] is { Length: > 0 } inviteCode)
        {
            var invites = new InviteRepository(host.DatabasePath);
            var invite = await invites.GetAsync(inviteCode, context.RequestAborted);
            if (invite is not null && invite.IsRedeemable(now) &&
                await invites.RedeemAsync(inviteCode, telegramUserId, now, context.RequestAborted))
            {
                user = new AppUser(telegramUserId, firstName, lastName, username, invite.Role, false, now, now);
                await users.UpsertAsync(user, context.RequestAborted);
            }
        }
    }

    if (user is null)
    {
        var display = string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {lastName}";
        return Results.Redirect($"/login?error=notinvited&uid={telegramUserId}&name={Uri.EscapeDataString(display)}");
    }

    if (user.IsBlocked)
        return Results.Redirect("/login?error=blocked");

    await users.TouchLoginAsync(telegramUserId, firstName, lastName, username, now, context.RequestAborted);

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, telegramUserId.ToString()),
        new(ClaimTypes.Name, user.DisplayName),
        new(ClaimTypes.Role, user.Role.ToString()),
        new("tcs:username", username ?? string.Empty)
    };
    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = now.AddHours(24)
    });

    context.Response.Cookies.Delete("tcs_invite");
    return Results.Redirect(user.Role == AppUserRole.Owner ? "/" : "/me");
});

app.MapGet("/invite/{code}", async (string code, HttpContext context, StudioHost host) =>
{
    var invite = await new InviteRepository(host.DatabasePath).GetAsync(code, context.RequestAborted);
    if (invite is null || !invite.IsRedeemable(DateTimeOffset.UtcNow))
        return Results.Redirect("/login?error=invite");

    context.Response.Cookies.Append("tcs_invite", invite.Code, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(30)
    });
    return Results.Redirect("/login?invited=1");
});

app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login?bye=1");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

static bool IsPublicPath(string path) =>
    path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/invite", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/logout", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/Error", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/app.css", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/login.js", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/favicon.png", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/health", StringComparison.OrdinalIgnoreCase);

static bool IsOwnerPath(string path) =>
    path is "/" or "" ||
    path.StartsWith("/recipients", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/compose", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/queue", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/settings", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/users", StringComparison.OrdinalIgnoreCase);

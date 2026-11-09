using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scon;
using Scon.Models;
using Scon.Services;

var builder = WebApplication.CreateSlimBuilder(args);

// Logging
builder.Logging.AddConsole();

// Options (fail fast on missing configuration instead of failing at first login)
var sconOptions = SconOptions.ParseFromEnvironment();
var configErrors = sconOptions.Validate();
if (configErrors.Count > 0)
{
    foreach (var error in configErrors) Console.Error.WriteLine($"[Scon] Configuration error: {error}");
    Environment.Exit(1);
}

builder.Services.AddSingleton(Options.Create(sconOptions));

// Services
builder.Services.AddSingleton<ConsoleService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsoleService>());

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // NOTE: trusts X-Forwarded-* from any source. Only expose Scon behind a reverse proxy.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// Authentication
static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Discord";
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/unauthorized";

    // spec: sessions older than 60 minutes must go through the guild/role check again.
    // Absolute expiry (no sliding) forces a fresh Discord login, which re-runs OnCreatingTicket.
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = false;

    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    // API calls should get status codes, not HTML redirects.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
})
.AddDiscord(options =>
{
    options.ClientId = sconOptions.DiscordClientId;
    options.ClientSecret = sconOptions.DiscordClientSecret;
    options.SaveTokens = false; // tokens are only needed inside OnCreatingTicket; don't keep them in the cookie
    options.AccessDeniedPath = "/unauthorized";

    options.Scope.Add("guilds.members.read"); // Required to check roles in a specific guild

    // Unauthenticated API calls get 401 instead of a redirect to Discord
    // (fetch/EventSource can't follow a cross-origin OAuth redirect anyway).
    options.Events.OnRedirectToAuthorizationEndpoint = context =>
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnCreatingTicket = async context =>
    {
        var accessToken = context.AccessToken;
        var guildId = sconOptions.DiscordAuthorizedGuildId;
        var requiredRole = sconOptions.DiscordAuthorizedRoleId.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/users/@me/guilds/{guildId}/member");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            context.Fail("Failed to fetch guild member info or user not in guild.");
            return;
        }

        var member = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DiscordGuildMember);

        if (member is null || !member.Roles.Contains(requiredRole))
        {
            context.Fail("User does not have the required role.");
            return;
        }

        if (context.Identity?.Name.IsEmpty() != false)
        {
            context.Fail("User identity is missing.");
            return;
        }

        // Sanitized Username (spec: strip quotes/newlines/semicolons, max 16 chars)
        var username = member.Nickname ?? context.Identity.Name!;
        var sanitized = username.ConsoleSafe().Trim().Cut(CommandParser.MaxUsernameLength).OrDefault("Unknown");

        context.Identity.AddClaim(new("SconUser", sanitized));
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Auth", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("SconUser")
    );

var app = builder.Build();

app.UseForwardedHeaders();

app.UseAuthentication();
app.UseAuthorization();

app.MapRoutes();
app.Run();

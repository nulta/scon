
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scon;
using Scon.Models;

using Scon.Services;

var builder = WebApplication.CreateSlimBuilder(args);

// Logging
builder.Logging.AddConsole();

var sconOptions = SconOptions.ParseFromEnvironment();
builder.Services.Configure<SconOptions>(options =>
{
    options.BaseUrl = sconOptions.BaseUrl;
    options.DiscordClientId = sconOptions.DiscordClientId;
    options.DiscordClientSecret = sconOptions.DiscordClientSecret;
    options.DiscordAuthorizedGuildId = sconOptions.DiscordAuthorizedGuildId;
    options.DiscordAuthorizedRoleId = sconOptions.DiscordAuthorizedRoleId;
    options.SconTailCmd = sconOptions.SconTailCmd;
    options.SconSendCmd = sconOptions.SconSendCmd;
});

// Services
builder.Services.AddSingleton<ConsoleService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsoleService>());

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Discord";
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.ExpireTimeSpan = TimeSpan.FromHours(6);
    options.AccessDeniedPath = "/unauthorized";
})
.AddDiscord(options =>
{
    options.ClientId = sconOptions.DiscordClientId;
    options.ClientSecret = sconOptions.DiscordClientSecret;
    options.SaveTokens = true;
    options.AccessDeniedPath = "/unauthorized";

    options.Scope.Add("guilds.members.read"); // Required to check roles in a specific guild

    options.Events.OnCreatingTicket = async context =>
    {
        var accessToken = context.AccessToken;
        var guildId = sconOptions.DiscordAuthorizedGuildId;
        var requiredRole = sconOptions.DiscordAuthorizedRoleId.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/users/@me/guilds/{guildId}/member");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
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

        // Sanitized Username
        var username = member.Nickname ?? context.Identity.Name!;
        var sanitized = username.ConsoleSafe().Cut(24).OrDefault("Unknown");

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

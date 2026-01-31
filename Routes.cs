using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scon.Models;
using Scon.Services;

namespace Scon;

public static class Routes
{
    public static void MapRoutes(this WebApplication app)
    {
        // /api/health
        app.MapGet("/api/health", () => Results.Ok(new ApiHealth("Healthy")));

        // /login
        app.MapGet("/login", () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            authenticationSchemes: ["Discord"]
        ));

        // /logout
        app.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.Redirect("/");
        });

        // /api/console (SSE)
        app.MapGet("/api/console", [Authorize("Auth")] async (HttpContext ctx, ConsoleService consoleService, CancellationToken ct) =>
        {
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            ctx.Response.Headers.Append("Cache-Control", "no-cache");
            ctx.Response.Headers.Append("Connection", "keep-alive");

            // Send history
            var history = consoleService.GetHistory();
            foreach (var line in history)
            {
                await ctx.Response.WriteAsync($"data: {line}\n\n", ct);
            }

            // Stream new logs
            try
            {
                await foreach (var line in consoleService.Logs.ReadAllAsync(ct))
                {
                    await ctx.Response.WriteAsync($"data: {line}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
        });

        // /api/command
        app.MapPost("/api/command", [Authorize("Auth")] async (HttpContext ctx, [FromBody] CommandRequest req, ConsoleService consoleService) =>
        {
            var user = ctx.User.FindFirst("SconUser")!.Value;
            var (success, b64, error) = CommandParser.Process(req.Command, user);

            if (!success) return Results.BadRequest(new ApiError(error));

            consoleService.SendCommand(b64);
            return Results.Ok();
        });

        // /api/whoami
        app.MapGet("/api/whoami", [Authorize("Auth")] (HttpContext ctx) =>
        {
            var user = ctx.User.FindFirst("SconUser")!.Value;
            return Results.Ok(new ApiWhoAmi(user));
        });

        // /console: redirect to login or serve static page
        app.MapGet("/console.html", (HttpContext ctx) => Results.NotFound());
        app.MapGet("/console", [Authorize("Auth")] (HttpContext ctx) =>
        {
            var consoleFilePath = Path.Combine(app.Environment.WebRootPath ?? "", "console.html");
            return Results.File(consoleFilePath, "text/html");
        });

        // /
        app.MapGet("/", async (HttpContext ctx) =>
        {
            var isAuthenticated = ctx.User.Identity?.IsAuthenticated == true;
            var hasClaim = ctx.User.Claims.Any(v => v.Type == "SconUser");

            if (hasClaim)
            {
                return Results.Redirect("/console");
            }

            if (isAuthenticated && !hasClaim) {
                // Authenticated, but missing SconUser claim
                await ctx.SignOutAsync();
            }

            var indexFilePath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
            return Results.File(indexFilePath, "text/html");
        });

        // /unauthorized
        app.MapGet("/unauthorized", async (HttpContext ctx) =>
        {
            var isAuthenticated = ctx.User.Identity?.IsAuthenticated == true;
            var hasClaim = ctx.User.Claims.Any(v => v.Type == "SconUser");

            if (isAuthenticated && !hasClaim) {
                // Authenticated, but missing SconUser claim
                await ctx.SignOutAsync();
            }

            var indexFilePath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
            return Results.File(indexFilePath, "text/html");
        });

        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }
}
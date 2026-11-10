using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Scon.Models;
using Scon.Services;

namespace Scon;

public static class Routes
{
    private static readonly TimeSpan SseHeartbeatInterval = TimeSpan.FromSeconds(15);

    public static void MapRoutes(this WebApplication app)
    {
        // /api/health
        app.MapGet("/api/health", () => Results.Ok(new ApiHealth("Healthy")));

        // /login
        // A stale (expired) cookie is replaced by the OAuth flow; with a still-valid
        // session there is nothing to re-authenticate, so go straight to the console.
        app.MapGet("/login", (HttpContext ctx) => HasSconClaim(ctx)
            ? Results.Redirect("/console")
            : Results.Challenge(
                new AuthenticationProperties { RedirectUri = "/" },
                authenticationSchemes: ["Discord"]));

        // /logout (POST per spec, so a third-party page can't log users out with a simple link/img)
        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.Redirect("/");
        });

        // /api/console (SSE)
        app.MapGet("/api/console", [Authorize("Auth")] async (HttpContext ctx, ConsoleService consoleService, CancellationToken ct) =>
        {
            ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // stop nginx from buffering the stream

            // On reconnect the browser sends the last id it received; only send what it missed.
            _ = long.TryParse(ctx.Request.Headers["Last-Event-ID"].ToString(), out var lastEventId);

            var (history, subscription) = consoleService.Subscribe(lastEventId);
            using var _sub = subscription;

            try
            {
                await ctx.Response.WriteAsync("retry: 3000\n\n", ct);
                foreach (var line in history)
                {
                    await WriteLineAsync(ctx.Response, line, ct);
                }
                await ctx.Response.Body.FlushAsync(ct);

                var reader = subscription.Reader;
                while (!ct.IsCancellationRequested)
                {
                    bool hasData;
                    using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        heartbeat.CancelAfter(SseHeartbeatInterval);
                        try
                        {
                            hasData = await reader.WaitToReadAsync(heartbeat.Token);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // Idle: send an SSE comment so proxies don't close the connection.
                            await ctx.Response.WriteAsync(": ping\n\n", ct);
                            await ctx.Response.Body.FlushAsync(ct);
                            continue;
                        }
                    }

                    if (!hasData) break;

                    while (reader.TryRead(out var evt))
                    {
                        await WriteEventAsync(ctx.Response, evt, ct);
                    }
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

            var sent = await consoleService.SendCommandAsync(b64, ctx.RequestAborted);
            if (!sent)
            {
                return Results.Json(new ApiError("Failed to deliver the command to the server"),
                    AppJsonContext.Default.ApiError, statusCode: StatusCodes.Status502BadGateway);
            }

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
            if (HasSconClaim(ctx))
            {
                return Results.Redirect("/console");
            }

            await SignOutIfIncompleteAsync(ctx);
            return IndexPage(app);
        });

        // /unauthorized
        app.MapGet("/unauthorized", async (HttpContext ctx) =>
        {
            await SignOutIfIncompleteAsync(ctx);
            return IndexPage(app);
        });

        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

    private static Task WriteEventAsync(HttpResponse response, ConsoleEvent evt, CancellationToken ct) => evt switch
    {
        ConsoleLineEvent e => WriteLineAsync(response, e.Line, ct),
        // Named event without an id, so it doesn't affect Last-Event-ID resumption.
        PlayersEvent e => response.WriteAsync(
            $"event: players\ndata: {JsonSerializer.Serialize(e.Snapshot, AppJsonContext.Default.PlayersSnapshot)}\n\n", ct),
        _ => Task.CompletedTask,
    };

    private static async Task WriteLineAsync(HttpResponse response, ConsoleLine line, CancellationToken ct)
    {
        // A line can't contain \n (it was read line by line), but strip \r just in case:
        // a bare CR would also terminate the SSE field.
        var text = line.Text.Replace("\r", "");
        await response.WriteAsync($"id: {line.Id}\ndata: {text}\n\n", ct);
    }

    private static bool HasSconClaim(HttpContext ctx) => ctx.User.HasClaim(c => c.Type == "SconUser");

    /// <summary>Authenticated, but missing the SconUser claim: clear the broken session.</summary>
    private static async Task SignOutIfIncompleteAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true && !HasSconClaim(ctx))
        {
            await ctx.SignOutAsync();
        }
    }

    private static IResult IndexPage(WebApplication app)
    {
        var indexFilePath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
        return Results.File(indexFilePath, "text/html");
    }
}

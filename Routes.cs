using Scon.Models;

namespace Scon;

public static class Routes
{
    public static void MapRoutes(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new ApiHealth("Healthy")));
    }
}
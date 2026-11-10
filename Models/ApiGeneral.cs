using System.Text.Json.Serialization;

namespace Scon.Models;

public record ApiError(string Message);
public record ApiHealth(string Status);
public record ApiWhoAmi(string Nick);
public record CommandRequest(string Command);

/// <summary>A player on the game server, as reported by a [SCON_PLAYERS] line.</summary>
public record PlayerInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("steamid")] string? SteamId
);

/// <summary>The latest player list. <see cref="UpdatedAt"/> is null until the first report arrives.</summary>
public record PlayersSnapshot(
    [property: JsonPropertyName("players")] PlayerInfo[] Players,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt
);

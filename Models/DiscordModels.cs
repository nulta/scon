using System.Text.Json.Serialization;

namespace Scon.Models;

public record DiscordGuildMember(
    [property: JsonPropertyName("roles")] string[] Roles,
    [property: JsonPropertyName("nick")] string? Nickname
);

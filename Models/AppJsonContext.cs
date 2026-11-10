using System.Text.Json.Serialization;

namespace Scon.Models;

[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiHealth))]
[JsonSerializable(typeof(ApiWhoAmi))]
[JsonSerializable(typeof(DiscordGuildMember))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(PlayerInfo[]))]
[JsonSerializable(typeof(PlayersSnapshot))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
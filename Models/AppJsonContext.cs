using System.Text.Json.Serialization;

namespace Scon.Models;

[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiHealth))]
[JsonSerializable(typeof(ApiWhoAmi))]
[JsonSerializable(typeof(DiscordGuildMember))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(string[]))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
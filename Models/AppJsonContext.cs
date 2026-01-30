using System.Text.Json.Serialization;

namespace Scon.Models;

[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiHealth))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
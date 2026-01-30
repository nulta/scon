using static System.Environment;

namespace Scon.Models;

public class SconOptions
{
    public string BaseUrl { get; set; } = "";
    public string DiscordClientId { get; set; } = "";
    public string DiscordClientSecret { get; set; } = "";
    public ulong DiscordAuthorizedGuildId { get; set; } = 0;
    public ulong DiscordAuthorizedRoleId { get; set; } = 0;
    public string SconTailCmd { get; set; } = "";
    public string SconSendCmd { get; set; } = "";


    public static SconOptions ParseFromEnvironment()
    {
        return new SconOptions
        {
            BaseUrl = GetEnvironmentVariable("SCON_BASE_URL") ?? "",
            DiscordClientId = GetEnvironmentVariable("DISCORD_CLIENT_ID") ?? "",
            DiscordClientSecret = GetEnvironmentVariable("DISCORD_CLIENT_SECRET") ?? "",
            DiscordAuthorizedGuildId = ulong.TryParse(GetEnvironmentVariable("DISCORD_AUTHORIZED_GUILD_ID"), out var guildId) ? guildId : 0,
            DiscordAuthorizedRoleId = ulong.TryParse(GetEnvironmentVariable("DISCORD_AUTHORIZED_ROLE_ID"), out var roleId) ? roleId : 0,
            SconTailCmd = GetEnvironmentVariable("SCON_TAIL_CMD") ?? "",
            SconSendCmd = GetEnvironmentVariable("SCON_SEND_CMD") ?? ""
        };
    }
}

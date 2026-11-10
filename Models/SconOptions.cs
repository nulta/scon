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

    /// <summary>Returns a list of configuration problems. Empty if the configuration is usable.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(DiscordClientId)) errors.Add("DISCORD_CLIENT_ID is not set.");
        if (string.IsNullOrEmpty(DiscordClientSecret)) errors.Add("DISCORD_CLIENT_SECRET is not set.");
        if (DiscordAuthorizedGuildId == 0) errors.Add("DISCORD_AUTHORIZED_GUILD_ID is not set or not a valid id.");
        if (DiscordAuthorizedRoleId == 0) errors.Add("DISCORD_AUTHORIZED_ROLE_ID is not set or not a valid id.");

        return errors;
    }
}

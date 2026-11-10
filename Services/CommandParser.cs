using System.Text;

namespace Scon.Services;

public static class CommandParser
{
    /// <summary>Maximum size of the command text sent to the game server (spec: 1024 bytes).</summary>
    public const int MaxCommandBytes = 1024;

    /// <summary>Maximum length of the raw user input. Anything longer is rejected outright.</summary>
    public const int MaxInputLength = 2000;

    public const int MaxUsernameLength = 16;

    public static (bool Success, string Base64Command, string Error) Process(string? commandBody, string username)
    {
        if (string.IsNullOrWhiteSpace(commandBody))
        {
            return (false, "", "Empty command");
        }

        if (commandBody.Length > MaxInputLength)
        {
            return (false, "", "Command is too long");
        }

        var (finalCommand, error) = ParseCommand(commandBody, username);

        if (finalCommand is null)
        {
            return (false, "", error);
        }

        if (Encoding.UTF8.GetByteCount(finalCommand) > MaxCommandBytes)
        {
            return (false, "", "Command is too long");
        }

        return (true, finalCommand.ToBase64(), "");
    }

    private static (string? Command, string Error) ParseCommand(string command, string username)
    {
        username = username.ConsoleSafe().Cut(MaxUsernameLength);
        command = command.ConsoleSafe().Trim();

        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return (null, "Empty command");
        }

        var action = parts[0].ToLowerInvariant();
        var args = parts.GetOr(1, "");

        switch (action)
        {
            case "say":
            {
                var text = args.Cut(200);
                if (text.IsEmpty()) return (null, "Usage: say [text]");

                return ($"scon_say \"{username}\" \"{text.ToBase64()}\"", "");
            }

            case "status":
                return ($"scon_status \"{username}\"", "");

            case "kick":
            {
                var kickParts = SplitArgs(args, 2);
                var target = kickParts.GetOr(0, "").Cut(32);
                if (target.IsEmpty()) return (null, "Usage: kick [target] {reason}");

                var reason = kickParts.GetOr(1, "").OrDefault("Kicked by admin").Cut(64);

                return ($"scon_kick \"{username}\" \"{target.ToBase64()}\" \"{reason.ToBase64()}\"", "");
            }

            case "banid":
            {
                var banParts = SplitArgs(args, 3);
                var steamId = banParts.GetOr(0, "").Cut(32);
                if (steamId.IsEmpty()) return (null, "Usage: banid [steamid] {duration} {reason}");

                var duration = banParts.GetOr(1, "").OrDefault("0").Cut(20);
                var reason = banParts.GetOr(2, "").OrDefault("No reason given").Cut(64);

                return ($"scon_banid \"{username}\" \"{steamId}\" \"{duration}\" \"{reason.ToBase64()}\"", "");
            }

            case "bancheck":
            {
                var steamId = SplitArgs(args, 2).GetOr(0, "").Cut(32);
                if (steamId.IsEmpty()) return (null, "Usage: bancheck [steamid]");

                return ($"scon_bancheck \"{username}\" \"{steamId}\"", "");
            }
        }

        return (null, $"Unknown command: {action.Cut(32)}");
    }

    /// <summary>Splits on spaces, collapsing repeated spaces (e.g. "kick  bob  afk").</summary>
    private static string[] SplitArgs(string args, int count)
    {
        return args.Split(' ', count, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

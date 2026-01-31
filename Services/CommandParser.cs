using System.Text;

namespace Scon.Services;

public static class CommandParser
{
    public static (bool Success, string Base64Command, string Error) Process(string commandBody, string username)
    {
        if (string.IsNullOrWhiteSpace(commandBody))
        {
            return (false, "", "Empty command");
        }

        var finalCommand = ParseCommand(commandBody, username);

        if (finalCommand is null)
        {
            return (false, "", "Invalid command");
        }

        return (true, finalCommand.ToBase64(), "");
    }

    private static string? ParseCommand(string command, string username)
    {
        username = username.ConsoleSafe().Cut(16);
        command = command.ConsoleSafe();

        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var action = parts[0].ToLowerInvariant();
        var args = parts.GetOr(1, "");

        switch (action)
        {
            case "say":
                args = args.Cut(200);
                return $"scon_say \"{username}\" \"{args.ToBase64()}\"";

            case "status":
                return $"scon_status \"{username}\"";

            case "kick":
                var kickParts = args.Split(' ', 2);
                var kickUser = kickParts[0].Cut(32);
                var kickReason = kickParts.GetOr(1, "Kicked by admin").Cut(64);

                return $"scon_kick \"{username}\" \"{kickUser.ToBase64()}\" \"{kickReason.ToBase64()}\"";

            case "banid":
                var banParts = args.Split(' ', 3);
                var banId = banParts[0].Cut(32);
                var banTime = banParts.GetOr(1, "0").Cut(16);
                var banReason = banParts.GetOr(2, "No reason given").Cut(64);

                return $"scon_banid \"{username}\" \"{banId}\" \"{banTime}\" \"{banReason.ToBase64()}\"";
            
            case "bancheck":
                var checkId = args.Split(' ', 2)[0].Cut(32);
                return $"scon_bancheck \"{username}\" \"{checkId}\"";
        }

        return null;
    }
}

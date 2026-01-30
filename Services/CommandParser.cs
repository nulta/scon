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
                return $"scon_say \"{username}\" \"{args}\"";

            case "status":
                return $"scon_status \"{username}\"";

            case "kick":
                var kickParts = args.Split(' ', 2);
                var kickUser = kickParts[0].Cut(32);
                var kickReason = kickParts.GetOr(1, "Kicked by admin").Cut(64);

                return $"scon_kick \"{username}\" \"{kickUser}\" \"{kickReason}\"";

            case "banid":
                var banParts = args.Split(' ', 2);
                var banId = banParts[0].Cut(32);
                var banReason = banParts.GetOr(1, "Banned by admin").Cut(64);

                return $"scon_banid \"{username}\" \"{banId}\" \"{banReason}\"";
        }

        return null;
    }
}


namespace Scon.Models;

public record ApiError(string Message);
public record ApiHealth(string Status);
public record ApiWhoAmi(string Nick);
public record CommandRequest(string Command);

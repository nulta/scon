using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Scon.Models;

namespace Scon.Services;

/// <summary>
/// A single console line with a monotonically increasing id.
/// The id is used as the SSE event id so that reconnecting clients
/// (which send Last-Event-ID automatically) only receive lines they missed.
/// </summary>
public readonly record struct ConsoleLine(long Id, string Text);

/// <summary>Something pushed to a console subscriber: a new line or an updated player list.</summary>
public abstract record ConsoleEvent;
public sealed record ConsoleLineEvent(ConsoleLine Line) : ConsoleEvent;
public sealed record PlayersEvent(PlayersSnapshot Snapshot) : ConsoleEvent;

public partial class ConsoleService : BackgroundService
{
    /// <summary>
    /// Marker the game server writes into the console/log to report its player list:
    /// <c>[SCON_PLAYERS] base64(json [{"name":..., "steamid":...}, ...])</c>.
    /// Anything may precede it on the line (e.g. a ULX timestamp).
    /// </summary>
    public const string PlayersMarker = "[SCON_PLAYERS]";
    private const int MaxPlayers = 256;

    private const int HistoryLimit = 1000;
    private const int SubscriberBufferLimit = 1000;
    private static readonly TimeSpan SendCommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly SconOptions _options;
    private readonly ILogger<ConsoleService> _logger;

    // Guards _history, _nextId, _subscribers and _players so that taking a history snapshot
    // and subscribing to new lines happen atomically (no lost or duplicated lines).
    // Every write to a subscriber channel happens under this lock, so each channel has a single writer at a time.
    private readonly Lock _lock = new();
    private readonly Queue<ConsoleLine> _history = new();
    private readonly HashSet<Channel<ConsoleEvent>> _subscribers = [];
    private PlayersSnapshot _players = new([], null); // UpdatedAt == null: no report received yet
    private long _nextId = 1;

    [GeneratedRegex(@"\[SCON_PLAYERS\]\s*([A-Za-z0-9+/=]+)")]
    private static partial Regex PlayersLineRegex();

    public ConsoleService(IOptions<SconOptions> options, ILogger<ConsoleService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Subscribes to the console output.
    /// Returns the buffered history (lines with id greater than <paramref name="afterId"/>)
    /// and a subscription that receives every line broadcast afterwards.
    /// Each subscriber gets its own channel, so every connected client sees every line.
    /// The current player list is delivered to the new subscriber right away.
    /// </summary>
    public (ConsoleLine[] History, ConsoleSubscription Subscription) Subscribe(long afterId = 0)
    {
        var channel = Channel.CreateBounded<ConsoleEvent>(new BoundedChannelOptions(SubscriberBufferLimit)
        {
            // A slow client loses its oldest unread lines instead of blocking everyone else.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        lock (_lock)
        {
            // An id we never issued means Scon restarted since the client last connected: send everything.
            if (afterId >= _nextId) afterId = 0;

            var history = _history.Where(l => l.Id > afterId).ToArray();
            _subscribers.Add(channel);
            channel.Writer.TryWrite(new PlayersEvent(_players));

            return (history, new ConsoleSubscription(this, channel));
        }
    }

    public PlayersSnapshot GetPlayers()
    {
        lock (_lock)
        {
            return _players;
        }
    }

    private void Unsubscribe(Channel<ConsoleEvent> channel)
    {
        lock (_lock)
        {
            _subscribers.Remove(channel);
        }
        channel.Writer.TryComplete();
    }

    /// <summary>
    /// Parses a <see cref="PlayersMarker"/> line. Returns null if the line isn't a valid report.
    /// </summary>
    public static PlayerInfo[]? TryParsePlayersLine(string text)
    {
        if (!text.Contains(PlayersMarker, StringComparison.Ordinal)) return null;

        var match = PlayersLineRegex().Match(text);
        if (!match.Success) return null;

        try
        {
            var json = Convert.FromBase64String(match.Groups[1].Value);
            var players = JsonSerializer.Deserialize(json, AppJsonContext.Default.PlayerInfoArray);
            if (players is null) return null;

            return [.. players
                .Where(p => p is not null && !p.Name.IsEmpty())
                .Take(MaxPlayers)
                .Select(p => new PlayerInfo(p.Name.Cut(64), (p.SteamId ?? "").Cut(32)))];
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.SconTailCmd.IsEmpty())
        {
            _logger.LogWarning("SCON_TAIL_CMD is not set. Console service will not run.");
            return;
        }

        _logger.LogInformation("Starting Console Service with: {Cmd}", _options.SconTailCmd);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTailProcess(stoppingToken);
                _logger.LogWarning("Console tail process exited. Restarting in {Delay}s...", RestartDelay.TotalSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in console tail process. Restarting in {Delay}s...", RestartDelay.TotalSeconds);
            }

            try { await Task.Delay(RestartDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunTailProcess(CancellationToken stoppingToken)
    {
        var startInfo = CreateShellStartInfo(_options.SconTailCmd);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true; // spec: stderr is merged into the output
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Broadcast(e.Data);
        process.ErrorDataReceived += (_, e) => Broadcast(e.Data);

        if (!process.Start())
        {
            _logger.LogError("Failed to start console tail process.");
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(stoppingToken);
        }
        finally
        {
            // WaitForExitAsync does not kill the child on cancellation;
            // without this, the tail process would be orphaned on shutdown.
            KillQuietly(process);
        }
    }

    private void Broadcast(string? text)
    {
        if (text.IsEmpty()) return;

        // Player reports are machine-readable status lines: update the list and keep them out of the console.
        // A malformed report falls through and is shown as a normal line, so it can be debugged.
        if (TryParsePlayersLine(text) is { } players)
        {
            lock (_lock)
            {
                _players = new PlayersSnapshot(players, DateTimeOffset.UtcNow);
                var playersEvent = new PlayersEvent(_players);
                foreach (var subscriber in _subscribers)
                {
                    subscriber.Writer.TryWrite(playersEvent);
                }
            }
            return;
        }

        lock (_lock)
        {
            var line = new ConsoleLine(_nextId++, text);

            if (_history.Count >= HistoryLimit) _history.Dequeue();
            _history.Enqueue(line);

            var evt = new ConsoleLineEvent(line);
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(evt);
            }
        }
    }

    /// <summary>
    /// Runs SCON_SEND_CMD with the given Base64 argument and waits for it to finish.
    /// Returns true if the command exited with code 0.
    /// </summary>
    public async Task<bool> SendCommandAsync(string commandBase64, CancellationToken ct = default)
    {
        if (_options.SconSendCmd.IsEmpty())
        {
            _logger.LogWarning("SCON_SEND_CMD is not set.");
            return false;
        }

        // Base64 only contains [A-Za-z0-9+/=], so it is safe to append to a shell command line.
        using var process = new Process { StartInfo = CreateShellStartInfo($"{_options.SconSendCmd} {commandBase64}") };

        try
        {
            if (!process.Start())
            {
                _logger.LogError("Failed to start send command process.");
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendCommandTimeout);
            await process.WaitForExitAsync(timeout.Token);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Send command exited with code {Code}.", process.ExitCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Send command timed out or was cancelled.");
            KillQuietly(process);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute send command.");
            return false;
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c \"{command}\"";
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

        return startInfo;
    }

    private void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill process.");
        }
    }

    /// <summary>
    /// A live subscription to console output. Dispose it when the client disconnects.
    /// </summary>
    public sealed class ConsoleSubscription : IDisposable
    {
        private readonly ConsoleService _owner;
        private readonly Channel<ConsoleEvent> _channel;

        internal ConsoleSubscription(ConsoleService owner, Channel<ConsoleEvent> channel)
        {
            _owner = owner;
            _channel = channel;
        }

        public ChannelReader<ConsoleEvent> Reader => _channel.Reader;

        public void Dispose() => _owner.Unsubscribe(_channel);
    }
}

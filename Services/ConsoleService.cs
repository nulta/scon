using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

public class ConsoleService : BackgroundService
{
    private const int HistoryLimit = 1000;
    private const int SubscriberBufferLimit = 1000;
    private static readonly TimeSpan SendCommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly SconOptions _options;
    private readonly ILogger<ConsoleService> _logger;

    // Guards _history, _nextId and _subscribers so that taking a history snapshot
    // and subscribing to new lines happen atomically (no lost or duplicated lines).
    private readonly Lock _lock = new();
    private readonly Queue<ConsoleLine> _history = new();
    private readonly HashSet<Channel<ConsoleLine>> _subscribers = [];
    private long _nextId = 1;

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
    /// </summary>
    public (ConsoleLine[] History, ConsoleSubscription Subscription) Subscribe(long afterId = 0)
    {
        var channel = Channel.CreateBounded<ConsoleLine>(new BoundedChannelOptions(SubscriberBufferLimit)
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
            return (history, new ConsoleSubscription(this, channel));
        }
    }

    private void Unsubscribe(Channel<ConsoleLine> channel)
    {
        lock (_lock)
        {
            _subscribers.Remove(channel);
        }
        channel.Writer.TryComplete();
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

        lock (_lock)
        {
            var line = new ConsoleLine(_nextId++, text);

            if (_history.Count >= HistoryLimit) _history.Dequeue();
            _history.Enqueue(line);

            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(line);
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
        private readonly Channel<ConsoleLine> _channel;

        internal ConsoleSubscription(ConsoleService owner, Channel<ConsoleLine> channel)
        {
            _owner = owner;
            _channel = channel;
        }

        public ChannelReader<ConsoleLine> Reader => _channel.Reader;

        public void Dispose() => _owner.Unsubscribe(_channel);
    }
}

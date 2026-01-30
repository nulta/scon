using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Scon.Models;

namespace Scon.Services;

public class ConsoleService : BackgroundService
{
    private readonly SconOptions _options;
    private readonly ILogger<ConsoleService> _logger;
    private readonly Channel<string> _logChannel;
    private readonly Queue<string> _history;
    private readonly Lock _lock = new();
    private const int HistoryLimit = 200;

    public ConsoleService(IOptions<SconOptions> options, ILogger<ConsoleService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _logChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _history = new Queue<string>(HistoryLimit);
    }

    public ChannelReader<string> Logs => _logChannel.Reader;

    public string[] GetHistory()
    {
        lock (_lock)
        {
            return [.. _history];
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_options.SconTailCmd))
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
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in console tail process. Restarting in 5s...");
                try { await Task.Delay(5000, stoppingToken); } catch { }
            }
        }
    }

    private async Task RunTailProcess(CancellationToken stoppingToken)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c \"{_options.SconTailCmd}\"";
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(_options.SconTailCmd);
        }

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (s, e) => Broadcast(e.Data);
        process.ErrorDataReceived += (s, e) => Broadcast(e.Data);

        if (!process.Start())
        {
            _logger.LogError("Failed to start process.");
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(stoppingToken);
    }

    private void Broadcast(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;

        lock (_lock)
        {
            if (_history.Count >= HistoryLimit) _history.Dequeue();
            _history.Enqueue(line);
        }

        _logChannel.Writer.TryWrite(line);
    }

    public void SendCommand(string commandBase64)
    {
        if (string.IsNullOrEmpty(_options.SconSendCmd))
        {
            _logger.LogWarning("SCON_SEND_CMD not set.");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/c \"{_options.SconSendCmd} {commandBase64}\"";
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add($"{_options.SconSendCmd} {commandBase64}");
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute send command.");
        }
    }
}

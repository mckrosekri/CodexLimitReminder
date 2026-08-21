using System.Diagnostics;
using System.Text.Json;

namespace CodexLimitReminder;

internal static class CodexAppServerClient
{
    public static async Task<WeeklyRateLimit> ReadWeeklyLimitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        string executable = CodexExecutableLocator.Find();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Codex App Server could not be started.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        CancellationToken token = timeoutSource.Token;

        try
        {
            await WriteMessageAsync(
                process,
                "{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_limit_reminder\",\"title\":\"Codex Limit Reminder\",\"version\":\"1.1.0\"}}}",
                token).ConfigureAwait(false);
            await WriteMessageAsync(process, "{\"method\":\"initialized\",\"params\":{}}", token).ConfigureAwait(false);
            await WriteMessageAsync(process, "{\"method\":\"account/rateLimits/read\",\"id\":2}", token).ConfigureAwait(false);

            while (!process.HasExited)
            {
                string? line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using JsonDocument document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("id", out JsonElement id) && id.TryGetInt32(out int value) && value == 2)
                {
                    return CodexRateLimitParser.ParseResponse(line);
                }
            }

            string error = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Codex closed before returning rate-limit data."
                : $"Codex App Server: {FirstLine(error)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Codex did not return rate-limit data within the connection timeout.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteMessageAsync(Process process, string json, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FirstLine(string value)
    {
        int lineEnd = value.IndexOfAny(new[] { '\r', '\n' });
        return (lineEnd >= 0 ? value[..lineEnd] : value).Trim();
    }
}

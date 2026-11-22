using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Onyx.App.Web.Services.Reports;

/// <summary>
/// Renders report charts by running <c>Reports/weekly_report.py</c> (Python 3 with matplotlib). The interpreter is
/// <c>Onyx:Reports:Python</c> (default <c>python3</c>). Charts are optional: errors come back as a message, and the
/// report is shown without them.
/// </summary>
public class ReportChartRenderer(IConfiguration configuration, ILogger<ReportChartRenderer> logger)
{
    private static readonly TimeSpan s_Timeout = TimeSpan.FromSeconds(60);

    // matplotlib takes a few hundred MB while rendering; limit how many reports render at once.
    private static readonly SemaphoreSlim s_Concurrency = new(2, 2);

    public static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Reports", "weekly_report.py");

    public virtual async Task<(Dictionary<string, string> Charts, string? Error)> RenderAsync(ChartInput input,
        CancellationToken ct = default)
    {
        var python = configuration["Onyx:Reports:Python"] is { Length: > 0 } configured ? configured : "python3";

        await s_Concurrency.WaitAsync(ct);
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(python)
            {
                ArgumentList = { ScriptPath },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // matplotlib writes a font cache; the container user's home may not be writable.
            process.StartInfo.Environment["MPLCONFIGDIR"] = Path.Combine(Path.GetTempPath(), "onyx-matplotlib");

            process.Start();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(s_Timeout);

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, input, cancellationToken: timeout.Token);
            process.StandardInput.Close();

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            if (process.ExitCode != 0)
            {
                var stderr = await errors;
                logger.LogWarning("Report chart script failed ({Code}): {Errors}", process.ExitCode, stderr);
                return ([], stderr.Contains("No module named 'matplotlib'")
                    ? "Charts need matplotlib on the server (e.g. apt install python3-matplotlib)."
                    : "The charts could not be rendered; see the server log.");
            }

            var result = JsonSerializer.Deserialize<ScriptOutput>(await output);
            return (result?.Charts ?? [], null);
        }
        catch (Win32Exception e)
        {
            logger.LogWarning(e, "Could not start {Python} for report charts", python);
            return ([], $"Charts need Python 3 with matplotlib on the server ({python} was not found).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Report chart script timed out after {Timeout}", s_Timeout);
            return ([], "Rendering the charts took too long.");
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Report chart script returned invalid output");
            return ([], "The charts could not be rendered; see the server log.");
        }
        finally
        {
            s_Concurrency.Release();
        }
    }

    private sealed record ScriptOutput(
        [property: System.Text.Json.Serialization.JsonPropertyName("charts")] Dictionary<string, string> Charts);
}

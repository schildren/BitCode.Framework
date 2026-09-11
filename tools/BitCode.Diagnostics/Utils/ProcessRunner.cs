using System.Diagnostics;

namespace BitCode.Framework.Tools.Diagnostics.Utils;

public static class ProcessRunner
{
    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

    public static async Task<ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                return new ProcessResult(process.ExitCode, stdout.Trim(), stderr.Trim(), false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignorar si el proceso ya terminó
                }

                return new ProcessResult(-1, string.Empty, "Timeout al ejecutar el comando.", true);
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false);
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal readonly record struct PastedTextLaunchResult(
    bool Success,
    bool Cancelled,
    int ProcessId,
    string? ErrorType,
    string? ErrorMessage);

internal sealed class PastedTextTransferState
{
    private readonly object _gate = new();
    private bool _completed;

    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _completed;
            }
        }
    }

    public void Cancel(Action terminate)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                terminate();
            }
        }
    }

    public void Complete(Action closeInput, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            closeInput();
            cancellationToken.ThrowIfCancellationRequested();
            _completed = true;
        }
    }
}

internal static class PastedTextWindowLauncher
{
    public static async Task<PastedTextLaunchResult> LaunchAsync(
        string executable,
        string argument,
        string text,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        var transferState = new PastedTextTransferState();
        CancellationTokenRegistration cancellationRegistration = default;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessStartInfo startInfo = new()
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(argument);
            process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Process.Start returned null.");
            }

            Process transferProcess = process;
            cancellationRegistration = cancellationToken.Register(() =>
                transferState.Cancel(() => TryTerminate(transferProcess)));

            await WriteUtf8Async(process.StandardInput.BaseStream, text, cancellationToken).ConfigureAwait(false);
            transferState.Complete(process.StandardInput.Close, cancellationToken);

            return new PastedTextLaunchResult(
                Success: true,
                Cancelled: false,
                ProcessId: process.Id,
                ErrorType: null,
                ErrorMessage: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            return new PastedTextLaunchResult(
                Success: false,
                Cancelled: true,
                ProcessId: process?.Id ?? 0,
                ErrorType: null,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            TryTerminate(process);
            if (cancellationToken.IsCancellationRequested)
            {
                return new PastedTextLaunchResult(
                    Success: false,
                    Cancelled: true,
                    ProcessId: process?.Id ?? 0,
                    ErrorType: null,
                    ErrorMessage: null);
            }

            return new PastedTextLaunchResult(
                Success: false,
                Cancelled: false,
                ProcessId: process?.Id ?? 0,
                ErrorType: ex.GetType().FullName ?? ex.GetType().Name,
                ErrorMessage: ex.Message);
        }
        finally
        {
            cancellationRegistration.Dispose();
            process?.Dispose();
        }
    }

    internal static async Task WriteUtf8Async(Stream output, string text, CancellationToken cancellationToken)
    {
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), cancellationToken).ConfigureAwait(false);
        var writer = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            1 << 16,
            leaveOpen: true);
        bool completed = false;
        try
        {
            await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (completed)
            {
                writer.Dispose();
            }
        }
    }

    private static void TryTerminate(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}

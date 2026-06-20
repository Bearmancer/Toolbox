using System.Diagnostics.CodeAnalysis;
using Azure;
using Core.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Core;

public abstract class CommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    /// <summary>
    /// Service type label for the log session opened around each command invocation.
    /// Override in domain-specific command bases (e.g. MusicCommandBase → ServiceType.Music).
    /// Defaults to Azure for the current phase.
    /// </summary>
    protected virtual ServiceType ServiceName => ServiceType.Azure;

    protected sealed override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken ct
    )
    {
        // Session is owned here — services no longer open their own sessions.
        // AsyncLocal flows through all awaits, so Log.Emit in service methods
        // correctly reads the ServiceContext set in this scope.
        using var session = Log.BeginSession(ServiceName);
        try
        {
            return await ExecuteCommandAsync(context, settings, ct);
        }
        catch (OperationCanceledException)
        {
            // Standard POSIX exit code for SIGINT / user cancellation
            return 130;
        }
        catch (ArgumentException ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "CLI error"));
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 2;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            Log.Emit(
                new ErrorOccurred(
                    $"Rate limited: {ex.Message}",
                    "Azure",
                    nameof(RequestFailedException)
                )
            );
            AnsiConsole.MarkupLine("[red]Error:[/] Rate limited. Please retry.");
            return 1;
        }
        catch (RequestFailedException ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "Azure service error"));
            AnsiConsole.MarkupLine($"[red]Error:[/] Azure error ({ex.Status}): {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "Unexpected error"));
            AnsiConsole.MarkupLine($"[red]Error:[/] Unexpected error: {ex.Message}");
            return 99;
        }
    }

    [SuppressMessage("ReSharper", "UnusedParameter.Global")]
    protected abstract Task<int> ExecuteCommandAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken ct
    );
}

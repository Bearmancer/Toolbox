using System.Diagnostics.CodeAnalysis;
using Azure;
using Spectre.Console.Cli;
using Toolbox.Core.Logging;

namespace Toolbox.Core;

public abstract class CommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected sealed override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken ct
    )
    {
        try
        {
            return await ExecuteCommandAsync(context, settings, ct);
        }
        catch (ArgumentException ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "CLI error"));
            Ui.Error(ex.Message);
            return 2;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            Log.Emit(new ErrorOccurred($"Rate limited: {ex.Message}", "Azure", nameof(RequestFailedException)));
            Ui.Error("Rate limited. Please retry.");
            return 1;
        }
        catch (RequestFailedException ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "Azure service error"));
            Ui.Error($"Azure error ({ex.Status}): {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Emit(ErrorOccurred.From(ex, "Unexpected error"));
            Ui.Error($"Unexpected error: {ex.Message}");
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
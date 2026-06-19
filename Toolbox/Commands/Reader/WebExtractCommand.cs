using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Core;
using Toolbox.Reader;

namespace Toolbox.Commands.Reader;

[Description("Extract content from a web page")]
public class WebExtractCommand : CommandBase<WebExtractCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var content = await WebExtractor.ExtractAsync(settings.Url, ct);

        if (content is null)
        {
            Ui.Error("Failed to extract content");
            return 1;
        }

        Ui.Info($"Title: {content.Title}");
        Ui.Info($"Words: {content.WordCount}");
        Ui.Info($"Source: {content.SourceUrl}");
        Ui.NewLine();
        Ui.Info("Content:");
        Ui.Info(content.Content.Length > 1000 ? content.Content[..1000] + "..." : content.Content);

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<url>")]
        [Description("URL of the web page to extract content from")]
        public string Url { get; init; } = "";
    }
}
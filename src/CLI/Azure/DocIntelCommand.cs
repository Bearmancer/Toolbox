using System.ComponentModel;
using ErrorOr;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
	"Extract text and structured data from documents using Azure Document Intelligence. "
		+ "Supports PDF, JPEG, PNG, TIFF, and BMP inputs."
)]
public class DocIntelCommand(DocIntelService service) : AsyncCommand<DocIntelCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		ErrorOr<string> result = await service.AnalyzeAsync(s.File, s.Model, cancellationToken);
		return result.Match(
			success =>
			{
				Console.WriteLine(success);
				return 0;
			},
			errors =>
			{
				Console.Error.WriteLine(errors[0].Description);
				return 1;
			}
		);
	}

	public sealed class Settings : CommandSettings
	{
		[Description(
			"Path to the document file to analyze. "
				+ "Supports PDF, JPEG, PNG, TIFF, and BMP formats."
		)]
		[CommandArgument(0, "<file>")]
		public required string File { get; init; }

		[Description(
			"Document Intelligence model to use. "
				+ "'prebuilt-read' extracts printed and handwritten text. "
				+ "'prebuilt-layout' extracts text, tables, selection marks, and structure. "
				+ "'prebuilt-invoice' extracts invoice fields (vendor, total, line items). "
				+ "(default: prebuilt-read)"
		)]
		[CommandOption("--model <MODEL>")]
		[DefaultValue("prebuilt-read")]
		public string Model { get; init; } = "prebuilt-read";
	}
}

using System.Diagnostics.CodeAnalysis;
using Spectre.Console;

namespace Toolbox.Core;

public static class Ui
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public static bool Suppress { get; set; }

    public static void Info(string message, params object?[] args)
    {
        if (Suppress)
            return;
        AnsiConsole.MarkupLine($"[blue]ℹ[/] {Markup.Escape(Format(message, args))}");
    }

    public static void Debug(string message)
    {
        if (Suppress)
            return;
        AnsiConsole.MarkupLine($"[default]{Markup.Escape(message)}[/]");
    }

    public static void Warning(string message, params object?[] args)
    {
        if (Suppress)
            return;
        AnsiConsole.MarkupLine($"[yellow]⚠[/] {Markup.Escape(Format(message, args))}");
    }

    public static void Error(string message, params object?[] args)
    {
        if (Suppress)
            return;
        AnsiConsole.MarkupLine($"[red]✖[/] {Markup.Escape(Format(message, args))}");
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void Progress(string message, params object?[] args)
    {
        if (Suppress)
            return;
        var formatted = args.Length > 0 ? Format(message, args) : message;
        AnsiConsole.MarkupLine(
            $"[cyan][[PROG]][/] [dim]{DateTime.Now:HH:mm:ss}:[/] {Markup.Escape(formatted)}"
        );
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void Success(string message, params object?[] args)
    {
        if (Suppress)
            return;
        AnsiConsole.MarkupLine($"[green]✔[/] {Markup.Escape(Format(message, args))}");
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void Failure(string message, params object?[] args)
    {
        if (Suppress)
            return;
        AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(Format(message, args))}");
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void WriteException(Exception ex)
    {
        if (Suppress)
            return;
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }

    private static string Colored(string color, string? text)
    {
        return $"[{color}]{Markup.Escape(text ?? "")}[/]";
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Cyan(string? text)
    {
        return Colored("cyan", text);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Green(string? text)
    {
        return Colored("green", text);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Yellow(string? text)
    {
        return Colored("yellow", text);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Red(string? text)
    {
        return Colored("red", text);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Blue(string? text)
    {
        return Colored("blue", text);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Magenta(string? text)
    {
        return Colored("magenta", text);
    }

    private static string Dim(string? text)
    {
        return Colored("dim", text);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Bold(string? text)
    {
        return $"[bold]{Markup.Escape(text ?? "")}[/]";
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void Field(string label, string? value, int labelWidth = 12)
    {
        var paddedLabel = label.PadRight(labelWidth);
        var safeValue = Markup.Escape(value ?? "");
        AnsiConsole.MarkupLine($"[bold]{paddedLabel}[/] {safeValue}");
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void LabelValue(string label, string? value, string valueColor = "cyan")
    {
        var display = string.IsNullOrEmpty(value) ? Dim("-") : Colored(valueColor, value);
        AnsiConsole.MarkupLine($"    {Dim(label + ":")} {display}");
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void KeyValue(string key, string value)
    {
        AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(key)}:[/] {Markup.Escape(value)}");
    }

    public static void NewLine()
    {
        AnsiConsole.WriteLine();
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void MarkupLine(string markup)
    {
        AnsiConsole.MarkupLine(markup);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static string Combine(params string?[] parts)
    {
        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string Format(string message, object?[] args)
    {
        if (args.Length == 0)
            return message;

        try
        {
            object?[] safeArgs = [.. args.Select(a => a ?? "null")];
            return string.Format(message, safeArgs);
        }
        catch (FormatException)
        {
            return message;
        }
    }
}
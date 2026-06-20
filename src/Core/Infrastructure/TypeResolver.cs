using Spectre.Console.Cli;

namespace Core.Infrastructure;

public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider Provider;

    public TypeResolver(IServiceProvider providerArg)
    {
        ArgumentNullException.ThrowIfNull(providerArg);
        Provider = providerArg;
    }

    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        try
        {
            return Provider.GetService(type);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DI Error resolving {type.Name}: {ex}");
            throw;
        }
    }

    public void Dispose() => (Provider as IDisposable)?.Dispose();
}

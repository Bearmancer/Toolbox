using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace CLI;

public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    private ServiceProvider? Provider;

    public ITypeResolver Build()
    {
        Provider ??= services.BuildServiceProvider();
        return new TypeResolver(Provider);
    }

    public void Register(Type service, Type implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    public object? Resolve(Type? type) => type is null ? null : provider.GetService(type);
}

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Core.Infrastructure;

/// <summary>
/// ITypeRegistrar implementation that wraps an IServiceCollection for Spectre.Console.Cli DI.
/// The ServiceProvider is built exactly once (on first Build() call) and cached — subsequent
/// Build() calls return a TypeResolver over the same provider, preventing duplicate singleton
/// instances across Spectre's multiple Build() invocations during command resolution.
/// </summary>
public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection Services;
    private ServiceProvider? Provider;

    public TypeRegistrar(IServiceCollection servicesArg)
    {
        ArgumentNullException.ThrowIfNull(servicesArg);
        Services = servicesArg;
    }

    public ITypeResolver Build() => new TypeResolver(Provider ??= Services.BuildServiceProvider());

    public void Register(Type service, Type implementation) =>
        Services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        Services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        Services.AddSingleton(service, _ => factory());
}

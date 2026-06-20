using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Core.Modules;

public interface ICommandModule
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    void ConfigureCommands(IConfigurator config);
}

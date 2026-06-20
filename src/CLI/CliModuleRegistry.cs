using CLI.Azure;
using Core.Modules;

namespace CLI;

public static class CliModuleRegistry
{
    public static ICommandModule[] GetAllModules() => [new AzureCommandModule()];
}

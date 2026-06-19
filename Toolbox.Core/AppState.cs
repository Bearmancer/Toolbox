using Azure.Core;

namespace Toolbox.Core;

public static class AppState
{
    public static TokenCredential? Credential { get; set; }
}
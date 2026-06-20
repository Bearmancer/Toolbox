namespace Core;

public enum ServiceType
{
    Azure,
    Music,
    Sync,
    Reader,
    Clean,
    Cloud,
}

public class ServiceContext
{
    private static readonly AsyncLocal<ServiceContext?> ContextHolder = new();
    public ServiceType ServiceType { get; private init; }
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    public string Instance { get; init; } =
        Environment.GetEnvironmentVariable("SEQ_INSTANCE") ?? "local";

    public static ServiceContext? Current
    {
        get => ContextHolder.Value;
        set => ContextHolder.Value = value;
    }

    public static IDisposable Begin(ServiceType serviceType)
    {
        var previous = Current;
        Current = new ServiceContext { ServiceType = serviceType };
        return new ServiceScope(previous);
    }

    private class ServiceScope(ServiceContext? previous) : IDisposable
    {
        public void Dispose()
        {
            Current = previous;
        }
    }
}

namespace Core;

public static class TaskExtensions
{
    extension<T>(Task<T> task)
    {
        public async Task<T> WithTelemetry(
            string service,
            string activityName,
            params object[] args
        )
        {
            using var _ = Telemetry.ForService(service);
            using var activity = Telemetry.StartActivity(activityName, args);
            var result = await task;
            activity.Complete();
            return result;
        }
    }

    extension(Task task)
    {
        public async Task WithTelemetry(string service, string activityName, params object[] args)
        {
            using var _ = Telemetry.ForService(service);
            using var activity = Telemetry.StartActivity(activityName, args);
            await task;
            activity.Complete();
        }
    }
}

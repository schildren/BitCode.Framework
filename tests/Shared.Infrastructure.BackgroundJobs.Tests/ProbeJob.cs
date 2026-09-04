using Quartz;

namespace BitCode.Framework.Shared.Infrastructure.BackgroundJobs.Tests;

public class ProbeJob(TaskCompletionSource executed) : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        executed.TrySetResult();
        return Task.CompletedTask;
    }
}

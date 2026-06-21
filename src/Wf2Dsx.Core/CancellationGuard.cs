namespace Wf2Dsx.Core;

public static class CancellationGuard
{
    public static async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User-requested shutdown is successful completion, not an application error.
        }
    }
}

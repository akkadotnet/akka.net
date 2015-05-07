using System;
using System.Threading.Tasks;

namespace Akka.Util.Internal
{
    public static class TaskExtensions
    {
        public static Task<TResult> CastTask<TTask, TResult>(this Task<TTask> task)
        {
            if (task.IsCompleted)
                return Task.FromResult((TResult) (object)task.Result);
            var tcs = new TaskCompletionSource<TResult>();
            if (task.IsFaulted)
                tcs.SetException(task.Exception);
            else
                task.ContinueWith(_ =>
                {
                    if (task.IsFaulted || task.Exception != null)
                        tcs.SetException(task.Exception);
                    else if (task.IsCanceled)
                        tcs.SetCanceled();
                    else
                        try
                        {
                            tcs.SetResult((TResult) (object) task.Result);
                        }
                        catch (Exception e)
                        {
                            tcs.SetException(e);
                        }
                }, TaskContinuationOptions.ExecuteSynchronously);
            return tcs.Task;
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

using System;
using System.Threading.Tasks;

namespace Akka.Tests.Util
{
    public static class TaskHelpers
    {
        public static async Task<bool> AwaitWithTimeout(this Task parentTask, TimeSpan timeout)
        {
            var delayed = Task.Delay(timeout);
            await Task.WhenAny(delayed, parentTask);
            return parentTask.IsCompleted;
        }
    }
}

using System.Threading.Tasks;

namespace Akka.Hosting.TestKit.Tests;

internal static class AsyncCompat
{
    public static Task ToTask(this Task task) => task;

    public static Task ToTask(this ValueTask task) => task.AsTask();
}

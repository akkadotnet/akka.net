using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    /// <summary>
    /// Persistent store for saving and retrieving <see cref="TestRunTree"/> instances
    /// from disk.
    /// </summary>
    public interface IPersistentTestRunStore
    {
        bool SaveTestRun(string filePath, TestRunTree data);

        bool TestRunExists(string filePath);

        TestRunTree FetchTestRun(string filePath);
    }
}

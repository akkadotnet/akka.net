namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    /// <summary>
    /// Logger implementation used inside the NodeTestRunner to write output to a semantic logging source
    /// </summary>
    public interface ITestRunnerLogger
    {
        void Write(object obj);
        void WriteLine(string formatStr, params object[] args);
    }
}

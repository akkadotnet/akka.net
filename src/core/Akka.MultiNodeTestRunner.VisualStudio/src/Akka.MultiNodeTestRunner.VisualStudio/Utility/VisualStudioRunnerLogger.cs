namespace Xunit.Runner.VisualStudio
{
    public class VisualStudioRunnerLogger : IRunnerLogger
    {
        static readonly object lockObject = new object();
        readonly LoggerHelper loggerHelper;

        public VisualStudioRunnerLogger(LoggerHelper loggerHelper)
        {
            this.loggerHelper = loggerHelper;
        }

        public object LockObject => lockObject;

        public void LogError(StackFrameInfo stackFrame, string message)
        {
            loggerHelper.LogError("{0}", message);
        }

        public void LogImportantMessage(StackFrameInfo stackFrame, string message)
        {
            loggerHelper.Log("{0}", message);
        }

        public void LogMessage(StackFrameInfo stackFrame, string message)
        {
            loggerHelper.Log("{0}", message);
        }

        public void LogWarning(StackFrameInfo stackFrame, string message)
        {
            loggerHelper.LogWarning("{0}", message);
        }
    }
}

namespace Xunit.Runner.VisualStudio
{
    public class DiagnosticMessageSink : DiagnosticEventSink
    {
        DiagnosticMessageSink() { }

        public static DiagnosticMessageSink ForDiagnostics(LoggerHelper log, string assemblyDisplayName, bool showDiagnostics)
        {
            var result = new DiagnosticMessageSink();

            if (showDiagnostics)
                result.DiagnosticMessageEvent += args => log.LogWarning("{0}: {1}", assemblyDisplayName, args.Message.Message);

            return result;
        }

        public static DiagnosticMessageSink ForInternalDiagnostics(LoggerHelper log, bool showDiagnostics)
        {
            var result = new DiagnosticMessageSink();

            if (showDiagnostics)
                result.DiagnosticMessageEvent += args => log.Log("{0}", args.Message.Message);

            return result;
        }
    }
}

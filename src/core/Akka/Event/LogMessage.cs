namespace Akka.Event
{
    public class LogMessage
    {
        private readonly ILogMessageFormatter _formatter;

        public string Format { get; private set; }
        public object[] Args { get; private set; }

        public LogMessage(ILogMessageFormatter formatter, string format, params object[] args)
        {
            _formatter = formatter;
            Format = format;
            Args = args;
        }

        public override string ToString()
        {
            return _formatter.Format(Format, Args);
        }
    }
}
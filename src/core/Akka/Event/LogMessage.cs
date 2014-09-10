namespace Akka.Event
{
    public class LogMessage
    {
        public string Format { get; private set; }
        public object[] Args { get; private set; }

        public LogMessage(string format, params object[] args)
        {
            Format = format;
            Args = args;
        }

        public override string ToString()
        {
            return string.Format(Format, Args);
        }
    }
}
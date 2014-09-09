namespace Akka.Event
{
    public class FormattedLogMessage
    {
        public string Format { get; private set; }
        public object[] Args { get; private set; }

        public FormattedLogMessage(string format, params object[] args)
        {
            Format = format;
            Args = args;
        }
    }
}
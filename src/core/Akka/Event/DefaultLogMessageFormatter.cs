namespace Akka.Event
{
    public class DefaultLogMessageFormatter : ILogMessageFormatter
    {
        public string Format(string format, params object[] args)
        {
            return string.Format(format, args);
        }
    }
}
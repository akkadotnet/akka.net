namespace Akka.Event
{
    public interface ILogMessageFormatter
    {
        string Format(string format, params object[] args);
    }
}
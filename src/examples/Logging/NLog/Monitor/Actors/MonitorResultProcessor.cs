using Akka.Actor;
using Akka.Event;
using Monitor.Actors.Events;
using Monitor.Extensions;

namespace Monitor.Actors
{
    /// <summary>
    /// Processes events from child-monitors of the parent supervisor
    /// </summary>
    public class MonitorResultProcessor : TypedActor, IHandle<MonitoringResult>
    {
        private readonly LoggingAdapter _logger = Logging.GetLogger(Context);

        public void Handle(MonitoringResult message)
        {
            if (message.Error)
            {
                _logger.Error("An error occured when trying to check the status of {0}. {1}",
                    message.Uri, message.Exception != null ?
                    message.Exception.ToMessageAndCompleteStacktrace() :
                    string.Empty);
            }
            else
            {
                _logger.Info("{0} responded in {1} [ms]", message.Uri, message.Time.TotalMilliseconds);
            }
        }
    }
}
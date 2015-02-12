using Akka.Actor;
using Akka.Event;
using System;

namespace Stashing
{
    /// <summary>
    /// This handler requires a database connection to process messages.
    /// If there is a timeout, it stashes messages until the connection is up again.
    /// </summary>
    public class RequestHandler : UntypedActor, WithUnboundedStash
    {
        // this property is automatically populated by Akka.NET
        public IStash Stash { get; set; }

        // a message we send to the actor itself to trigger a connecion check
        private readonly object CheckConnetionMessage = new object();
        
        LoggingAdapter log = Logging.GetLogger(Context);
        
        protected override void OnReceive(object message)
        {
            // for the first message, let's assume the 'Up' state
            Up(message);
        }

        // The 'Up' state runs when the database is up
        private void Up(object message)
        {
            var request = message as Request;

            if (request != null)
            {
                try
                {
                    // try to process the request
                    log.Info("Processing request {0}", request.ID);
                    ProcessRequest(request);
                }
                catch (TimeoutException)
                {
                    // if the database is down, we stash, become down and schedule a connection check
                    log.Warning("Timeout for request {0}, stashing...", request.ID);
                    Stash.Stash();
                    Become(Down);
                    ScheduleNextConnectionCheck();
                }
                catch (Exception ex)
                {
                    // for other exceptions, retrying won't help so we just log
                    log.Error(ex, "Fatal error for {0}, will not retry this one...", request.ID);
                }
            }
            else
            {
                Unhandled(request);
            }
        }

        // The 'Down' state runs when the databse is down
        private void Down(object message)
        {
            // check if we should check for database connection
            if (message == CheckConnetionMessage)
            {
                CheckConnection();
                return;
            }

            // if there is a request during 'Down', just stash
            var request = message as Request;

            if (request != null)
            {
                log.Info("Stashing request {0} due to database down", request.ID);
                Stash.Stash();
            }
            else
            {
                Unhandled(message);
            }
        }

        protected override void PreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);

            log.Info("RequestHandler is restarting, unstashing all items...");
            Stash.UnstashAll();
        }

        private Random _rnd = new Random();

        private void ProcessRequest(Request request)
        {
            // simulates a timeout 10% of the time
            if (_rnd.NextDouble() <= 0.10)
                throw new TimeoutException("Your database is down!");

            // every 30 requests, an unhandled exception will be thrown
            if (request.ID % 30 == 0)
                throw new Exception("There is a bug in your code!");
        }

        private void CheckConnection()
        {
            // simulates a connection check. once the connection
            // is down, it will come back up only 70% of the time
            var connectionRestored = _rnd.NextDouble() <= 0.7;

            if (connectionRestored)
            {
                log.Info("CheckConnection: connection is back, unstashing requests");
                Stash.UnstashAll();
                Become(Up);
            }
            else
            {
                log.Info("CheckConnection: connection down");
                ScheduleNextConnectionCheck();
            }
        }

        private void ScheduleNextConnectionCheck()
        {
            var self = Self;
            Context.System.Scheduler.ScheduleOnce(TimeSpan.FromSeconds(1), () => self.Tell(CheckConnetionMessage));
        }
    }
}

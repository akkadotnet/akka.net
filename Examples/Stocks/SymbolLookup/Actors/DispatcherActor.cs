using System;
using System.Net.Http;
using Pigeon.Actor;
using QDFeedParser;
using SymbolLookup.Actors.Messages;
using Failure = SymbolLookup.Actors.Messages.Failure;

namespace SymbolLookup.Actors
{
    /// <summary>
    /// Root actor used by the application
    /// </summary>
    public class DispatcherActor : TypedActor, IHandle<string>, IHandle<FullStockData>, IHandle<Failure>
    {
        private readonly EventHandler<FullStockData> _dataHandler;
        private readonly EventHandler<string> _statusHandler;
        public Props RssFeedProps = Props.Create(() => new SymbolRssActor(new HttpFeedFactory()));
        public Props StockQuoteProps = Props.Create(() => new StockQuoteActor(new HttpClient()));

        public DispatcherActor(EventHandler<FullStockData> dataHandler, EventHandler<string> statusHandler)
        {
            _dataHandler = dataHandler;
            _statusHandler = statusHandler;
        }

        protected override void PreStart()
        {
            //Create child actors for managing RSS feed and Stock quotes.
            Context.ActorOf(RssFeedProps, "symbolrss");
            Context.ActorOf(StockQuoteProps, "symbolquotes");
        }

        public void Handle(string message)
        {
            var symbols = message.Split(',');
            _statusHandler(this, string.Format("Downloading symbol data for symbols {0}", message));
            foreach (var symbol in symbols)
            {
                var stockActor = Context.ActorOf(Props.Create<StockActor>());
                stockActor.Tell(new DownloadSymbolData() { Symbol = symbol }, Self);
            }
        }

        public void Handle(FullStockData sd)
        {
            _statusHandler(this, string.Format("Received data for {0}", sd.Symbol));
            _dataHandler(this, sd);
            Sender.Stop(); //tell the sender to shut down
        }

        public void Handle(Failure fail)
        {
            var exception = fail.Cause;
            if (exception is AggregateException)
            {
                var agg = ((AggregateException)exception);
                exception = agg.InnerException;
                agg.Handle((exception1 => true));
            }

            _statusHandler(this, string.Format("Error {0} - {1}", fail.Child.Path, exception != null ? exception.Message : "no exception object"));
        }
    }
}

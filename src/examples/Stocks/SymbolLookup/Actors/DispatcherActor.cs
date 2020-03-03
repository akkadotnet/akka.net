//-----------------------------------------------------------------------
// <copyright file="DispatcherActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net.Http;
using Akka.Actor;
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
        private IActorRef rss = Context.ActorOf(Props.Create(() => new SymbolRssActor(new HttpFeedFactory())), "symbolrss");
        private IActorRef stock = Context.ActorOf(Props.Create(() => new StockQuoteActor(new HttpClient())), "symbolquotes");
        private int _stockActorNumber = 1;
        public DispatcherActor(EventHandler<FullStockData> dataHandler, EventHandler<string> statusHandler)
        {
            _dataHandler = dataHandler;
            _statusHandler = statusHandler;
        }

        public void Handle(string message)
        {
            var symbols = message.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries);
            _statusHandler(this, string.Format("Downloading symbol data for symbols {0}", message));
            foreach(var symbol in symbols)
            {
                var stockActor = Context.ActorOf(Props.Create<StockActor>(), "stock-" + _stockActorNumber + "-" + symbol);
                stockActor.Tell(new DownloadSymbolData {Symbol = symbol});
                _stockActorNumber++;
            }
        }

        public void Handle(FullStockData sd)
        {
            _statusHandler(this, string.Format("Received data for {0}", sd.Symbol));
            _dataHandler(this, sd);
            ((IInternalActorRef)Sender).Stop(); //tell the sender to shut down
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


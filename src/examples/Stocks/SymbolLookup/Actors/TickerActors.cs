//-----------------------------------------------------------------------
// <copyright file="TickerActors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net.Http;
using Akka.Actor;
using QDFeedParser;
using SymbolLookup.Actors.Messages;
using SymbolLookup.YahooFinance;
using Failure = SymbolLookup.Actors.Messages.Failure;

namespace SymbolLookup.Actors
{
    //Actor responsible for fetching a stock
    public class StockActor : TypedActor, 
        IHandle<CompletedHeadlinesDownload>, 
        IHandle<CompletedStockQuoteDownload>, 
        IHandle<DownloadSymbolData>
    {
        //these will get initialized during create/recreate
        private ActorSelection _headlineActor = Context.ActorSelection("../symbolrss");
        private ActorSelection _quoteActor = Context.ActorSelection("../symbolquotes");

        private string _symbol;
        private Quote _stockQuote;
        private IFeed _headlines;

        public void Handle(CompletedHeadlinesDownload message)
        {
            _headlines = message.Feed;

            //Finished processing! send the parent the full data payload
            if (_headlines != null && _stockQuote != null)
                Context.Parent.Tell(
                    new FullStockData() { Symbol = _symbol, Headlines = _headlines, Quote = _stockQuote });
        }

        public void Handle(CompletedStockQuoteDownload message)
        {
            _stockQuote = message.Quote;

            //Finished processing! send the parent the full data payload
            if (_headlines != null && _stockQuote != null)
                Context.Parent.Tell(
                    new FullStockData() { Symbol = _symbol, Headlines = _headlines, Quote = _stockQuote });
        }

        public void Handle(DownloadSymbolData message)
        {
            _symbol = message.Symbol;
            _headlineActor.Tell(message);
            _quoteActor.Tell(message);
        }
    }

    public class SymbolRssActor : UntypedActor
    {
        private readonly IFeedFactory _feedFactory;

        public SymbolRssActor(IFeedFactory feedFactory)
        {
            _feedFactory = feedFactory;
        }

        protected override void PreRestart(Exception cause, object message)
        {
            Context.Parent.Tell(new Failure(cause, Self));
        }

        protected override void OnReceive(object msg)
        {
            var symboldata = msg as DownloadSymbolData;
            if (symboldata != null)
            {
                var feedTask = _feedFactory.CreateFeedAsync(StockUriHelper.CreateHeadlinesRssUri(symboldata.Symbol));
                feedTask.Wait();
                Sender.Tell(new CompletedHeadlinesDownload() {Feed = feedTask.Result, Symbol = symboldata.Symbol});
            }
            else
                Unhandled(msg);
        }
    }

    public class StockQuoteActor : UntypedActor
    {
        private readonly HttpClient _client;

        public StockQuoteActor(HttpClient client)
        {
            _client = client;
        }

        protected override void PreRestart(Exception cause, object message)
        {
            Context.Parent.Tell(new Failure(cause, Self));
        }

        protected override void OnReceive(object msg)
        {
            var symboldata = msg as DownloadSymbolData;
            if (symboldata != null)
            {
                var quoteStrTask = _client.GetStringAsync(StockUriHelper.CreateStockQuoteUri(symboldata.Symbol));
                quoteStrTask.Wait();
                var quoteStr = quoteStrTask.Result;
                var quoteData = Newtonsoft.Json.JsonConvert.DeserializeObject<RootObject>(quoteStr);
                if (quoteData == null || quoteData.query == null || quoteData.query.results == null)
                {
                    //request failed for whatever reason, 
                }
                else
                {
                    Sender.Tell(
                            new CompletedStockQuoteDownload()
                            {
                                Quote = quoteData.query.results.quote,
                                Symbol = symboldata.Symbol
                            });
                }
            }
            else
                Unhandled(msg);
        }
    }
}


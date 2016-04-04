//-----------------------------------------------------------------------
// <copyright file="CompletedStockQuoteDownload.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using SymbolLookup.YahooFinance;

namespace SymbolLookup.Actors.Messages
{
    public class CompletedStockQuoteDownload
    {
        public string Symbol { get; set; }

        public Quote Quote { get; set; } 
    }
}


//-----------------------------------------------------------------------
// <copyright file="CompletedStockQuoteDownload.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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


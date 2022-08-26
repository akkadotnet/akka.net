﻿//-----------------------------------------------------------------------
// <copyright file="FullStockData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using QDFeedParser;
using SymbolLookup.YahooFinance;

namespace SymbolLookup.Actors.Messages
{
    public class FullStockData
    {
        public string Symbol { get; set; }

        public IFeed Headlines { get; set; }

        public Quote Quote { get; set; }
    }
}


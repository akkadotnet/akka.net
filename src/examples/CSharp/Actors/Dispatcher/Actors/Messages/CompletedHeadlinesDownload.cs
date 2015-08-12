﻿//-----------------------------------------------------------------------
// <copyright file="CompletedHeadlinesDownload.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using QDFeedParser;

namespace SymbolLookup.Actors.Messages
{
    public class CompletedHeadlinesDownload
    {
        public string Symbol { get; set; }
        public IFeed Feed { get; set; }
    }
}


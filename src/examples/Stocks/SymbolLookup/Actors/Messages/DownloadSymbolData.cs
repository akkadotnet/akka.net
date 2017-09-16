//-----------------------------------------------------------------------
// <copyright file="DownloadSymbolData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace SymbolLookup.Actors.Messages
{
    public class DownloadSymbolData
    {
        public string Symbol { get; set; }
    }
}


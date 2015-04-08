//-----------------------------------------------------------------------
// <copyright file="DownloadSymbolData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace SymbolLookup.Actors.Messages
{
    public class DownloadSymbolData
    {
        public string Symbol { get; set; }
    }
}

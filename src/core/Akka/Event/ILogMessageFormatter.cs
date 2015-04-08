//-----------------------------------------------------------------------
// <copyright file="ILogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Event
{
    public interface ILogMessageFormatter
    {
        string Format(string format, params object[] args);
    }
}


//-----------------------------------------------------------------------
// <copyright file="TeamCityLoggerActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.MultiNode.TestAdapter.Internal.Reporting
{
    internal class TeamCityLoggerActor : ReceiveActor
    {
        private readonly bool _unMuted = false;
        public TeamCityLoggerActor(bool unMuted)
        {
            _unMuted = unMuted;
            
            ReceiveAny(o =>
            {
                if (_unMuted)
                {
                    Console.WriteLine(o.ToString());
                }
            });
        }
    }
}

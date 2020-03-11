//-----------------------------------------------------------------------
// <copyright file="TeamCityLoggerActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    public class TeamCityLoggerActor : ReceiveActor
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

//-----------------------------------------------------------------------
// <copyright file="Logger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class Logger : ActorBase
    {
        private int _count;
        private string _msg;
        protected override bool Receive(object message)
        {
            var warning = message as Warning;
            if(warning != null && warning.Message is string)
            {
                _count++;
                _msg = (string)warning.Message;
                return true;
            }
            return false;
        }
    }
}


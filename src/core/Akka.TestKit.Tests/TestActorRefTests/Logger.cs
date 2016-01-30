//-----------------------------------------------------------------------
// <copyright file="Logger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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


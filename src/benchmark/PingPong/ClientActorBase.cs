//-----------------------------------------------------------------------
// <copyright file="ClientActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace PingPong
{
    public class ClientActorBase : ActorBase
    {
        private readonly IActorRef _actor;
        private readonly TaskCompletionSource<bool> _latch;
        private long _received;
        private readonly long _repeat;
        private long _sent;

        public ClientActorBase(IActorRef actor, long repeat, TaskCompletionSource<bool> latch)
        {
            _actor = actor;
            _repeat = repeat;
            _latch = latch;
        }

        protected override bool Receive(object message)
        {
            if(message is Messages.Msg)
            {
                _received++;
                if(_sent < _repeat)
                {
                    _actor.Tell(message);
                    _sent++;
                }
                else if(_received >= _repeat)
                {
                    //       Console.WriteLine("done {0}", Self.Path);
                    _latch.SetResult(true);
                }
                return true;
            }
            if(message is Messages.Run)
            {
                var msg = new Messages.Msg();
                for(int i = 0; i < Math.Min(1000, _repeat); i++)
                {
                    _actor.Tell(msg);
                    _sent++;
                }
                return true;
            }
            if(message is Messages.Started)
            {
                Sender.Tell(message);
                return true;
            }
            return false;
        }
    }
}


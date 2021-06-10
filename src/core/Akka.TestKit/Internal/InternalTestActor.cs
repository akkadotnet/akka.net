﻿//-----------------------------------------------------------------------
// <copyright file="InternalTestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Event;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// An actor that enqueues received messages to a <see cref="BlockingCollection{T}"/>.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class InternalTestActor : ActorBase
    {
        private readonly ITestOutputAdapter _output;
        private readonly ITestActorQueue<MessageEnvelope> _queue;
        private TestKit.TestActor.Ignore _ignore;
        private AutoPilot _autoPilot;
        private DelegatingSupervisorStrategy _supervisorStrategy = new DelegatingSupervisorStrategy();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queue">TBD</param>
        /// <param name="output">TBD</param>
        public InternalTestActor(ITestActorQueue<MessageEnvelope> queue, ITestOutputAdapter output)
        {
            _queue = queue;
            _output = output;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            try
            {
                _output?.WriteLine("TestActor received " + message);
            }
            catch (FormatException)
            {
                if (message is LogEvent evt && evt.Message is LogMessage msg)
                    _output?.WriteLine(
                        $"TestActor received a malformed formatted message. Template:[{msg.Format}], args:[{string.Join(",", msg.Args)}]");
                else
                    throw;
            }

            switch (message)
            {
                case TestActor.SetIgnore setIgnore:
                    _ignore = setIgnore.Ignore;
                    return true;
                case TestActor.Watch watch:
                    Context.Watch(watch.Actor);
                    return true;
                case TestActor.Unwatch unwatch:
                    Context.Unwatch(unwatch.Actor);
                    return true;
                case TestActor.SetAutoPilot setAutoPilot:
                    _autoPilot = setAutoPilot.AutoPilot;
                    return true;
                case TestActor.Spawn spawn:
                {
                    var actor = spawn.Apply(Context);
                    if (spawn._supervisorStrategy.HasValue)
                    {
                        _supervisorStrategy.Update(actor, spawn._supervisorStrategy.Value);
                    }
                    _queue.Enqueue(new RealMessageEnvelope(actor, Self));
                    return true;
                }
            }

            var actorRef = Sender;
            if(_autoPilot != null)
            {
                var newAutoPilot = _autoPilot.Run(actorRef, message);
                if(!(newAutoPilot is KeepRunning))
                    _autoPilot = newAutoPilot;
            }
            if(_ignore == null || !_ignore(message))
                _queue.Enqueue(new RealMessageEnvelope(message, actorRef));
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            var self = Self;
            foreach(var messageEnvelope in _queue.ToList())
            {
                var messageSender = messageEnvelope.Sender;
                var message = messageEnvelope.Message;
                Context.System.DeadLetters.Tell(new DeadLetter(message, messageSender, self), messageSender);
            }
        }
    }

    public interface ITestOutputAdapter
    {
        void WriteLine(string message);
        void WriteLine(string format, params object[] args);
    }
}

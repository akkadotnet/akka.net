//-----------------------------------------------------------------------
// <copyright file="InternalTestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// An actor that enqueues received messages to a <see cref="BlockingCollection{T}"/>.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    internal sealed class InternalTestActor : UntypedActor
    {
        private readonly ChannelWriter<MessageEnvelope> _queue;
        private TestActor.Ignore _ignore;
        private AutoPilot _autoPilot;
        private readonly DelegatingSupervisorStrategy _supervisorStrategy = new();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queue">TBD</param>
        public InternalTestActor(ChannelWriter<MessageEnvelope> queue)
        {
            _queue = queue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override void OnReceive(object message)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("TestActor received " + message);
            }
            catch (FormatException)
            {
                if (message is LogEvent { Message: LogMessage msg })
                    System.Diagnostics.Debug.WriteLine(
                        $"TestActor received a malformed formatted message. Template:[{msg.Format}], args:[{string.Join(",", msg.Unformatted())}]");
                else
                    throw;
            }

            switch (message)
            {
                case TestActor.SetIgnore setIgnore:
                    _ignore = setIgnore.Ignore;
                    return;
                case TestActor.Watch watch:
                    Context.Watch(watch.Actor);
                    Sender.Tell(TestActor.WatchAck.Instance);
                    return;
                case TestActor.Unwatch unwatch:
                    Context.Unwatch(unwatch.Actor);
                    Sender.Tell(TestActor.UnwatchAck.Instance);
                    return;
                case TestActor.SetAutoPilot setAutoPilot:
                    _autoPilot = setAutoPilot.AutoPilot;
                    return;
                case TestActor.Spawn spawn:
                {
                    var actor = spawn.Apply(Context);
                    if (spawn._supervisorStrategy.HasValue)
                    {
                        _supervisorStrategy.Update(actor, spawn._supervisorStrategy.Value);
                    }
                    var wrote = _queue.TryWrite(new RealMessageEnvelope(actor, Self));
                    if (!wrote)
                    {
                        throw new InvalidOperationException("Failed to write to internal TestActor queue");
                    }
                    return;
                }
            }

            var actorRef = Sender;
            if(_autoPilot != null)
            {
                var newAutoPilot = _autoPilot.Run(actorRef, message);
                if(newAutoPilot is not KeepRunning)
                    _autoPilot = newAutoPilot;
            }

            if (_ignore != null && _ignore(message)) return;
            {
                var wrote =  _queue.TryWrite(new RealMessageEnvelope(message, actorRef));
                if (!wrote)
                {
                    throw new InvalidOperationException("Failed to write to internal TestActor queue");
                }
            }
        }

        protected override SupervisorStrategy SupervisorStrategy() => _supervisorStrategy;
    }
}

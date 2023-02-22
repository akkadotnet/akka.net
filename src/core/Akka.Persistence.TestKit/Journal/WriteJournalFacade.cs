// //-----------------------------------------------------------------------
// // <copyright file="ReadJournalFacade.cs" company="Akka.NET Project">
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Common.Journal;

#nullable enable
namespace Akka.Persistence.TestKit.Journal
{
    public class WriteJournalFacade: WriteJournalBase
    {
        private readonly IActorRef _underlying;

        public Func<IUntypedActorContext, object, bool>? ReceiveOverride { get; set; } = null;

        public WriteJournalFacade(Config config)
        {
            var underlyingId = config.GetString("underlying-journal-id");
            _underlying = Persistence.Instance.Apply(Context.System).JournalFor(underlyingId);
        }

        private Func<IActorContext, object, bool>? _receiveInterceptor;

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case SetFacadeReceive rcv:
                    _receiveInterceptor = rcv.Receive;
                    return true;
                case EventReplayFailure:
                case ReplayMessagesFailure:
                case Status.Failure:
                    Sender.Tell(message);
                    return true;
            }
            
            if (_receiveInterceptor?.Invoke(Context, message) ?? false)
                return true;
            
            _underlying.Tell(message, Sender);
            return true;
        }
    }

    public sealed class SetFacadeReceive
    {
        public SetFacadeReceive(Func<IActorContext, object, bool> receive)
        {
            Receive = receive;
        }

        public Func<IActorContext, object, bool> Receive { get; }
    }
}
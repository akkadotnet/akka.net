// //-----------------------------------------------------------------------
// // <copyright file="SqlQueryThrottler.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using static Akka.Persistence.Query.Sql.SqlQueryConstants;

namespace Akka.Persistence.Query.Sql
{
    /// <summary>
    /// Rate limits the number of active Akka.Persistence.Query instances in order to prevent the journal
    /// from opening too many concurrent connections.
    /// </summary>
    internal sealed class SqlQueryThrottler : ReceiveActor
    {
        private readonly int _maxConcurrentQueries;
        private readonly IActorRef _journalRef;

        private IActorRef _throttler;

        public SqlQueryThrottler(IActorRef journalRef, int maxConcurrentQueries = 30)
        {
            _journalRef = journalRef;
            _maxConcurrentQueries = maxConcurrentQueries;
            
            Receive<IJournalRequest>(req =>
            {
                _throttler.Tell((req, Sender));
            });
        }
        
        protected override void PreStart()
        {
            var materializer = Context.Materializer();
            var (actor, source) = Source.ActorRef<(IJournalRequest req, IActorRef sender)>(20000, OverflowStrategy.DropHead)
                .PreMaterialize(materializer);
            _throttler = actor;

            source
                .SelectAsyncUnordered(_maxConcurrentQueries, async request =>
                {
                    object r;
                    try
                    {
                        r = await _journalRef.Ask(request.req, DefaultQueryTimeout).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        r = new Status.Failure(ex);
                    }

                    return (r, request.sender);
                })
                .RunForeach(tuple =>
                {
                    tuple.sender.Tell(tuple.r);
                }, materializer);
        }
    }
}
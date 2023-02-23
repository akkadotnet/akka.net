// //-----------------------------------------------------------------------
// // <copyright file="SqlQueryThrottler.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;

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
            
            Receive<IJournalQueryRequest>(req =>
            {
                _throttler.Tell((req, Sender));
            });
        }
        
        protected override void PreStart()
        {
            var materializer = Context.Materializer();
            var (actor, source) = Source.ActorRef<(IJournalQueryRequest req, IActorRef sender)>(20000, OverflowStrategy.DropHead)
                .PreMaterialize(materializer);
            _throttler = actor;

            source
                .Where(req => req.req.Deadline > DateTime.UtcNow)
                .SelectAsyncUnordered(_maxConcurrentQueries, async request =>
                {
                    object r;
                    try
                    {
                        r = await _journalRef.Ask(request.req, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        r = new Status.Failure(ex);
                    }

                    return (request.req.Deadline, r, request.sender);
                })
                .Where(tuple => tuple.Deadline > DateTime.UtcNow)
                .RunForeach(tuple =>
                {
                    tuple.sender.Tell(tuple.r);
                }, materializer);
        }
    }
}
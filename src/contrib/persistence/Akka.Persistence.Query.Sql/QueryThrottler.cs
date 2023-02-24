// //-----------------------------------------------------------------------
// // <copyright file="QueryThrottler.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Persistence.Query.Sql;

/// <summary>
/// Request token from throttler
/// </summary>
internal sealed class RequestQueryStart
{
    public static readonly RequestQueryStart Instance = new RequestQueryStart();
    private RequestQueryStart() { }
}

/// <summary>
/// Token request granted
/// </summary>
internal sealed class QueryStartGranted
{
    public static readonly QueryStartGranted Instance = new QueryStartGranted();
    private QueryStartGranted() { }
}

/// <summary>
/// Return token to throttler
/// </summary>
internal sealed class ReturnQueryStart
{
    public static readonly ReturnQueryStart Instance = new ReturnQueryStart();
    private ReturnQueryStart() { }
}

/// <summary>
/// Token bucket throttler that grants queries permissions to run each iteration
/// </summary>
/// <remarks>
/// Works identically to the RecoveryPermitter built into Akka.Persistence.
/// </remarks>
internal sealed class QueryThrottler : ReceiveActor
{
    private readonly LinkedList<IActorRef> _pending = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private int _usedPermits;
    private int _maxPendingStats;

    public QueryThrottler(int maxPermits)
    {
        MaxPermits = maxPermits;
        
        Receive<RequestQueryStart>(_ =>
        {
            Context.Watch(Sender);
            if (_usedPermits >= MaxPermits)
            {
                if (_pending.Count == 0)
                    _log.Debug("Exceeded max-concurrent-queries[{0}]. First pending {1}", MaxPermits, Sender);
                _pending.AddLast(Sender);
                _maxPendingStats = Math.Max(_maxPendingStats, _pending.Count);
            }
            else
            {
                QueryStartGranted(Sender);   
            }
        });
        
        Receive<ReturnQueryStart>(_ =>
        {
            ReturnQueryPermit(Sender);
        });
        
        Receive<Terminated>(terminated =>
        {
            if (!_pending.Remove(terminated.ActorRef))
            {
                ReturnQueryPermit(terminated.ActorRef);
            }
        });
    }

    public int MaxPermits { get; }
    
    private void QueryStartGranted(IActorRef actorRef)
    {
        _usedPermits++;
        actorRef.Tell(Sql.QueryStartGranted.Instance);
    }
    
    private void ReturnQueryPermit(IActorRef actorRef)
    {
        _usedPermits--;
        Context.Unwatch(actorRef);

        if (_usedPermits < 0)
            throw new IllegalStateException("Permits must not be negative");

        if (_pending.Count > 0)
        {
            var popRef = _pending.First?.Value;
            _pending.RemoveFirst();
            QueryStartGranted(popRef);
        }

        if (_pending.Count != 0 || _maxPendingStats <= 0)
            return;
        
        if(_log.IsDebugEnabled)
            _log.Debug("Drained pending recovery permit requests, max in progress was [{0}], still [{1}] in progress", _usedPermits + _maxPendingStats, _usedPermits);
        _maxPendingStats = 0;
    }
}
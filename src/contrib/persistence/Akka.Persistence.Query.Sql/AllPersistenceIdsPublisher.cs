//-----------------------------------------------------------------------
// <copyright file="AllPersistenceIdsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sql
{
    internal sealed class CurrentPersistenceIdsPublisher : ActorPublisher<string>, IWithUnboundedStash, IWithTimers
    {
        public static Props Props(IActorRef writeJournal, QuerySettings settings)
        {
            return Actor.Props.Create(() => new CurrentPersistenceIdsPublisher(writeJournal, settings));
        }

        private readonly string _retryKey = nameof(_retryKey);
        private readonly string _operationTimeoutKey = nameof(_operationTimeoutKey);
        
        private int _retries;
        private readonly int _maxRetries;
        private readonly TimeSpan _maxBackoff;
        private readonly float _backoffMultiplier;
        private readonly TimeSpan _backoffTime;
        private readonly TimeSpan _operationTimeout;
        
        private readonly IActorRef _journalRef;

        private readonly DeliveryBuffer<string> _buffer;
        private readonly ILoggingAdapter _log;

        public IStash Stash { get; set; }
        public ITimerScheduler Timers { get; set; }

        public CurrentPersistenceIdsPublisher(IActorRef journalRef, QuerySettings settings)
        {
            _journalRef = journalRef;
            _buffer = new DeliveryBuffer<string>(OnNext);
            _log = Context.GetLogger();
            
            _operationTimeout = settings.OperationTimeout;
            _maxRetries = settings.MaxRetries;
            _maxBackoff = settings.MaxBackoff;
            _backoffMultiplier = settings.BackoffMultiplier;
            _backoffTime = settings.BackoffTime;
        }

        protected override void PostStop()
        {
            base.PostStop();
            Timers.CancelAll();
        }
        
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    Become(Initializing);
                    _journalRef.Tell(new SelectCurrentPersistenceIds(0, Self, _operationTimeout));
                    Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                default:
                    return false;
            }
        }

        private bool Initializing(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds current:
                    _retries = 0;
                    _buffer.AddRange(current.AllPersistenceIds);
                    _buffer.DeliverBuffer(TotalDemand);

                    if (_buffer.IsEmpty)
                    {
                        OnCompleteThenStop();
                        return true;
                    }

                    Become(Active);
                    Stash.UnstashAll();
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                case Status.Failure msg:
                    RetryQuery(msg.Cause);
                    return true;
                    
                case OperationTimedOut _:
                    Exception ex;
                    try
                    {
                        throw new TimeoutException("Query timed out");
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }
                    RetryQuery(ex);
                    return true;
                
                default:
                    Stash.Stash();
                    return true;
            }
        }
        
        private bool Retrying(object message)
        {
            switch (message)
            {
                case Retry _:
                    Become(Initializing);
                    _journalRef.Tell(new SelectCurrentPersistenceIds(0, Self, _operationTimeout));
                    Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    Stash.Stash();
                    return true;
            }
        }
        
        private void RetryQuery(Exception ex)
        {
            _retries++;
            if (_retries > _maxRetries)
            {
                _buffer.DeliverBuffer(TotalDemand);
                _log.Debug(ex, "Current persistence id query failed due to [{0}]. Maximum retry reached", ex.Message);
                OnErrorThenStop(ex);
                return;
            }
            
            Become(Retrying);
            var retryTime = new TimeSpan((long)(_backoffTime.Ticks * (_backoffMultiplier * _retries)));
            if (retryTime > _maxBackoff)
                retryTime = _maxBackoff;
            _log.Debug(ex, "Current persistence id query failed due to [{0}]. Retrying in {1} ms ({2}/{3})", 
                ex.Message, retryTime.TotalMilliseconds, _retries, _maxRetries);
            Timers.StartSingleTimer(_retryKey, Retry.Instance, retryTime);
        }

        private bool Active(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds _:
                    // Ignore duplicate CurrentPersistenceIds response
                    return true;
                
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    if (_buffer.IsEmpty)
                        OnCompleteThenStop();
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                default:
                    return false;
            }
        }
    }

    internal sealed class LivePersistenceIdsPublisher : ActorPublisher<string>, IWithTimers
    {
        private sealed class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue() { }
        }

        public static Props Props(TimeSpan refreshInterval, IActorRef writeJournal, int maxBufferSize, QuerySettings settings)
        {
            return Actor.Props.Create(() => new LivePersistenceIdsPublisher(refreshInterval, writeJournal, maxBufferSize, settings));
        }

        private readonly string _retryKey = nameof(_retryKey);
        private readonly string _operationTimeoutKey = nameof(_operationTimeoutKey);

        private int _retries;
        private readonly int _maxRetries;
        private readonly TimeSpan _maxBackoff;
        private readonly float _backoffMultiplier;
        private readonly TimeSpan _backoffTime;
        private readonly TimeSpan _operationTimeout;

        private readonly int _maxBufferSize;
        private long _lastOrderingOffset;
        private readonly ICancelable _tickCancelable;
        private readonly IActorRef _journalRef;
        private readonly DeliveryBuffer<string> _buffer;
        private readonly ILoggingAdapter _log;

        public ITimerScheduler Timers { get; set; }

        public LivePersistenceIdsPublisher(TimeSpan refreshInterval, IActorRef journalRef, int maxBufferSize, QuerySettings settings)
        {
            _journalRef = journalRef;
            _log = Context.GetLogger();
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                refreshInterval, 
                refreshInterval, 
                Self, 
                Continue.Instance, 
                Self);
            _maxBufferSize = maxBufferSize;
            _buffer = new DeliveryBuffer<string>(OnNext);
            
            _operationTimeout = settings.OperationTimeout;
            _maxRetries = settings.MaxRetries;
            _maxBackoff = settings.MaxBackoff;
            _backoffMultiplier = settings.BackoffMultiplier;
            _backoffTime = settings.BackoffTime;
        }

        protected override void PostStop()
        {
            Timers.CancelAll();
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    Become(Waiting);
                    _journalRef.Tell(new SelectCurrentPersistenceIds(_lastOrderingOffset, Self, _operationTimeout));
                    Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
                    return true;
                
                case Continue _:
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                default:
                    return false;
            }
        }

        private bool Waiting(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds current:
                    // drop stale reply
                    if (_lastOrderingOffset > current.HighestOrderingNumber)
                    {
                        Become(Active);
                        return true;
                    }
                    
                    _lastOrderingOffset = current.HighestOrderingNumber;
                    _buffer.AddRange(current.AllPersistenceIds);
                    _buffer.DeliverBuffer(TotalDemand);

                    Become(Active);
                    return true;
                
                case Continue _:
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                case Status.Failure msg:
                    RetryQuery(msg.Cause);
                    return true;
                
                case OperationTimedOut _:
                    Exception ex;
                    try
                    {
                        throw new TimeoutException($"Query timed out");
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }
                    RetryQuery(ex);
                    return true;
                    
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    return true;
                
                default:
                    return false;
            }
        }

        private bool Active(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds _:
                    // Ignore duplicate CurrentPersistenceIds response
                    return true;
                
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    return true;
                
                case Continue _:
                    if (_buffer.Length < _maxBufferSize / 2)
                    {
                        Become(Waiting);
                        _journalRef.Tell(new SelectCurrentPersistenceIds(_lastOrderingOffset, Self, _operationTimeout));
                        Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
                    }
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                case Status.Failure msg:
                    _log.Info(msg.Cause, "Unexpected failure received");
                    return true;
                
                default:
                    return false;
            }
        }
        
        private bool Retrying(object message)
        {
            switch (message)
            {
                case Retry _:
                    Become(Waiting);
                    _journalRef.Tell(new SelectCurrentPersistenceIds(_lastOrderingOffset, Self, _operationTimeout));
                    Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
                    return true;
                
                case Continue _:
                    // no-op
                    return true;
                
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                case Status.Failure msg:
                    _log.Info(msg.Cause, "Unexpected failure received");
                    return true;
                
                default:
                    return false;
            }
        }
        
        private void RetryQuery(Exception ex)
        {
            _retries++;
            if (_retries > _maxRetries)
            {
                _buffer.DeliverBuffer(TotalDemand);
                _log.Debug(ex, "Live persistence id query failed due to [{0}]. Maximum retry reached", ex.Message);
                OnErrorThenStop(ex);
                return;
            }
            
            Become(Retrying);
            var retryTime = new TimeSpan((long)(_backoffTime.Ticks * (_backoffMultiplier * _retries)));
            if (retryTime > _maxBackoff)
                retryTime = _maxBackoff;
            _log.Debug(ex, "Live persistence id query failed due to [{0}]. Retrying in {1} ms ({2}/{3})", 
                ex.Message, retryTime.TotalMilliseconds, _retries, _maxRetries);
            Timers.StartSingleTimer(_retryKey, Retry.Instance, retryTime);
        }
    }
}

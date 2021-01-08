//-----------------------------------------------------------------------
// <copyright file="Inbox.Actor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class InboxActor : ActorBase
    {
        private readonly InboxQueue<object> _messages = new InboxQueue<object>();
        private readonly InboxQueue<IQuery> _clients = new InboxQueue<IQuery>();

        private readonly ISet<IQuery> _clientsByTimeout = new SortedSet<IQuery>(DeadlineComparer.Instance);
        private bool _printedWarning;

        private object _currentMessage;
        private Select? _currentSelect;
        private (TimeSpan, ICancelable)? _currentDeadline;

        private readonly int _size;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly IQuery[] _matched = new IQuery[1];

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="size">TBD</param>
        public InboxActor(int size)
        {
            _size = size;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="query">TBD</param>
        private void EnqueueQuery(IQuery query)
        {
            var q = query.WithClient(Sender);
            _clients.Enqueue(q);
            _clientsByTimeout.Add(q);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        private void EnqueueMessage(object message)
        {
            if (_messages.Count < _size)
            {
                _messages.Enqueue(message);
            }
            else
            {
                if (!_printedWarning)
                {
                    _log.Warning("Dropping message: Inbox size has been exceeded, use akka.actor.inbox.inbox-size to increase maximum allowed inbox size. Current is {0}", _size);
                    _printedWarning = true;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="query">TBD</param>
        /// <returns>TBD</returns>
        private bool ClientPredicate(IQuery query)
        {
            if (query is Select select)
                return select.Predicate(_currentMessage);

            return query is Get;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        private bool MessagePredicate(object message)
        {
            if (_currentSelect.HasValue)
                return _currentSelect.Value.Predicate(message);

            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if (message is Get get)
            {
                if (_messages.Count == 0)
                {
                    EnqueueQuery(get);
                }
                else
                {
                    Sender.Tell(_messages.Dequeue());
                }
            }
            else if (message is Select select)
            {
                if (_messages.Count == 0)
                {
                    EnqueueQuery(select);
                }
                else
                {
                    _currentSelect = select;
                    var firstMatch = _messages.DequeueFirstOrDefault(MessagePredicate);
                    if (firstMatch == null)
                    {
                        EnqueueQuery(select);
                    }
                    else
                    {
                        Sender.Tell(firstMatch);
                    }
                    _currentSelect = null;
                }
            }
            else if (message is StartWatch startwatch)
            {
                if (startwatch.Message == null)
                    Context.Watch(startwatch.Target);
                else
                    Context.WatchWith(startwatch.Target, startwatch.Message);
            }
            else if (message is StopWatch stopwatch)
            {
                Context.Unwatch(stopwatch.Target);
            }
            else if (message is Kick)
            {
                var now = Context.System.Scheduler.MonotonicClock;
                var overdue = _clientsByTimeout.TakeWhile(q => q.Deadline < now);

                foreach (var query in overdue)
                {
                    query.Client.Tell(new Status.Failure(new TimeoutException("Deadline passed")));
                }
                _clients.RemoveAll(q => q.Deadline < now);

                var afterDeadline = _clientsByTimeout.Where(q => q.Deadline >= now);
                _clientsByTimeout.IntersectWith(afterDeadline);
            }
            else
            {
                if (_clients.Count == 0)
                {
                    EnqueueMessage(message);
                }
                else
                {
                    _currentMessage = message;
                    var firstMatch = _matched[0] = _clients.DequeueFirstOrDefault(ClientPredicate); //TODO: this should work as DequeueFirstOrDefault
                    if (firstMatch != null)
                    {
                        _clientsByTimeout.ExceptWith(_matched);
                        firstMatch.Client.Tell(message);
                    }
                    else
                    {
                        EnqueueMessage(message);
                    }
                    _currentMessage = null;
                }
            }

            if (_clients.Count == 0)
            {
                if (_currentDeadline != null)
                {
                    _currentDeadline.Value.Item2.Cancel();
                    _currentDeadline = null;
                }
            }
            else
            {
                var next = _clientsByTimeout.FirstOrDefault();
                if (next != null)
                {
                    if (_currentDeadline != null)
                    {
                        _currentDeadline.Value.Item2.Cancel();
                        _currentDeadline = null;
                    }

                    var delay = next.Deadline - Context.System.Scheduler.MonotonicClock;

                    if (delay > TimeSpan.Zero)
                    {
                        var cancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new Kick(), Self);
                        _currentDeadline = (next.Deadline, cancelable);
                    }
                    else
                    {
                        // The client already timed out, Kick immediately
                        Self.Tell(new Kick(), ActorRefs.NoSender);
                    }
                }
            }

            return true;
        }
    }
}

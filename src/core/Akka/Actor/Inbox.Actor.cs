//-----------------------------------------------------------------------
// <copyright file="Inbox.Actor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;

namespace Akka.Actor
{
    internal class InboxActor : ActorBase
    {
        private readonly InboxQueue<object> _messages = new InboxQueue<object>();
        private readonly InboxQueue<IQuery> _clients = new InboxQueue<IQuery>();

        private readonly ISet<IQuery> _clientsByTimeout = new SortedSet<IQuery>(DeadlineComparer.Instance);
        private bool _printedWarning;

        private object _currentMessage;
        private Select? _currentSelect;
        private Tuple<TimeSpan, ICancelable> _currentDeadline;

        private int _size;
        private ILoggingAdapter _log = Context.GetLogger();

        public InboxActor(int size)
        {
            _size = size;
        }

        public void EnqueueQuery(IQuery q)
        {
            var query = q.WithClient(Sender);
            _clients.Enqueue(query);
            _clientsByTimeout.Add(query);
        }

        public void EnqueueMessage(object msg)
        {
            if (_messages.Count < _size)
            {
                _messages.Enqueue(msg);
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

        public bool ClientPredicate(IQuery query)
        {
            if (query is Get)
            {
                return true;
            }
            else if (query is Select)
            {
                return ((Select)query).Predicate(_currentMessage);
            }
            else
            {
                return false;
            }
        }

        public bool MessagePredicate(object msg)
        {
            if (!_currentSelect.HasValue)
            {
                return false;
            }

            return _currentSelect.Value.Predicate(msg);
        }

        protected override bool Receive(object message)
        {
            message.Match()
                .With<Get>(get =>
                {
                    if (_messages.Count == 0)
                    {
                        EnqueueQuery(get);
                    }
                    else
                    {
                        Sender.Tell(_messages.Dequeue());
                    }
                })
                .With<Select>(select =>
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
                })
                .With<StartWatch>(sw => Context.Watch(sw.Target))
                .With<StopWatch>(sw => Context.Unwatch(sw.Target))
                .With<Kick>(() =>
                {
                    var now = Context.System.Scheduler.MonotonicClock;
                    var overdue = _clientsByTimeout.TakeWhile(q => q.Deadline < now);
                    foreach (var query in overdue)
                    {
                        query.Client.Tell(new Status.Failure(new TimeoutException("Deadline passed")));
                    }
                    _clients.RemoveAll(q => q.Deadline < now);
                    var afterDeadline = _clientsByTimeout.Where(q => q.Deadline >= now).ToList();
                    _clientsByTimeout.IntersectWith(afterDeadline);
                }).Default(msg =>
                {
                    if (_clients.Count == 0)
                    {
                        EnqueueMessage(msg);
                    }
                    else
                    {
                        _currentMessage = msg;
                        var firstMatch = _clients.DequeueFirstOrDefault(ClientPredicate); //TODO: this should work as DequeueFirstOrDefault
                        if (firstMatch != null)
                        {
                            _clientsByTimeout.ExceptWith(new[] { firstMatch });
                            firstMatch.Client.Tell(msg);
                        }
                        else
                        {
                            EnqueueMessage(msg);
                        }
                        _currentMessage = null;
                    }
                });

            if (_clients.Count == 0)
            {
                if (_currentDeadline != null)
                {
                    _currentDeadline.Item2.Cancel();
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
                        _currentDeadline.Item2.Cancel();
                        _currentDeadline = null;
                    }

                    TimeSpan delay = next.Deadline - Context.System.Scheduler.MonotonicClock;

                    if (delay > TimeSpan.Zero)
                    {
                        var cancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new Kick(), Self);
                        _currentDeadline = Tuple.Create(next.Deadline, cancelable);
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

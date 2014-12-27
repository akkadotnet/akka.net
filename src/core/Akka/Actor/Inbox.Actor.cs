using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Event;

namespace Akka.Actor
{
    internal class InboxActor : ActorBase, IActorLogging
    {
        private readonly InboxQueue<object> _messages = new InboxQueue<object>();
        private readonly InboxQueue<IQuery> _clients = new InboxQueue<IQuery>();

        private readonly ISet<IQuery> _clientsByTimeout = new SortedSet<IQuery>(DeadlineComparer.Instance);
        private bool _printedWarning;

        private object _currentMessage;
        private Select? _currentSelect;
        private Tuple<DateTime, CancellationTokenSource> _currentDeadline;

        private int size;
        private LoggingAdapter log = Context.GetLogger();
        public LoggingAdapter Log { get { return log; } }

        public InboxActor(int size)
        {
            this.size = size;
        }

        public void EnqueueQuery(IQuery q)
        {
            var query = q.WithClient(Sender);
            _clients.Enqueue(query);
            _clientsByTimeout.Add(query);
        }

        public void EnqueueMessage(object msg)
        {
            if (_messages.Count < size)
            {
                _messages.Enqueue(msg);
            }
            else
            {
                if (!_printedWarning)
                {
                    Log.Warning("Dropping message: Inbox size has been exceeded, use akka.actor.inbox.inbox-size to increase maximum allowed inbox size. Current is " + size);
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
                    var now = DateTime.Now;
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
                    }
                    var cancellationTokenSource = new CancellationTokenSource();
                    Context.System.Scheduler.ScheduleOnce(next.Deadline - DateTime.Now, Self, new Kick(),
                        cancellationTokenSource.Token);

                    _currentDeadline = Tuple.Create(next.Deadline, cancellationTokenSource);
                }
            }

            return true;
        }
    }

}
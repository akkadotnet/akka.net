using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public class ConcurrentSet<T> : ISet<T>
    {
        private ConcurrentDictionary<T, T> content = new ConcurrentDictionary<T, T>();

        public bool Add(T item)
        {
            return content.TryAdd(item, item);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        public void UnionWith(IEnumerable<T> other)
        {
            throw new NotImplementedException();
        }

        void ICollection<T>.Add(T item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            content.Clear();
        }

        public bool Contains(T item)
        {
            return content.ContainsKey(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get { return content.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public bool Remove(T item)
        {
            T tmp;
            return content.TryRemove(item, out tmp);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return content.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return content.Values.GetEnumerator();
        }
    }

    public abstract class EventBus<TEvent,TClassifier,TSubscriber>
    {
        private ConcurrentDictionary<TSubscriber, ConcurrentSet<TClassifier>> subscribers = new ConcurrentDictionary<TSubscriber, ConcurrentSet<TClassifier>>();

        protected string SimpleName(object source)
        {
            return source.GetType().Name;
        }

        public virtual void Subscribe(TSubscriber subscriber, TClassifier classifier)
        {
            while (true)
            {
                ConcurrentSet<TClassifier> set;
                if (subscribers.TryGetValue(subscriber, out set))
                {
                    set.Add(classifier);
                    if (!(subscribers[subscriber] == set))
                    {
                        //failed to add the new set, some other thread raced us. (unsubscribe all -> add new)
                        continue;
                    }
                }
                else
                {
                    set = new ConcurrentSet<TClassifier>();
                    set.Add(classifier);
                    if (!subscribers.TryAdd(subscriber, set))
                    {
                        //failed to add the new set, some other thread raced us.
                        continue;
                    }
                }
                break;
            }
        }

        public virtual void Unsubscribe(TSubscriber subscriber)
        {
            var set = subscribers[subscriber];
            if (set != null)
            {
                set.Clear();
            }
            else
            {
                //ignore
            }
        }

        public virtual void Unsubscribe(TSubscriber subscriber,TClassifier classifier)
        {
            var set = subscribers[subscriber];
            if (set != null)
            {
                set.Remove(classifier);
            }
            else
            {
                //ignore
            }
        }

        protected abstract void Publish(TEvent @event,TSubscriber subscriber);

        protected abstract bool Classify(TEvent @event, TClassifier classifier);        

        public virtual void Publish(TEvent @event)
        {
            foreach (var kvp in subscribers)
            {
                var subscriber = kvp.Key;
                var set = kvp.Value;
                foreach (var classifier in set)
                {
                    if (Classify(@event,classifier))
                    {
                        this.Publish(@event, subscriber);
                    }
                }
            }
        }
    }

    //public abstract class Classifier
    //{
    //    public static readonly Classifier EveryClassifier = new EveryClassifier();

    //    public abstract bool Classify(EventMessage @event);
    //}

    //public class EveryClassifier : Classifier
    //{
    //    public override bool Classify(EventMessage @event)
    //    {
    //        return true;
    //    }
    //}

    //public class SubClassifier : Classifier
    //{
    //    private Type type;
    //    public SubClassifier(Type type)
    //    {
    //        this.type = type;
    //    }
    //    public override bool Classify(EventMessage @event)
    //    {
    //        return type.IsAssignableFrom(@event.GetType());
    //    }
    //}

    //public class SubClassifier<T> : SubClassifier
    //{
    //    public SubClassifier()
    //        : base(typeof(T))
    //    { }
    //}

    //public abstract class Subscriber
    //{
    //    public abstract void Publish(EventMessage @event);

    //    public static implicit operator Subscriber(ActorRef actor)
    //    {
    //        return new ActorSubscriber(actor);
    //    }
    //}

    //public class ActorSubscriber : Subscriber
    //{
    //    private ActorRef actor;
    //    public ActorSubscriber(ActorRef actor)
    //    {
    //        this.actor = actor;
    //    }
    //    public override void Publish(EventMessage @event)
    //    {
    //        actor.Tell(@event);
    //    }
    //}

    //public class BlockingCollectionSubscriber : Subscriber
    //{
    //    private BlockingCollection<EventMessage> queue;
    //    public BlockingCollectionSubscriber(BlockingCollection<EventMessage> queue)
    //    {
    //        this.queue = queue;
    //    }
    //    public override void Publish(EventMessage @event)
    //    {
    //        this.queue.Add(@event);
    //    }
    //}

    //public class ActionSubscriber : Subscriber
    //{
    //    private Action<EventMessage> action;
    //    public ActionSubscriber(Action<EventMessage> action)
    //    {
    //        this.action = action;
    //    }

    //    public override void Publish(EventMessage @event)
    //    {
    //        action(@event);
    //    }
    //}
}

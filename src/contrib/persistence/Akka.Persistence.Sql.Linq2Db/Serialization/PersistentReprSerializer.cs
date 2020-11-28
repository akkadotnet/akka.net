using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Linq2Db.Utility;
using LanguageExt;
using static LanguageExt.Prelude;

namespace Akka.Persistence.Sql.Linq2Db.Serialization
{
    public abstract class PersistentReprSerializer<T>
    {
        public List<Akka.Util.Try<List<T>>> Serialize(
            IEnumerable<AtomicWrite> messages, long timeStamp = 0)
        {
            return messages.Select(aw =>
            {
                //Hot Path:
                //Instead of using Try.From (worst, extra delegate)
                //Or Try/Catch (other penalties)
                //We cheat here and use fast state checking of Try<T>/Option<T>.
                //Also, if we are only persisting a single event
                //We will only enumerate if we have more than one element.
                var payloads =
                        (aw.Payload as IImmutableList<IPersistentRepresentation>
                        );
                if (payloads is null)
                {
                    return new Util.Try<List<T>>(
                        new ArgumentNullException(
                            $"{aw.PersistenceId} received empty payload for sequenceNr range " +
                            $"{aw.LowestSequenceNr} - {aw.HighestSequenceNr}"));
                }
                //Preallocate our list; In the common case
                //This saves a tiny bit of garbage
                var retList = new List<T>(payloads.Count);
                if (payloads.Count == 1)
                    {
                        // If there's only one payload
                        // Don't allocate the enumerable.
                        var ser = Serialize(payloads[0], timeStamp);
                        var opt = ser.Success;
                        if (opt.HasValue)
                        {
                            retList.Add(opt.Value);    
                        }
                        else
                        {
                            return new Util.Try<List<T>>(ser.Failure.Value);
                        }
                    }
                else
                {
                    foreach (var p in payloads)
                    {
                        var ser = Serialize(p, timeStamp);
                        var opt = ser.Success;
                        if (opt.HasValue)
                        {
                            retList.Add(opt.Value);
                        }
                        else
                        {
                            return new Util.Try<List<T>>(ser.Failure.Value);
                        }
                    }
                }

                return new Util.Try<List<T>>(retList);
                
            }).ToList();
        }


        public Seq<Akka.Util.Try<Seq<T>>> SerializeSeq(
            IEnumerable<AtomicWrite> messages)
        {
            return messages.Select(aw =>
                {
                    return Util.Try<Seq<T>>.From(() =>
                    {
                        var serialized =
                            (aw.Payload as IEnumerable<IPersistentRepresentation>)
                            .Select(p=> Serialize(p).Get());
                        return serialized.ToSeq();
                    });
                }).ToSeq();
            
            //return messages.Select(aw =>
            //{
            //    var serialized = (aw.Payload as IEnumerable<IPersistentRepresentation>)
            //        .Select(Serialize).
            //})
            //return Seq(messages.Select(aw =>
            //{
            //    var serialized =
            //        (aw.Payload as IEnumerable<IPersistentRepresentation>)
            //        .Select(Serialize);
            //    return TrySeq.SequenceSeq(serialized);
            //}));
        }


        public Akka.Util.Try<T> Serialize(IPersistentRepresentation persistentRepr, long timeStamp = 0)
        {
            switch (persistentRepr.Payload)
            {
                case Tagged t:
                    return Serialize(persistentRepr.WithPayload(t.Payload), t.Tags, timeStamp);
                default:
                    return Serialize(persistentRepr,
                        ImmutableHashSet<string>.Empty, timeStamp);
            }
        }

        protected abstract Akka.Util.Try<T> Serialize(
            IPersistentRepresentation persistentRepr,
            IImmutableSet<string> tTags, long timeStamp =0);

        protected abstract Akka.Util.Try<(IPersistentRepresentation, IImmutableSet<string>, long)>
            Deserialize(
                T t);
    }
}
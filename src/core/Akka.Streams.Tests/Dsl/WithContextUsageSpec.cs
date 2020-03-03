//-----------------------------------------------------------------------
// <copyright file="WithContextUsageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class WithContextUsageSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        private Sink<Offset, TestSubscriber.Probe<Offset>> _commitOffsets;
        
        public WithContextUsageSpec(ITestOutputHelper output) : base(output)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
            _commitOffsets = Commit(Offset.Uninitialized);
        }

        [Fact]
        public void Context_propagation_used_for_committing_offsets_must_be_able_to_commit_on_offset_change()
        {
            var input = GenInput(0, 10);
            var expectedOffsets = Enumerable.Range(0, 10).Select(ix => new Offset(ix)).ToArray();
            expectedOffsets = expectedOffsets.Take(expectedOffsets.Length - 1).ToArray();
            
            Func<Record, Record> f = record => new Record(record.Key, record.Value + 1);
            var expectedRecords = ToRecords(input).Select(f).ToArray();

            var src = CreateSourceWithContext(input)
                .Select(f)
                .AsSource();

            var probe = this.CreateSubscriberProbe<Record>();
            
            src.Select(t => t.Item1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            probe.Request(input.Length)
                .ExpectNextN(expectedRecords)
                .ExpectComplete();

            src.Select(t => t.Item2)
                .ToMaterialized(_commitOffsets, Keep.Right)
                .Run(Materializer)
                .Request(input.Length)
                .ExpectNextN(expectedOffsets)
                .ExpectComplete();
        }

        [Fact]
        public void Context_propagation_used_for_committing_offsets_must_only_commit_filtered_offsets_on_offset_change()
        {
            var input = GenInput(0, 10);

            Func<Record, bool> f = record => record.Key.EndsWith("2");
            
            var expectedOffsets = input.Where(cm => f(cm.Record)).Select(cm => new Offset(cm.Offset.Offset)).ToArray();
            expectedOffsets = expectedOffsets.Take(expectedOffsets.Length - 1).ToArray();
            var expectedRecords = ToRecords(input).Where(f);

            var src = CreateSourceWithContext(input)
                .Where(f)
                .AsSource();

            var probe = this.CreateSubscriberProbe<Record>();
            
            src.Select(t => t.Item1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            var s = probe.ExpectSubscription();
            s.Request(input.Length);
            probe.ExpectNextN(expectedRecords);
            probe.ExpectComplete();

            src.Select(t => t.Item2)
                .ToMaterialized(_commitOffsets, Keep.Right)
                .Run(Materializer)
                .Request(input.Length)
                .ExpectNextN(expectedOffsets)
                .ExpectComplete(); 
        }

        [Fact]
        public void Context_propagation_used_for_committing_offsets_must_only_commit_after_SelectConcat_on_offset_change()
        {
            var input = GenInput(0, 10);

            Func<Record, IEnumerable<Record>> f = record => new[]{record,record,record};
            
            var expectedOffsets = Enumerable.Range(0,10).Select(x => new Offset(x)).ToArray();
            expectedOffsets = expectedOffsets.Take(expectedOffsets.Length - 1).ToArray();
            var expectedRecords = ToRecords(input).SelectMany(f).ToArray();

            var src = CreateSourceWithContext(input)
                .SelectConcat(f)
                .AsSource();

            var probe = this.CreateSubscriberProbe<Record>();
            
            src.Select(t => t.Item1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            var s = probe.ExpectSubscription();
            s.Request(expectedRecords.Length);
            probe.ExpectNextN(expectedRecords);
            probe.ExpectComplete();

            src.Select(t => t.Item2)
                .ToMaterialized(_commitOffsets, Keep.Right)
                .Run(Materializer)
                .Request(input.Length)
                .ExpectNextN(expectedOffsets)
                .ExpectComplete(); 
        }

        [Fact]
        public void Context_propagation_used_for_committing_offsets_must_commit_offsets_after_Grouped_on_offset_change()
        {
            const int groupSize = 2;
            var input = GenInput(0, 10);

            Func<Record, IEnumerable<Record>> f = record => new[]{record,record,record};
            
            var expectedOffsets = Enumerable.Range(0,10).Grouped(groupSize).Select(x => new Offset(x.Last())).ToArray();
            expectedOffsets = expectedOffsets.Take(expectedOffsets.Length - 1).ToArray();
            var expectedRecords = ToRecords(input).Grouped(groupSize).Select(r => new MultiRecord(r.ToArray())).ToArray();

            var src = CreateSourceWithContext(input)
                .Grouped(groupSize)
                .Select(r => new MultiRecord(r))
                .SelectContext(x => x.Last())
                .AsSource();

            var probe = this.CreateSubscriberProbe<MultiRecord>();
            
            src.Select(t => t.Item1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            var s = probe.ExpectSubscription();
            s.Request(expectedRecords.Length);
            probe.ExpectNextN(expectedRecords);
            probe.ExpectComplete();

            src.Select(t => t.Item2)
                .ToMaterialized(_commitOffsets, Keep.Right)
                .Run(Materializer)
                .Request(input.Length)
                .ExpectNextN(expectedOffsets)
                .ExpectComplete();
        }

        [Fact]
        public void Context_propagation_used_for_committing_offsets_must_commit_offsets_after_SelectConcat_plus_Grouped_on_offset_change()
        {
            const int groupSize = 2;
            var input = GenInput(0, 10);

            Func<Record, IEnumerable<Record>> f = record => new[]{record,record,record};
            
            // the SelectConcat creates bigger lists than the groups, which is why all offsets are seen.
            // (The mapContext selects the last offset in a group)
            var expectedOffsets = Enumerable.Range(0,10).Select(x => new Offset(x)).ToArray();
            expectedOffsets = expectedOffsets.Take(expectedOffsets.Length - 1).ToArray();
            var expectedRecords = ToRecords(input).SelectMany(f).Grouped(groupSize).Select(r => new MultiRecord(r.ToArray())).ToArray();

            var src = CreateSourceWithContext(input)
                .SelectConcat(f)
                .Grouped(groupSize)
                .Select(r => new MultiRecord(r))
                .SelectContext(x => x.Last())
                .AsSource();

            var probe = this.CreateSubscriberProbe<MultiRecord>();
            
            src.Select(t => t.Item1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            var s = probe.ExpectSubscription();
            s.Request(expectedRecords.Length);
            probe.ExpectNextN(expectedRecords);
            probe.ExpectComplete();

            src.Select(t => t.Item2)
                .ToMaterialized(_commitOffsets, Keep.Right)
                .Run(Materializer)
                .Request(input.Length)
                .ExpectNextN(expectedOffsets)
                .ExpectComplete(); 
        }

        private static string GenKey(int i) => $"k{i}";
        private static string GenValue(int i) => $"v{i}";

        private static IEnumerable<Record> ToRecords(params CommittableMessage<Record>[] messages) =>
            messages.Select(m => m.Record);

        private static CommittableMessage<Record>[] GenInput(int start, int end) =>
            Enumerable.Range(start, end)
                .Select(i =>
                    new CommittableMessage<Record>(new Record(GenKey(i), GenValue(i)), new CommittableOffsetImpl(i)))
                .ToArray();

        private static SourceWithContext<Offset, Record, NotUsed> CreateSourceWithContext(
            params CommittableMessage<Record>[] messages) =>
            CommittableConsumer.CommittableSource(messages)
                .AsSourceWithContext(m => new Offset(m.Offset.Offset))
                .Select(m => m.Record);

        private Sink<TCtx, TestSubscriber.Probe<TCtx>> Commit<TCtx>(TCtx uninitialized) where TCtx: IEquatable<TCtx>
        {
            var testSink = this.CreateSubscriberProbe<TCtx>();
            return Flow.Create<TCtx>()
                .MapMaterializedValue(_ => testSink)
                .StatefulSelectMany<TCtx, TCtx, TCtx, TestSubscriber.Probe<TCtx>>(() =>
                {
                    var prev = uninitialized;
                    return ctx =>
                    {
                        var res = (!prev.Equals(uninitialized) && !ctx.Equals(prev)) 
                            ? new[] { prev } 
                            : Enumerable.Empty<TCtx>();
                        prev = ctx;
                        return res;
                    };
                }).To(Sink.FromSubscriber(testSink));
        }

        #region internal classes

        sealed class Offset : IEquatable<Offset>
        {
            public static readonly Offset Uninitialized = new Offset(-1);
            public int Value { get; }

            public Offset(int value)
            {
                Value = value;
            }

            public bool Equals(Offset other)
            {
                return Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is Offset other && Equals(other);
            }

            public override int GetHashCode() => Value;
            public override string ToString() => $"Offset({Value})";
        }

        sealed class Record : IEquatable<Record>
        {
            public string Key { get; }
            public string Value { get; }

            public Record(string key, string value)
            {
                Key = key;
                Value = value;
            }

            public bool Equals(Record other)
            {
                return string.Equals(Key, other.Key) && string.Equals(Value, other.Value);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is Record other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Key != null ? Key.GetHashCode() : 0) * 397) ^ (Value != null ? Value.GetHashCode() : 0);
                }
            }
            
            public override string ToString() => $"Record(key:{Key}, value:{Value})";
        }

        sealed class Committed<T>
        {
            public T Record { get; }
            public Offset Offset { get; }

            public Committed(T record, Offset offset)
            {
                Record = record;
                Offset = offset;
            }
            public override string ToString() => $"Committed(record:{Record}, offset:{Offset})";
        }

        sealed class MultiRecord : IEquatable<MultiRecord>
        {
            public IReadOnlyList<Record> Records { get; }

            public MultiRecord(IReadOnlyList<Record> records)
            {
                Records = records;
            }

            public override string ToString() => $"MultiRecord({string.Join(", ", Records)})";

            public bool Equals(MultiRecord other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                if (Records.Count != other.Records.Count) return false;
                
                for (int i = 0; i < Records.Count; i++)
                {
                    if (Records[i] != other.Records[i]) return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is MultiRecord other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Records != null ? Records.GetHashCode() : 0);
            }
        }

        interface ICommittable
        {
            void Commit();
        }

        interface ICommittableOffset : ICommittable
        {
            int Offset { get; }
        }

        sealed class CommittableOffsetImpl : ICommittableOffset
        {
            public CommittableOffsetImpl(int offset)
            {
                Offset = offset;
            }

            public void Commit() { }

            public int Offset { get; }
            public override string ToString() => $"CommittableOffsetImpl({Offset})";
        }

        sealed class CommittableMessage<T>
        {
            public T Record { get; }
            public ICommittableOffset Offset { get; }

            public CommittableMessage(T record, ICommittableOffset offset)
            {
                Record = record;
                Offset = offset;
            }

            public override string ToString() => $"CommittableMessage(record:{Record}, offset:{Offset})";
        }

        static class CommittableConsumer
        {
            public static Source<CommittableMessage<Record>, NotUsed> CommittableSource(
                params CommittableMessage<Record>[] committableMessages) =>
                Source.From(committableMessages);
        }

        #endregion
    }
}

//-----------------------------------------------------------------------
// <copyright file="FixedBufferSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Streams.Implementation;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Buffer = Akka.Streams.Implementation.Buffer;

namespace Akka.Streams.Tests.Implementation
{
    public class FixedBufferSpec : AkkaSpec
    {
        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_start_as_empty(int size)
        {
            var buf = FixedSizeBuffer.Create<NotUsed>(size);
            buf.IsEmpty.Should().BeTrue();
            buf.IsFull.Should().BeFalse();
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_become_nonempty_after_enqueuing(int size)
        {
            var buf = FixedSizeBuffer.Create<string>(size);
            buf.Enqueue("test");
            buf.IsEmpty.Should().BeFalse();
            buf.IsFull.Should().Be(size == 1);
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_become_full_after_size_elements_are_enqueued(int size)
        {
            var buf = FixedSizeBuffer.Create<string>(size);
            for (var i = 0; i < size; i++)
                buf.Enqueue("test");
            buf.IsEmpty.Should().BeFalse();
            buf.IsFull.Should().BeTrue();
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_become_empty_after_enqueuing_and_tail_drop(int size)
        {
            var buf = FixedSizeBuffer.Create<string>(size);
            buf.Enqueue("test");
            buf.DropTail();
            buf.IsEmpty.Should().BeTrue();
            buf.IsFull.Should().BeFalse();
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_become_empty_after_enqueuing_and_head_drop(int size)
        {
            var buf = FixedSizeBuffer.Create<string>(size);
            buf.Enqueue("test");
            buf.DropHead();
            buf.IsEmpty.Should().BeTrue();
            buf.IsFull.Should().BeFalse();
        }
        
        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_drop_head_properly(int size)
        {
            var buf = FixedSizeBuffer.Create<int>(size);
            for (var elem = 1; elem <= size; elem++)
                buf.Enqueue(elem);

            buf.DropHead();
            for (var elem = 2; elem <= size; elem++)
                buf.Dequeue().Should().Be(elem);
        }
        
        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_drop_tail_properly(int size)
        {
            var buf = FixedSizeBuffer.Create<int>(size);
            for (var elem = 1; elem <= size; elem++)
                buf.Enqueue(elem);

            buf.DropTail();

            for (var elem = 1; elem <= size - 1; elem++)
                buf.Dequeue().Should().Be(elem);
        }
        
        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_become_non_full_after_tail_dropped_from_full_buffer(int size)
        {
            var buf = FixedSizeBuffer.Create<string>(size);
            for (var elem = 1; elem <= size; elem++)
                buf.Enqueue("test");

            buf.DropTail();

            buf.IsEmpty.Should().Be(size == 1);
            buf.IsFull.Should().BeFalse();
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_become_non_full_after_head_dropped_from_full_buffer(int size)
        {
            var buf = FixedSizeBuffer.Create<string>(size);
            for (var elem = 1; elem <= size; elem++)
                buf.Enqueue("test");

            buf.DropHead();

            buf.IsEmpty.Should().Be(size == 1);
            buf.IsFull.Should().BeFalse();
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_work_properly_with_full_range_filling_draining_cycles(int size)
        {
            var buf = FixedSizeBuffer.Create<int>(size);
            for (var i = 0; i < 10; i++)
            {
                buf.IsEmpty.Should().BeTrue();
                buf.IsFull.Should().BeFalse();
                for (var elem = 1; elem <= size; elem++)
                    buf.Enqueue(elem);
                buf.IsEmpty.Should().BeFalse();
                buf.IsFull.Should().BeTrue();
                for (var elem = 1; elem <= size; elem++)
                    buf.Dequeue().Should().Be(elem);
            }
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void FixedSizeBuffer_must_work_when_indexes_wrap_around_at_Int_MaxValue(int size)
        {
            IBuffer<int> buf;

            if (((size - 1) & size) == 0)
                buf= new CheatPowerOfTwoFixedSizeBuffer(size);
            else
               buf = new CheatModuloFixedSizeBuffer(size);

            for (var i = 0; i < 10; i++)
            {
                buf.IsEmpty.Should().BeTrue();
                buf.IsFull.Should().BeFalse();
                for (var elem = 1; elem <= size; elem++)
                    buf.Enqueue(elem);
                buf.IsEmpty.Should().BeFalse();
                buf.IsFull.Should().BeTrue();
                for (var elem = 1; elem <= size; elem++)
                    buf.Dequeue().Should().Be(elem);
            }
        }
        
        private class CheatPowerOfTwoFixedSizeBuffer : PowerOfTwoFixedSizeBuffer<int>
        {
            public CheatPowerOfTwoFixedSizeBuffer(int size) : base(size)
            {
                ReadIndex = int.MaxValue;
                WriteIndex = int.MaxValue;
            }
        }

        private class CheatModuloFixedSizeBuffer : ModuloFixedSizeBuffer<int>
        {
            public CheatModuloFixedSizeBuffer(int size) : base(size)
            {
                ReadIndex = int.MaxValue;
                WriteIndex = int.MaxValue;
            }
        }

        public static IEnumerable<object[]> Sizes
        {
            get
            {
                yield return new object[] { 1 };
                yield return new object[] { 3 };
                yield return new object[] { 4 };
            }
        }

        private ActorMaterializerSettings Default => ActorMaterializerSettings.Create(Sys);

        [Fact]
        public void Buffer_factory_must_set_default_to_one_billion_for_MaxFixedBufferSize()
        {
            Default.MaxFixedBufferSize.Should().Be(1000000000);
        }

        [Fact]
        public void Buffer_factory_must_produce_BoundedBuffers_when_capacity_greater_than_MaxFixedBufferSize()
        {
            Buffer.Create<int>(int.MaxValue, Default).Should().BeOfType<BoundedBuffer<int>>();
        }

        [Fact]
        public void Buffer_factory_must_produce_FixedSizeBuffers_when_capacity_lower_than_MaxFixedBufferSize()
        {
            Buffer.Create<int>(1000, Default).Should().BeOfType<ModuloFixedSizeBuffer<int>>();
            Buffer.Create<int>(1024, Default).Should().BeOfType<PowerOfTwoFixedSizeBuffer<int>>();
        }

        [Fact]
        public void Buffer_factory_must_produce_FixedSizeBuffers_when_MaxFixedBufferSize_lower_than_BoundedBufferSize()
        {
            var settings = Default.WithMaxFixedBufferSize(9);

            Buffer.Create<int>(5, settings).Should().BeOfType<ModuloFixedSizeBuffer<int>>();
            Buffer.Create<int>(10, settings).Should().BeOfType<ModuloFixedSizeBuffer<int>>();
            Buffer.Create<int>(16, settings).Should().BeOfType<PowerOfTwoFixedSizeBuffer<int>>();
        }
    }
}

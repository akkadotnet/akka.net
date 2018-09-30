#region copyright
// //-----------------------------------------------------------------------
// // <copyright file="InputBufferBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------
#endregion

using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using Akka.Streams.Implementation;

namespace Akka.Benchmarks.Streams
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class InputBufferBenchmarks
    {
        private IBuffer<int> _moduloBuffer;
        private IBuffer<int> _powerOfTwoBuffer;
        private IBuffer<int> _boundedBuffer;
        
        private const int Init = 20;

        [GlobalSetup]
        public void Setup()
        {
            _moduloBuffer = new ModuloFixedSizeBuffer<int>(128);
            _powerOfTwoBuffer = new PowerOfTwoFixedSizeBuffer<int>(128);
            _boundedBuffer = new BoundedBuffer<int>(128);

            for (int i = 0; i < Init; i++)
            {
                _moduloBuffer.Enqueue(i);
                _powerOfTwoBuffer.Enqueue(i);
                _boundedBuffer.Enqueue(i);
            }
        }

        [Benchmark]
        [Arguments(1)]
        [Arguments(10)]
        [Arguments(100)]
        public void ModuloFixedSizeBufferEnqueue(int operations)
        {
            for (int i = 0; i < operations; i++)
                _moduloBuffer.Enqueue(i);
        }

        [Benchmark]
        [Arguments(1)]
        [Arguments(10)]
        [Arguments(100)]
        public void PowerOfTwoFixedSizeBufferEnqueue(int operations)
        {
            for (int i = 0; i < operations; i++)
                _powerOfTwoBuffer.Enqueue(i);
        }
        
        [Benchmark]
        [Arguments(1)]
        [Arguments(10)]
        [Arguments(100)]
        public void BoundedSizeBufferEnqueue(int operations)
        {
            for (int i = 0; i < operations; i++)
                _boundedBuffer.Enqueue(i);
        }

        [Benchmark]
        public int ModuloFixedSizeBufferDequeue()
        {
            int last = 0;
            for (int i = 0; i < Init; i++)
                last = _moduloBuffer.Dequeue();
            return last;
        }

        [Benchmark]
        public int PowerOfTwoFixedSizeBufferDequeue()
        {
            int last = 0;
            for (int i = 0; i < Init; i++)
                last = _powerOfTwoBuffer.Dequeue();
            return last;
        }

        [Benchmark]
        public int BoundedSizeBufferDequeue()
        {
            int last = 0;
            for (int i = 0; i < Init; i++)
                last = _boundedBuffer.Dequeue();
            return last;
        }

        [Benchmark]
        [Arguments(1)]
        [Arguments(10)]
        [Arguments(100)]
        public int ModuloFixedSizeBufferEnqueueDequeue(int operations)
        {
            for (int i = 0; i < operations; i++)
                _moduloBuffer.Enqueue(i);

            int last = 0;
            for (int i = 0; i < operations; i++)
                last = _moduloBuffer.Dequeue();
            return last;
        }

        [Benchmark]
        [Arguments(1)]
        [Arguments(10)]
        [Arguments(100)]
        public int PowerOfTwoFixedSizeBufferEnqueueDequeue(int operations)
        {
            for (int i = 0; i < operations; i++)
                _powerOfTwoBuffer.Enqueue(i);

            int last = 0;
            for (int i = 0; i < operations; i++)
                last = _powerOfTwoBuffer.Dequeue();
            return last;
        }

        [Benchmark]
        [Arguments(1)]
        [Arguments(10)]
        [Arguments(100)]
        public int BoundedSizeBufferEnqueueDequeue(int operations)
        {
            for (int i = 0; i < operations; i++)
                _boundedBuffer.Enqueue(i);

            int last = 0;
            for (int i = 0; i < operations; i++)
                last = _boundedBuffer.Dequeue();
            return last;
        }

    }
}
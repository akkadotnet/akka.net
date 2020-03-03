//-----------------------------------------------------------------------
// <copyright file="SimpleDnsCacheSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.IO;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.IO
{

    public class SimpleDnsCacheSpec
    {
        private class SimpleDnsCacheTestDouble : SimpleDnsCache
        {
            private readonly AtomicReference<long> _clock;

            public SimpleDnsCacheTestDouble(AtomicReference<long> clock)
            {
                _clock = clock;
            }

            protected override long Clock()
            {
                return _clock.Value;
            }
        }

        [Fact]
        public void Cache_should_not_reply_with_expired_but_not_yet_swept_out_entries()
        {
            var localClock = new AtomicReference<long>(0);
            var cache = new SimpleDnsCacheTestDouble(localClock);
            var cacheEntry = Dns.Resolved.Create("test.local", System.Net.Dns.GetHostEntryAsync("127.0.0.1").Result.AddressList);
            cache.Put(cacheEntry, 5000);

            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(0, 4999);
            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(4999, 5000);
            cache.Cached("test.local").ShouldBe(null);

        }

        [Fact]
        public void Cache_should_sweep_out_expired_entries_on_cleanup()
        {
            var localClock = new AtomicReference<long>(0);
            var cache = new SimpleDnsCacheTestDouble(localClock);
            var cacheEntry = Dns.Resolved.Create("test.local", System.Net.Dns.GetHostEntryAsync("127.0.0.1").Result.AddressList);
            cache.Put(cacheEntry, 5000);

            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(0, 5000);
            cache.Cached("test.local").ShouldBe(null);
            localClock.CompareAndSet(5000, 0);
            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(0, 5000);
            cache.CleanUp();
            cache.Cached("test.local").ShouldBe(null);
            localClock.CompareAndSet(5000, 0);
            cache.Cached("test.local").ShouldBe(null);
           
        }

        [Fact]
        public void Cache_should_be_updated_with_the_latest_resolved()
        {
            var localClock = new AtomicReference<long>(0);
            var cache = new SimpleDnsCacheTestDouble(localClock);
            var cacheEntryOne = Dns.Resolved.Create("test.local", System.Net.Dns.GetHostEntryAsync("127.0.0.1").Result.AddressList);
            var cacheEntryTwo = Dns.Resolved.Create("test.local", System.Net.Dns.GetHostEntryAsync("127.0.0.1").Result.AddressList);
            long ttl = 500;
            cache.Put(cacheEntryOne, ttl);
            cache.Cached("test.local").ShouldBe(cacheEntryOne);
            cache.Put(cacheEntryTwo, ttl);
            cache.Cached("test.local").ShouldBe(cacheEntryTwo);
        }


        //This version was replaced with a new version which is restricted to reference types, 
        //therefore we use the old version here.
        private class AtomicReference<T>
        {
            /// <summary>
            /// Sets the initial value of this <see cref="AtomicReference{T}"/> to <paramref name="originalValue"/>.
            /// </summary>
            public AtomicReference(T originalValue)
            {
                atomicValue = originalValue;
            }

            /// <summary>
            /// Default constructor
            /// </summary>
            public AtomicReference()
            {
                atomicValue = default(T);
            }

            // ReSharper disable once InconsistentNaming
            protected T atomicValue;

            /// <summary>
            /// The current value of this <see cref="AtomicReference{T}"/>
            /// </summary>
            public T Value
            {
                get
                {
                    Interlocked.MemoryBarrier();
                    return atomicValue;
                }
                set
                {
                    Interlocked.MemoryBarrier();
                    atomicValue = value;
                    Interlocked.MemoryBarrier();
                }
            }

            /// <summary>
            /// If <see cref="Value"/> equals <paramref name="expected"/>, then set the Value to
            /// <paramref name="newValue"/>.
            /// </summary>
            /// <returns><c>true</c> if <paramref name="newValue"/> was set</returns>
            public bool CompareAndSet(T expected, T newValue)
            {
                //special handling for null values
                if (Value == null)
                {
                    if (expected == null)
                    {
                        Value = newValue;
                        return true;
                    }
                    return false;
                }

                if (Value.Equals(expected))
                {
                    Value = newValue;
                    return true;
                }
                return false;
            }

            #region Conversion operators

            /// <summary>
            /// Implicit conversion operator = automatically casts the <see cref="AtomicReference{T}"/> to an instance of <typeparamref name="T"/>.
            /// </summary>
            public static implicit operator T(AtomicReference<T> aRef)
            {
                return aRef.Value;
            }

            /// <summary>
            /// Implicit conversion operator = allows us to cast any type directly into a <see cref="AtomicReference{T}"/> instance.
            /// </summary>
            /// <param name="newValue"></param>
            /// <returns></returns>
            public static implicit operator AtomicReference<T>(T newValue)
            {
                return new AtomicReference<T>(newValue);
            }

            #endregion
        }
    }
}

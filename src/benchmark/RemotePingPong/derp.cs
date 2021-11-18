// //-----------------------------------------------------------------------
// // <copyright file="UnfairSemaphoreV2.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Blargh
{
    [StructLayout(LayoutKind.Sequential)]
        public sealed class UnfairSemaphoreV2
        {
            public const int MaxWorker = 0x7FFF;

            private static readonly int ProcessorCount = Environment.ProcessorCount;

            // We track everything we care about in a single 64-bit struct to allow us to
            // do CompareExchanges on this for atomic updates.
            private struct SemaphoreStateV2
            {
                private const byte CurrentSpinnerCountShift = 0;
                private const byte CountForSpinnerCountShift = 16;
                private const byte WaiterCountShift = 32;
                private const byte CountForWaiterCountShift = 48;
                
                //Ugh. So, Older versions of .NET,
                //for whatever reason, don't have
                //Interlocked compareexchange for ULong.
                public long _data;

                private SemaphoreStateV2(ulong data)
                {
                    unchecked
                    {
                        _data = (long)data;    
                    }
                }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void AddSpinners(ushort value)
                {
                    Debug.Assert(value <= uint.MaxValue - Spinners);
                    unchecked
                    {
                        _data += (long)value << CurrentSpinnerCountShift;    
                    }
                }
                
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void DecrSpinners(ushort value)
                {
                    Debug.Assert(value >= ushort.MinValue + Spinners);
                    unchecked
                    {
                        _data -= (long)value << CurrentSpinnerCountShift;    
                    }
                }

                private uint GetUInt32Value(byte shift) => (uint)(_data >> shift);
                private void SetUInt32Value(uint value, byte shift) 
                {
                    unchecked
                    {
                        _data = (_data & ~((long)uint.MaxValue << shift)) | ((long)value << shift);    
                    }
                    
                }
                    
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private ushort GetUInt16Value(byte shift) => (ushort)(_data >> shift);

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private void SetUInt16Value(ushort value, byte shift)
                {
                    unchecked
                    {
                        _data = (_data & ~((long)ushort.MaxValue << shift)) | ((long)value << shift);    
                    }
                    
                }
                    
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private byte GetByteValue(byte shift) => (byte)(_data >> shift);

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private void SetByteValue(byte value, byte shift)
                {
                    unchecked
                    {
                        _data = (_data & ~((long)byte.MaxValue << shift)) | ((long)value << shift);    
                    }
                    
                }
                    

                //how many threads are currently spin-waiting for this semaphore?
                public ushort Spinners
                {
                    get { return GetUInt16Value(CurrentSpinnerCountShift); }
                    //set{SetUInt16Value(value,CurrentSpinnerCountShift);}

                }

                //how much of the semaphore's count is available to spinners?
                //[FieldOffset(2)]
                public ushort CountForSpinners
                {
                    get { return GetUInt16Value(CountForSpinnerCountShift); }
                    //set{SetUInt16Value(value,CountForSpinnerCountShift);}
                }
                
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void IncrementCountForSpinners(ushort count)
                {
                    Debug.Assert(CountForSpinners+count < ushort.MaxValue);
                    unchecked
                    {
                        _data += (long)count << CountForSpinnerCountShift;    
                    }
                    
                }

                public void DecrementCountForSpinners()
                {
                    Debug.Assert(CountForSpinners != 0);
                    unchecked
                    {
                        _data -= (long)1 << CountForSpinnerCountShift;    
                    }
                    
                }


                //how many threads are blocked in the OS waiting for this semaphore?
                public ushort Waiters
                {
                    get { return GetUInt16Value(WaiterCountShift); }
                    //set{SetUInt16Value(value,WaiterCountShift);}
                }
                
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void AddWaiters(ushort value)
                {
                    Debug.Assert(value <= uint.MaxValue - Waiters);
                    unchecked
                    {
                        _data += (long)value << WaiterCountShift;    
                    }
                }
                
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void DecrWaiters(ushort value)
                {
                    Debug.Assert(value >= ushort.MinValue + Waiters);
                    unchecked
                    {
                        _data -= (long)value << WaiterCountShift;    
                    }
                }
                //how much count is available to waiters?
                public ushort CountForWaiters
                {
                    get { return GetUInt16Value(CountForWaiterCountShift); }
                }
                
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void IncrCountForWaiters(ushort value)
                {
                    Debug.Assert(value <= ushort.MaxValue + CountForWaiters);
                    unchecked
                    {
                        _data += (long)value << CountForWaiterCountShift;    
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void DecrCountForWaiters(ushort value)
                {
                    Debug.Assert(value >= ushort.MinValue + CountForWaiters);
                    unchecked
                    {
                        _data -= (long)value << CountForWaiterCountShift;    
                    }
                }
            }

            [StructLayout(LayoutKind.Explicit, Size = 64)]
            private struct CacheLinePadding
            { }

            private readonly Semaphore m_semaphore;

            // padding to ensure we get our own cache line
#pragma warning disable 169
            private readonly CacheLinePadding m_padding1;
            private SemaphoreStateV2 m_state;
            private readonly CacheLinePadding m_padding2;
#pragma warning restore 169

            public UnfairSemaphoreV2()
            {
                m_semaphore = new Semaphore(0, short.MaxValue);
            }

            public bool Wait()
            {
                return Wait(Timeout.InfiniteTimeSpan);
            }

            public bool Wait(TimeSpan timeout)
            {
                while (true)
                {
                    SemaphoreStateV2 currentCounts = GetCurrentState();
                    SemaphoreStateV2 newCounts = currentCounts;

                    // First, just try to grab some count.
                    if (currentCounts.CountForSpinners > 0)
                    {
                        newCounts.DecrementCountForSpinners();
                        if (TryUpdateState(newCounts, currentCounts))
                            return true;
                    }
                    else
                    {
                        // No count available, become a spinner
                        newCounts.AddSpinners(1);
                        if (TryUpdateState(newCounts, currentCounts))
                            break;
                    }
                }

                //
                // Now we're a spinner.
                //
                int numSpins = 0;
                const int spinLimitPerProcessor = 50;
                while (true)
                {
                    SemaphoreStateV2 currentCounts = GetCurrentState();
                    SemaphoreStateV2 newCounts = currentCounts;

                    if (currentCounts.CountForSpinners > 0)
                    {
                        newCounts.DecrementCountForSpinners();
                        newCounts.DecrSpinners(1);
                        if (TryUpdateState(newCounts, currentCounts))
                            return true;
                    }
                    else
                    {
                        double spinnersPerProcessor = (double)currentCounts.Spinners / ProcessorCount;
                        int spinLimit = (int)((spinLimitPerProcessor / spinnersPerProcessor) + 0.5);
                        if (numSpins >= spinLimit)
                        {
                            newCounts.DecrSpinners(1);
                            newCounts.AddWaiters(1);
                            if (TryUpdateState(newCounts, currentCounts))
                                break;
                        }
                        else
                        {
                            //
                            // We yield to other threads using Thread.Sleep(0) rather than the more traditional Thread.Yield().
                            // This is because Thread.Yield() does not yield to threads currently scheduled to run on other
                            // processors.  On a 4-core machine, for example, this means that Thread.Yield() is only ~25% likely
                            // to yield to the correct thread in some scenarios.
                            // Thread.Sleep(0) has the disadvantage of not yielding to lower-priority threads.  However, this is ok because
                            // once we've called this a few times we'll become a "waiter" and wait on the Semaphore, and that will
                            // yield to anything that is runnable.
                            //
                            Thread.Sleep(0);
                            numSpins++;
                        }
                    }
                }

                //
                // Now we're a waiter
                //
                bool waitSucceeded = m_semaphore.WaitOne(timeout);

                while (true)
                {
                    SemaphoreStateV2 currentCounts = GetCurrentState();
                    SemaphoreStateV2 newCounts = currentCounts;

                    newCounts.DecrWaiters(1);

                    if (waitSucceeded)
                        newCounts.DecrCountForWaiters(1);

                    if (TryUpdateState(newCounts, currentCounts))
                        return waitSucceeded;
                }
            }

            public void Release()
            {
                Release(1);
            }

            public void Release(short count)
            {
                while (true)
                {
                    SemaphoreStateV2 currentState = GetCurrentState();
                    SemaphoreStateV2 newState = currentState;

                    ushort remainingCount = (ushort)count;

                    // First, prefer to release existing spinners,
                    // because a) they're hot, and b) we don't need a kernel
                    // transition to release them.
                    
                    ushort spinnersToRelease = (ushort)Math.Max((short)0, Math.Min(remainingCount, (short)(currentState.Spinners - currentState.CountForSpinners)));
                    newState.IncrementCountForSpinners((ushort)spinnersToRelease);// .CountForSpinners = (ushort)(newState.CountForSpinners + spinnersToRelease);
                    remainingCount -= spinnersToRelease;

                    // Next, prefer to release existing waiters
                    ushort waitersToRelease = (ushort)Math.Max((short)0, Math.Min(remainingCount, (short)(currentState.Waiters - currentState.CountForWaiters)));
                    newState.IncrCountForWaiters((ushort)waitersToRelease);// .CountForWaiters = (ushort)(newState.CountForWaiters+ waitersToRelease);
                    remainingCount -= waitersToRelease;

                    // Finally, release any future spinners that might come our way
                    newState.IncrementCountForSpinners((ushort)remainingCount);

                    // Try to commit the transaction
                    if (TryUpdateState(newState, currentState))
                    {
                        // Now we need to release the waiters we promised to release
                        if (waitersToRelease > 0)
                            m_semaphore.Release(waitersToRelease);

                        break;
                    }
                }
            }

            private bool TryUpdateState(SemaphoreStateV2 newState, SemaphoreStateV2 currentState)
            {
                if (Interlocked.CompareExchange(ref m_state._data, newState._data, currentState._data) == currentState._data)
                {
                    Debug.Assert(newState.CountForSpinners <= MaxWorker, "CountForSpinners is greater than MaxWorker");
                    Debug.Assert(newState.CountForSpinners >= 0, "CountForSpinners is lower than zero");
                    Debug.Assert(newState.Spinners <= MaxWorker, "Spinners is greater than MaxWorker");
                    Debug.Assert(newState.Spinners >= 0, "Spinners is lower than zero");
                    Debug.Assert(newState.CountForWaiters <= MaxWorker, "CountForWaiters is greater than MaxWorker");
                    Debug.Assert(newState.CountForWaiters >= 0, "CountForWaiters is lower than zero");
                    Debug.Assert(newState.Waiters <= MaxWorker, "Waiters is greater than MaxWorker");
                    Debug.Assert(newState.Waiters >= 0, "Waiters is lower than zero");
                    Debug.Assert(newState.CountForSpinners + newState.CountForWaiters <= MaxWorker, "CountForSpinners + CountForWaiters is greater than MaxWorker");

                    return true;
                }

                return false;
            }

            private SemaphoreStateV2 GetCurrentState()
            {
                // Volatile.Read of a long can get a partial read in x86 but the invalid
                // state will be detected in TryUpdateState with the CompareExchange.

                SemaphoreStateV2 state = new SemaphoreStateV2();
                state._data = Volatile.Read(ref m_state._data);
                return state;
            }
        }
}
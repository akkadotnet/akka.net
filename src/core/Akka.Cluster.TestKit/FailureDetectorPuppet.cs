//-----------------------------------------------------------------------
// <copyright file="FailureDetectorPuppet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;

namespace Akka.Cluster.Tests.MultiNode
{
    /// <summary>
    /// User controllable "puppet" failure detector.
    /// </summary>
    public class FailureDetectorPuppet : FailureDetector
    {
        public FailureDetectorPuppet(Config config, EventStream ev)
        {
        }

        public enum Status
        {
            Up,
            Down,
            Unknown
        }

        readonly AtomicReference<Status> _status = new AtomicReference<Status>(Status.Unknown);

        public void MarkNodeAsUnavailable()
        {
            var oldStatus = _status.Value;
            bool set;
            do
            {
                set = _status.CompareAndSet(oldStatus, Status.Down);
            } while (!set);

        }

        public void MarkNodeAsAvailable()
        {
            var oldStatus = _status.Value;
            bool set;
            do
            {
                set = _status.CompareAndSet(oldStatus, Status.Up);
            } while (!set);
        }

        public override bool IsAvailable
        {
            get
            {
                var status = _status.Value;
                return status == Status.Up || status == Status.Unknown;
            }
        }

        public override bool IsMonitoring
        {
            get { return _status.Value != Status.Unknown; }
        }

        public override void HeartBeat()
        {
            _status.CompareAndSet(Status.Unknown, Status.Up);
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


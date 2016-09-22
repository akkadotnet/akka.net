//-----------------------------------------------------------------------
// <copyright file="FastLazy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// A fast, atomic lazy that only allows a single publish operation to happen,
    /// but allows executions to occur concurrently.
    /// 
    /// Does not cache exceptions. Designed for use with <typeparam name="T"/> types that are <see cref="IDisposable"/>
    /// or are otherwise considered to be expensive to allocate. 
    /// 
    /// Read the full explanation here: https://github.com/Aaronontheweb/FastAtomicLazy#rationale
    /// </summary>
    public sealed class FastLazy<T>
    {
        private readonly Func<T> _producer;
        private byte _created = 0;
        private byte _creating = 0;
        private T _createdValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastLazy{T}"/> class.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="producer"/> is undefined.
        /// </exception>
        public FastLazy(Func<T> producer)
        {
            if (producer == null) throw new ArgumentNullException(nameof(producer), "Producer cannot be null");
            _producer = producer;
        }

        public bool IsValueCreated => IsValueCreatedInternal();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValueCreatedInternal()
        {
            return Volatile.Read(ref _created) == 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValueCreationInProgress()
        {
            return Volatile.Read(ref _creating) == 1;
        }

        public T Value
        {
            get
            {
                if (IsValueCreatedInternal())
                    return _createdValue;
                if (!IsValueCreationInProgress())
                {
                    Volatile.Write(ref _creating, 1);
                    _createdValue = _producer();
                    Volatile.Write(ref _created, 1);
                }
                else
                {
                    SpinWait.SpinUntil(IsValueCreatedInternal);
                }
                return _createdValue;
            }
        }
    }
}


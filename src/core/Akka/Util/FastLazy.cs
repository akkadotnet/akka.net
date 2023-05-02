//-----------------------------------------------------------------------
// <copyright file="FastLazy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// A fast, atomic lazy that only allows a single publish operation to happen,
    /// but allows executions to occur concurrently.
    /// 
    /// Does not cache exceptions. Designed for use with <typeparam name="T"/> types that are <see cref="IDisposable"/>
    /// or are otherwise considered to be expensive to allocate. Read the full explanation here: https://github.com/Aaronontheweb/FastAtomicLazy#rationale
    /// </summary>
    public sealed class FastLazy<T>
    {
        private Func<T> _producer;
        private int _status = 0;
        private Exception _exception;
        private T _createdValue;

        public FastLazy(Func<T> producer)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        }

        public bool IsValueCreated => Volatile.Read(ref _status) == 2;
        private bool IsExceptionThrown => Volatile.Read(ref _exception) != null;
        
        public T Value
        {
            get
            {
                if (IsValueCreated)
                    return _createdValue;
                
                if (Interlocked.CompareExchange(ref _status, 1, 0) == 0)
                {
                    try
                    {
                        _createdValue = _producer();
                    }
                    catch (Exception e)
                    {
                        Volatile.Write(ref _exception, e);
                        throw;
                    }

                    Volatile.Write(ref _status, 2);
                    _producer = null; // release for GC
                }
                else
                {
                    SpinWait.SpinUntil(() => IsValueCreated || IsExceptionThrown);
                    var e = Volatile.Read(ref _exception);
                    if (e != null)
                        throw e;
                }
                return _createdValue;
            }
        }
    }
}

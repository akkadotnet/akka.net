//-----------------------------------------------------------------------
// <copyright file="AtomicBoolean.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// Implementation of the java.concurrent.util.AtomicBoolean type.
    /// 
    /// Uses <see cref="Interlocked.MemoryBarrier"/> internally to enforce ordering of writes
    /// without any explicit locking. .NET's strong memory on write guarantees might already enforce
    /// this ordering, but the addition of the MemoryBarrier guarantees it.
    /// </summary>
    public class AtomicBoolean
    {
        private const int _falseValue = 0;
        private const int _trueValue = 1;

        private int _value;
        /// <summary>
        /// Sets the initial value of this <see cref="AtomicBoolean"/> to <paramref name="initialValue"/>.
        /// </summary>
        public AtomicBoolean(bool initialValue = false)
        {
            _value = initialValue ? _trueValue : _falseValue;
        }

        /// <summary>
        /// The current value of this <see cref="AtomicReference{T}"/>
        /// </summary>
        public bool Value
        {
            get
            {
                Interlocked.MemoryBarrier();
                return _value==_trueValue;
            }
            set
            {
                Interlocked.Exchange(ref _value, value ? _trueValue : _falseValue);    
            }
        }

        /// <summary>
        /// If <see cref="Value"/> equals <paramref name="expected"/>, then set the Value to
        /// <paramref name="newValue"/>.
        /// </summary>
        /// <returns><c>true</c> if <paramref name="newValue"/> was set</returns>
        public bool CompareAndSet(bool expected, bool newValue)
        {
            var expectedInt = expected ? _trueValue : _falseValue;
            var newInt = newValue ? _trueValue : _falseValue;
            return Interlocked.CompareExchange(ref _value, newInt, expectedInt) == expectedInt;           
        }

        #region Conversion operators

        /// <summary>
        /// Implicit conversion operator = automatically casts the <see cref="AtomicBoolean"/> to a <see cref="bool"/>
        /// </summary>
        public static implicit operator bool(AtomicBoolean boolean)
        {
            return boolean.Value;
        }

        /// <summary>
        /// Implicit conversion operator = allows us to cast any bool directly into a <see cref="AtomicBoolean"/> instance.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static implicit operator AtomicBoolean(bool value)
        {
            return new AtomicBoolean(value);
        }

        #endregion
    }
}


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
    internal class AtomicBoolean
    {
        private const int _falseValue = 0;
        private const int _trueValue = 1;

        private int _value;
        /// <summary>
        /// Sets the initial value of this <see cref="AtomicBoolean"/> to <see cref="initialValue"/>.
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
        /// If <see cref="Value"/> equals <see cref="expected"/>, then set the Value to
        /// <see cref="newValue"/>.
        /// 
        /// Returns true if <see cref="newValue"/> was set, false otherise.
        /// </summary>
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
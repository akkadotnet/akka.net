//-----------------------------------------------------------------------
// <copyright file="ElementSlot.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// Per-materialization payload storage for an exact-type fused connection. Control signals
    /// remain in the connection's existing object slot.
    /// </summary>
    internal abstract class ElementSlot
    {
        public abstract bool HasValue { get; }
        public abstract object BoxedValue { get; }
        public abstract void Clear();
    }

    internal sealed class ElementSlot<T> : ElementSlot
    {
        private T _value = default!;
        private object? _boxedValue;
        private bool _hasValue;

        public override bool HasValue => _hasValue;

        // Once exposed, the box is authoritative: callers may mutate a boxed mutable struct.
        public T Value => _boxedValue is null ? _value : (T)_boxedValue;

        // Compatibility reads must return the same box until this payload is consumed or replaced.
        public override object BoxedValue
        {
            get
            {
                if (!HasValue)
                    return GraphInterpreter.Empty.Instance;

                if (_boxedValue is null)
                {
                    _boxedValue = _value!;
                    _value = default!;
                }

                return _boxedValue;
            }
        }

        public void Store(T value)
        {
            _value = value;
            _boxedValue = null;
            _hasValue = true;
        }

        public override void Clear()
        {
            _value = default!;
            _boxedValue = null;
            _hasValue = false;
        }
    }
}

// <copyright file="LogContextProperties.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace Akka.Event
{
    internal readonly struct LogContextProperties : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly KeyValuePair<string, object>[] _values;

        public LogContextProperties(KeyValuePair<string, object>[] values)
        {
            _values = values is { Length: > 0 } ? values : null;
        }

        public int Count => _values?.Length ?? 0;

        public bool IsEmpty => _values == null;

        public KeyValuePair<string, object> this[int index]
        {
            get
            {
                if (_values == null)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _values[index];
            }
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<string, object>>)(_values ?? Array.Empty<KeyValuePair<string, object>>())).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

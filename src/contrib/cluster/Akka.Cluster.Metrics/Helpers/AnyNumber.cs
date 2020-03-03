//-----------------------------------------------------------------------
// <copyright file="AnyNumber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;

namespace Akka.Cluster.Metrics.Helpers
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public struct AnyNumber
    {
        private readonly long _innerLong;
        private readonly double _innerDouble;
        
        public enum NumberType
        {
            Int,
            Long,
            Float,
            Double
        }
        
        public NumberType Type { get; }
        public long LongValue => Type == NumberType.Int || Type == NumberType.Long ? _innerLong : (long)_innerDouble;
        public double DoubleValue => Type == NumberType.Int || Type == NumberType.Long ? _innerLong : _innerDouble;
        
        public AnyNumber(int n)
        {
            Type = NumberType.Int;
            _innerLong = n;
            _innerDouble = 0;
        }

       public AnyNumber(long n)
        {
            Type = NumberType.Long;
            _innerLong = n;
            _innerDouble = 0;
        }
        
        public AnyNumber(float n)
        {
            Type = NumberType.Float;
            _innerDouble = n;
            _innerLong = 0;
        }
        
        public AnyNumber(double n)
        {
            Type = NumberType.Double;
            _innerDouble = n;
            _innerLong = 0;
        }
        
        public static implicit operator AnyNumber(int n)
        {
            return new AnyNumber(n);
        }
        
        public static implicit operator AnyNumber(long n)
        {
            return new AnyNumber(n);
        }

        public static implicit operator AnyNumber(float n)
        {
            return new AnyNumber(n);
        }

        public static implicit operator AnyNumber(double n)
        {
            return new AnyNumber(n);
        }
    }
}

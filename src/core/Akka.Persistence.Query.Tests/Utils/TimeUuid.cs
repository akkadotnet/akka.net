//-----------------------------------------------------------------------
// <copyright file="TimeUuid.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

//
//      Copyright (C) DataStax Inc.
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using System;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// Represents a v1 uuid 
    /// </summary>
    internal struct TimeUuid : IEquatable<TimeUuid>, IComparable<TimeUuid>
    {
        private static readonly DateTimeOffset GregorianCalendarTime = new DateTimeOffset(1582, 10, 15, 0, 0, 0, TimeSpan.Zero);
        //Reuse the random generator to avoid collisions
        private static readonly Random RandomGenerator = new Random();
        private static readonly object RandomLock = new object();
        private static readonly byte[] MinNodeId = { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 };
        private static readonly byte[] MinClockId = { 0x80, 0x80 };
        private static readonly byte[] MaxNodeId = { 0x7f, 0x7f, 0x7f, 0x7f, 0x7f, 0x7f };
        private static readonly byte[] MaxClockId = { 0x7f, 0x7f };

        private readonly Guid _value;

        private TimeUuid(Guid value)
        {
            _value = value;
        }

        /// <summary>
        /// Creates a new instance of <see cref="TimeUuid"/>.
        /// </summary>
        /// <param name="nodeId">6-byte node identifier</param>
        /// <param name="clockId">2-byte clock identifier</param>
        /// <param name="time">The timestamp</param>
        /// <exception cref="ArgumentException"></exception>
        private TimeUuid(byte[] nodeId, byte[] clockId, DateTimeOffset time)
        {
            if (nodeId == null || nodeId.Length != 6)
            {
                throw new ArgumentException("node Id should contain 6 bytes", nameof(nodeId));
            }
            if (clockId == null || clockId.Length != 2)
            {
                throw new ArgumentException("clock Id should contain 2 bytes", nameof(clockId));
            }
            var timeBytes = BitConverter.GetBytes((time - GregorianCalendarTime).Ticks);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(timeBytes);
            }
            var buffer = new byte[16];
            //Positions 0-7 Timestamp
            Buffer.BlockCopy(timeBytes, 0, buffer, 0, 8);
            //Position 8-9 Clock
            Buffer.BlockCopy(clockId, 0, buffer, 8, 2);
            //Positions 10-15 Node
            Buffer.BlockCopy(nodeId, 0, buffer, 10, 6);

            //Version Byte: Time based
            //0001xxxx
            //turn off first 4 bits
            buffer[7] &= 0x0f; //00001111
            //turn on fifth bit
            buffer[7] |= 0x10; //00010000

            //Variant Byte: 1.0.x
            //10xxxxxx
            //turn off first 2 bits
            buffer[8] &= 0x3f; //00111111
            //turn on first bit
            buffer[8] |= 0x80; //10000000

            _value = new Guid(buffer);
        }

        /// <summary>
        /// Returns a value indicating whether this instance and a specified TimeUuid object represent the same value.
        /// </summary>
        public bool Equals(TimeUuid other)
        {
            return _value.Equals(other._value);
        }

        /// <summary>
        /// Returns a value indicating whether this instance and a specified TimeUuid object represent the same value.
        /// </summary>
        public override bool Equals(object obj)
        {
            var otherTimeUuid = obj as TimeUuid?;
            return otherTimeUuid != null && Equals(otherTimeUuid.Value);
        }

        /// <summary>
        /// Gets the DateTimeOffset representation of this instance
        /// </summary>
        public DateTimeOffset GetDate()
        {
            var bytes = _value.ToByteArray();
            //Remove version bit
            bytes[7] &= 0x0f; //00001111
            //Remove variant
            bytes[8] &= 0x3f; //00111111
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            var timestamp = BitConverter.ToInt64(bytes, 0);
            var ticks = timestamp + GregorianCalendarTime.Ticks;

            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        /// <summary>
        /// Returns a 16-element byte array that contains the value of this instance.
        /// </summary>
        public byte[] ToByteArray()
        {
            return _value.ToByteArray();
        }

        /// <summary>
        /// Gets the Guid representation of the Id
        /// </summary>
        public Guid ToGuid()
        {
            return _value;
        }

        /// <summary>
        /// Compares the current TimeUuid with another TimeUuid based on the time representation of this instance.
        /// </summary>
        public int CompareTo(TimeUuid other)
        {
            return GetDate().CompareTo(other.GetDate());
        }

        /// <summary>
        /// Returns a string representation of the value of this instance in registry format.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return _value.ToString();
        }

        /// <summary>
        /// Returns a string representation
        /// </summary>
        public string ToString(string format, IFormatProvider provider)
        {
            return _value.ToString(format, provider);
        }

        /// <summary>
        /// Returns a string representation
        /// </summary>
        public string ToString(string format)
        {
            return _value.ToString(format);
        }

        /// <summary>
        /// Returns the smaller possible type 1 uuid with the provided date.
        /// </summary>
        public static TimeUuid Min(DateTimeOffset date)
        {
            return new TimeUuid(MinNodeId, MinClockId, date);
        }

        /// <summary>
        /// Returns the biggest possible type 1 uuid with the provided Date.
        /// </summary>
        public static TimeUuid Max(DateTimeOffset date)
        {
            return new TimeUuid(MaxNodeId, MaxClockId, date);
        }

        /// <summary>
        /// Initializes a new instance of the TimeUuid structure, using a random node id and clock sequence and the current date time
        /// </summary>
        public static TimeUuid NewId()
        {
            return NewId(DateTimeOffset.Now);
        }

        /// <summary>
        /// Initializes a new instance of the TimeUuid structure, using a random node id and clock sequence
        /// </summary>
        public static TimeUuid NewId(DateTimeOffset date)
        {
            byte[] nodeId;
            byte[] clockId;
            lock (RandomLock)
            {
                //oh yeah, thread safety
                nodeId = new byte[6];
                clockId = new byte[2];
                RandomGenerator.NextBytes(nodeId);
                RandomGenerator.NextBytes(clockId);
            }
            return new TimeUuid(nodeId, clockId, date);
        }

        /// <summary>
        /// Initializes a new instance of the TimeUuid structure
        /// </summary>
        public static TimeUuid NewId(byte[] nodeId, byte[] clockId, DateTimeOffset date)
        {
            return new TimeUuid(nodeId, clockId, date);
        }

        /// <summary>
        /// Converts the string representation of a time-based uuid (v1) to the equivalent 
        /// <see cref="TimeUuid"/> structure.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static TimeUuid Parse(string input)
        {
            return new TimeUuid(Guid.Parse(input));
        }

        /// <summary>
        /// From TimeUuid to Guid
        /// </summary>
        public static implicit operator Guid(TimeUuid value)
        {
            return value.ToGuid();
        }

        /// <summary>
        /// From Guid to TimeUuid
        /// </summary>
        public static implicit operator TimeUuid(Guid value)
        {
            return new TimeUuid(value);
        }

        public static bool operator ==(TimeUuid id1, TimeUuid id2)
        {
            return id1.ToGuid() == id2.ToGuid();
        }

        public static bool operator !=(TimeUuid id1, TimeUuid id2)
        {
            return id1.ToGuid() != id2.ToGuid();
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="PayloadSizeExceededException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// Thrown when a write to a <see cref="PooledPayloadWriter"/> -- via <see cref="PooledPayloadWriter.GetSpan"/>,
    /// <see cref="PooledPayloadWriter.GetMemory"/>, or <see cref="PooledPayloadWriter.Advance"/> -- would push the
    /// writer's total written byte count past its configured <c>maxCapacity</c>.
    ///
    /// <para>
    /// This is deliberate groundwork for deterministic oversized-payload failure (messagepack-sourcegen task
    /// 6.8): an oversized payload fails HERE, at encode time, with a typed exception carrying the attempted
    /// size and the configured cap -- it is never discovered downstream as a corrupt or truncated wire frame.
    /// </para>
    /// </summary>
    public class PayloadSizeExceededException : AkkaException
    {
        /// <summary>
        /// The total byte count (already-written bytes plus the rejected request) that would have
        /// resulted had the write been allowed.
        /// </summary>
        public long AttemptedSize { get; }

        /// <summary>
        /// The configured maximum capacity, in bytes, that <see cref="AttemptedSize"/> would have exceeded.
        /// </summary>
        public int MaxCapacity { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PayloadSizeExceededException"/> class.
        /// </summary>
        public PayloadSizeExceededException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PayloadSizeExceededException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public PayloadSizeExceededException(string message, Exception? cause = null) : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PayloadSizeExceededException"/> class.
        /// </summary>
        /// <param name="attemptedSize">The total byte count that would have resulted had the write been allowed.</param>
        /// <param name="maxCapacity">The configured maximum capacity, in bytes.</param>
        public PayloadSizeExceededException(long attemptedSize, int maxCapacity)
            : base($"Payload size {attemptedSize} byte(s) would exceed the configured maximum capacity of {maxCapacity} byte(s).")
        {
            AttemptedSize = attemptedSize;
            MaxCapacity = maxCapacity;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PayloadSizeExceededException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected PayloadSizeExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            AttemptedSize = info.GetInt64(nameof(AttemptedSize));
            MaxCapacity = info.GetInt32(nameof(MaxCapacity));
        }

        /// <inheritdoc />
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            info.AddValue(nameof(AttemptedSize), AttemptedSize);
            info.AddValue(nameof(MaxCapacity), MaxCapacity);
            base.GetObjectData(info, context);
        }
    }
}

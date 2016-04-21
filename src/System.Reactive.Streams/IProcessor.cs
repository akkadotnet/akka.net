//-----------------------------------------------------------------------
// <copyright file="IProcessor.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace System.Reactive.Streams
{
    /// <summary>
    /// A Processor represents a processing stage—which is both a <see cref="ISubscriber{T}"/>
    /// and a <see cref="IPublisher{T}"/> and obeys the contracts of both.
    /// </summary>
    /// <typeparam name="T1">The type of element signaled to the <see cref="ISubscriber{T}"/></typeparam>
    /// <typeparam name="T2">The type of element signaled to the <see cref="IPublisher{T}"/></typeparam>
    public interface IProcessor<in T1, out T2> : ISubscriber<T1>, IPublisher<T2>
    {
    }
}
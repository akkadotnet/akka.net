//-----------------------------------------------------------------------
// <copyright file="TestReceiveActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
// ReSharper disable once CheckNamespace


namespace Akka.TestKit
{
    /// <summary>
    /// Just like <see cref="ReceiveActor"/>. Adds a Receive-overload that allows you to write code like:
    /// <code>Receive("the message", m => ... );</code>
    /// </summary>
    public class TestReceiveActor : ReceiveActor
    {
        public void Receive<T>(T value, Action<T> handler) where T : IEquatable<T>
        {
            Receive<T>(m => Equals(value, m), handler);
        }
       
    }
}


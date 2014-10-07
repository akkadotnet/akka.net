using System;
using Akka.Actor;
// ReSharper disable once CheckNamespace


namespace Akka.TestKit
{
    /// <summary>
    /// Just like <see cref="ReceiveActor"/>. Adds a Receive-overload that allows yout write code like:
    /// <pre><code>Receive("the message", m => ... );</code></pre>
    /// </summary>
    public class TestReceiveActor : ReceiveActor
    {
        public void Receive<T>(T value, Action<T> handler) where T : IEquatable<T>
        {
            Receive<T>(m => Equals(value, m), handler);
        }
       
    }
}
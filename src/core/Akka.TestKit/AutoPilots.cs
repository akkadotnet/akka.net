//-----------------------------------------------------------------------
// <copyright file="AutoPilots.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Creates an <see cref="AutoPilot"/>.
    /// <para>An <see cref="AutoPilot"/> will be called for each received message and can be 
    /// used to send or forward messages, etc. </para>
    /// <para>Each invocation must return the <see cref="AutoPilot"/> for the next round.</para>
    /// <para>To reuse an <see cref="AutoPilot"/> for the next message either 
    /// return the instance or return <see cref="AutoPilot.KeepRunning"/>.</para>
    /// <para>Return <see cref="AutoPilot.NoAutoPilot"/> to stop handling messages.</para>
    /// </summary>
    public abstract class AutoPilot
    {
        /// <summary>
        /// <para>This function will be called for each received message and can be 
        /// used to send or forward messages, etc. </para>
        /// <para>Each invocation must return the <see cref="AutoPilot"/> for the next round.</para> 
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="message">The message.</param>
        /// <returns>The <see cref="AutoPilot"/> to be used for the next round</returns>
        public abstract AutoPilot Run(IActorRef sender, object message);

        /// <summary>
        /// When returned by another <see cref="AutoPilot"/> then no
        /// action will be performed by the <see cref="TestActor"/>
        /// for the next message. This is the default <see cref="AutoPilot"/> used 
        /// by <see cref="AutoPilot"/>.
        /// </summary>
        public static NoAutoPilot NoAutoPilot { get { return NoAutoPilot.Instance; } }

        /// <summary>
        /// When returned by another <see cref="AutoPilot"/> then <see cref="TestActor"/>
        /// will reuse the AutoPilot for the next message.
        /// </summary>
        public static KeepRunning KeepRunning { get { return KeepRunning.Instance; } }
    }

    /// <summary>
    /// When returned by another <see cref="AutoPilot"/> then no
    /// action will be performed by the <see cref="TestActor"/>
    /// for the next message. This is the default <see cref="AutoPilot"/> used 
    /// by <see cref="AutoPilot"/>.
    /// </summary>
    public sealed class NoAutoPilot : AutoPilot
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static NoAutoPilot Instance = new NoAutoPilot();

        private NoAutoPilot() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sender">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public override AutoPilot Run(IActorRef sender, object message)
        {
            return this;
        }
    }

    /// <summary>
    /// When returned by another <see cref="AutoPilot"/> then <see cref="TestActor"/>
    /// will reuse the AutoPilot for the next message.
    /// </summary>
    public sealed class KeepRunning : AutoPilot
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static KeepRunning Instance = new KeepRunning();

        private KeepRunning(){ }
        
        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="message">N/A</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is automatically thrown since calling this function would never occur in normal operation.
        /// </exception>
        /// <returns>N/A</returns>
        public override AutoPilot Run(IActorRef sender, object message)
        {
            throw new InvalidOperationException("Must not call");
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="sender">TBD</param>
    /// <param name="message">TBD</param>
    /// <returns>TBD</returns>
    public delegate AutoPilot AutoPilotDelegate(IActorRef sender, object message);

    /// <summary>
    /// Creates an <see cref="AutoPilot"/>.
    /// <para>The <see cref="AutoPilotDelegate"/> specified in the constructor will 
    /// be called for each received message and can be used to send or forward 
    /// messages, etc. </para>
    /// <para>Each invocation must return the <see cref="AutoPilot"/> for the next round.</para>
    /// <para>To have this instance handle the next message either return this instance
    /// or return <see cref="AutoPilot.KeepRunning"/>.</para>
    /// <para>Return <see cref="AutoPilot.NoAutoPilot"/> to stop handling messages.</para>
    /// </summary>
    public sealed class DelegateAutoPilot : AutoPilot
    {
        private readonly AutoPilotDelegate _autoPilotDelegate;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="autoPilotDelegate">TBD</param>
        public DelegateAutoPilot(AutoPilotDelegate autoPilotDelegate)
        {
            _autoPilotDelegate = autoPilotDelegate;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sender">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public override AutoPilot Run(IActorRef sender, object message)
        {
            return _autoPilotDelegate(sender,message);
        }
    }
}

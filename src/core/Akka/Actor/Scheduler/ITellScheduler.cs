//-----------------------------------------------------------------------
// <copyright file="ITellScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// This interface defines a scheduler that is able to send messages on a set schedule.
    /// </summary>
    public interface ITellScheduler
    {
        /// <summary>
        /// Schedules a message to be sent once after a specified period of time.
        /// </summary>
        /// <param name="delay">The time period that has to pass before the message is sent.</param>
        /// <param name="receiver">The actor that receives the message.</param>
        /// <param name="message">The message that is being sent.</param>
        /// <param name="sender">The actor that sent the message.</param>
        void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender);

        /// <summary>
        /// Schedules a message to be sent once after a specified period of time.
        /// </summary>
        /// <param name="delay">The time period that has to pass before the message is sent.</param>
        /// <param name="receiver">The actor that receives the message.</param>
        /// <param name="message">The message that is being sent.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="cancelable">A cancelable used to cancel sending the message. Once the message has been sent, it cannot be canceled.</param>
        void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable);

        /// <summary>
        /// Schedules a message to be sent repeatedly after an initial delay.
        /// </summary>
        /// <param name="initialDelay">The time period that has to pass before the first message is sent.</param>
        /// <param name="interval">The time period that has to pass between sending of the message.</param>
        /// <param name="receiver">The actor that receives the message.</param>
        /// <param name="message">The message that is being sent.</param>
        /// <param name="sender">The actor that sent the message.</param>
        void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender);

        /// <summary>
        /// Schedules a message to be sent repeatedly after an initial delay.
        /// </summary>
        /// <param name="initialDelay">The time period that has to pass before the first message is sent.</param>
        /// <param name="interval">The time period that has to pass between sending of the message.</param>
        /// <param name="receiver">The actor that receives the message.</param>
        /// <param name="message">The message that is being sent.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="cancelable">An cancelable used to cancel sending the message. Once the message has been sent, it cannot be canceled.</param>
        void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable);
    }
}

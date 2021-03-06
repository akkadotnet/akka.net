// //-----------------------------------------------------------------------
// // <copyright file="ServiceProviderProps.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.DependencyInjection
{
    /// <summary>
    /// This class represents a specialized <see cref="Akka.Actor.Props"/> that uses delegate invocation
    /// to create new actor instances, rather than a traditional <see cref="System.Activator"/>.
    ///
    /// Relies on having an active <see cref="IServiceProvider"/> implementation available
    /// </summary>
    internal class ServiceProviderProps : Props
    {
        private readonly IServiceProvider _provider;
        private readonly Type _type;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceProviderProps{TActor}" /> class.
        /// </summary>
        /// <param name="provider">The <see cref="IServiceProvider"/> used to power this class</param>
        /// <param name="args">The constructor arguments passed to the actor's constructor.</param>
        public ServiceProviderProps(IServiceProvider provider, Type type, params object[] args)
            : base(type, args)
        {
            _provider = provider;
            _type = type;
        }

        /// <summary>
        /// Creates a new actor using the configured factory method.
        /// </summary>
        /// <returns>The actor created using the factory method.</returns>
        public override ActorBase NewActor()
        {
            return (ActorBase) ActivatorUtilities.CreateInstance(_provider, _type, Arguments);
        }

        #region Copy methods

        /// <summary>
        /// Creates a copy of the current instance.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.Props"/></returns>
        protected override Props Copy()
        {
            return new ServiceProviderProps(_provider, _type, Arguments)
            {
                Deploy = Deploy,
                SupervisorStrategy = SupervisorStrategy
            };
        }

        #endregion
    }
}
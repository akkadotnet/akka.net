//-----------------------------------------------------------------------
// <copyright file="ActorSystemSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Annotations;
using Akka.Util;

namespace Akka.Actor.Setup
{
    /// <summary>
    /// Marker base class for a setup part that can be put inside <see cref="ActorSystemSetup"/>, if a specific concrete setup
    /// is not specified in the actor system setup that means defaults are used(usually from the config file) - no concrete
    /// setup instance should be mandatory in the <see cref="ActorSystemSetup"/> that an <see cref="ActorSystem"/> is created with.
    /// </summary>
    public abstract class Setup
    {
        /// <summary>
        /// Construct an <see cref="ActorSystemSetup"/> with this setup combined with another one.
        ///
        /// Allows for fluent creation of settings.
        /// </summary>
        /// <param name="other">If other is of the same concrete <see cref="Setup"/> type as this, it will replace this.</param>
        /// <returns>A new <see cref="ActorSystemSetup"/> instance.</returns>
        public ActorSystemSetup And(Setup other)
        {
            return ActorSystemSetup.Create(this, other);
        }
    }

    /// <summary>
    /// A set of setup classes to allow for programmatic configuration of the <see cref="ActorSystem"/>
    /// </summary>
    /// <remarks>
    /// The constructor is internal. Use <see cref="ActorSystemSetup.Create"/> or <see cref="ActorSystemSetup.WithSetup{T}"/>
    /// to create instances.
    /// </remarks>
    [InternalApi]
    public sealed class ActorSystemSetup
    {
        public static readonly ActorSystemSetup Empty = new ActorSystemSetup(ImmutableDictionary<Type, Setup>.Empty);

        public static ActorSystemSetup Create(params Setup[] setup)
        {
            return new ActorSystemSetup(setup
                .Select(x => new KeyValuePair<Type, Setup>(x.GetType(), x))
                .Aggregate(ImmutableDictionary<Type, Setup>.Empty, (setups, pair) => setups.SetItem(pair.Key, pair.Value)));
        }

        internal ActorSystemSetup(ImmutableDictionary<Type, Setup> setups)
        {
            _setups = setups;
        }

        private readonly ImmutableDictionary<Type, Setup> _setups;

        public Option<T> Get<T>() where T:Setup
        {
            return _setups.ContainsKey(typeof(T)) ? new Option<T>((T)_setups[typeof(T)]) : Option<T>.None;
        }
        
        /// <summary>
        /// Add a concrete <see cref="Setup"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="Setup"/></typeparam>
        /// <param name="setup">Setup input. If a setting of the same type is already present it will be replaced.</param>
        /// <returns>A new, immutable <see cref="ActorSystemSetup"/> instance.</returns>
        public ActorSystemSetup WithSetup<T>(T setup) where T : Setup
        {
            return new ActorSystemSetup(_setups.SetItem(typeof(T), setup));
        }

        /// <summary>
        /// Shortcut for <see cref="Setup.And"/> to make it easier to chain the fluent interface together.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="Setup"/></typeparam>
        /// <param name="setup">Setup input. If a setting of the same type is already present it will be replaced.</param>
        /// <returns>A new, immutable <see cref="ActorSystemSetup"/> instance.</returns>
        /// <remarks>
        /// Calls <see cref="WithSetup{T}"/> internally.
        /// </remarks>
        public ActorSystemSetup And<T>(T setup) where T : Setup
        {
            return WithSetup(setup);
        }

        public override string ToString()
        {
            return $"ActorSystemSetup({string.Join(",", _setups.Keys.Select(x => x.FullName))})";
        }
    }
}

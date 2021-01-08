//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// This interface is used to mark an object as an <see cref="ActorSystem"/> extension.
    /// </summary>
    public interface IExtension { }

    /// <summary>
    /// This interface is used to distinguish unique <see cref="ActorSystem"/> extensions.
    /// </summary>
    public interface IExtensionId
    {
        /// <summary>
        /// Registers the current extension to a given actor system.
        /// </summary>
        /// <param name="system">The actor system in which to register the extension.</param>
        /// <returns>The extension registered to the given actor system.</returns>
        object Apply(ActorSystem system);

        /// <summary>
        /// Retrieves the current extension from a given actor system.
        /// </summary>
        /// <param name="system">The actor system from which to retrieve the extension.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        object Get(ActorSystem system);

        /// <summary>
        /// Creates the current extension using a given actor system.
        /// 
        /// <note>
        /// Internal use only.
        /// </note>
        /// </summary>
        /// <param name="system">The actor system to use when creating the extension.</param>
        /// <returns>The extension created using the given actor system.</returns>
        object CreateExtension(ExtendedActorSystem system);

        /// <summary>
        /// Retrieves the underlying type for the current extension
        /// </summary>
        Type ExtensionType { get; }
    }

    /// <summary>
    /// This interface is used to distinguish unique <see cref="ActorSystem"/> extensions.
    /// </summary>
    /// <typeparam name="T">The type associated with the current extension.</typeparam>
    public interface IExtensionId<out T> : IExtensionId where T:IExtension
    {
        /// <summary>
        /// Registers the current extension to a given actor system.
        /// </summary>
        /// <param name="system">The actor system in which to register the extension.</param>
        /// <returns>The extension registered to the given actor system.</returns>
        new T Apply(ActorSystem system);

        /// <summary>
        /// Retrieves the current extension from a given actor system.
        /// </summary>
        /// <param name="system">The actor system from which to retrieve the extension.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        new T Get(ActorSystem system);

        /// <summary>
        /// Creates the current extension using a given actor system.
        /// 
        /// <note>
        /// Internal use only.
        /// </note>
        /// </summary>
        /// <param name="system">The actor system to use when creating the extension.</param>
        /// <returns>The extension created using the given actor system.</returns>
        new T CreateExtension(ExtendedActorSystem system);
    }

    /// <summary>
    /// This class contains extension methods used for resolving <see cref="ActorSystem"/> extensions.
    /// </summary>
    public static class ActorSystemWithExtensions
    {
        /// <summary>
        /// Retrieves the extension specified by a given type, <typeparamref name="T"/>, from a given actor system.
        /// </summary>
        /// <typeparam name="T">The type associated with the extension to retrieve.</typeparam>
        /// <param name="system">The actor system from which to retrieve the extension.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        public static T WithExtension<T>(this ActorSystem system) where T : class, IExtension
        {
            return system.GetExtension<T>();
        }

        /// <summary>
        /// Retrieves the extension specified by a given type, <typeparamref name="T"/>, from a given actor system.
        /// If the extension does not exist within the actor system, then the extension specified by <paramref name="extensionId"/>
        /// is registered to the actor system.
        /// </summary>
        /// <typeparam name="T">The type associated with the extension to retrieve.</typeparam>
        /// <param name="system">The actor system from which to retrieve the extension or to register with if it does not exist.</param>
        /// <param name="extensionId">The type of the extension to register if it does not exist in the given actor system.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        public static T WithExtension<T>(this ActorSystem system, Type extensionId) where T : class, IExtension
        {
            if (system.HasExtension<T>())
                return system.GetExtension<T>();
            else
            {
                return (T) system.RegisterExtension((IExtensionId)Activator.CreateInstance(extensionId));
            }
        }

        /// <summary>
        /// Retrieves the extension specified by a given type, <typeparamref name="T"/>, from a given actor system.
        /// If the extension does not exist within the actor system, then the extension specified by <typeparamref name="TI"/>
        /// is registered to the actor system.
        /// </summary>
        /// <typeparam name="T">The type associated with the extension to retrieve.</typeparam>
        /// <typeparam name="TI">The type associated with the extension to retrieve if it does not exist within the system.</typeparam>
        /// <param name="system">The actor system from which to retrieve the extension or to register with if it does not exist.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        public static T WithExtension<T,TI>(this ActorSystem system) where T : class, IExtension
                                                                     where TI: IExtensionId
        {
            if (system.HasExtension<T>())
                return system.GetExtension<T>();
            else
            {
                return (T)system.RegisterExtension((IExtensionId)Activator.CreateInstance(typeof(TI)));
            }
            
        }
    }

    /// <summary>
    /// This class represents the base provider implementation for creating, registering and retrieving extensions within an <see cref="ActorSystem"/>.
    /// </summary>
    /// <typeparam name="T">The type of the extension being provided.</typeparam>
    public abstract class ExtensionIdProvider<T> : IExtensionId<T> where T:IExtension
    {
        /// <summary>
        /// Registers the current extension to a given actor system.
        /// </summary>
        /// <param name="system">The actor system in which to register the extension.</param>
        /// <returns>The extension registered to the given actor system.</returns>
        public T Apply(ActorSystem system)
        {
            return (T)system.RegisterExtension(this);
        }

        object IExtensionId.Get(ActorSystem system)
        {
            return Get(system);
        }

        object IExtensionId.CreateExtension(ExtendedActorSystem system)
        {
            return CreateExtension(system);
        }

        /// <summary>
        /// Retrieves the underlying type for the current extension
        /// </summary>
        public Type ExtensionType
        {
            get { return typeof (T); }
        }

        object IExtensionId.Apply(ActorSystem system)
        {
            return Apply(system);
        }

        /// <summary>
        /// Retrieves the current extension from a given actor system.
        /// </summary>
        /// <param name="system">The actor system from which to retrieve the extension.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        public T Get(ActorSystem system)
        {
            return (T)system.GetExtension(this);
        }

        /// <summary>
        /// Creates the current extension using a given actor system.
        /// </summary>
        /// <param name="system">The actor system to use when creating the extension.</param>
        /// <returns>The extension created using the given actor system.</returns>
        public abstract T CreateExtension(ExtendedActorSystem system);

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is T;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return typeof (T).GetHashCode();
        }
    }
}

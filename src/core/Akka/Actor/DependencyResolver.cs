//-----------------------------------------------------------------------
// <copyright file="DependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// An information about the lifetime scope of a created value.
    /// </summary>
    public enum DependencyScope
    {
        /// <summary>
        /// A new instance of an object will be created every time, 
        /// a <see cref="IDependencyResolver.Resolve"/> is called.
        /// </summary>
        Transient = 0,

        /// <summary>
        /// An instance of an object will be created only once.
        /// </summary>
        Singleton = 1
    }

    /// <summary>
    /// A common interface used for managed object creation. Dependency resolver is used 
    /// within Akka <see cref="ExtendedActorSystem"/> to produce various kinds of internal 
    /// components, starting from actors, to serializers, event adapter and even extensions.
    /// 
    /// A custom user implementation can bring integration with third-party IoC containers.
    /// They can be set with <see cref="ActorSystem.UseDependencyResolver"/> method.
    /// </summary>
    public interface IDependencyResolver : IDisposable
    {
        /// <summary>
        /// Resolves component of registered type.
        /// </summary>
        /// <param name="registeredType">Type of an object to resolve.</param>
        /// <returns>
        /// Either new or existing instance of an object, depending on the 
        /// <see cref="DependencyScope"/> used during registration.
        /// </returns>
        object Resolve(Type registeredType);

        /// <summary>
        /// Resolves a producer function used to resolve objects of registered type.
        /// </summary>
        /// <param name="registeredType">Type of an object to resolve.</param>
        /// <returns>
        /// A function, that can return either new or existing instance of an object, 
        /// depending on the <see cref="DependencyScope"/> used during registration.
        /// </returns>
        Func<object> ResolveProducer(Type registeredType);

        /// <summary>
        /// Registers an instance of <paramref name="createdType"/> as a resolver for
        /// a requests using <paramref name="registeredType"/>. It must be assignable
        /// from that type. It's up to dependency resolver to figure out how to build
        /// and instance of <paramref name="createdType"/>.
        /// </summary>
        /// <param name="registeredType"></param>
        /// <param name="createdType"></param>
        /// <param name="scope"></param>
        void Register(Type registeredType, Type createdType, DependencyScope scope = DependencyScope.Transient);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="registeredType"></param>
        /// <param name="producer"></param>
        /// <param name="scope"></param>
        void Register(Type registeredType, Func<object> producer, DependencyScope scope = DependencyScope.Transient);
    }
    
    internal sealed class DefaultDependencyResolver : IDependencyResolver
    {
        private readonly ConcurrentDictionary<Type, Func<object>> _producers = new ConcurrentDictionary<Type, Func<object>>();
        private readonly AtomicBoolean _isDisposed = new AtomicBoolean(false);

        public DefaultDependencyResolver()
        {
        }

        public object Resolve(Type registered)
        {
            var producer = ResolveProducer(registered);
            return producer();
        }

        public Func<object> ResolveProducer(Type registered)
        {
            var producer = _producers.GetOrAdd(registered, () => CreateProducer(registered, DependencyScope.Transient));
            return producer;
        }

        public void Register(Type registeredType, Type createdType, DependencyScope scope = DependencyScope.Transient)
        {
            if (!registeredType.IsAssignableFrom(createdType))
                throw new ArgumentException($"Cannot register type [{createdType}] as it cannot be assigned to [{registeredType}]", nameof(createdType));

            var func = CreateProducer(createdType, scope);
            Register(registeredType, func);
        }

        public void Register(Type registeredType, Func<object> producer, DependencyScope scope = DependencyScope.Transient)
        {
            _producers.TryAdd(registeredType, ApplyScope(registeredType, producer, scope));
        }

        private Func<object> CreateProducer(Type createdType, DependencyScope scope)
        {
            var ctors = createdType.GetConstructors();
            var ctor = ctors[0];
            var parameters = ctor.GetParameters();
            var exprs = new Expression[parameters.Length];
            if (exprs.Length > 0)
            {
                var self = Expression.Constant(this);
                var resolveMethod = this.GetType().GetMethod(nameof(Resolve), new[] { typeof(Type) });
                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    exprs[i] = Expression.Call(self, resolveMethod, Expression.Constant(parameter.ParameterType));
                }
            }

            var expr = Expression.Lambda<Func<object>>(Expression.New(ctor, arguments: exprs));
            var producer = expr.Compile();
            return producer;
        }

        private static Func<object> ApplyScope(Type createdType, Func<object> producer, DependencyScope scope)
        {
            switch (scope)
            {
                case DependencyScope.Transient: return producer;
                case DependencyScope.Singleton:
                    var lazy = new FastLazy<object>(producer);
                    return () => lazy.Value;
                default:
                    throw new NotSupportedException($"Cannot create producer for {createdType} in scope {scope}. Only Transient and Singleton dependency scopes are supported");
            }
        }

        public void Dispose()
        {
        }
    }
}
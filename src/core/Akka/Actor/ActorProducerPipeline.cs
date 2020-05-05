//-----------------------------------------------------------------------
// <copyright file="ActorProducerPipeline.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Plugin interface used to define
    /// </summary>
    [Obsolete("Actor producer pipeline API will be removed in v1.5.")]
    public interface IActorProducerPlugin
    {
        /// <summary>
        /// Determines if current plugin can be applied to provided actor based on it's type.
        /// </summary>
        /// <param name="actorType">TBD</param>
        /// <returns>TBD</returns>
        bool CanBeAppliedTo(Type actorType);

        /// <summary>
        /// Plugin behavior applied to underlying <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        void AfterIncarnated(ActorBase actor, IActorContext context);

        /// <summary>
        /// Plugin behavior applied to underlying <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        void BeforeIncarnated(ActorBase actor, IActorContext context);
    }

    /// <summary>
    /// Base actor producer pipeline plugin class.
    /// </summary>
    [Obsolete("Actor producer pipeline API will be removed in v1.5.")]
    public abstract class ActorProducerPluginBase : IActorProducerPlugin
    {
        /// <summary>
        /// By default derivatives of this plugin will be applied to all actors.
        /// </summary>
        /// <param name="actorType">TBD</param>
        /// <returns>TBD</returns>
        public virtual bool CanBeAppliedTo(Type actorType)
        {
            return true;
        }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        public virtual void AfterIncarnated(ActorBase actor, IActorContext context) { }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        public virtual void BeforeIncarnated(ActorBase actor, IActorContext context) { }
    }

    /// <summary>
    /// Base generic actor producer pipeline plugin class.
    /// </summary>
    [Obsolete("Actor producer pipeline API will be removed in v1.5.")]
    public abstract class ActorProducerPluginBase<TActor> : IActorProducerPlugin where TActor : ActorBase
    {
        /// <summary>
        /// By default derivatives of this plugin will be applied to all actors inheriting from <typeparamref name="TActor">actor generic type</typeparamref>.
        /// </summary>
        /// <param name="actorType">TBD</param>
        /// <returns>TBD</returns>
        public virtual bool CanBeAppliedTo(Type actorType)
        {
            return typeof(TActor).IsAssignableFrom(actorType);
        }

        void IActorProducerPlugin.AfterIncarnated(ActorBase actor, IActorContext context)
        {
            AfterIncarnated(actor as TActor, context);
        }

        void IActorProducerPlugin.BeforeIncarnated(ActorBase actor, IActorContext context)
        {
            BeforeIncarnated(actor as TActor, context);
        }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        public virtual void AfterIncarnated(TActor actor, IActorContext context) { }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        public virtual void BeforeIncarnated(TActor actor, IActorContext context) { }
    }

    /// <summary>
    /// Class used to resolving actor producer pipelines depending on actor type.
    /// </summary>
    [Obsolete("Actor producer pipeline API will be removed in v1.5.")]
    public class ActorProducerPipelineResolver
    {
        private readonly Lazy<ILoggingAdapter> _log;
        private readonly List<IActorProducerPlugin> _plugins = new List<IActorProducerPlugin>
        {   
            // collection of plugins loaded by default
            new ActorStashPlugin()
        };

        private readonly ConcurrentDictionary<Type, ActorProducerPipeline> _pipelines = new ConcurrentDictionary<Type, ActorProducerPipeline>();

        /// <summary>
        /// Gets total number of unique plugins registered inside current resolver.
        /// </summary>
        public int TotalPluginCount { get { return _plugins.Count; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="logBuilder">TBD</param>
        public ActorProducerPipelineResolver(Func<ILoggingAdapter> logBuilder)
        {
            _log = new Lazy<ILoggingAdapter>(logBuilder);
        }

        /// <summary>
        /// Register target <paramref name="plugin"/> at the end of producer pipeline.
        /// </summary>
        /// <param name="plugin">TBD</param>
        /// <returns>True if plugin was registered (it has not been found in pipeline already). False otherwise. </returns>
        public bool Register(IActorProducerPlugin plugin)
        {
            if (!AlreadyExists(plugin))
            {
                _plugins.Add(plugin);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Register target <paramref name="plugin"/> inside producer pipeline at specified <paramref name="index"/>.
        /// </summary>
        /// <param name="index">TBD</param>
        /// <param name="plugin">TBD</param>
        /// <returns>True if plugin was registered (it has not been found in pipeline already). False otherwise. </returns>
        public bool Insert(int index, IActorProducerPlugin plugin)
        {
            if (!AlreadyExists(plugin))
            {
                _plugins.Insert(index, plugin);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Unregisters plugin from producer pipeline, returning false if plugin was not found.
        /// </summary>
        /// <param name="plugin">TBD</param>
        /// <returns>TBD</returns>
        public bool Unregister(IActorProducerPlugin plugin)
        {
            return _plugins.Remove(plugin);
        }

        /// <summary>
        /// Returns true if current actor producer pipeline already has registered provided plugin type.
        /// </summary>
        /// <param name="plugin">TBD</param>
        /// <returns>TBD</returns>
        public bool IsRegistered(IActorProducerPlugin plugin)
        {
            return _plugins.Any(p => p.GetType() == plugin.GetType());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorType">TBD</param>
        /// <returns>TBD</returns>
        internal ActorProducerPipeline ResolvePipeline(Type actorType)
        {
            return _pipelines.GetOrAdd(actorType, CreatePipeline);
        }

        private ActorProducerPipeline CreatePipeline(Type type)
        {
            return new ActorProducerPipeline(_log, PluginCollectionFor(type));
        }

        private IEnumerable<IActorProducerPlugin> PluginCollectionFor(Type actorType)
        {
            foreach (var plugin in _plugins)
            {
                if (plugin.CanBeAppliedTo(actorType))
                {
                    yield return plugin;
                }
            }
        }

        private bool AlreadyExists(IActorProducerPlugin plugin)
        {
            var pluginType = plugin.GetType();
            var alreadyExists = _plugins.Any(p => p.GetType() == pluginType);
            return alreadyExists;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Obsolete("Actor producer pipeline API will be removed in v1.5.")]
    public class ActorProducerPipeline : IEnumerable<IActorProducerPlugin>
    {
        private Lazy<ILoggingAdapter> _log;
        private readonly List<IActorProducerPlugin> _plugins;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="log">TBD</param>
        /// <param name="plugins">TBD</param>
        public ActorProducerPipeline(Lazy<ILoggingAdapter> log, IEnumerable<IActorProducerPlugin> plugins)
        {
            _log = log;
            _plugins = new List<IActorProducerPlugin>(plugins);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Count { get { return _plugins.Count; } }

        /// <summary>
        /// Resolves and applies all plugins valid to specified underlying <paramref name="actor"/> 
        /// registered in current producer pipeline to newly created actor.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        public void AfterActorIncarnated(ActorBase actor, IActorContext context)
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.AfterIncarnated(actor, context);
                }
                catch (Exception cause)
                {
                    const string fmt = "An exception occurred while trying to apply plugin of type {0} to the newly created actor (Type = {1}, Path = {2})";
                    LogException(actor, cause, fmt, plugin);
                }
            }
        }

        /// <summary>
        /// Resolves and applies all plugins valid to specified underlying <paramref name="actor"/> 
        /// registered in current producer pipeline before old actor would be recycled.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="context">TBD</param>
        public void BeforeActorIncarnated(ActorBase actor, IActorContext context)
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.BeforeIncarnated(actor, context);
                }
                catch (Exception cause)
                {
                    const string fmt = "An exception occurred while trying to apply plugin of type {0} to the actor before it's destruction (Type = {1}, Path = {2})";
                    LogException(actor, cause, fmt, plugin);
                }
            }
        }

        private void LogException(ActorBase actor, Exception e, string errorMessageFormat, IActorProducerPlugin plugin)
        {
            var internalActor = (actor as IInternalActor);
            var actorPath = internalActor != null && internalActor.ActorContext != null
                ? internalActor.ActorContext.Self.Path.ToString()
                : string.Empty;

            _log.Value.Error(e, errorMessageFormat, plugin.GetType(), actor.GetType(), actorPath);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<IActorProducerPlugin> GetEnumerator()
        {
            return _plugins.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}


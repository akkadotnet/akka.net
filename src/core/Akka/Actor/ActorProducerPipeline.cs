//-----------------------------------------------------------------------
// <copyright file="ActorProducerPipeline.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
#if DNXCORE50
using System.Reflection;
#endif
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Plugin interface used to define
    /// </summary>
    public interface IActorProducerPlugin
    {
        /// <summary>
        /// Determines if current plugin can be applied to provided actor based on it's type.
        /// </summary>
        bool CanBeAppliedTo(Type actorType);

        /// <summary>
        /// Plugin behavior applied to underlying <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        void AfterIncarnated(ActorBase actor, IActorContext context);

        /// <summary>
        /// Plugin behavior applied to underlying <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        void BeforeIncarnated(ActorBase actor, IActorContext context);
    }

    /// <summary>
    /// Base actor producer pipeline plugin class.
    /// </summary>
    public abstract class ActorProducerPluginBase : IActorProducerPlugin
    {
        /// <summary>
        /// By default derivatives of this plugin will be applied to all actors.
        /// </summary>
        public virtual bool CanBeAppliedTo(Type actorType)
        {
            return true;
        }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        public virtual void AfterIncarnated(ActorBase actor, IActorContext context) { }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        public virtual void BeforeIncarnated(ActorBase actor, IActorContext context) { }
    }

    /// <summary>
    /// Base generic actor producer pipeline plugin class.
    /// </summary>
    public abstract class ActorProducerPluginBase<TActor> : IActorProducerPlugin where TActor : ActorBase
    {
        /// <summary>
        /// By default derivatives of this plugin will be applied to all actors inheriting from <typeparamref name="TActor">actor generic type</typeparamref>.
        /// </summary>
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
        public virtual void AfterIncarnated(TActor actor, IActorContext context) { }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        public virtual void BeforeIncarnated(TActor actor, IActorContext context) { }
    }

    /// <summary>
    /// Class used to resolving actor producer pipelines depending on actor type.
    /// </summary>
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

        public ActorProducerPipelineResolver(Func<ILoggingAdapter> logBuilder)
        {
            _log = new Lazy<ILoggingAdapter>(logBuilder);
        }

        /// <summary>
        /// Register target <paramref name="plugin"/> at the end of producer pipeline.
        /// </summary>
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
        public bool Unregister(IActorProducerPlugin plugin)
        {
            return _plugins.Remove(plugin);
        }

        /// <summary>
        /// Returns true if current actor producer pipeline already has registered provided plugin type.
        /// </summary>
        public bool IsRegistered(IActorProducerPlugin plugin)
        {
            return _plugins.Any(p => p.GetType() == plugin.GetType());
        }

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

    public class ActorProducerPipeline : IEnumerable<IActorProducerPlugin>
    {
        private Lazy<ILoggingAdapter> _log;
        private readonly List<IActorProducerPlugin> _plugins;

        public ActorProducerPipeline(Lazy<ILoggingAdapter> log, IEnumerable<IActorProducerPlugin> plugins)
        {
            _log = log;
            _plugins = new List<IActorProducerPlugin>(plugins);
        }

        public int Count { get { return _plugins.Count; } }

        /// <summary>
        /// Resolves and applies all plugins valid to specified underlying <paramref name="actor"/> 
        /// registered in current producer pipeline to newly created actor.
        /// </summary>
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


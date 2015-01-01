using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Plugin interface used to define
    /// </summary>
    public interface IActorProducerPlugin
    {
        /// <summary>
        /// Determines if current plugin can be applied to provided <paramref name="actor"/> instance.
        /// </summary>
        bool CanBeAppliedTo(ActorBase actor);

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        void AfterActorCreated(ActorBase actor, IActorContext context);

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        void BeforeActorTerminated(ActorBase actor, IActorContext context);
    }

    /// <summary>
    /// Base actor producer pipeline plugin class.
    /// </summary>
    public abstract class ActorProducerPluginBase : IActorProducerPlugin
    {
        /// <summary>
        /// By default derivatives of this plugin will be applied to all actors.
        /// </summary>
        public virtual bool CanBeAppliedTo(ActorBase actor)
        {
            return true;
        }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        public virtual void AfterActorCreated(ActorBase actor, IActorContext context) { }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        public virtual void BeforeActorTerminated(ActorBase actor, IActorContext context) { }
    }

    /// <summary>
    /// Base generic actor producer pipeline plugin class.
    /// </summary>
    public abstract class ActorProducerPluginBase<TActor> : IActorProducerPlugin where TActor : ActorBase
    {
        /// <summary>
        /// By default derivatives of this plugin will be applied to all actors inheriting from <typeparam name="TActor">actor generic type</typeparam>.
        /// </summary>
        public virtual bool CanBeAppliedTo(ActorBase actor)
        {
            return actor is TActor;
        }

        void IActorProducerPlugin.AfterActorCreated(ActorBase actor, IActorContext context)
        {
            AfterActorCreated(actor as TActor, context);
        }

        void IActorProducerPlugin.BeforeActorTerminated(ActorBase actor, IActorContext context)
        {
            BeforeActorDestroyed(actor as TActor, context);
        }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance when the new one is being created.
        /// </summary>
        public virtual void AfterActorCreated(TActor actor, IActorContext context) { }

        /// <summary>
        /// Plugin behavior applied to <paramref name="actor"/> instance before the actor is being recycled.
        /// </summary>
        public virtual void BeforeActorDestroyed(TActor actor, IActorContext context) { }
    }

    internal class DefaultProducerPipeline : ActorProducerPipeline
    {
        public DefaultProducerPipeline(Func<LoggingAdapter> logBuilder)
            : base(logBuilder, new ActorStashPlugin())
        {
        }
    }

    public class ActorProducerPipeline : IEnumerable<IActorProducerPlugin>
    {
        private LoggingAdapter _log;
        private readonly Func<LoggingAdapter> _logBuilder;
        private readonly List<IActorProducerPlugin> _plugins;

        public ActorProducerPipeline(Func<LoggingAdapter> logBuilder, params IActorProducerPlugin[] defaultPlugins)
        {
            _logBuilder = logBuilder;
            _plugins = defaultPlugins != null && defaultPlugins.Length > 0
                ? new List<IActorProducerPlugin>(defaultPlugins)
                : new List<IActorProducerPlugin>();
        }

        public LoggingAdapter Log { get { return _log ?? (_log = _logBuilder()); } }

        public int Count { get { return _plugins.Count; } }

        public IEnumerator<IActorProducerPlugin> GetEnumerator()
        {
            return _plugins.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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

        /// <summary>
        /// Resolves and applies all plugins valid to specified <paramref name="actor"/> 
        /// registered in current producer pipeline to newly created actor.
        /// </summary>
        internal void AfterActorCreated(ActorBase actor, IActorContext context)
        {
            var pipeline = ResolvePipelineFor(actor).ToArray();

            LogDebugPipeline(actor.GetType(), pipeline);

            foreach (var plugin in pipeline)
            {
                try
                {
                    plugin.AfterActorCreated(actor, context);
                }
                catch (Exception cause)
                {
                    const string fmt = "An exception occured while trying to apply plugin of type {0} to the newly created actor (Type = {1}, Path = {2})";
                    LogException(actor, cause, fmt, plugin);
                }
            }
        }

        /// <summary>
        /// Resolves and applies all plugins valid to specified <paramref name="actor"/> 
        /// registered in current producer pipeline before old actor would be recycled.
        /// </summary>
        internal void BeforeActorTerminated(ActorBase actor, IActorContext context)
        {
            var pipeline = ResolvePipelineFor(actor).ToArray();

            LogDebugPipeline(actor.GetType(), pipeline);

            foreach (var plugin in pipeline)
            {
                try
                {
                    plugin.BeforeActorTerminated(actor, context);
                }
                catch (Exception cause)
                {
                    const string fmt = "An exception occured while trying to apply plugin of type {0} to the actor before it's destruction (Type = {1}, Path = {2})";
                    LogException(actor, cause, fmt, plugin);
                }
            }
        }

        private IEnumerable<IActorProducerPlugin> ResolvePipelineFor(ActorBase actor)
        {
            return _plugins.Where(plugin => plugin.CanBeAppliedTo(actor));
        }

        private void LogDebugPipeline(Type actorType, IActorProducerPlugin[] pipeline)
        {
            if (pipeline.Length > 0 && Log != null && Log.IsDebugEnabled)
            {
                var debugMessage = string.Format("Resolved plugin pipeline for {0} actor: [{1}]", actorType,
                    string.Join(", ", pipeline.Select(plug => plug.GetType())));

                Log.Debug(debugMessage);
            }
        }

        private void LogException(ActorBase actor, Exception e, string errorMessageFormat, IActorProducerPlugin plugin)
        {
            var internalActor = (actor as IInternalActor);
            var actorPath = internalActor != null && internalActor.ActorContext != null
                ? internalActor.ActorContext.Self.Path.ToString()
                : string.Empty;

            Log.Error(e, errorMessageFormat, plugin.GetType(), actor.GetType(), actorPath);
        }

        private bool AlreadyExists(IActorProducerPlugin plugin)
        {
            var pluginType = plugin.GetType();
            var alreadyExists = _plugins.Any(p => p.GetType() == pluginType);
            return alreadyExists;
        }
    }
}
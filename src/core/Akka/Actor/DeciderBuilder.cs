using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using Akka.Actor.Internal;
namespace Akka.Actor
{
    /// <summary>
    /// Creates a builder that given a set of Exception-type to <see cref="Directive"/> mappings builds a Decider.
    /// The builder supports collection initialization, both  <see cref="Directive"/> and a 
    /// Func&lt;<see cref="Exception"/>,<see cref="Directive"/>&gt; that given the matched exception returns the 
    /// <see cref="Directive"/>
    /// <example>
    /// <pre><code>var decider = new DeciderBuilder {
    ///   { typeof(SomeException), Directive.Restart },
    ///   { typeof(SomeOtherException), Directive.Stop },
    ///   { typeof(SomeThirdException), e => e.SomeValue ? Directive.Restart : Directive.Escalate },
    /// }.Build();
    /// </code></pre>
    /// </example>
    /// As a DeciderBuilder can be implicitly casted to a decider function, it can be used directly when creating a <see cref="SupervisorStrategy"/>.
    /// <example>
    /// <pre><code>var strategy = new OneForOneStrategy(new DeciderBuilder {
    ///   { typeof(SomeException), Directive.Restart },
    ///   { typeof(SomeOtherException), Directive.Stop },
    ///   { typeof(SomeThirdException), e => e.SomeValue ? Directive.Restart : Directive.Escalate },
    /// }));
    /// </code></pre>
    /// </example>
    /// </summary>
    public class DeciderBuilder : IEnumerable           //Need to be an IEnumerable so that collection initialization works
    {
        private readonly Func<Exception, Directive> _fallback;
        private readonly List<Tuple<Type, Func<Exception, Directive>>> _mappings;

        /// <summary>
        /// Creates a new builder.
        /// <example>
        /// <pre><code>var decider = new DeciderBuilder {
        ///   { typeof(SomeException), Directive.Restart },
        ///   { typeof(SomeOtherException), Directive.Stop },
        ///   { typeof(SomeThirdException), e => e.SomeValue ? Directive.Restart : Directive.Escalate },
        /// }.Build();
        /// </code></pre>
        /// </example>
        /// </summary>
        public DeciderBuilder()
        {
            _fallback = SupervisorStrategy.DefaultDecider;
            _mappings = new List<Tuple<Type, Func<Exception, Directive>>>();
        }

        /// <summary>
        /// Creates a new builder that creates a decider that will fallback 
        /// to the specified <see cref="Directive"/> if no matching mapping is found.
        /// <example>
        /// <pre><code>var decider = new DeciderBuilder {
        ///   { typeof(SomeException), Directive.Restart },
        ///   { typeof(SomeOtherException), Directive.Stop },
        /// }.Build();
        /// </code></pre>
        /// </example>
        /// </summary>
        public DeciderBuilder(Directive fallback)
        {
            _fallback = e => fallback;
            _mappings = new List<Tuple<Type, Func<Exception, Directive>>>();
        }


        /// <summary>
        /// Creates a new builder that creates a decider that will fallback and call
        /// the specified <paramref name="fallback"/> if no matching mapping is found.
        /// <example>
        /// <pre><code>var decider = new DeciderBuilder {
        ///   { typeof(SomeException), Directive.Restart },
        ///   { typeof(SomeThirdException), e => e.SomeValue ? Directive.Restart : Directive.Escalate },
        ///   { typeof(SomeOtherException), Directive.Stop },
        /// }.Build();
        /// </code></pre>
        /// </example>
        /// </summary>
        public DeciderBuilder(Func<Exception, Directive> fallback)
        {
            _fallback = fallback;
            _mappings = new List<Tuple<Type, Func<Exception, Directive>>>();
        }

        /// <summary>Creates a new builder that will fallback to <see cref="SupervisorStrategy.DefaultDecider"/> 
        /// if no matching mapping is found.</summary>
        public DeciderBuilder(IEnumerable<Tuple<Type, Directive>> mappings)
        {
            _mappings = new List<Tuple<Type, Func<Exception, Directive>>>(mappings.Select(t => new Tuple<Type, Func<Exception, Directive>>(t.Item1, e => t.Item2)));
        }

        /// <summary>
        /// Creates a new builder that creates a decider that will fallback 
        /// to the specified <see cref="Directive"/> if no matching mapping is found.
        /// </summary>
        public DeciderBuilder(IEnumerable<Tuple<Type, Directive>> mappings, Directive fallback)
            : this(mappings)
        {
            _fallback = e => fallback;
        }

        /// <summary>
        /// Creates a new builder that creates a decider that will fallback and call
        /// the specified <paramref name="fallback"/> if no matching mapping is found.
        /// </summary>
        public DeciderBuilder(IEnumerable<Tuple<Type, Directive>> mappings, Func<Exception, Directive> fallback)
            : this(mappings)
        {
            _fallback = fallback;
        }


        /// <summary>Creates a new builder that will fallback to <see cref="SupervisorStrategy.DefaultDecider"/> 
        /// if no matching mapping is found.</summary>
        public DeciderBuilder(IEnumerable<Tuple<Type, Func<Exception,Directive>>> mappings)
        {
            _mappings = new List<Tuple<Type, Func<Exception, Directive>>>(mappings);
        }

        /// <summary>
        /// Creates a new builder that creates a decider that will fallback 
        /// to the specified <see cref="Directive"/> if no matching mapping is found.
        /// </summary>
        public DeciderBuilder(IEnumerable<Tuple<Type, Func<Exception, Directive>>> mappings, Directive fallback)
            : this(mappings)
        {
            _fallback = e => fallback;
        }

        /// <summary>
        /// Creates a new builder that creates a decider that will fallback and call
        /// the specified <paramref name="fallback"/> if no matching mapping is found.
        /// </summary>
        public DeciderBuilder(IEnumerable<Tuple<Type, Func<Exception, Directive>>> mappings, Func<Exception, Directive> fallback)
            : this(mappings)
        {
            _fallback = fallback;
        }

        /// <summary>
        /// Adds a new mapping from a type of exception to a <see cref="Directive"/>.
        /// Typically you would use a collection initializer instead of calling this manually.
        /// <example>
        /// <pre><code>var decider = new DeciderBuilder {
        ///   { typeof(SomeException), Directive.Restart },
        ///   { typeof(SomeOtherException), Directive.Stop },
        ///   { typeof(SomeThirdException), e => e.SomeValue ? Directive.Restart : Directive.Escalate },
        /// }.Build();
        /// </code></pre>
        /// </example>
        /// </summary>
        /// <param name="exceptionType">Type of exception.</param>
        /// <param name="directive">The directive.</param>
        /// <returns>Returns this instance. Note that the instance is modified AND returned to allow for chaining.</returns>
        public DeciderBuilder Add(Type exceptionType, Directive directive)
        {
            _mappings.Add(new Tuple<Type, Func<Exception, Directive>>(exceptionType, e => directive));
            return this;
        }


        /// <summary>
        /// Adds a new mapping from a type of exception to a <see cref="Directive"/>. 
        /// The <see cref="Directive"/> is created by calling <paramref name="getDirective"/>.
        /// Typically you would use a constructor initializer instead of calling this manually.
        /// <example>
        /// <pre><code>var decider = new DeciderBuilder {
        ///   { typeof(SomeException), Directive.Restart },
        ///   { typeof(SomeOtherException), Directive.Stop },
        ///   { typeof(SomeThirdException), e => e.SomeValue ? Directive.Restart : Directive.Escalate },
        /// }.Build();
        /// </code></pre>
        /// </example>
        /// </summary>
        /// <param name="exceptionType">Type of exception.</param>
        /// <param name="getDirective">A function that given the matched exception returns a <see cref="Directive"/>.</param>
        /// <returns>Returns this instance. Note that the instance is modified AND returned to allow for chaining.</returns>
        public DeciderBuilder Add(Type exceptionType, Func<Exception,Directive> getDirective)
        {
            _mappings.Add(Tuple.Create(exceptionType, getDirective));
            return this;
        }

        /// <summary>
        /// Adds a new mapping from a type of exception to a <see cref="Directive"/>.
        /// Typically you would use a constructor initializer instead of calling this manually.
        /// <example>
        /// <pre><code>var decider = new DeciderBuilder {
        ///   { typeof(SomeException), Directive.Restart },
        ///   { typeof(SomeOtherException), Directive.Stop },
        /// }.Build();
        /// </code></pre>
        /// </example>
        /// </summary>
        /// <typeparam name="TException">Type of exception.</typeparam>        
        /// <param name="directive">The directive.</param>
        public DeciderBuilder Add<TException>(Directive directive) where TException : Exception
        {
            _mappings.Add(new Tuple<Type, Func<Exception, Directive>>(typeof(TException), e=>directive));
            return this;
        }

       
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _mappings.GetEnumerator();
        }


        /// <summary>
        /// Creates a decider from the set of type-to-Directive mappings. 
        /// The mappings are sorted so the most specific type is checked before all its subtypes.
        /// If no matching mapping exists, the fallback provided in the constructor will be used, or if none given
        /// <see cref="SupervisorStrategy.DefaultDecider"/> will be used.
        /// </summary>
        public Func<Exception, Directive> Build()
        {
            return CreateDecider(_mappings, _fallback);
        }


        /// <summary>
        /// When supervisorStrategy is not specified for an actor this
        /// decider is used by default in the supervisor strategy.
        /// The child will be stopped when <see cref="ActorInitializationException"/>,
        /// <see cref="ActorKilledException"/>, or <see cref="DeathPactException"/> is
        /// thrown. It will be restarted for other <see cref="Exception"/> types.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>A <see cref="Directive"/> that instructs the supervisor how to handle the failure.</returns>
        public static Directive DefaultDecider(Exception exception)
        {
            return SupervisorStrategy.DefaultDecider(exception);
        }

        /// <summary>
        /// Creates a decider from a set of <see cref="Exception"/> to <see cref="Directive"/> mappings. 
        /// The mappings are sorted so the most specific type is checked before all its subtypes.
        /// If no matching mapping exists, <paramref name="fallbackDecider"/> will be called
        /// to map the exception to a <see cref="Directive"/>. If <paramref name="fallbackDecider"/> is <c>null</c>
        /// <see cref="Directive.Escalate"/> will be returned by the returned decider.
        /// <example>If mappings for <see cref="Exception"/> and <see cref="InvalidOperationException"/> types are given,
        /// then <see cref="InvalidOperationException"/> will be checked before <see cref="Exception"/>.</example>
        /// </summary>
        /// <param name="exceptionTypeToDirectiveMappings">A set of mappings that maps an <see cref="Exception"/> 
        /// type to a <see cref="Directive"/>.</param>
        /// <param name="fallbackDecider">This decider is called if no matching mapping was found, to handle an exception</param>
        /// <returns>A decider, i.e. a Func that given an <see cref="Exception"/>
        /// returns a <see cref="Directive"/></returns>
        public static Func<Exception, Directive> CreateDecider(IEnumerable<Tuple<Type, Func<Exception, Directive>>> exceptionTypeToDirectiveMappings, Func<Exception, Directive> fallbackDecider)
        {
            var orderedMappings = SortMappingsSoSubtypesPrecedeTheirSupertypes(exceptionTypeToDirectiveMappings);
            Func<Exception, Directive> decider = e =>
            {
                var match = orderedMappings.FirstOrDefault(m => m.Item1.IsInstanceOfType(e));
                if(match != null)
                    return match.Item2(e);
                return fallbackDecider != null ? fallbackDecider(e) : Directive.Escalate;
            };
            return decider;
        }

        private static List<Tuple<Type, Func<Exception, Directive>>> SortMappingsSoSubtypesPrecedeTheirSupertypes(IEnumerable<Tuple<Type, Func<Exception, Directive>>> mappings)
        {
            return TypeListSorter.SortSoSubtypesPrecedeTheirSupertypes(mappings, t => t.Item1);
        }

        /// <summary>
        /// Converts a <see cref="DeciderBuilder"/> to a decider, i.e. <see cref="Func{Exception, Directive}"/>.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <returns>The decider</returns>
        public static implicit operator Func<Exception, Directive>(DeciderBuilder builder)
        {
            return builder.Build();
        }
    }

}
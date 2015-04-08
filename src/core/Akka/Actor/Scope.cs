using System;

namespace Akka.Actor
{
    /// <summary>
    /// Defines the scope of a <see cref="Deploy"/>
    /// 
    /// Valid values are:
    /// 
    /// * Local - this actor will be deployed locally in this process
    /// * Remote - this actor will be deployed remotely on another system
    /// * Cluster - this actor will be deployed into a cluster of remote processes
    /// </summary>
    public abstract class Scope : IEquatable<Scope>
    {
        public static readonly LocalScope Local = LocalScope.Instance;

        public abstract Scope WithFallback(Scope other);

        public abstract Scope Copy();

        public virtual bool Equals(Scope other)
        {
            if (other == null) return false;

            //we don't do equality checks on fallbacks
            return GetType() == other.GetType();
        }
    }

    /// <summary>
    /// Place-holder for when a scope of this deployment has not been specified yet
    /// </summary>
    internal class NoScopeGiven : Scope
    {
        private NoScopeGiven() { }

        private static readonly NoScopeGiven _instance = new NoScopeGiven();

        public static NoScopeGiven Instance
        {
            get { return _instance; }
        }

        public override Scope WithFallback(Scope other)
        {
            return other;
        }

        public override Scope Copy()
        {
            return Instance;
        }
    }
}

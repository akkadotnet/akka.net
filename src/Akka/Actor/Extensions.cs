namespace Akka.Actor
{
    /// <summary>
    /// Marker interface used to identify an object as ActorSystem extension
    /// </summary>
    public interface IExtension { }

    /// <summary>
    /// Non-generic version of interface, mostly to avoid issues with generic casting
    /// </summary>
    public interface IExtensionId
    {
        /// <summary>
        /// Returns an instance of the extension identified by this ExtensionId instance
        /// </summary>
        object Apply(ActorSystem system);

        /// <summary>
        /// Returns an instance of the extension identified by this <see cref="IExtensionId{T}"/> instance
        /// </summary>
        object Get(ActorSystem system);

        /// <summary>
        /// Is used by Akka to instantiate the <see cref="IExtension"/> identified by this ExtensionId.
        /// Internal use only.
        /// </summary>
        object CreateExtension(ActorSystem system);
    }

    /// <summary>
    /// Marker interface used to distinguish a unqiue ActorSystem extensions
    /// </summary>
    public interface IExtensionId<out T> : IExtensionId where T:IExtension
    {
        /// <summary>
        /// Returns an instance of the extension identified by this ExtensionId instance
        /// </summary>
        new T Apply(ActorSystem system);

        /// <summary>
        /// Returns an instance of the extension identified by this <see cref="IExtensionId{T}"/> instance
        /// </summary>
        new T Get(ActorSystem system);

        /// <summary>
        /// Is used by Akka to instantiate the <see cref="IExtension"/> identified by this ExtensionId.
        /// Internal use only.
        /// </summary>
        new T CreateExtension(ActorSystem system);
    }

    /// <summary>
    ///     Class ExtensionBase.
    /// </summary>
    public abstract class ExtensionIdProvider<T> : IExtensionId<T> where T:IExtension
    {
        public T Apply(ActorSystem system)
        {
            return (T)system.RegisterExtension(this);
        }

        object IExtensionId.Get(ActorSystem system)
        {
            return Get(system);
        }

        object IExtensionId.CreateExtension(ActorSystem system)
        {
            return CreateExtension(system);
        }

        object IExtensionId.Apply(ActorSystem system)
        {
            return Apply(system);
        }

        public T Get(ActorSystem system)
        {
            return (T)system.GetExtension(this);
        }

        public abstract T CreateExtension(ActorSystem system);

        public override bool Equals(object obj)
        {
            return obj is T;
        }

        public override int GetHashCode()
        {
            return typeof (T).GetHashCode();
        }
    }
}

namespace Akka.Actor.Internals
{
    /// <summary>
    /// Marks that the actor needs to be initialized directly after it has been created.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public interface IInitializableActor
    {
        void Init();
    }
}
namespace Akka.Actor
{
    /// <summary>
    ///     Marker Interface INoSerializationVerificationNeeded, this interface prevents
    ///     implementing message types from being serialized if configuration setting 'akka.actor.serialize-messages' is "on"
    /// </summary>
// ReSharper disable once InconsistentNaming
    public interface INoSerializationVerificationNeeded
    {
    }

    /// <summary>
    /// Marker interface to indicate that a message might be potentially harmful;
    /// this is used to block messages coming in over remoting.
    /// </summary>
// ReSharper disable once InconsistentNaming
    public interface IPossiblyHarmful { }
}
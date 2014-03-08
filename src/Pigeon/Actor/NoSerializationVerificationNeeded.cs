namespace Akka.Actor
{
    /// <summary>
    ///     Marker Interface NoSerializationVerificationNeeded, this interface prevents
    ///     implementing message types from being serialized if configuration setting 'akka.actor.serialize-messages' is "on"
    /// </summary>
    public interface NoSerializationVerificationNeeded
    {
    }
}
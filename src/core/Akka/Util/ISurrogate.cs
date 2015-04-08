using Akka.Actor;

namespace Akka.Util
{
    public interface ISurrogate
    {
        ISurrogated FromSurrogate(ActorSystem system);
    }

    public interface ISurrogated
    {
        ISurrogate ToSurrogate(ActorSystem system);
    }
}
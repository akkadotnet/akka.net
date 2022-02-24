#region actor-bridge
namespace Akka.AspNetCore
{
    public interface IActorBridge
    {
        void Tell(object message);
        Task<T> Ask<T>(object message);
    }
}
#endregion

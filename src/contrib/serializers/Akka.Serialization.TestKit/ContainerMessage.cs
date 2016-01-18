namespace Akka.Tests.Serialization
{
    public class ContainerMessage<T>
    {
        public ContainerMessage(T contents)
        {
            Contents = contents;
        }
        public T Contents { get; private set; }
    }
}
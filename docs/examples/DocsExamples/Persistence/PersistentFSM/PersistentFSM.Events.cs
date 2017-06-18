namespace DocsExamples.Persistence.PersistentFSM
{
    public interface IDomainEvent
    {
    }

    public class ItemAdded : IDomainEvent
    {
        public ItemAdded(Item item)
        {
            Item = item;
        }

        public Item Item { get; set; }
    }

    public class OrderExecuted : IDomainEvent
    {
    }

    public class OrderDiscarded : IDomainEvent
    {
    }
}

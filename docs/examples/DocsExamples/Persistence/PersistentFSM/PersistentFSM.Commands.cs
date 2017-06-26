namespace DocsExamples.Persistence.PersistentFSM
{
    public interface ICommand
    {
    }

    public class AddItem : ICommand
    {
        public AddItem(Item item)
        {
            Item = item;
        }

        public Item Item { get; set; }
    }

    public class Buy : ICommand
    {
    }

    public class Leave : ICommand
    {
    }

    public class GetCurrentCart : ICommand
    {
    }
}

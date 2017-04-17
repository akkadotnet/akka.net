using System.Collections.Generic;

namespace DocsExamples.Persistence.PersistentFSM
{
    public class Item
    {
        public Item(string id, string name, double price)
        {
            Id = id;
            Name = name;
            Price = price;
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public double Price { get; set; }
    }

    public interface IShoppingCart
    {
        ICollection<Item> Items { get; set; }

        IShoppingCart AddItem(Item item);

        IShoppingCart Empty();
    }

    public class EmptyShoppingCart : IShoppingCart
    {
        public IShoppingCart AddItem(Item item)
        {
            return new NonEmptyShoppingCart(item);
        }

        public IShoppingCart Empty()
        {
            return this;
        }

        public ICollection<Item> Items { get; set; }
    }

    public class NonEmptyShoppingCart : IShoppingCart
    {
        public NonEmptyShoppingCart(Item item)
        {
            Items = new List<Item>();
            Items.Add(item);
        }

        public IShoppingCart AddItem(Item item)
        {
            Items.Add(item);
            return this;
        }

        public IShoppingCart Empty()
        {
            return new EmptyShoppingCart();
        }

        public ICollection<Item> Items { get; set; }
    }
}

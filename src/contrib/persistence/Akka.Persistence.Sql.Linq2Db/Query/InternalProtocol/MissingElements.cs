using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol
{
    public class MissingElements
    {
        public MissingElements(Seq<NumericRangeEntry> elements)
        {
            Elements = elements;
        }

        public MissingElements AddRange(long from, long until)
        {
            return new MissingElements(
                Elements.Add(new NumericRangeEntry(from, until)));
        }

        public bool Contains(long id)
        {
            return Elements.Any(r => r.InRange(id));
        }

        public bool Isempty => Elements.IsEmpty;
        public Seq<NumericRangeEntry> Elements { get; protected set; }
        
        public static readonly MissingElements Empty = new MissingElements(Seq<NumericRangeEntry>.Empty);
    }
}
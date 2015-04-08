using QDFeedParser;

namespace SymbolLookup.Actors.Messages
{
    public class CompletedHeadlinesDownload
    {
        public string Symbol { get; set; }
        public IFeed Feed { get; set; }
    }
}

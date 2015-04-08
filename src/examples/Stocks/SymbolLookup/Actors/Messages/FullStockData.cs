using QDFeedParser;
using SymbolLookup.YahooFinance;

namespace SymbolLookup.Actors.Messages
{
    public class FullStockData
    {
        public string Symbol { get; set; }

        public IFeed Headlines { get; set; }

        public Quote Quote { get; set; }
    }
}

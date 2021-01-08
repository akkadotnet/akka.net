//-----------------------------------------------------------------------
// <copyright file="MainForm.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Windows.Forms;
using Akka.Actor;
using QDFeedParser;
using SymbolLookup.Actors;
using SymbolLookup.Actors.Messages;
using SymbolLookup.YahooFinance;

namespace SymbolLookup
{
    public partial class MainForm : Form
    {
        private readonly object m_lock = new object();
        public ImmutableDictionary<string, Tuple<Quote, IFeed>> StockData { get; set; }
        public ActorSystem ActorSystem;
        public IActorRef StockActor;

        public event EventHandler<FullStockData> DataAvailable;
        public event EventHandler<string> StatusChange;

        public void UpdateStatus(string status)
        {
            tsStatusLabel.Text = status;
        }

        public MainForm()
        {
            InitializeComponent();
            ActorSystem = ActorSystem.Create("stocks");
            StockData = ImmutableDictionary.Create<string, Tuple<Quote, IFeed>>();
            DataAvailable += OnDataAvailable;
            StatusChange += (sender, s) =>
            {
                UpdateStatus(s);
            };
        }

        private void OnDataAvailable(object sender, FullStockData fullStockData)
        {
            UpdateStockData(fullStockData);
        }

        public void UpdateStockData(FullStockData data)
        {
            StockData = StockData.SetItem(data.Symbol, new Tuple<Quote, IFeed>(data.Quote, data.Headlines));
            this.lstDownloadedStocks.Items.Clear();
            lstDownloadedStocks.Items.AddRange(StockData.Select(x => new ListViewItem(x.Key)).ToArray());
        }

        private void lstDownloadedStocks_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            var key = e.Item.Text;
            var data = StockData[key];
            txtQuote.Text =
                string.Format(string.Format("Symbol: {0} - Ask {1} - Bid {2} @ {3}", data.Item1.Symbol, data.Item1.Ask ?? data.Item1.AskRealtime,
                    data.Item1.Bid ?? data.Item1.BidRealtime, data.Item1.LastTradeDate));
            lstNews.Items.Clear();
            lstNews.Items.AddRange(data.Item2.Items.Select(x => new ListViewItem(string.Format("{0}: {1} - {2}", x.Author, x.Title, x.DatePublished))).ToArray());
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            StockActor =
                ActorSystem
                .ActorOf(
                    Props.Create(() => new DispatcherActor(DataAvailable, StatusChange))
                    .WithDispatcher("akka.actor.synchronized-dispatcher") //dispatch on GUI thread
                ,"dispatcher"); //new DispatcherActor(DataAvailable, StatusChange)
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ActorSystem.Terminate();
        }

        private void btn_Go_Click(object sender, EventArgs e)
        {
            //Do nothing
            if (string.IsNullOrEmpty(txtSymbols.Text))
                return;
            StockActor.Tell(txtSymbols.Text, ActorRefs.NoSender);
        }
    }
}


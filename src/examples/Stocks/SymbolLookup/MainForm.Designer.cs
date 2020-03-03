//-----------------------------------------------------------------------
// <copyright file="MainForm.Designer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace SymbolLookup
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.txtSymbols = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btn_Go = new System.Windows.Forms.Button();
            this.lstDownloadedStocks = new System.Windows.Forms.ListView();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.lblCurrentStock = new System.Windows.Forms.Label();
            this.lblPrice = new System.Windows.Forms.Label();
            this.txtQuote = new System.Windows.Forms.TextBox();
            this.lblNews = new System.Windows.Forms.Label();
            this.lstNews = new System.Windows.Forms.ListView();
            this.activityStatus = new System.Windows.Forms.StatusStrip();
            this.tsStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.activityStatus.SuspendLayout();
            this.SuspendLayout();
            // 
            // txtSymbols
            // 
            this.txtSymbols.Location = new System.Drawing.Point(208, 10);
            this.txtSymbols.Name = "txtSymbols";
            this.txtSymbols.Size = new System.Drawing.Size(210, 20);
            this.txtSymbols.TabIndex = 0;
            this.txtSymbols.Text = "msft,aapl";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(13, 13);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(189, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Get Quotes (separate symbols with \",\")";
            // 
            // btn_Go
            // 
            this.btn_Go.Location = new System.Drawing.Point(434, 7);
            this.btn_Go.Name = "btn_Go";
            this.btn_Go.Size = new System.Drawing.Size(75, 23);
            this.btn_Go.TabIndex = 2;
            this.btn_Go.Text = "Go!";
            this.btn_Go.UseVisualStyleBackColor = true;
            this.btn_Go.Click += new System.EventHandler(this.btn_Go_Click);
            // 
            // lstDownloadedStocks
            // 
            this.lstDownloadedStocks.Location = new System.Drawing.Point(16, 61);
            this.lstDownloadedStocks.MultiSelect = false;
            this.lstDownloadedStocks.Name = "lstDownloadedStocks";
            this.lstDownloadedStocks.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.lstDownloadedStocks.Size = new System.Drawing.Size(186, 109);
            this.lstDownloadedStocks.TabIndex = 3;
            this.lstDownloadedStocks.UseCompatibleStateImageBehavior = false;
            this.lstDownloadedStocks.ItemSelectionChanged += new System.Windows.Forms.ListViewItemSelectionChangedEventHandler(this.lstDownloadedStocks_ItemSelectionChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 42);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(103, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "Downloaded Stocks";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(16, 173);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(75, 13);
            this.label3.TabIndex = 5;
            this.label3.Text = "Current Stock:";
            // 
            // lblCurrentStock
            // 
            this.lblCurrentStock.AutoSize = true;
            this.lblCurrentStock.Location = new System.Drawing.Point(98, 173);
            this.lblCurrentStock.Name = "lblCurrentStock";
            this.lblCurrentStock.Size = new System.Drawing.Size(10, 13);
            this.lblCurrentStock.TabIndex = 6;
            this.lblCurrentStock.Text = "-";
            // 
            // lblPrice
            // 
            this.lblPrice.AutoSize = true;
            this.lblPrice.Location = new System.Drawing.Point(209, 61);
            this.lblPrice.Name = "lblPrice";
            this.lblPrice.Size = new System.Drawing.Size(39, 13);
            this.lblPrice.TabIndex = 7;
            this.lblPrice.Text = "Quote:";
            // 
            // txtQuote
            // 
            this.txtQuote.Location = new System.Drawing.Point(254, 58);
            this.txtQuote.Name = "txtQuote";
            this.txtQuote.ReadOnly = true;
            this.txtQuote.Size = new System.Drawing.Size(255, 20);
            this.txtQuote.TabIndex = 8;
            // 
            // lblNews
            // 
            this.lblNews.AutoSize = true;
            this.lblNews.Location = new System.Drawing.Point(209, 85);
            this.lblNews.Name = "lblNews";
            this.lblNews.Size = new System.Drawing.Size(37, 13);
            this.lblNews.TabIndex = 10;
            this.lblNews.Text = "News:";
            // 
            // lstNews
            // 
            this.lstNews.Location = new System.Drawing.Point(254, 85);
            this.lstNews.Name = "lstNews";
            this.lstNews.Size = new System.Drawing.Size(255, 287);
            this.lstNews.TabIndex = 11;
            this.lstNews.UseCompatibleStateImageBehavior = false;
            // 
            // activityStatus
            // 
            this.activityStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsStatusLabel});
            this.activityStatus.Location = new System.Drawing.Point(0, 384);
            this.activityStatus.Name = "activityStatus";
            this.activityStatus.Size = new System.Drawing.Size(534, 22);
            this.activityStatus.TabIndex = 12;
            this.activityStatus.Text = "statusStrip1";
            // 
            // tsStatusLabel
            // 
            this.tsStatusLabel.Name = "tsStatusLabel";
            this.tsStatusLabel.Size = new System.Drawing.Size(23, 17);
            this.tsStatusLabel.Text = "OK";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(534, 406);
            this.Controls.Add(this.activityStatus);
            this.Controls.Add(this.lstNews);
            this.Controls.Add(this.lblNews);
            this.Controls.Add(this.txtQuote);
            this.Controls.Add(this.lblPrice);
            this.Controls.Add(this.lblCurrentStock);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.lstDownloadedStocks);
            this.Controls.Add(this.btn_Go);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtSymbols);
            this.Name = "MainForm";
            this.Text = "Hyperion Stock Downloader Example";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.activityStatus.ResumeLayout(false);
            this.activityStatus.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtSymbols;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btn_Go;
        private System.Windows.Forms.ListView lstDownloadedStocks;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label lblCurrentStock;
        private System.Windows.Forms.Label lblPrice;
        private System.Windows.Forms.TextBox txtQuote;
        private System.Windows.Forms.Label lblNews;
        private System.Windows.Forms.ListView lstNews;
        private System.Windows.Forms.StatusStrip activityStatus;
        private System.Windows.Forms.ToolStripStatusLabel tsStatusLabel;
    }
}



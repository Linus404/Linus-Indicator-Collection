#region Using declarations
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using DxBrush = SharpDX.Direct2D1.Brush;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLMarketDepth : Indicator
    {
        private readonly object depthLock = new object();
        private readonly SortedDictionary<double, double> bids = new SortedDictionary<double, double>();
        private readonly SortedDictionary<double, double> asks = new SortedDictionary<double, double>();
        private DxBrush bidBrushDx;
        private DxBrush askBrushDx;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Market Depth";
                Description = "DOM histogram built from market depth updates, rendered as bid/ask depth bars.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                Levels = 30;
                Width = 90;
                BarOpacity = 70;
                BidColor = WpfBrushes.Green;
                AskColor = WpfBrushes.Red;
                AddPlot(WpfBrushes.Transparent, "DepthMid");
            }
            else if (State == State.Historical)
            {
                SetZOrder(-1);
            }
            else if (State == State.Terminated)
            {
                DisposeBrushes();
            }
        }

        protected override void OnMarketDepth(MarketDepthEventArgs e)
        {
            SortedDictionary<double, double> book = e.MarketDataType == MarketDataType.Bid ? bids : e.MarketDataType == MarketDataType.Ask ? asks : null;
            if (book == null) return;

            lock (depthLock)
            {
                if (e.Operation == Operation.Remove || e.Volume <= 0) book.Remove(e.Price);
                else book[e.Price] = e.Volume;
            }
            ForceRefresh();
        }

        protected override void OnBarUpdate()
        {
            Values[0][0] = Close[0];
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || RenderTarget == null) return;
            if (bidBrushDx == null || askBrushDx == null) OnRenderTargetChanged();
            if (bidBrushDx == null || askBrushDx == null) return;

            List<KeyValuePair<double, double>> visibleBids;
            List<KeyValuePair<double, double>> visibleAsks;
            lock (depthLock)
            {
                visibleBids = bids.Where(kv => kv.Value > 0 && kv.Key >= chartScale.MinValue && kv.Key <= chartScale.MaxValue)
                                  .OrderByDescending(kv => kv.Key).Take(Levels).ToList();
                visibleAsks = asks.Where(kv => kv.Value > 0 && kv.Key >= chartScale.MinValue && kv.Key <= chartScale.MaxValue)
                                  .OrderBy(kv => kv.Key).Take(Levels).ToList();
            }
            double maxVolume = visibleBids.Concat(visibleAsks).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            if (maxVolume <= 0) return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            foreach (var row in visibleBids) FillDepthRow(chartControl, chartScale, row.Key, row.Value, maxVolume, bidBrushDx);
            foreach (var row in visibleAsks) FillDepthRow(chartControl, chartScale, row.Key, row.Value, maxVolume, askBrushDx);
        }

        private void FillDepthRow(ChartControl chartControl, ChartScale chartScale, double price, double volume, double maxVolume, DxBrush brush)
        {
            double tick = NTLCore.TickSize(this);
            float yTop = chartScale.GetYByValue(price + tick * 0.5);
            float yBottom = chartScale.GetYByValue(price - tick * 0.5);
            float height = Math.Max(1, yBottom - yTop);
            float width = (float)(Math.Max(1, Width) * (volume / maxVolume));
            float x = chartControl.CanvasRight - width;
            RenderTarget.FillRectangle(new SharpDX.RectangleF(x, yTop, width, height), brush);
        }

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null) return;
            bidBrushDx = BidColor.ToDxBrush(RenderTarget);
            askBrushDx = AskColor.ToDxBrush(RenderTarget);
            bidBrushDx.Opacity = Math.Max(0, Math.Min(100, BarOpacity)) / 100f;
            askBrushDx.Opacity = Math.Max(0, Math.Min(100, BarOpacity)) / 100f;
        }

        private void DisposeBrushes()
        {
            if (bidBrushDx != null) { bidBrushDx.Dispose(); bidBrushDx = null; }
            if (askBrushDx != null) { askBrushDx.Dispose(); askBrushDx = null; }
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Levels", GroupName = "Display", Order = 0)]
        public int Levels { get; set; }

        [NinjaScriptProperty]
        [Range(10, 400)]
        [Display(Name = "Width", GroupName = "Display", Order = 1)]
        public int Width { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Bar opacity %", GroupName = "Display", Order = 2)]
        public int BarOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Bid color", GroupName = "Display", Order = 3)]
        public WpfBrush BidColor { get; set; }

        [Browsable(false)]
        public string BidColorSerializable
        {
            get { return Serialize.BrushToString(BidColor); }
            set { BidColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Ask color", GroupName = "Display", Order = 4)]
        public WpfBrush AskColor { get; set; }

        [Browsable(false)]
        public string AskColorSerializable
        {
            get { return Serialize.BrushToString(AskColor); }
            set { AskColor = Serialize.StringToBrush(value); }
        }
    }
}

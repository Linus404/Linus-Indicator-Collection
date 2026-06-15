#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using SharpDX;
using DxBrush = SharpDX.Direct2D1.Brush;
using SolidColorBrushDx = SharpDX.Direct2D1.SolidColorBrush;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLBookmap : Indicator
    {
        private sealed class DepthLevel
        {
            public double Bid;
            public double Ask;
            public double Liquidity { get { return Bid + Ask; } }
        }

        private sealed class HeatSnapshot
        {
            public int BarIndex;
            public readonly Dictionary<double, double> LiquidityByPrice = new Dictionary<double, double>();
            public double LastPrice = double.NaN;
        }

        private readonly object bookLock = new object();
        private readonly Dictionary<double, DepthLevel> currentBook = new Dictionary<double, DepthLevel>();
        private readonly List<HeatSnapshot> snapshots = new List<HeatSnapshot>();
        private double lastPrice = double.NaN;
        private int lastSnapshotBar = -1;

        private DxBrush bidBrushDx;
        private DxBrush askBrushDx;
        private DxBrush lastBrushDx;
        private DxBrush[] heatBrushes;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Bookmap";
                Description = "Bookmap-style historical limit-order-book liquidity heatmap.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;

                BarsBack = 300;
                PriceBucketTicks = 1;
                MinimumSize = 10;
                MaxLevels = 500;
                CellOpacity = 80;
                IntensityPower = 0.65;
                HeatFloorPercent = 8;
                HeatScalePercentile = 95;
                ShowLastPrice = false;
                ShowCurrentDepth = true;
                Width = 90;

                BidColor = WpfBrushes.DodgerBlue;
                AskColor = WpfBrushes.IndianRed;
                LastTradeColor = WpfBrushes.White;

                AddPlot(WpfBrushes.Transparent, "BookmapMid");
            }
            else if (State == State.Historical)
            {
                SetZOrder(-3);
            }
            else if (State == State.Terminated)
            {
                DisposeBrushes();
            }
        }

        protected override void OnMarketDepth(MarketDepthEventArgs e)
        {
            string type = e.MarketDataType.ToString();
            bool isBid = type == "Bid";
            bool isAsk = type == "Ask";
            if (!isBid && !isAsk)
                return;

            double price = RoundToHeatBucket(e.Price);
            double volume = e.Operation.ToString() == "Remove" ? 0 : Math.Max(0, e.Volume);

            lock (bookLock)
            {
                DepthLevel level;
                if (!currentBook.TryGetValue(price, out level))
                {
                    level = new DepthLevel();
                    currentBook[price] = level;
                }

                if (isBid)
                    level.Bid = volume;
                else
                    level.Ask = volume;

                if (level.Bid <= 0 && level.Ask <= 0)
                    currentBook.Remove(price);
            }

            CaptureSnapshot(CurrentBar);
            ForceRefresh();
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType.ToString() != "Last")
                return;

            lastPrice = e.Price;
            CaptureSnapshot(CurrentBar);
            ForceRefresh();
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0)
                return;

            CaptureSnapshot(CurrentBar);
            TrimSnapshots();
            Values[0][0] = double.IsNaN(lastPrice) ? Close[0] : lastPrice;
        }

        private void CaptureSnapshot(int barIndex)
        {
            if (barIndex < 0)
                return;

            lock (bookLock)
            {
                HeatSnapshot snapshot = snapshots.Count > 0 && snapshots[snapshots.Count - 1].BarIndex == barIndex
                    ? snapshots[snapshots.Count - 1]
                    : null;

                if (snapshot == null)
                {
                    snapshot = new HeatSnapshot { BarIndex = barIndex };
                    snapshots.Add(snapshot);
                }
                else
                    snapshot.LiquidityByPrice.Clear();

                foreach (var row in currentBook.Where(kv => kv.Value.Liquidity >= MinimumSize)
                                               .OrderByDescending(kv => kv.Value.Liquidity)
                                               .Take(Math.Max(10, MaxLevels)))
                    snapshot.LiquidityByPrice[row.Key] = row.Value.Liquidity;

                snapshot.LastPrice = lastPrice;
                lastSnapshotBar = barIndex;
                TrimSnapshots();
            }
        }

        private void TrimSnapshots()
        {
            int firstBarToKeep = Math.Max(0, CurrentBar - Math.Max(1, BarsBack) + 1);
            lock (bookLock)
            {
                while (snapshots.Count > 0 && snapshots[0].BarIndex < firstBarToKeep)
                    snapshots.RemoveAt(0);

                int maxSnapshots = Math.Max(10, BarsBack + 5);
                while (snapshots.Count > maxSnapshots)
                    snapshots.RemoveAt(0);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null)
                return;

            if (heatBrushes == null || bidBrushDx == null || askBrushDx == null || lastBrushDx == null)
                OnRenderTargetChanged();
            if (heatBrushes == null || bidBrushDx == null || askBrushDx == null || lastBrushDx == null)
                return;

            List<HeatSnapshot> visibleSnapshots;
            Dictionary<double, DepthLevel> visibleCurrentBook;
            lock (bookLock)
            {
                int from = Math.Max(0, ChartBars.FromIndex);
                int to = Math.Min(ChartBars.ToIndex, CurrentBar);
                visibleSnapshots = snapshots.Where(s => s.BarIndex >= from && s.BarIndex <= to && s.LiquidityByPrice.Count > 0).ToList();
                visibleCurrentBook = currentBook.ToDictionary(kv => kv.Key, kv => new DepthLevel { Bid = kv.Value.Bid, Ask = kv.Value.Ask });
            }

            if (visibleSnapshots.Count == 0 && visibleCurrentBook.Count == 0)
                return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

            HeatScale heatScale = CalculateHeatScale(visibleSnapshots, visibleCurrentBook);
            if (heatScale.Max > 0)
                RenderHeatmap(chartControl, chartScale, visibleSnapshots, heatScale);

            if (ShowCurrentDepth)
                RenderCurrentDepthStrip(chartControl, chartScale, visibleCurrentBook, heatScale.Max);

        }

        private sealed class HeatScale
        {
            public double Floor;
            public double Max;
        }

        private HeatScale CalculateHeatScale(List<HeatSnapshot> visibleSnapshots, Dictionary<double, DepthLevel> visibleCurrentBook)
        {
            List<double> values = visibleSnapshots.SelectMany(s => s.LiquidityByPrice.Values)
                                                  .Concat(visibleCurrentBook.Values.Select(v => v.Liquidity))
                                                  .Where(v => v >= MinimumSize)
                                                  .OrderBy(v => v)
                                                  .ToList();
            if (values.Count == 0)
                return new HeatScale();

            double max = Percentile(values, Math.Max(50, Math.Min(100, HeatScalePercentile)));
            max = Math.Max(max, values[values.Count - 1] * 0.25);
            max = Math.Max(max, MinimumSize);
            double floor = Math.Max(MinimumSize, max * Math.Max(0, Math.Min(95, HeatFloorPercent)) / 100.0);
            return new HeatScale { Floor = floor, Max = max };
        }

        private double Percentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
                return 0;
            if (sortedValues.Count == 1)
                return sortedValues[0];
            double rank = (percentile / 100.0) * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            if (lower == upper)
                return sortedValues[lower];
            double weight = rank - lower;
            return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
        }

        private void RenderHeatmap(ChartControl chartControl, ChartScale chartScale, List<HeatSnapshot> visibleSnapshots, HeatScale heatScale)
        {
            double tick = GetHeatBucketSize();
            foreach (var snapshot in visibleSnapshots)
            {
                float x = chartControl.GetXByBarIndex(ChartBars, snapshot.BarIndex);
                float width = GetBarColumnWidth(chartControl, snapshot.BarIndex);
                float left = x - width * 0.5f;

                foreach (var row in snapshot.LiquidityByPrice)
                {
                    if (!IsPriceVisible(chartScale, row.Key))
                        continue;

                    float yTop = chartScale.GetYByValue(row.Key + tick * 0.5);
                    float yBottom = chartScale.GetYByValue(row.Key - tick * 0.5);
                    float height = Math.Max(1, yBottom - yTop);
                    DxBrush brush = HeatBrush(row.Value, heatScale);
                    if (brush != null)
                        RenderTarget.FillRectangle(new RectangleF(left, yTop, Math.Max(1, width), height), brush);
                }
            }
        }

        private void RenderCurrentDepthStrip(ChartControl chartControl, ChartScale chartScale, Dictionary<double, DepthLevel> book, double maxLiquidity)
        {
            if (book.Count == 0 || maxLiquidity <= 0)
                return;

            double tick = GetHeatBucketSize();
            float right = chartControl.CanvasRight;
            float stripWidth = Math.Max(10, Width);
            double maxBid = book.Values.Select(v => v.Bid).DefaultIfEmpty(0).Max();
            double maxAsk = book.Values.Select(v => v.Ask).DefaultIfEmpty(0).Max();
            float half = stripWidth * 0.5f;

            foreach (var row in book.Where(kv => kv.Value.Liquidity >= MinimumSize && IsPriceVisible(chartScale, kv.Key)))
            {
                float yTop = chartScale.GetYByValue(row.Key + tick * 0.5);
                float yBottom = chartScale.GetYByValue(row.Key - tick * 0.5);
                float height = Math.Max(1, yBottom - yTop);
                float mid = right - half;

                if (row.Value.Bid > 0 && maxBid > 0)
                {
                    float width = half * (float)(row.Value.Bid / maxBid);
                    RenderTarget.FillRectangle(new RectangleF(mid - width, yTop, width, height), bidBrushDx);
                }

                if (row.Value.Ask > 0 && maxAsk > 0)
                {
                    float width = half * (float)(row.Value.Ask / maxAsk);
                    RenderTarget.FillRectangle(new RectangleF(mid, yTop, width, height), askBrushDx);
                }
            }
        }

        private float GetBarColumnWidth(ChartControl chartControl, int barIndex)
        {
            int nextBar = Math.Min(CurrentBar, barIndex + 1);
            int previousBar = Math.Max(0, barIndex - 1);
            float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
            float right = nextBar == barIndex ? x : chartControl.GetXByBarIndex(ChartBars, nextBar);
            float left = previousBar == barIndex ? x : chartControl.GetXByBarIndex(ChartBars, previousBar);
            float width = Math.Max(Math.Abs(right - x), Math.Abs(x - left));
            return Math.Max(2, Math.Min(30, width));
        }

        private DxBrush HeatBrush(double liquidity, HeatScale heatScale)
        {
            if (heatBrushes == null || heatBrushes.Length == 0)
                return lastBrushDx;
            if (heatScale == null || heatScale.Max <= heatScale.Floor || liquidity < heatScale.Floor)
                return null;

            double normalized = Math.Max(0, Math.Min(1, (liquidity - heatScale.Floor) / Math.Max(1, heatScale.Max - heatScale.Floor)));
            normalized = Math.Pow(normalized, Math.Max(0.1, Math.Min(2.0, IntensityPower)));
            int index = Math.Max(0, Math.Min(heatBrushes.Length - 1, (int)Math.Round(normalized * (heatBrushes.Length - 1))));
            return heatBrushes[index];
        }

        private bool IsPriceVisible(ChartScale chartScale, double price)
        {
            double min = Math.Min(chartScale.MinValue, chartScale.MaxValue);
            double max = Math.Max(chartScale.MinValue, chartScale.MaxValue);
            return price >= min && price <= max;
        }

        private double RoundToHeatBucket(double price)
        {
            double size = GetHeatBucketSize();
            return Math.Round(price / size) * size;
        }

        private double GetHeatBucketSize()
        {
            return Math.Max(0.0000001, NTLCore.TickSize(this) * Math.Max(1, PriceBucketTicks));
        }

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null)
                return;

            bidBrushDx = BidColor.ToDxBrush(RenderTarget);
            askBrushDx = AskColor.ToDxBrush(RenderTarget);
            lastBrushDx = LastTradeColor.ToDxBrush(RenderTarget);
            bidBrushDx.Opacity = 0.70f;
            askBrushDx.Opacity = 0.70f;
            lastBrushDx.Opacity = 0.95f;

            float opacity = Math.Max(5, Math.Min(100, CellOpacity)) / 100f;
            Color4[] palette =
            {
                new Color4(0.00f, 0.00f, 0.00f, 0.00f),
                new Color4(0.01f, 0.06f, 0.12f, opacity * 0.10f),
                new Color4(0.01f, 0.16f, 0.24f, opacity * 0.20f),
                new Color4(0.02f, 0.34f, 0.35f, opacity * 0.34f),
                new Color4(0.12f, 0.48f, 0.24f, opacity * 0.48f),
                new Color4(0.42f, 0.52f, 0.10f, opacity * 0.62f),
                new Color4(0.80f, 0.48f, 0.06f, opacity * 0.76f),
                new Color4(0.95f, 0.32f, 0.04f, opacity * 0.86f),
                new Color4(1.00f, 0.72f, 0.12f, opacity * 0.96f),
                new Color4(1.00f, 0.95f, 0.35f, opacity)
            };

            heatBrushes = palette.Select(color => (DxBrush)new SolidColorBrushDx(RenderTarget, color)).ToArray();
        }

        private void DisposeBrushes()
        {
            if (bidBrushDx != null) { bidBrushDx.Dispose(); bidBrushDx = null; }
            if (askBrushDx != null) { askBrushDx.Dispose(); askBrushDx = null; }
            if (lastBrushDx != null) { lastBrushDx.Dispose(); lastBrushDx = null; }
            if (heatBrushes != null)
            {
                foreach (DxBrush brush in heatBrushes)
                    if (brush != null)
                        brush.Dispose();
                heatBrushes = null;
            }
        }

        [NinjaScriptProperty]
        [Range(20, 5000)]
        [Display(Name = "Bars back", GroupName = "Heatmap", Order = 0)]
        public int BarsBack { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Price bucket ticks", GroupName = "Heatmap", Order = 1)]
        public int PriceBucketTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Minimum liquidity", GroupName = "Heatmap", Order = 2)]
        public int MinimumSize { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name = "Max levels per snapshot", GroupName = "Heatmap", Order = 3)]
        public int MaxLevels { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Heat opacity %", GroupName = "Heatmap", Order = 4)]
        public int CellOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 2.0)]
        [Display(Name = "Intensity power", GroupName = "Heatmap", Order = 5)]
        public double IntensityPower { get; set; }

        [NinjaScriptProperty]
        [Range(0, 95)]
        [Display(Name = "Heat floor %", GroupName = "Heatmap", Order = 6)]
        public double HeatFloorPercent { get; set; }

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "Heat scale percentile", GroupName = "Heatmap", Order = 7)]
        public double HeatScalePercentile { get; set; }

        [Browsable(false)]
        [Display(Name = "Show last price path", GroupName = "Overlays", Order = 0)]
        public bool ShowLastPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show current depth strip", GroupName = "Overlays", Order = 1)]
        public bool ShowCurrentDepth { get; set; }

        [NinjaScriptProperty]
        [Range(20, 400)]
        [Display(Name = "Current depth width", GroupName = "Overlays", Order = 2)]
        public int Width { get; set; }

        [Browsable(false)]
        public double Decay { get; set; }

        [XmlIgnore]
        [Display(Name = "Current bid color", GroupName = "Colors", Order = 0)]
        public WpfBrush BidColor { get; set; }
        [Browsable(false)]
        public string BidColorSerializable { get { return Serialize.BrushToString(BidColor); } set { BidColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Current ask color", GroupName = "Colors", Order = 1)]
        public WpfBrush AskColor { get; set; }
        [Browsable(false)]
        public string AskColorSerializable { get { return Serialize.BrushToString(AskColor); } set { AskColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Last price color", GroupName = "Colors", Order = 2)]
        public WpfBrush LastTradeColor { get; set; }
        [Browsable(false)]
        public string LastTradeColorSerializable { get { return Serialize.BrushToString(LastTradeColor); } set { LastTradeColor = Serialize.StringToBrush(value); } }
    }
}

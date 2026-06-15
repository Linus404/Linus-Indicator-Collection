#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using DxBrush = SharpDX.Direct2D1.Brush;
using DwFactory = SharpDX.DirectWrite.Factory;
using TextFormat = SharpDX.DirectWrite.TextFormat;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.ChartStyles
{
    public class NTLFootprintChartStyle : ChartStyle
    {
        private sealed class FootprintRow
        {
            public double Price;
            public double Bid;
            public double Ask;
            public double Total { get { return Bid + Ask; } }
            public double Delta { get { return Ask - Bid; } }
            public bool BuyImbalance;
            public bool SellImbalance;
            public bool InValueArea;
            public bool IsPoc;
        }

        private DxBrush bidBrushDx;
        private DxBrush askBrushDx;
        private DxBrush outlineBrushDx;
        private DxBrush textBrushDx;
        private DxBrush pocBrushDx;
        private DxBrush valueAreaBrushDx;
        private DxBrush imbalanceBrushDx;
        private DwFactory textFactory;
        private TextFormat textFormat;
        private int cachedTextSize;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Footprint ChartStyle";
                // ChartStyle renders footprint-style price buckets from primary-bar OHLCV because ChartStyles do not receive the same tick-replay series as indicators.
                ChartStyleType = (ChartStyleType)6004;
                DisplayMode = NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintStyle.Detailed;
                FootprintType = NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType.BuySell;
                BidAskLayout = NinjaTrader.NinjaScript.Indicators.NTLambda.NTLBidAskLayout.BidAsk;
                BucketTicks = 4;
                AutoSizeRows = true;
                BucketPixelWidth = 76;
                TextSize = 9;
                ShowNumbers = true;
                ShowPoc = true;
                ShowValueArea = true;
                ValueAreaPercent = 70;
                ApplyGradient = true;
                HighlightImbalances = true;
                ImbalancePercent = 300;
                ShowSummaryInfo = false;
                BidColor = WpfBrushes.IndianRed;
                AskColor = WpfBrushes.Teal;
                PocColor = WpfBrushes.Gold;
                ValueAreaColor = WpfBrushes.RoyalBlue;
                ImbalanceColor = WpfBrushes.Yellow;
                OutlineColor = WpfBrushes.Gray;
                TextColor = WpfBrushes.White;
            }
            else if (State == State.Terminated)
            {
                DisposeBrushes();
            }
        }

        public override int GetBarPaintWidth(int barWidth)
        {
            return Math.Max(barWidth, BucketPixelWidth);
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars)
        {
            if (chartControl == null || chartScale == null || chartBars == null || chartBars.Bars == null || RenderTarget == null) return;
            if (bidBrushDx == null || askBrushDx == null || outlineBrushDx == null || textBrushDx == null || pocBrushDx == null || valueAreaBrushDx == null || imbalanceBrushDx == null) OnRenderTargetChanged();
            EnsureTextFormat();
            if (bidBrushDx == null || askBrushDx == null || outlineBrushDx == null || textBrushDx == null || pocBrushDx == null || valueAreaBrushDx == null || imbalanceBrushDx == null) return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            int from = Math.Max(0, chartBars.FromIndex);
            int to = Math.Min(chartBars.ToIndex, chartBars.Bars.Count - 1);
            double tickSize = GetTickSize(chartControl, chartBars);
            double bucketSize = GetReadableBucketSize(chartControl, chartScale, tickSize);

            for (int barIdx = from; barIdx <= to; barIdx++)
                DrawFootprintBar(chartControl, chartScale, chartBars, barIdx, bucketSize);
        }

        private void DrawFootprintBar(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars, int barIdx, double bucketSize)
        {
            double open = chartBars.Bars.GetOpen(barIdx);
            double close = chartBars.Bars.GetClose(barIdx);
            double high = chartBars.Bars.GetHigh(barIdx);
            double low = chartBars.Bars.GetLow(barIdx);
            double totalVolume = Math.Max(1, chartBars.Bars.GetVolume(barIdx));
            double firstBucket = Math.Floor(low / bucketSize) * bucketSize;
            double lastBucket = Math.Ceiling(high / bucketSize) * bucketSize;
            int bucketCount = Math.Max(1, (int)Math.Round((lastBucket - firstBucket) / bucketSize) + 1);
            // ChartStyle has primary-bar OHLCV only, so bid/ask cell values are estimated from volume, bar direction, and row location.
            double directionBias = close >= open ? 0.62 : 0.38;
            double center = (open + high + low + close) / 4.0;
            double halfRange = Math.Max(bucketSize, (high - low) / 2.0);
            float x = chartControl.GetXByBarIndex(chartBars, barIdx);
            float halfWidth = Math.Max(10, BucketPixelWidth) / 2f;
            var rows = new System.Collections.Generic.List<FootprintRow>();
            double rawTotal = 0;

            for (int i = 0; i < bucketCount; i++)
            {
                double price = firstBucket + i * bucketSize;
                double locationBias = high <= low ? 0.5 : (price - low) / Math.Max(bucketSize, high - low);
                double askShare = Math.Max(0.05, Math.Min(0.95, (directionBias + locationBias) / 2.0));
                double distanceFromCenter = Math.Abs(price - center) / halfRange;
                double participation = Math.Max(0.18, 1.0 - Math.Min(0.85, distanceFromCenter * 0.55));
                double askVolume = participation * askShare;
                double bidVolume = participation - askVolume;
                rows.Add(new FootprintRow { Price = price, Bid = bidVolume, Ask = askVolume });
                rawTotal += participation;
            }

            if (rawTotal > 0)
            {
                double scale = totalVolume / rawTotal;
                foreach (var row in rows)
                {
                    row.Bid *= scale;
                    row.Ask *= scale;
                }
            }

            AnnotateRows(rows);
            double maxBid = Math.Max(1, rows.Select(r => r.Bid).DefaultIfEmpty(1).Max());
            double maxAsk = Math.Max(1, rows.Select(r => r.Ask).DefaultIfEmpty(1).Max());
            double maxTotal = Math.Max(1, rows.Select(r => Math.Max(Math.Abs(r.Delta), r.Total)).DefaultIfEmpty(1).Max());

            foreach (var row in rows)
            {
                if (row.Price < chartScale.MinValue || row.Price > chartScale.MaxValue) continue;
                float yTop = chartScale.GetYByValue(row.Price + bucketSize * 0.5);
                float yBottom = chartScale.GetYByValue(row.Price - bucketSize * 0.5);
                float rowHeight = Math.Max(1, yBottom - yTop);
                DrawBucketRow(x, yTop, rowHeight, halfWidth, row, maxBid, maxAsk, maxTotal);
            }

            DrawCandleGuide(chartScale, x, halfWidth, open, high, low, close);

            if (ShowSummaryInfo && textFormat != null)
            {
                double bid = rows.Sum(r => r.Bid);
                double ask = rows.Sum(r => r.Ask);
                string summary = string.Format("V {0:0}\nB {1:0}\nA {2:0}\nD {3:0}", bid + ask, bid, ask, ask - bid);
                float y = chartScale.GetYByValue(low - bucketSize * 2.25);
                RenderTarget.DrawText(summary, textFormat, new RectangleF(x - halfWidth, y, halfWidth * 2, Math.Max(48, (TextSize + 4) * 4)), textBrushDx);
            }
        }

        private void AnnotateRows(System.Collections.Generic.List<FootprintRow> rows)
        {
            if (rows.Count == 0) return;
            FootprintRow poc = rows.OrderByDescending(r => r.Total).First();
            poc.IsPoc = true;

            double target = rows.Sum(r => r.Total) * Math.Max(1, Math.Min(100, ValueAreaPercent)) / 100.0;
            double running = 0;
            foreach (var row in rows.OrderByDescending(r => r.Total))
            {
                row.InValueArea = true;
                running += row.Total;
                if (running >= target) break;
            }

            double threshold = Math.Max(100, ImbalancePercent) / 100.0;
            for (int i = 0; i < rows.Count; i++)
            {
                double lowerBid = i > 0 ? rows[i - 1].Bid : 0;
                double upperAsk = i < rows.Count - 1 ? rows[i + 1].Ask : 0;
                rows[i].BuyImbalance = HighlightImbalances && lowerBid > 0 && rows[i].Ask >= threshold * lowerBid;
                rows[i].SellImbalance = HighlightImbalances && upperAsk > 0 && rows[i].Bid >= threshold * upperAsk;
            }
        }

        private void DrawBucketRow(float x, float yTop, float rowHeight, float halfWidth, FootprintRow row, double maxBid, double maxAsk, double maxTotal)
        {
            float leftX = x - halfWidth;
            float cellWidth = halfWidth;
            RectangleF leftCell = new RectangleF(leftX, yTop, cellWidth, rowHeight);
            RectangleF rightCell = new RectangleF(x, yTop, cellWidth, rowHeight);
            bool bidLeft = BidAskLayout == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLBidAskLayout.BidAsk;
            RectangleF bidCell = bidLeft ? leftCell : rightCell;
            RectangleF askCell = bidLeft ? rightCell : leftCell;
            RectangleF bidRect = bidCell;
            RectangleF askRect = askCell;

            if (row.InValueArea && ShowValueArea)
                RenderTarget.FillRectangle(new RectangleF(leftX, yTop, halfWidth * 2, rowHeight), valueAreaBrushDx);

            if (FootprintType == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType.Delta)
            {
                double delta = row.Delta;
                RectangleF deltaRect = new RectangleF(x, yTop, halfWidth, rowHeight);
                deltaRect.Width = Math.Max(1, halfWidth * (float)(Math.Abs(delta) / maxTotal));
                RenderTarget.FillRectangle(deltaRect, delta >= 0 ? askBrushDx : bidBrushDx);
                if (ShowNumbers && textFormat != null && rowHeight >= TextSize + 2)
                    RenderTarget.DrawText(delta.ToString("0"), textFormat, new RectangleF(leftX, yTop, halfWidth * 2, rowHeight), textBrushDx);
            }
            else if (FootprintType == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType.Total)
            {
                RectangleF totalRect = new RectangleF(x, yTop, halfWidth, rowHeight);
                totalRect.Width = Math.Max(1, halfWidth * (float)(row.Total / maxTotal));
                RenderTarget.FillRectangle(totalRect, row.Delta >= 0 ? askBrushDx : bidBrushDx);
                if (ShowNumbers && textFormat != null && rowHeight >= TextSize + 2)
                    RenderTarget.DrawText(row.Total.ToString("0"), textFormat, new RectangleF(leftX, yTop, halfWidth * 2, rowHeight), textBrushDx);
            }
            else if (DisplayMode == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintStyle.Tabular)
            {
                RenderTarget.DrawRectangle(new RectangleF(leftX, yTop, halfWidth * 2, rowHeight), outlineBrushDx, 1f);
                RenderTarget.DrawLine(new Vector2(x, yTop), new Vector2(x, yTop + rowHeight), outlineBrushDx, 1f);
                RenderTarget.FillRectangle(bidRect, bidBrushDx);
                RenderTarget.FillRectangle(askRect, askBrushDx);
            }
            else
            {
                double bidScale = FootprintType == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType.Ladder ? maxTotal : (ApplyGradient ? maxBid : Math.Max(1, Math.Max(row.Bid, row.Ask)));
                double askScale = FootprintType == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType.Ladder ? maxTotal : (ApplyGradient ? maxAsk : Math.Max(1, Math.Max(row.Bid, row.Ask)));
                bidRect = ScaleCellFromCenter(bidCell, bidCell.X < x, x, row.Bid, bidScale);
                askRect = ScaleCellFromCenter(askCell, askCell.X < x, x, row.Ask, askScale);
                RenderTarget.FillRectangle(bidRect, bidBrushDx);
                RenderTarget.FillRectangle(askRect, askBrushDx);
            }

            RenderTarget.DrawRectangle(new RectangleF(leftX, yTop, halfWidth * 2, rowHeight), outlineBrushDx, 1f);
            if (row.IsPoc && ShowPoc)
                RenderTarget.DrawRectangle(new RectangleF(leftX, yTop, halfWidth * 2, rowHeight), pocBrushDx, 2f);
            if (row.SellImbalance)
                RenderTarget.DrawLine(new Vector2(leftX, yTop), new Vector2(leftX, yTop + rowHeight), imbalanceBrushDx, 2f);
            if (row.BuyImbalance)
                RenderTarget.DrawLine(new Vector2(x + halfWidth, yTop), new Vector2(x + halfWidth, yTop + rowHeight), imbalanceBrushDx, 2f);

            if (ShowNumbers && textFormat != null && rowHeight >= TextSize + 2 && FootprintType == NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType.BuySell)
            {
                RenderTarget.DrawText(row.Bid.ToString("0"), textFormat, bidCell, textBrushDx);
                RenderTarget.DrawText(row.Ask.ToString("0"), textFormat, askCell, textBrushDx);
            }
        }

        private RectangleF ScaleCellFromCenter(RectangleF cell, bool isLeftCell, float centerX, double value, double scale)
        {
            float width = Math.Max(1, cell.Width * (float)(value / Math.Max(1, scale)));
            width = Math.Min(cell.Width, width);
            return isLeftCell
                ? new RectangleF(centerX - width, cell.Y, width, cell.Height)
                : new RectangleF(centerX, cell.Y, width, cell.Height);
        }

        private void DrawCandleGuide(ChartScale chartScale, float x, float halfWidth, double open, double high, double low, double close)
        {
            if (outlineBrushDx == null) return;
            float yHigh = chartScale.GetYByValue(high);
            float yLow = chartScale.GetYByValue(low);
            float yOpen = chartScale.GetYByValue(open);
            float yClose = chartScale.GetYByValue(close);
            DxBrush candleBrush = close > open ? askBrushDx : close < open ? bidBrushDx : outlineBrushDx;
            DxBrush candleOutlineBrush = textBrushDx ?? outlineBrushDx;
            float bodyHalfWidth = Math.Max(2, Math.Min(6, halfWidth * 0.16f));
            float bodyTop = Math.Min(yOpen, yClose);
            float bodyHeight = Math.Max(2, Math.Abs(yClose - yOpen));

            RenderTarget.DrawLine(new Vector2(x, yHigh), new Vector2(x, yLow), candleOutlineBrush, 4f);
            RenderTarget.DrawLine(new Vector2(x, yHigh), new Vector2(x, yLow), candleBrush, 2f);
            RenderTarget.FillRectangle(new RectangleF(x - bodyHalfWidth, bodyTop, bodyHalfWidth * 2, bodyHeight), candleBrush);
            RenderTarget.DrawRectangle(new RectangleF(x - bodyHalfWidth, bodyTop, bodyHalfWidth * 2, bodyHeight), candleOutlineBrush, 2f);
            RenderTarget.DrawLine(new Vector2(x - halfWidth * 0.45f, yOpen), new Vector2(x - bodyHalfWidth, yOpen), candleOutlineBrush, 4f);
            RenderTarget.DrawLine(new Vector2(x - halfWidth * 0.45f, yOpen), new Vector2(x - bodyHalfWidth, yOpen), candleBrush, 2f);
            RenderTarget.DrawLine(new Vector2(x + bodyHalfWidth, yClose), new Vector2(x + halfWidth * 0.45f, yClose), candleOutlineBrush, 4f);
            RenderTarget.DrawLine(new Vector2(x + bodyHalfWidth, yClose), new Vector2(x + halfWidth * 0.45f, yClose), candleBrush, 2f);
        }

        private double GetReadableBucketSize(ChartControl chartControl, ChartScale chartScale, double tickSize)
        {
            double configured = Math.Max(1, BucketTicks) * tickSize;
            if (!AutoSizeRows || chartControl == null || chartScale == null)
                return configured;

            double canvasHeight = Math.Max(1, Math.Abs(chartScale.GetYByValue(chartScale.MinValue) - chartScale.GetYByValue(chartScale.MaxValue)));
            double priceRange = Math.Max(tickSize, chartScale.MaxValue - chartScale.MinValue);
            double pricePerPixel = priceRange / canvasHeight;
            double desiredPixels = Math.Max(TextSize + 4, 12);
            double readableTicks = Math.Ceiling((pricePerPixel * desiredPixels) / tickSize);
            return Math.Max(configured, Math.Max(1, readableTicks) * tickSize);
        }

        private double GetTickSize(ChartControl chartControl, ChartBars chartBars)
        {
            try
            {
                if (chartControl.Instrument != null && chartControl.Instrument.MasterInstrument != null)
                    return chartControl.Instrument.MasterInstrument.TickSize;
            }
            catch { }
            try
            {
                if (chartBars.Bars != null && chartBars.Bars.Instrument != null && chartBars.Bars.Instrument.MasterInstrument != null)
                    return chartBars.Bars.Instrument.MasterInstrument.TickSize;
            }
            catch { }
            return 1.0;
        }

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null) return;
            bidBrushDx = BidColor.ToDxBrush(RenderTarget);
            askBrushDx = AskColor.ToDxBrush(RenderTarget);
            outlineBrushDx = OutlineColor.ToDxBrush(RenderTarget);
            textBrushDx = TextColor.ToDxBrush(RenderTarget);
            pocBrushDx = PocColor.ToDxBrush(RenderTarget);
            valueAreaBrushDx = ValueAreaColor.ToDxBrush(RenderTarget);
            imbalanceBrushDx = ImbalanceColor.ToDxBrush(RenderTarget);
            bidBrushDx.Opacity = 0.72f;
            askBrushDx.Opacity = 0.72f;
            outlineBrushDx.Opacity = 0.65f;
            textBrushDx.Opacity = 0.98f;
            valueAreaBrushDx.Opacity = 0.25f;
        }

        private void EnsureTextFormat()
        {
            if (!ShowNumbers) return;
            if (textFormat != null && cachedTextSize == TextSize) return;
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (textFactory == null) textFactory = new DwFactory();
            cachedTextSize = TextSize;
            textFormat = new TextFormat(textFactory, "Consolas", Math.Max(6, TextSize));
            textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
            textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
        }

        private void DisposeBrushes()
        {
            if (bidBrushDx != null) { bidBrushDx.Dispose(); bidBrushDx = null; }
            if (askBrushDx != null) { askBrushDx.Dispose(); askBrushDx = null; }
            if (outlineBrushDx != null) { outlineBrushDx.Dispose(); outlineBrushDx = null; }
            if (textBrushDx != null) { textBrushDx.Dispose(); textBrushDx = null; }
            if (pocBrushDx != null) { pocBrushDx.Dispose(); pocBrushDx = null; }
            if (valueAreaBrushDx != null) { valueAreaBrushDx.Dispose(); valueAreaBrushDx = null; }
            if (imbalanceBrushDx != null) { imbalanceBrushDx.Dispose(); imbalanceBrushDx = null; }
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (textFactory != null) { textFactory.Dispose(); textFactory = null; }
        }

        [Display(Name = "Display mode", GroupName = "Footprint", Order = 0)]
        public NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintStyle DisplayMode { get; set; }
        [Display(Name = "Bid/ask layout", GroupName = "Footprint", Order = 1)]
        public NinjaTrader.NinjaScript.Indicators.NTLambda.NTLBidAskLayout BidAskLayout { get; set; }
        [Display(Name = "Type", GroupName = "Footprint", Order = 2)]
        public NinjaTrader.NinjaScript.Indicators.NTLambda.NTLFootprintDisplayType FootprintType { get; set; }
        [Range(1, 100)]
        [Display(Name = "Bucket ticks", GroupName = "Footprint", Order = 3)]
        public int BucketTicks { get; set; }
        [Display(Name = "Auto-size rows", GroupName = "Footprint", Order = 4)]
        public bool AutoSizeRows { get; set; }
        [Range(8, 160)]
        [Display(Name = "Bucket pixel width", GroupName = "Footprint", Order = 5)]
        public int BucketPixelWidth { get; set; }
        [Range(6, 24)]
        [Display(Name = "Text size", GroupName = "Footprint", Order = 6)]
        public int TextSize { get; set; }
        [Display(Name = "Show numbers", GroupName = "Footprint", Order = 7)]
        public bool ShowNumbers { get; set; }
        [Display(Name = "Apply gradient", GroupName = "Footprint", Order = 8)]
        public bool ApplyGradient { get; set; }
        [Display(Name = "Show POC", GroupName = "Footprint", Order = 9)]
        public bool ShowPoc { get; set; }
        [Display(Name = "Show value area", GroupName = "Footprint", Order = 10)]
        public bool ShowValueArea { get; set; }
        [Range(1, 100)]
        [Display(Name = "Value area percent", GroupName = "Footprint", Order = 11)]
        public double ValueAreaPercent { get; set; }
        [Display(Name = "Highlight imbalances", GroupName = "Imbalance", Order = 12)]
        public bool HighlightImbalances { get; set; }
        [Range(100, 1000)]
        [Display(Name = "Imbalance percent", GroupName = "Imbalance", Order = 13)]
        public double ImbalancePercent { get; set; }
        [Display(Name = "Show summary info", GroupName = "Labels", Order = 14)]
        public bool ShowSummaryInfo { get; set; }
        [XmlIgnore]
        [Display(Name = "Bid color", GroupName = "Colors", Order = 15)] public WpfBrush BidColor { get; set; }
        [Browsable(false)]
        public string BidColorSerializable { get { return Serialize.BrushToString(BidColor); } set { BidColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Ask color", GroupName = "Colors", Order = 16)] public WpfBrush AskColor { get; set; }
        [Browsable(false)]
        public string AskColorSerializable { get { return Serialize.BrushToString(AskColor); } set { AskColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "POC color", GroupName = "Colors", Order = 17)] public WpfBrush PocColor { get; set; }
        [Browsable(false)]
        public string PocColorSerializable { get { return Serialize.BrushToString(PocColor); } set { PocColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Value area color", GroupName = "Colors", Order = 18)] public WpfBrush ValueAreaColor { get; set; }
        [Browsable(false)]
        public string ValueAreaColorSerializable { get { return Serialize.BrushToString(ValueAreaColor); } set { ValueAreaColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Imbalance color", GroupName = "Colors", Order = 19)] public WpfBrush ImbalanceColor { get; set; }
        [Browsable(false)]
        public string ImbalanceColorSerializable { get { return Serialize.BrushToString(ImbalanceColor); } set { ImbalanceColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Outline color", GroupName = "Colors", Order = 20)] public WpfBrush OutlineColor { get; set; }
        [Browsable(false)]
        public string OutlineColorSerializable { get { return Serialize.BrushToString(OutlineColor); } set { OutlineColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Text color", GroupName = "Colors", Order = 21)] public WpfBrush TextColor { get; set; }
        [Browsable(false)]
        public string TextColorSerializable { get { return Serialize.BrushToString(TextColor); } set { TextColor = Serialize.StringToBrush(value); } }
    }
}

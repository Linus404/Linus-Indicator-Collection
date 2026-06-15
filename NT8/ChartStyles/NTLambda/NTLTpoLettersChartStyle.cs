#region Using declarations
using System;
using System.Collections.Generic;
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
    public class NTLTpoLettersChartStyle : ChartStyle
    {
        private sealed class TpoProfile
        {
            public readonly Dictionary<double, HashSet<int>> Rows = new Dictionary<double, HashSet<int>>();
            public readonly Dictionary<int, int> PeriodSlots = new Dictionary<int, int>();
            public int StartBar;
            public int EndBar;
            public int NextPeriodSlot;
            public double Poc;
            public double Vah;
            public double Val;
        }

        private DxBrush blockBrushDx;
        private DxBrush valueAreaBrushDx;
        private DxBrush pocBrushDx;
        private DxBrush valueLineBrushDx;
        private DxBrush textBrushDx;
        private DwFactory textFactory;
        private TextFormat textFormat;
        private int cachedTextSize;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL TPO Letters ChartStyle";
                ChartStyleType = (ChartStyleType)6006;
                TpoMinutes = 30;
                BucketTicks = 4;
                AutoSizeRows = true;
                ValueAreaPercent = 70;
                BlockWidth = 12;
                TextSize = 9;
                ShowLetters = true;
                ProfileOpacity = 95;
                ValueAreaOpacity = 95;
                SplitBySession = true;
                BlockColor = WpfBrushes.DeepSkyBlue;
                ValueAreaColor = WpfBrushes.DodgerBlue;
                PocColor = WpfBrushes.Goldenrod;
                ValueLineColor = WpfBrushes.DodgerBlue;
                TextColor = WpfBrushes.White;
            }
            else if (State == State.Terminated)
            {
                DisposeBrushes();
            }
        }

        public override int GetBarPaintWidth(int barWidth)
        {
            return Math.Max(barWidth, BlockWidth);
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars)
        {
            if (chartControl == null || chartScale == null || chartBars == null || chartBars.Bars == null || RenderTarget == null) return;

            try
            {
                if (blockBrushDx == null || valueAreaBrushDx == null || pocBrushDx == null || valueLineBrushDx == null || textBrushDx == null) OnRenderTargetChanged();
                TryEnsureTextFormat();
                if (blockBrushDx == null || valueAreaBrushDx == null || pocBrushDx == null || valueLineBrushDx == null || textBrushDx == null) return;

                int from = Math.Max(0, chartBars.FromIndex);
                int to = Math.Min(chartBars.ToIndex, chartBars.Bars.Count - 1);
                if (to < from) return;

                double bucket = GetReadableBucketSize(chartControl, chartScale, GetTickSize(chartControl, chartBars));
                int buildFrom = SplitBySession ? FindProfileBuildStart(chartBars, from) : from;
                foreach (var profile in BuildProfiles(chartBars, buildFrom, to, bucket))
                {
                    if (profile.EndBar < from || profile.StartBar > to)
                        continue;
                    RenderProfile(chartControl, chartScale, chartBars, profile, bucket);
                }
            }
            catch (Exception ex)
            {
                Print("NTL TPO render error: " + ex.Message);
            }
        }

        private int FindProfileBuildStart(ChartBars chartBars, int from)
        {
            int start = Math.Max(0, from);
            for (int barIdx = start; barIdx > 0; barIdx--)
            {
                DateTime previousTime = chartBars.Bars.GetTime(barIdx - 1);
                DateTime time = chartBars.Bars.GetTime(barIdx);
                if (IsNewProfileBoundary(previousTime, time))
                    return barIdx;
            }

            return 0;
        }

        private List<TpoProfile> BuildProfiles(ChartBars chartBars, int from, int to, double bucket)
        {
            var profiles = new List<TpoProfile>();
            TpoProfile current = null;
            DateTime sessionStart = DateTime.MinValue;
            DateTime previousTime = DateTime.MinValue;

            for (int barIdx = from; barIdx <= to; barIdx++)
            {
                DateTime time = chartBars.Bars.GetTime(barIdx);
                bool newProfile = current == null || (SplitBySession && IsNewProfileBoundary(sessionStart, previousTime, time));
                if (newProfile)
                {
                    current = new TpoProfile { StartBar = barIdx, EndBar = barIdx };
                    profiles.Add(current);
                    sessionStart = time;
                }
                previousTime = time;

                current.EndBar = barIdx;
                int rawPeriod = (int)Math.Max(0, (time - sessionStart).TotalMinutes / Math.Max(1, TpoMinutes));
                int period = GetCompactPeriodSlot(current, rawPeriod);
                double low = Math.Floor(chartBars.Bars.GetLow(barIdx) / bucket) * bucket;
                double high = Math.Ceiling(chartBars.Bars.GetHigh(barIdx) / bucket) * bucket;
                for (double price = low; price <= high + bucket / 2.0; price += bucket)
                {
                    HashSet<int> periods;
                    if (!current.Rows.TryGetValue(price, out periods))
                    {
                        periods = new HashSet<int>();
                        current.Rows[price] = periods;
                    }
                    periods.Add(period);
                }
            }

            foreach (var profile in profiles)
                CalculateStats(profile);

            return profiles;
        }

        private int GetCompactPeriodSlot(TpoProfile profile, int rawPeriod)
        {
            int slot;
            if (profile.PeriodSlots.TryGetValue(rawPeriod, out slot))
                return slot;

            slot = profile.NextPeriodSlot++;
            profile.PeriodSlots[rawPeriod] = slot;
            return slot;
        }

        private bool IsNewProfileBoundary(DateTime sessionStart, DateTime previousTime, DateTime time)
        {
            if (sessionStart == DateTime.MinValue || previousTime == DateTime.MinValue)
                return false;

            return IsNewProfileBoundary(previousTime, time);
        }

        private bool IsNewProfileBoundary(DateTime previousTime, DateTime time)
        {
            if (previousTime == DateTime.MinValue)
                return false;

            TimeSpan gap = time - previousTime;
            if (gap > TimeSpan.FromHours(2))
                return true;

            if (time.Date != previousTime.Date)
                return true;

            return false;
        }

        private void CalculateStats(TpoProfile profile)
        {
            if (profile.Rows.Count == 0) return;
            var ordered = profile.Rows.OrderByDescending(kv => kv.Value.Count).ToList();
            profile.Poc = ordered[0].Key;
            double target = ordered.Sum(kv => kv.Value.Count) * Math.Max(1, Math.Min(100, ValueAreaPercent)) / 100.0;
            double running = 0;
            profile.Vah = profile.Poc;
            profile.Val = profile.Poc;
            foreach (var kv in ordered)
            {
                running += kv.Value.Count;
                profile.Vah = Math.Max(profile.Vah, kv.Key);
                profile.Val = Math.Min(profile.Val, kv.Key);
                if (running >= target) break;
            }
        }

        private void RenderProfile(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars, TpoProfile profile, double bucket)
        {
            if (profile.Rows.Count == 0) return;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            float panelLeft = GetPanelLeft(chartControl, chartBars);
            float panelRight = GetPanelRight(chartControl, chartBars);
            float x = Math.Max(panelLeft, chartControl.GetXByBarIndex(chartBars, profile.StartBar));
            int maxRowWidth = Math.Max(1, profile.Rows.Values.Select(v => v.Count).DefaultIfEmpty(1).Max());
            float maxRight = Math.Min(panelRight, x + (maxRowWidth + 1) * Math.Max(2, BlockWidth));
            if (maxRight <= panelLeft || x >= panelRight) return;

            foreach (var row in profile.Rows.OrderBy(kv => kv.Key))
            {
                if (!IsPriceVisible(chartScale, row.Key)) continue;
                float yTop = chartScale.GetYByValue(row.Key + bucket * 0.5);
                float yBottom = chartScale.GetYByValue(row.Key - bucket * 0.5);
                float height = Math.Max(1, yBottom - yTop);
                DxBrush brush = row.Key >= profile.Val && row.Key <= profile.Vah ? valueAreaBrushDx : blockBrushDx;
                int slot = 0;
                foreach (int period in row.Value.OrderBy(p => p))
                {
                    float cellX = x + slot * Math.Max(2, BlockWidth);
                    if (cellX > maxRight) break;
                    if (cellX + BlockWidth < panelLeft) continue;
                    RectangleF cell = new RectangleF(cellX, yTop, Math.Max(1, BlockWidth - 1), height);
                    RenderTarget.DrawRectangle(cell, brush, 1f);
                    if (ShowLetters && textFormat != null && height >= TextSize + 2 && BlockWidth >= TextSize)
                        RenderTarget.DrawText(PeriodLetter(period), textFormat, cell, textBrushDx);
                    slot++;
                }
            }

            DrawProfileLevelLine(chartScale, x, maxRight, profile.Poc, pocBrushDx, 2f);
            DrawProfileLevelLine(chartScale, x, maxRight, profile.Vah, valueLineBrushDx, 1f);
            DrawProfileLevelLine(chartScale, x, maxRight, profile.Val, valueLineBrushDx, 1f);
        }

        private void DrawProfileLevelLine(ChartScale chartScale, float xStart, float xEnd, double price, DxBrush brush, float width)
        {
            if (!IsPriceVisible(chartScale, price)) return;
            float y = chartScale.GetYByValue(price);
            RenderTarget.DrawLine(new Vector2(xStart, y), new Vector2(xEnd, y), brush, width);
        }

        private bool IsPriceVisible(ChartScale chartScale, double price)
        {
            double min = Math.Min(chartScale.MinValue, chartScale.MaxValue);
            double max = Math.Max(chartScale.MinValue, chartScale.MaxValue);
            return price >= min && price <= max;
        }

        private float GetPanelLeft(ChartControl chartControl, ChartBars chartBars)
        {
            if (chartBars != null && chartBars.ChartPanel != null)
                return chartBars.ChartPanel.X;
            return chartControl != null ? chartControl.CanvasLeft : 0;
        }

        private float GetPanelRight(ChartControl chartControl, ChartBars chartBars)
        {
            if (chartBars != null && chartBars.ChartPanel != null)
                return chartBars.ChartPanel.X + chartBars.ChartPanel.W;
            return chartControl != null ? chartControl.CanvasRight : 0;
        }

        private string PeriodLetter(int period)
        {
            int normalized = Math.Max(0, period) % 52;
            if (normalized < 26)
                return ((char)('A' + normalized)).ToString();
            return ((char)('a' + normalized - 26)).ToString();
        }

        private double GetReadableBucketSize(ChartControl chartControl, ChartScale chartScale, double tickSize)
        {
            tickSize = Math.Max(0.0000001, tickSize);
            double configured = Math.Max(1, BucketTicks) * tickSize;
            if (!AutoSizeRows || chartControl == null || chartScale == null)
                return configured;

            double canvasHeight = Math.Max(1, Math.Abs(chartScale.GetYByValue(chartScale.MinValue) - chartScale.GetYByValue(chartScale.MaxValue)));
            double priceRange = Math.Max(tickSize, Math.Abs(chartScale.MaxValue - chartScale.MinValue));
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
            blockBrushDx = BlockColor.ToDxBrush(RenderTarget);
            valueAreaBrushDx = ValueAreaColor.ToDxBrush(RenderTarget);
            pocBrushDx = PocColor.ToDxBrush(RenderTarget);
            valueLineBrushDx = ValueLineColor.ToDxBrush(RenderTarget);
            textBrushDx = TextColor.ToDxBrush(RenderTarget);
            blockBrushDx.Opacity = Math.Max(0, Math.Min(100, ProfileOpacity)) / 100f;
            valueAreaBrushDx.Opacity = Math.Max(0, Math.Min(100, ValueAreaOpacity)) / 100f;
            textBrushDx.Opacity = 0.95f;
        }

        private void EnsureTextFormat()
        {
            if (!ShowLetters) return;
            if (textFormat != null && cachedTextSize == TextSize) return;
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (textFactory == null) textFactory = new DwFactory();
            cachedTextSize = TextSize;
            textFormat = new TextFormat(textFactory, "Consolas", Math.Max(6, TextSize));
            textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
            textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
        }

        private void TryEnsureTextFormat()
        {
            try
            {
                EnsureTextFormat();
            }
            catch (Exception ex)
            {
                Print("NTL TPO text render disabled: " + ex.Message);
                if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
                if (textFactory != null) { textFactory.Dispose(); textFactory = null; }
            }
        }

        private void DisposeBrushes()
        {
            if (blockBrushDx != null) { blockBrushDx.Dispose(); blockBrushDx = null; }
            if (valueAreaBrushDx != null) { valueAreaBrushDx.Dispose(); valueAreaBrushDx = null; }
            if (pocBrushDx != null) { pocBrushDx.Dispose(); pocBrushDx = null; }
            if (valueLineBrushDx != null) { valueLineBrushDx.Dispose(); valueLineBrushDx = null; }
            if (textBrushDx != null) { textBrushDx.Dispose(); textBrushDx = null; }
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (textFactory != null) { textFactory.Dispose(); textFactory = null; }
        }

        [Range(1, 240)]
        [Display(Name = "TPO minutes", GroupName = "TPO", Order = 0)]
        public int TpoMinutes { get; set; }
        [Range(1, 100)]
        [Display(Name = "Bucket ticks", GroupName = "TPO", Order = 1)]
        public int BucketTicks { get; set; }
        [Display(Name = "Auto-size rows", GroupName = "TPO", Order = 2)]
        public bool AutoSizeRows { get; set; }
        [Range(1, 100)]
        [Display(Name = "Value area percent", GroupName = "TPO", Order = 3)]
        public double ValueAreaPercent { get; set; }
        [Range(2, 40)]
        [Display(Name = "Block width", GroupName = "TPO", Order = 4)]
        public int BlockWidth { get; set; }
        [Range(6, 24)]
        [Display(Name = "Text size", GroupName = "TPO", Order = 5)]
        public int TextSize { get; set; }
        [Display(Name = "Show letters", GroupName = "TPO", Order = 6)]
        public bool ShowLetters { get; set; }
        [Display(Name = "Split by session", GroupName = "TPO", Order = 7)]
        public bool SplitBySession { get; set; }
        [Range(5, 100)]
        [Display(Name = "Profile opacity %", GroupName = "Visual", Order = 8)]
        public int ProfileOpacity { get; set; }
        [Range(5, 100)]
        [Display(Name = "Value area opacity %", GroupName = "Visual", Order = 9)]
        public int ValueAreaOpacity { get; set; }
        [XmlIgnore]
        [Display(Name = "Block color", GroupName = "Colors", Order = 10)]
        public WpfBrush BlockColor { get; set; }
        [Browsable(false)]
        public string BlockColorSerializable { get { return Serialize.BrushToString(BlockColor); } set { BlockColor = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Value area color", GroupName = "Colors", Order = 11)]
        public WpfBrush ValueAreaColor { get; set; }
        [Browsable(false)]
        public string ValueAreaColorSerializable { get { return Serialize.BrushToString(ValueAreaColor); } set { ValueAreaColor = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "POC color", GroupName = "Colors", Order = 12)]
        public WpfBrush PocColor { get; set; }
        [Browsable(false)]
        public string PocColorSerializable { get { return Serialize.BrushToString(PocColor); } set { PocColor = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Value line color", GroupName = "Colors", Order = 13)]
        public WpfBrush ValueLineColor { get; set; }
        [Browsable(false)]
        public string ValueLineColorSerializable { get { return Serialize.BrushToString(ValueLineColor); } set { ValueLineColor = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Text color", GroupName = "Colors", Order = 14)]
        public WpfBrush TextColor { get; set; }
        [Browsable(false)]
        public string TextColorSerializable { get { return Serialize.BrushToString(TextColor); } set { TextColor = Serialize.StringToBrush(value); } }
    }
}

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
using DxBrush = SharpDX.Direct2D1.Brush;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLRangeVolumeProfile : Indicator
    {
        private readonly Dictionary<int, Dictionary<double, NTLVolumeLevel>> barBuckets = new Dictionary<int, Dictionary<double, NTLVolumeLevel>>();
        private double lastTradePrice = double.NaN;
        private double poc, vah, val, maxProfileValue;
        private Dictionary<double, NTLVolumeLevel> lastRangeProfile = new Dictionary<double, NTLVolumeLevel>();
        private DxBrush volumeBrushDx, valueAreaBrushDx, buyBrushDx, sellBrushDx, pocBrushDx, valueLineBrushDx;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Range Volume Profile";
                Description = "Bars-back range volume profile with horizontal volume bars.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                BarsBack = 100;
                ProfileMode = NTLProfileMode.Total;
                ValueAreaPercent = 70;
                Width = 160;
                Opacity = 35;
                ValueAreaOpacity = 75;
                ShowPoc = true;
                ShowValueArea = true;
                VolumeColor = WpfBrushes.CornflowerBlue;
                ValueAreaColor = WpfBrushes.RoyalBlue;
                BuyColor = WpfBrushes.DarkCyan;
                SellColor = WpfBrushes.MediumVioletRed;
                PocColor = WpfBrushes.Goldenrod;
                ValueLineColor = WpfBrushes.CornflowerBlue;
                AddPlot(WpfBrushes.Crimson, "RangePOC");
                AddPlot(WpfBrushes.DodgerBlue, "RangeVAH");
                AddPlot(WpfBrushes.DodgerBlue, "RangeVAL");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
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

        protected override void OnMarketData(MarketDataEventArgs e) { }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1) { ProcessTickSeries(); return; }
            if (BarsInProgress != 0) return;
            BuildRangeProfile(CurrentBar);
            Values[0][0] = poc;
            Values[1][0] = vah;
            Values[2][0] = val;
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume)) return;
            bool isAsk = NTLCore.IsAskTrade(price, bid, ask, lastTradePrice);
            NTLCore.AddVolume(GetBucket(primaryBar), NTLCore.RoundToTick(this, price), isAsk ? 0 : volume, isAsk ? volume : 0);
            lastTradePrice = price;
        }

        private Dictionary<double, NTLVolumeLevel> GetBucket(int bar)
        {
            Dictionary<double, NTLVolumeLevel> bucket;
            if (!barBuckets.TryGetValue(bar, out bucket))
            {
                bucket = new Dictionary<double, NTLVolumeLevel>();
                barBuckets[bar] = bucket;
            }
            return bucket;
        }

        private void BuildRangeProfile(int targetBar)
        {
            int firstIncludedBar = Math.Max(0, targetBar - BarsBack + 1);
            foreach (int staleBar in barBuckets.Keys.Where(bar => bar < firstIncludedBar).ToList())
                barBuckets.Remove(staleBar);

            Dictionary<double, NTLVolumeLevel> rangeProfile = new Dictionary<double, NTLVolumeLevel>();
            foreach (var bar in barBuckets.Where(kv => kv.Key >= firstIncludedBar && kv.Key <= targetBar))
                foreach (var priceLevel in bar.Value)
                    NTLCore.AddVolume(rangeProfile, priceLevel.Key, priceLevel.Value.Bid, priceLevel.Value.Ask);
            lastRangeProfile = rangeProfile;
            CalculateProfileStats(lastRangeProfile);
        }

        private void CalculateProfileStats(Dictionary<double, NTLVolumeLevel> source)
        {
            poc = vah = val = maxProfileValue = 0;
            if (source.Count == 0) return;
            var ordered = source.OrderByDescending(kv => Math.Abs(NTLCore.ProfileValue(kv.Value, ProfileMode))).ToList();
            poc = ordered[0].Key;
            maxProfileValue = Math.Max(1, Math.Abs(NTLCore.ProfileValue(ordered[0].Value, ProfileMode)));
            double target = ordered.Sum(kv => Math.Abs(NTLCore.ProfileValue(kv.Value, ProfileMode))) * ValueAreaPercent / 100.0;
            double running = 0;
            vah = poc;
            val = poc;
            foreach (var kv in ordered)
            {
                running += Math.Abs(NTLCore.ProfileValue(kv.Value, ProfileMode));
                vah = Math.Max(vah, kv.Key);
                val = Math.Min(val, kv.Key);
                if (running >= target) break;
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null || lastRangeProfile.Count == 0) return;
            if (volumeBrushDx == null || valueAreaBrushDx == null || buyBrushDx == null || sellBrushDx == null || pocBrushDx == null || valueLineBrushDx == null) OnRenderTargetChanged();
            if (volumeBrushDx == null || valueAreaBrushDx == null || buyBrushDx == null || sellBrushDx == null || pocBrushDx == null || valueLineBrushDx == null) return;
            RenderProfile(chartControl, chartScale, lastRangeProfile, Math.Max(0, CurrentBar - BarsBack + 1));
        }

        private void RenderProfile(ChartControl chartControl, ChartScale chartScale, Dictionary<double, NTLVolumeLevel> source, int startBar)
        {
            CalculateProfileStats(source);
            if (maxProfileValue <= 0) return;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            float x = Math.Max(chartControl.CanvasLeft, chartControl.GetXByBarIndex(ChartBars, Math.Max(0, startBar)));
            foreach (var row in source.OrderBy(kv => kv.Key))
            {
                if (row.Key < chartScale.MinValue || row.Key > chartScale.MaxValue) continue;
                double value = Math.Abs(NTLCore.ProfileValue(row.Value, ProfileMode));
                float width = (float)(Width * value / maxProfileValue);
                double tick = NTLCore.TickSize(this);
                float yTop = chartScale.GetYByValue(row.Key + tick * 0.5);
                float yBottom = chartScale.GetYByValue(row.Key - tick * 0.5);
                float height = Math.Max(1, yBottom - yTop);
                DxBrush brush = row.Key >= val && row.Key <= vah ? valueAreaBrushDx : volumeBrushDx;
                if (ProfileMode == NTLProfileMode.BidAsk)
                {
                    float bidWidth = width * (float)(row.Value.Bid / Math.Max(1, row.Value.Total));
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x, yTop, bidWidth, height), sellBrushDx);
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x + bidWidth, yTop, width - bidWidth, height), buyBrushDx);
                }
                else
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x, yTop, width, height), brush);
            }
            if (ShowPoc) DrawLevelLine(chartControl, chartScale, poc, pocBrushDx, 2f);
            if (ShowValueArea)
            {
                DrawLevelLine(chartControl, chartScale, vah, valueLineBrushDx, 1f);
                DrawLevelLine(chartControl, chartScale, val, valueLineBrushDx, 1f);
            }
        }

        private void DrawLevelLine(ChartControl chartControl, ChartScale chartScale, double price, DxBrush brush, float width)
        {
            if (price <= 0 || price < chartScale.MinValue || price > chartScale.MaxValue) return;
            float y = chartScale.GetYByValue(price);
            RenderTarget.DrawLine(new SharpDX.Vector2(chartControl.CanvasLeft, y), new SharpDX.Vector2(chartControl.CanvasRight, y), brush, width);
        }

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null) return;
            volumeBrushDx = VolumeColor.ToDxBrush(RenderTarget);
            valueAreaBrushDx = ValueAreaColor.ToDxBrush(RenderTarget);
            buyBrushDx = BuyColor.ToDxBrush(RenderTarget);
            sellBrushDx = SellColor.ToDxBrush(RenderTarget);
            pocBrushDx = PocColor.ToDxBrush(RenderTarget);
            valueLineBrushDx = ValueLineColor.ToDxBrush(RenderTarget);
            volumeBrushDx.Opacity = Math.Max(0, Math.Min(100, Opacity)) / 100f;
            valueAreaBrushDx.Opacity = Math.Max(0, Math.Min(100, ValueAreaOpacity)) / 100f;
            buyBrushDx.Opacity = valueAreaBrushDx.Opacity;
            sellBrushDx.Opacity = valueAreaBrushDx.Opacity;
        }

        private void DisposeBrushes()
        {
            if (volumeBrushDx != null) { volumeBrushDx.Dispose(); volumeBrushDx = null; }
            if (valueAreaBrushDx != null) { valueAreaBrushDx.Dispose(); valueAreaBrushDx = null; }
            if (buyBrushDx != null) { buyBrushDx.Dispose(); buyBrushDx = null; }
            if (sellBrushDx != null) { sellBrushDx.Dispose(); sellBrushDx = null; }
            if (pocBrushDx != null) { pocBrushDx.Dispose(); pocBrushDx = null; }
            if (valueLineBrushDx != null) { valueLineBrushDx.Dispose(); valueLineBrushDx = null; }
        }

        private bool TryGetTick(out int primaryBar, out double price, out double bid, out double ask, out double volume)
        {
            primaryBar = -1; price = bid = ask = volume = 0;
            if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[0] < 0 || CurrentBars[1] < 0) return false;
            int tickIndex = CurrentBars[1];
            primaryBar = BarsArray[0].GetBar(BarsArray[1].GetTime(tickIndex));
            if (primaryBar < 0) return false;
            price = BarsArray[1].GetClose(tickIndex);
            bid = BarsArray[1].GetBid(tickIndex);
            ask = BarsArray[1].GetAsk(tickIndex);
            volume = BarsArray[1].GetVolume(tickIndex);
            return volume > 0;
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Bars back", GroupName = "Range", Order = 0)] public int BarsBack { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Profile mode", GroupName = "Calculation", Order = 1)] public NTLProfileMode ProfileMode { get; set; }
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Value area percent", GroupName = "Calculation", Order = 2)] public double ValueAreaPercent { get; set; }
        [NinjaScriptProperty]
        [Range(20, 600)]
        [Display(Name = "Profile width", GroupName = "Visual", Order = 3)] public int Width { get; set; }
        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Opacity %", GroupName = "Visual", Order = 4)] public int Opacity { get; set; }
        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Value area opacity %", GroupName = "Visual", Order = 5)] public int ValueAreaOpacity { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show POC", GroupName = "Visual", Order = 6)] public bool ShowPoc { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show value area", GroupName = "Visual", Order = 7)] public bool ShowValueArea { get; set; }
        [XmlIgnore]
        [Display(Name = "Volume color", GroupName = "Colors", Order = 0)] public WpfBrush VolumeColor { get; set; }
        [Browsable(false)]
        public string VolumeColorSerializable { get { return Serialize.BrushToString(VolumeColor); } set { VolumeColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Value area color", GroupName = "Colors", Order = 1)] public WpfBrush ValueAreaColor { get; set; }
        [Browsable(false)]
        public string ValueAreaColorSerializable { get { return Serialize.BrushToString(ValueAreaColor); } set { ValueAreaColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Buy color", GroupName = "Colors", Order = 2)] public WpfBrush BuyColor { get; set; }
        [Browsable(false)]
        public string BuyColorSerializable { get { return Serialize.BrushToString(BuyColor); } set { BuyColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Sell color", GroupName = "Colors", Order = 3)] public WpfBrush SellColor { get; set; }
        [Browsable(false)]
        public string SellColorSerializable { get { return Serialize.BrushToString(SellColor); } set { SellColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "POC color", GroupName = "Colors", Order = 4)] public WpfBrush PocColor { get; set; }
        [Browsable(false)]
        public string PocColorSerializable { get { return Serialize.BrushToString(PocColor); } set { PocColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Value line color", GroupName = "Colors", Order = 5)] public WpfBrush ValueLineColor { get; set; }
        [Browsable(false)]
        public string ValueLineColorSerializable { get { return Serialize.BrushToString(ValueLineColor); } set { ValueLineColor = Serialize.StringToBrush(value); } }
    }
}

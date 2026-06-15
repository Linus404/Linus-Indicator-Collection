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
    public class NTLVolumeProfile : Indicator
    {
        private sealed class ProfileSnapshot
        {
            public readonly Dictionary<double, NTLVolumeLevel> Profile = new Dictionary<double, NTLVolumeLevel>();
            public int StartBar;
            public int EndBar;
        }

        private sealed class ProfileStats
        {
            public double Poc;
            public double Vah;
            public double Val;
            public double MaxProfileValue;
        }

        private readonly Dictionary<double, NTLVolumeLevel> profile = new Dictionary<double, NTLVolumeLevel>();
        private readonly List<ProfileSnapshot> completedProfiles = new List<ProfileSnapshot>();
        private DateTime lastResetTime = DateTime.MinValue;
        private int activePrimaryBar = -1;
        private int profileStartBar;
        private double lastTradePrice = double.NaN;
        private double poc, vah, val, maxProfileValue;
        private DxBrush volumeBrushDx, valueAreaBrushDx, buyBrushDx, sellBrushDx, pocBrushDx, valueLineBrushDx;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Volume Profile";
                Description = "Session volume profile with horizontal volume bars, POC, VAH, and VAL.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                ResetPeriod = NTLResetPeriod.Session;
                ProfileMode = NTLProfileMode.Total;
                ProfilesToShow = 1;
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
                AddPlot(WpfBrushes.Crimson, "POC");
                AddPlot(WpfBrushes.DodgerBlue, "VAH");
                AddPlot(WpfBrushes.DodgerBlue, "VAL");
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
            if (activePrimaryBar < CurrentBar) EnsurePrimaryBar(CurrentBar);
            ProfileStats stats = CalculateProfileStats(profile);
            poc = stats.Poc;
            vah = stats.Vah;
            val = stats.Val;
            maxProfileValue = stats.MaxProfileValue;
            Values[0][0] = stats.Poc;
            Values[1][0] = stats.Vah;
            Values[2][0] = stats.Val;
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume)) return;
            EnsurePrimaryBar(primaryBar);
            bool isAsk = NTLCore.IsAskTrade(price, bid, ask, lastTradePrice);
            NTLCore.AddVolume(profile, NTLCore.RoundToTick(this, price), isAsk ? 0 : volume, isAsk ? volume : 0);
            lastTradePrice = price;
        }

        private void EnsurePrimaryBar(int primaryBar)
        {
            if (activePrimaryBar == primaryBar) return;
            activePrimaryBar = primaryBar;
            if (ShouldResetAt(primaryBar))
            {
                SnapshotCurrentProfile(Math.Max(0, primaryBar - 1));
                profile.Clear();
                profileStartBar = primaryBar;
                lastResetTime = BarsArray[0].GetTime(primaryBar);
            }
        }

        private void SnapshotCurrentProfile(int endBar)
        {
            if (profile.Count == 0)
                return;

            ProfileSnapshot snapshot = new ProfileSnapshot();
            snapshot.StartBar = profileStartBar;
            snapshot.EndBar = Math.Max(profileStartBar, endBar);
            foreach (var row in profile)
            {
                snapshot.Profile[row.Key] = new NTLVolumeLevel { Bid = row.Value.Bid, Ask = row.Value.Ask };
            }
            completedProfiles.Add(snapshot);
            TrimCompletedProfiles();
        }

        private void TrimCompletedProfiles()
        {
            int keepCompleted = Math.Max(0, ProfilesToShow - 1);
            while (completedProfiles.Count > keepCompleted)
                completedProfiles.RemoveAt(0);
        }

        private bool ShouldResetAt(int primaryBar)
        {
            if (primaryBar == 0) return true;
            if (ResetPeriod == NTLResetPeriod.NoReset) return false;
            DateTime now = BarsArray[0].GetTime(primaryBar);
            if (ResetPeriod == NTLResetPeriod.Session) return IsSessionReset(primaryBar);
            if (ResetPeriod == NTLResetPeriod.Daily) return lastResetTime == DateTime.MinValue || now.Date != lastResetTime.Date;
            return lastResetTime == DateTime.MinValue || now.Date.AddDays(-(int)now.DayOfWeek) != lastResetTime.Date.AddDays(-(int)lastResetTime.DayOfWeek);
        }

        private bool IsSessionReset(int primaryBar)
        {
            if (primaryBar == 0)
                return true;
            if (!BarsArray[0].IsFirstBarOfSessionByIndex(primaryBar))
                return false;

            DateTime now = BarsArray[0].GetTime(primaryBar);
            DateTime previous = BarsArray[0].GetTime(primaryBar - 1);
            if (IsMidnightContinuation(now, previous))
                return false;

            return true;
        }

        private bool IsMidnightContinuation(DateTime now, DateTime previous)
        {
            if (now.Date <= previous.Date)
                return false;
            if (now.TimeOfDay > TimeSpan.FromMinutes(1))
                return false;

            TimeSpan gap = now - previous;
            return gap >= TimeSpan.Zero && gap <= TimeSpan.FromHours(2);
        }

        private ProfileStats CalculateProfileStats(Dictionary<double, NTLVolumeLevel> source)
        {
            ProfileStats stats = new ProfileStats();
            if (source.Count == 0) return stats;
            var ordered = source.OrderByDescending(kv => Math.Abs(NTLCore.ProfileValue(kv.Value, ProfileMode))).ToList();
            stats.Poc = ordered[0].Key;
            stats.MaxProfileValue = Math.Max(1, Math.Abs(NTLCore.ProfileValue(ordered[0].Value, ProfileMode)));
            double target = ordered.Sum(kv => Math.Abs(NTLCore.ProfileValue(kv.Value, ProfileMode))) * ValueAreaPercent / 100.0;
            double running = 0;
            stats.Vah = stats.Poc;
            stats.Val = stats.Poc;
            foreach (var kv in ordered)
            {
                running += Math.Abs(NTLCore.ProfileValue(kv.Value, ProfileMode));
                stats.Vah = Math.Max(stats.Vah, kv.Key);
                stats.Val = Math.Min(stats.Val, kv.Key);
                if (running >= target) break;
            }
            return stats;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null || (profile.Count == 0 && completedProfiles.Count == 0)) return;
            if (volumeBrushDx == null || valueAreaBrushDx == null || buyBrushDx == null || sellBrushDx == null || pocBrushDx == null || valueLineBrushDx == null) OnRenderTargetChanged();
            if (volumeBrushDx == null || valueAreaBrushDx == null || buyBrushDx == null || sellBrushDx == null || pocBrushDx == null || valueLineBrushDx == null) return;
            TrimCompletedProfiles();
            foreach (var snapshot in completedProfiles)
                RenderProfile(chartControl, chartScale, snapshot.Profile, snapshot.StartBar, snapshot.EndBar, true);
            RenderProfile(chartControl, chartScale, profile, profileStartBar, CurrentBar);
        }

        private void RenderProfile(ChartControl chartControl, ChartScale chartScale, Dictionary<double, NTLVolumeLevel> source, int startBar, int endBar, bool completed = false)
        {
            ProfileStats stats = CalculateProfileStats(source);
            if (stats.MaxProfileValue <= 0) return;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            float x = Math.Max(chartControl.CanvasLeft, chartControl.GetXByBarIndex(ChartBars, Math.Max(0, startBar)));
            float rightLimit = Math.Min(chartControl.CanvasRight, chartControl.GetXByBarIndex(ChartBars, Math.Max(startBar, endBar)) + Math.Max(10, Width));
            foreach (var row in source.OrderBy(kv => kv.Key))
            {
                if (row.Key < chartScale.MinValue || row.Key > chartScale.MaxValue) continue;
                double value = Math.Abs(NTLCore.ProfileValue(row.Value, ProfileMode));
                float width = (float)(Width * value / stats.MaxProfileValue);
                width = Math.Min(width, Math.Max(1, rightLimit - x));
                double tick = NTLCore.TickSize(this);
                float yTop = chartScale.GetYByValue(row.Key + tick * 0.5);
                float yBottom = chartScale.GetYByValue(row.Key - tick * 0.5);
                float height = Math.Max(1, yBottom - yTop);
                DxBrush brush = row.Key >= stats.Val && row.Key <= stats.Vah ? valueAreaBrushDx : volumeBrushDx;
                if (ProfileMode == NTLProfileMode.BidAsk)
                {
                    float bidWidth = width * (float)(row.Value.Bid / Math.Max(1, row.Value.Total));
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x, yTop, bidWidth, height), sellBrushDx);
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x + bidWidth, yTop, width - bidWidth, height), buyBrushDx);
                }
                else
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x, yTop, width, height), brush);
            }
            if (ShowPoc) DrawLevelLine(chartControl, chartScale, stats.Poc, pocBrushDx, completed ? 1f : 2f);
            if (ShowValueArea)
            {
                DrawLevelLine(chartControl, chartScale, stats.Vah, valueLineBrushDx, 1f);
                DrawLevelLine(chartControl, chartScale, stats.Val, valueLineBrushDx, 1f);
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
        [Display(Name = "Reset period", GroupName = "Calculation", Order = 0)] public NTLResetPeriod ResetPeriod { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Profile mode", GroupName = "Calculation", Order = 1)] public NTLProfileMode ProfileMode { get; set; }
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Profiles to show", GroupName = "Calculation", Order = 2)] public int ProfilesToShow { get; set; }
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Value area percent", GroupName = "Calculation", Order = 3)] public double ValueAreaPercent { get; set; }
        [NinjaScriptProperty]
        [Range(20, 600)]
        [Display(Name = "Profile width", GroupName = "Visual", Order = 4)] public int Width { get; set; }
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

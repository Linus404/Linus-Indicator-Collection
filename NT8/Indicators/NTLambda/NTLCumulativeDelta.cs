#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLCumulativeDelta : Indicator
    {
        private double cumulativeDelta, openDelta;
        private DateTime lastResetTime = DateTime.MinValue;
        private int activePrimaryBar = -1;
        private double lastTradePrice = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Cumulative Delta";
                Description = "Cumulative volume delta with session, daily, weekly, or no reset.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DrawHorizontalGridLines = true;
                ResetPeriod = NTLResetPeriod.Session;
                DeltaMode = NTLDeltaMode.BidAsk;
                PositiveBrush = Brushes.LimeGreen;
                NegativeBrush = Brushes.IndianRed;
                AddPlot(Brushes.DodgerBlue, "DeltaClose");
                AddPlot(Brushes.Transparent, "DeltaOpen");
                AddLine(Brushes.DimGray, 0, "Zero");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e) { }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                ProcessTickSeries();
                return;
            }
            if (BarsInProgress != 0) return;

            if (activePrimaryBar < CurrentBar) EnsurePrimaryBar(CurrentBar);
            UpdatePlots(0);
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume)) return;

            EnsurePrimaryBar(primaryBar);
            bool isAsk = DeltaMode == NTLDeltaMode.BidAsk
                ? NTLCore.IsAskTrade(price, bid, ask, lastTradePrice)
                : NTLCore.IsAskTrade(price, 0, 0, lastTradePrice);
            cumulativeDelta += isAsk ? volume : -volume;
            lastTradePrice = price;
        }

        private void EnsurePrimaryBar(int primaryBar)
        {
            if (activePrimaryBar == primaryBar) return;
            activePrimaryBar = primaryBar;
            if (ShouldResetAt(primaryBar))
            {
                cumulativeDelta = 0;
                lastResetTime = BarsArray[0].GetTime(primaryBar);
            }
            openDelta = cumulativeDelta;
        }

        private bool ShouldResetAt(int primaryBar)
        {
            if (primaryBar == 0) return true;
            if (ResetPeriod == NTLResetPeriod.NoReset) return false;

            DateTime now = BarsArray[0].GetTime(primaryBar);
            if (ResetPeriod == NTLResetPeriod.Session) return BarsArray[0].IsFirstBarOfSessionByIndex(primaryBar);
            if (ResetPeriod == NTLResetPeriod.Daily) return lastResetTime == DateTime.MinValue || now.Date != lastResetTime.Date;
            return lastResetTime == DateTime.MinValue || now.Date.AddDays(-(int)now.DayOfWeek) != lastResetTime.Date.AddDays(-(int)lastResetTime.DayOfWeek);
        }

        private void UpdatePlots(int barsAgo)
        {
            Values[0][barsAgo] = cumulativeDelta;
            Values[1][barsAgo] = openDelta;
            PlotBrushes[0][barsAgo] = cumulativeDelta >= openDelta ? PositiveBrush : NegativeBrush;
        }

        private bool TryGetTick(out int primaryBar, out double price, out double bid, out double ask, out double volume)
        {
            primaryBar = -1;
            price = bid = ask = volume = 0;
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
        [Display(Name = "Reset period", GroupName = "Calculation", Order = 0)]
        public NTLResetPeriod ResetPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Delta mode", GroupName = "Calculation", Order = 1)]
        public NTLDeltaMode DeltaMode { get; set; }

        [XmlIgnore]
        [Display(Name = "Positive brush", GroupName = "Visual", Order = 0)]
        public Brush PositiveBrush { get; set; }

        [Browsable(false)]
        public string PositiveBrushSerializable
        {
            get { return Serialize.BrushToString(PositiveBrush); }
            set { PositiveBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Negative brush", GroupName = "Visual", Order = 1)]
        public Brush NegativeBrush { get; set; }

        [Browsable(false)]
        public string NegativeBrushSerializable
        {
            get { return Serialize.BrushToString(NegativeBrush); }
            set { NegativeBrush = Serialize.StringToBrush(value); }
        }
    }
}

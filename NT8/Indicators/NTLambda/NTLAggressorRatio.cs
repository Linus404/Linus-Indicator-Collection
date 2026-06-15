#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLAggressorRatio : Indicator
    {
        private double buyAggression, sellAggression, smoothed = double.NaN, smoothingBase = double.NaN, lastPrice = double.NaN;
        private int activePrimaryBar = -1, smoothingPrimaryBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Aggressor Ratio";
                Description = "Smoothed aggressive buy/sell ratio.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DrawHorizontalGridLines = true;
                SmoothingPeriod = 8;
                AddPlot(System.Windows.Media.Brushes.DodgerBlue, "Ratio");
                AddPlot(System.Windows.Media.Brushes.Orange, "SmoothedRatio");
                AddLine(System.Windows.Media.Brushes.DimGray, 1.0, "Parity");
                AddLine(System.Windows.Media.Brushes.DarkGray, 0.5, "Half");
                AddLine(System.Windows.Media.Brushes.DarkGray, 2.0, "Double");
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

            double ratio = activePrimaryBar == CurrentBar ? Ratio() : 0;
            if (smoothingPrimaryBar != CurrentBar)
            {
                smoothingPrimaryBar = CurrentBar;
                smoothingBase = smoothed;
            }
            smoothed = NTLCore.Ema(smoothingBase, ratio, SmoothingPeriod);
            Values[0][0] = ratio;
            Values[1][0] = smoothed;
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume)) return;

            EnsurePrimaryBar(primaryBar);
            if (NTLCore.IsAskTrade(price, bid, ask, lastPrice)) buyAggression += volume;
            else sellAggression += volume;
            lastPrice = price;
        }

        private void EnsurePrimaryBar(int primaryBar)
        {
            if (activePrimaryBar == primaryBar) return;
            activePrimaryBar = primaryBar;
            buyAggression = 0;
            sellAggression = 0;
        }

        private double Ratio()
        {
            return Math.Abs(sellAggression) < 0.0000001 ? buyAggression : buyAggression / sellAggression;
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
        [Range(1, int.MaxValue)]
        [Display(Name = "Smoothing period", GroupName = "Calculation", Order = 0)]
        public int SmoothingPeriod { get; set; }
    }
}

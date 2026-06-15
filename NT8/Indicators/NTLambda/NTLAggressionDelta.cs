#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLAggressionDelta : Indicator
    {
        private double bidVolume, askVolume;
        private double lastPrice = double.NaN;
        private int activePrimaryBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Aggression Delta";
                Description = "Per-bar aggressive buy minus sell volume.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DrawHorizontalGridLines = true;

                AddPlot(new Stroke(System.Windows.Media.Brushes.Gray, 2), PlotStyle.Bar, "AggressionDelta");
                AddLine(System.Windows.Media.Brushes.DimGray, 0, "Zero");
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

            // Calculate the delta
            double delta = activePrimaryBar == CurrentBar ? askVolume - bidVolume : 0;
            Values[0][0] = delta;

            // Dynamically color the current bar based on value
            if (delta >= 0)
                PlotBrushes[0][0] = System.Windows.Media.Brushes.Green;
            else
                PlotBrushes[0][0] = System.Windows.Media.Brushes.Red;
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume)) return;

            EnsurePrimaryBar(primaryBar);
            if (NTLCore.IsAskTrade(price, bid, ask, lastPrice)) askVolume += volume;
            else bidVolume += volume;
            lastPrice = price;
        }

        private void EnsurePrimaryBar(int primaryBar)
        {
            if (activePrimaryBar == primaryBar) return;
            activePrimaryBar = primaryBar;
            askVolume = 0;
            bidVolume = 0;
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
    }
}

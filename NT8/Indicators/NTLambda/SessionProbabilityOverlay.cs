#region Using declarations
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class SessionProbabilityOverlay : Indicator
    {
        private List<double> sessionReturns;   // Historische Open->Close Returns
        private double openPrice;
        private bool newSession;

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "LookbackSessions", Order = 1, GroupName = "Parameters")]
        public int LookbackSessions { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Overlay mit Konfidenzzonen basierend auf historischen Session Returns";
                Name = "SessionProbabilityOverlay";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                LookbackSessions = 250;
                AddPlot(Brushes.Transparent, "Dummy"); // Zum Zeichnen
            }
            else if (State == State.DataLoaded)
            {
                sessionReturns = new List<double>();
            }
        }

        protected override void OnBarUpdate()
        {
            // Detect Session Open
            if (Bars.IsFirstBarOfSession)
            {
                openPrice = Open[0];
                newSession = true;
            }

            // At session close: calculate return & update distribution
            if (Bars.IsLastBarOfSession && newSession)
            {
                double ret = (Close[0] - openPrice) / openPrice;
                sessionReturns.Add(ret);

                if (sessionReturns.Count > LookbackSessions)
                    sessionReturns.RemoveAt(0);

                newSession = false;
            }

            // Draw probability zones intraday
            if (sessionReturns.Count > 30 && Bars.IsFirstBarOfSession == false)
            {
                double[] sorted = sessionReturns.OrderBy(x => x).ToArray();

                // Quantile ranges (empirisch)
                double p10 = Quantile(sorted, 0.10);
                double p25 = Quantile(sorted, 0.25);
                double p75 = Quantile(sorted, 0.75);
                double p90 = Quantile(sorted, 0.90);

                double basePrice = openPrice;
                double low90 = basePrice * (1 + p10);
                double high90 = basePrice * (1 + p90);
                double low50 = basePrice * (1 + p25);
                double high50 = basePrice * (1 + p75);

                // Draw as regions
                var lightGray = new SolidColorBrush(Colors.LightGray);
                lightGray.Opacity = 0.3;
                var lightGreen = new SolidColorBrush(Colors.LightGreen);
                lightGreen.Opacity = 0.5;
                
                Draw.Region(this, "Zone90" + CurrentBar, CurrentBar, 0, low90, 0, high90,
                    null, lightGray, 10);
                Draw.Region(this, "Zone50" + CurrentBar, CurrentBar, 0, low50, 0, high50,
                    null, lightGreen, 20);
            }
        }

        private double Quantile(double[] sorted, double q)
        {
            if (sorted.Length == 0) return 0;
            double pos = (sorted.Length - 1) * q;
            int idx = (int)Math.Floor(pos);
            double frac = pos - idx;
            if (idx + 1 < sorted.Length)
                return sorted[idx] * (1 - frac) + sorted[idx + 1] * frac;
            else
                return sorted[idx];
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private NTLambda.SessionProbabilityOverlay[] cacheSessionProbabilityOverlay;
        public NTLambda.SessionProbabilityOverlay SessionProbabilityOverlay(int lookbackSessions)
        {
            return SessionProbabilityOverlay(Input, lookbackSessions);
        }

        public NTLambda.SessionProbabilityOverlay SessionProbabilityOverlay(ISeries<double> input, int lookbackSessions)
        {
            if (cacheSessionProbabilityOverlay != null)
                for (int idx = 0; idx < cacheSessionProbabilityOverlay.Length; idx++)
                    if (cacheSessionProbabilityOverlay[idx] != null && cacheSessionProbabilityOverlay[idx].LookbackSessions == lookbackSessions && cacheSessionProbabilityOverlay[idx].EqualsInput(input))
                        return cacheSessionProbabilityOverlay[idx];
            return CacheIndicator<NTLambda.SessionProbabilityOverlay>(new NTLambda.SessionProbabilityOverlay() { LookbackSessions = lookbackSessions }, input, ref cacheSessionProbabilityOverlay);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.NTLambda.SessionProbabilityOverlay SessionProbabilityOverlay(int lookbackSessions)
        {
            return indicator.SessionProbabilityOverlay(Input, lookbackSessions);
        }

        public Indicators.NTLambda.SessionProbabilityOverlay SessionProbabilityOverlay(ISeries<double> input, int lookbackSessions)
        {
            return indicator.SessionProbabilityOverlay(input, lookbackSessions);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.NTLambda.SessionProbabilityOverlay SessionProbabilityOverlay(int lookbackSessions)
        {
            return indicator.SessionProbabilityOverlay(Input, lookbackSessions);
        }

        public Indicators.NTLambda.SessionProbabilityOverlay SessionProbabilityOverlay(ISeries<double> input, int lookbackSessions)
        {
            return indicator.SessionProbabilityOverlay(input, lookbackSessions);
        }
    }
}

#endregion

#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class AggressionDelta : Indicator
    {
        private double buys;
        private double sells;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"NTLambda Aggression Delta";
                Name = "NTL Aggression Delta";
                Calculate = Calculate.OnEachTick;
                DrawOnPricePanel = false;
                IsOverlay = false;
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                PaintPriceMarkers = true;
                ScaleJustification = ScaleJustification.Right;
                PositiveBrush = Brushes.Green;
                NegativeBrush = Brushes.Red;
            }
            else if (State == State.Configure)
            {
                AddLine(Brushes.Gray, 0, "Zero Line");
                AddPlot(Brushes.Gray, "Delta");
                Plots[0].PlotStyle = PlotStyle.Bar;
                Plots[0].AutoWidth = true;
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                // Handle main timeframe bar updates
                if (IsFirstTickOfBar && State != State.Historical && !(IsTickReplays[0] ?? false))
                {
                    // Reset for real-time non-tick-replay
                    buys = 0;
                    sells = 0;
                }

                // Set the current delta value
                Values[0][0] = buys - sells;
                PlotBrushes[0][0] = (Values[0][0] > 0) ? PositiveBrush : NegativeBrush;

                // Reset after historical or tick replay bar completion
                if (State == State.Historical || (IsTickReplays[0] ?? false))
                {
                    buys = 0;
                    sells = 0;
                }
            }

            // Process tick data (works for both historical and tick replay)
            if (BarsInProgress == 1)
            {
                double price = BarsArray[1].GetClose(CurrentBars[1]);
                double ask = BarsArray[1].GetAsk(CurrentBars[1]);
                double bid = BarsArray[1].GetBid(CurrentBars[1]);
                double volume = BarsArray[1].GetVolume(CurrentBars[1]);

                // Classify trades using bid/ask comparison
                if (price >= ask)
                {
                    buys += volume;
                }
                else if (price <= bid)
                {
                    sells += volume;
                }
                else
                {
                    // Split volume for mid-market trades
                    buys += volume * 0.5;
                    sells += volume * 0.5;
                }
            }
        }

        #region Properties
        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Positive Color", GroupName = "Visual")]
        public Brush PositiveBrush { get; set; }

        [Browsable(false)]
        public string PositiveBrushSerializable
        {
            get { return Serialize.BrushToString(PositiveBrush); }
            set { PositiveBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Negative Color", GroupName = "Visual")]
        public Brush NegativeBrush { get; set; }

        [Browsable(false)]
        public string NegativeBrushSerializable
        {
            get { return Serialize.BrushToString(NegativeBrush); }
            set { NegativeBrush = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private NTLambda.AggressionDelta[] cacheAggressionDelta;
        public NTLambda.AggressionDelta AggressionDelta()
        {
            return AggressionDelta(Input);
        }

        public NTLambda.AggressionDelta AggressionDelta(ISeries<double> input)
        {
            if (cacheAggressionDelta != null)
                for (int idx = 0; idx < cacheAggressionDelta.Length; idx++)
                    if (cacheAggressionDelta[idx] != null && cacheAggressionDelta[idx].EqualsInput(input))
                        return cacheAggressionDelta[idx];
            return CacheIndicator<NTLambda.AggressionDelta>(new NTLambda.AggressionDelta(), input, ref cacheAggressionDelta);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.NTLambda.AggressionDelta AggressionDelta()
        {
            return indicator.AggressionDelta(Input);
        }

        public Indicators.NTLambda.AggressionDelta AggressionDelta(ISeries<double> input)
        {
            return indicator.AggressionDelta(input);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.NTLambda.AggressionDelta AggressionDelta()
        {
            return indicator.AggressionDelta(Input);
        }

        public Indicators.NTLambda.AggressionDelta AggressionDelta(ISeries<double> input)
        {
            return indicator.AggressionDelta(input);
        }
    }
}

#endregion

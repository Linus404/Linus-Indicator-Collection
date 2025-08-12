#region Using declarations
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
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators.LambdaLinus;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.LambdaLinus
{
    public class COFI : Indicator
    {
        private double buyVolume;
        private double sellVolume;
        private double currentBid;
        private double currentAsk;
        private double lastTradePrice;
        private List<double> ofiHistory;
        private List<double> volumeHistory;
        private Series<double> cofiValues;
        private SMA sma1;
        private EMA ema1;
        private SMA sma2;
        private EMA ema2;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Cumulative Order Flow Imbalance (COFI) / Weighted Order Flow Imbalance (WOFI) Indicator";
                Name = "COFI";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // User parameters
                Weighting = WeightingType.Exponential;
                Lambda = 0.05;
                WindowSize = 100;
                ResetPeriod = 0;

                // Moving Average parameters
                MA1_Type = MAType.EMA;
                MA1_Length = 12;
                MA2_Type = MAType.EMA;
                MA2_Length = 26;
                ShowMADifference = true;

                // Visual parameters
                LineColor = Brushes.Blue;
                LineWidth = 2;
                MA1Color = Brushes.Orange;
                MA2Color = Brushes.Red;
                MADiffPositiveColor = Brushes.Green;
                MADiffNegativeColor = Brushes.Red;

                // Add plots - COFI line, MA1 line, MA2 line, and MA difference histogram
                AddPlot(new Stroke(Brushes.Blue, 2), PlotStyle.Line, "COFI");
                AddPlot(new Stroke(Brushes.Orange, 1), PlotStyle.Line, "MA1");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "MA2");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Bar, "MADiff");

                // Lines
                AddLine(Brushes.Gray, 0, "Zero");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Data.BarsPeriodType.Tick, 1);
                AddDataSeries(Instrument.FullName, Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Bid);
                AddDataSeries(Instrument.FullName, Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Ask);
            }
            else if (State == State.DataLoaded)
            {
                ofiHistory = new List<double>();
                volumeHistory = new List<double>();
                cofiValues = new Series<double>(this);

                // Initialize first MA
                if (MA1_Type == MAType.SMA)
                    sma1 = SMA(cofiValues, MA1_Length);
                else if (MA1_Type == MAType.EMA)
                    ema1 = EMA(cofiValues, MA1_Length);

                // Initialize second MA
                if (MA2_Type == MAType.SMA)
                    sma2 = SMA(cofiValues, MA2_Length);
                else if (MA2_Type == MAType.EMA)
                    ema2 = EMA(cofiValues, MA2_Length);
            }
        }

        protected override void OnBarUpdate()
        {
            // Update bid/ask prices
            if (BarsInProgress == 2) // Bid series
            {
                currentBid = Close[0];
                return;
            }
            else if (BarsInProgress == 3) // Ask series
            {
                currentAsk = Close[0];
                return;
            }

            // Handle trade ticks
            if (BarsInProgress == 1)
            {
                double tradePrice = Close[0];
                double tradeVolume = Volume[0];

                // Classify trade using utility method
                int classification = OrderFlowUtils.ClassifyTrade(tradePrice, currentBid, currentAsk, lastTradePrice);

                if (classification > 0)
                    buyVolume += tradeVolume;
                else if (classification < 0)
                    sellVolume += tradeVolume;
                else
                {
                    buyVolume += tradeVolume * 0.5;
                    sellVolume += tradeVolume * 0.5;
                }

                lastTradePrice = tradePrice;
            }
            // Handle main timeframe bars
            else if (BarsInProgress == 0)
            {
                if (CurrentBar < 1) return;

                // Calculate current bar OFI using utility
                double ofi = OrderFlowUtils.CalculateOFI(buyVolume, sellVolume);
                double totalVolume = buyVolume + sellVolume;

                // Add to history
                ofiHistory.Add(ofi);
                volumeHistory.Add(totalVolume);

                // Check for reset period
                if (ResetPeriod > 0 && ofiHistory.Count > ResetPeriod)
                {
                    ofiHistory.RemoveAt(0);
                    volumeHistory.RemoveAt(0);
                }

                // Calculate weighted/cumulative value
                double cofiValue = CalculateWeightedOFI();

                // Store and plot COFI value
                cofiValues[0] = cofiValue;
                Values[0][0] = cofiValue;
                PlotBrushes[0][0] = LineColor;

                // Calculate and plot MA1 if enabled
                double ma1Value = 0;
                if (MA1_Type != MAType.None && CurrentBar >= MA1_Length)
                {
                    if (MA1_Type == MAType.SMA)
                        ma1Value = sma1[0];
                    else if (MA1_Type == MAType.EMA)
                        ma1Value = ema1[0];

                    Values[1][0] = ma1Value;
                    PlotBrushes[1][0] = MA1Color;
                }

                // Calculate and plot MA2 if enabled
                double ma2Value = 0;
                if (MA2_Type != MAType.None && CurrentBar >= MA2_Length)
                {
                    if (MA2_Type == MAType.SMA)
                        ma2Value = sma2[0];
                    else if (MA2_Type == MAType.EMA)
                        ma2Value = ema2[0];

                    Values[2][0] = ma2Value;
                    PlotBrushes[2][0] = MA2Color;
                }

                // Calculate and plot MA difference histogram if both MAs are active
                if (ShowMADifference && MA1_Type != MAType.None && MA2_Type != MAType.None
                    && CurrentBar >= Math.Max(MA1_Length, MA2_Length))
                {
                    double maDiff = ma1Value - ma2Value;
                    Values[3][0] = maDiff;

                    // Color based on positive/negative
                    if (maDiff >= 0)
                        PlotBrushes[3][0] = MADiffPositiveColor;
                    else
                        PlotBrushes[3][0] = MADiffNegativeColor;
                }
                else
                {
                    PlotBrushes[3][0] = Brushes.Transparent;
                }

                // Reset volumes for next bar
                buyVolume = 0;
                sellVolume = 0;
            }
        }

        private double CalculateWeightedOFI()
        {
            if (ofiHistory.Count == 0) return 0;

            double result = 0;
            int t = ofiHistory.Count - 1;

            switch (Weighting)
            {
                case WeightingType.Cumulative:
                    for (int i = 0; i <= t; i++)
                        result += ofiHistory[i] * volumeHistory[i];
                    break;

                case WeightingType.Exponential:
                    int maxLookback = OrderFlowUtils.CalculateEffectiveLookback(Lambda);
                    int lookback = Math.Min(t, Math.Min(maxLookback, 500));
                    int startIdx = Math.Max(0, t - lookback);

                    for (int i = startIdx; i <= t; i++)
                    {
                        double weight = OrderFlowUtils.ExponentialWeight(t - i, Lambda);
                        result += weight * ofiHistory[i] * volumeHistory[i];
                    }
                    break;

                case WeightingType.Linear:
                    int linearLookback = Math.Min(t, WindowSize);
                    int linearStart = Math.Max(0, t - linearLookback);

                    for (int i = linearStart; i <= t; i++)
                    {
                        double weight = OrderFlowUtils.LinearWeight(t - i, WindowSize);
                        if (weight > 0)
                            result += weight * ofiHistory[i] * volumeHistory[i];
                    }
                    break;

                case WeightingType.FixedWindow:
                    int windowLookback = Math.Min(t, WindowSize);
                    int windowStart = Math.Max(0, t - windowLookback);

                    for (int i = windowStart; i <= t; i++)
                        result += ofiHistory[i] * volumeHistory[i];
                    break;
            }

            return result;
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Weighting Type", Order = 1, GroupName = "Calculation")]
        public WeightingType Weighting { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1)]
        [Display(Name = "Lambda", Order = 2, GroupName = "Calculation")]
        public double Lambda { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Window Size", Order = 3, GroupName = "Calculation")]
        public int WindowSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Reset Period", Order = 4, GroupName = "Calculation")]
        public int ResetPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA1 Type", Order = 1, GroupName = "Moving Average 1")]
        public MAType MA1_Type { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MA1 Length", Order = 2, GroupName = "Moving Average 1")]
        public int MA1_Length { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA2 Type", Order = 1, GroupName = "Moving Average 2")]
        public MAType MA2_Type { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MA2 Length", Order = 2, GroupName = "Moving Average 2")]
        public int MA2_Length { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show MA Difference", Order = 1, GroupName = "MA Difference")]
        public bool ShowMADifference { get; set; }

        [XmlIgnore]
        [Display(Name = "Line Color", Order = 1, GroupName = "Appearance")]
        public Brush LineColor { get; set; }

        [Browsable(false)]
        public string LineColorSerializable
        {
            get { return Serialize.BrushToString(LineColor); }
            set { LineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Line Width", Order = 2, GroupName = "Appearance")]
        public int LineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "MA1 Color", Order = 3, GroupName = "Appearance")]
        public Brush MA1Color { get; set; }

        [Browsable(false)]
        public string MA1ColorSerializable
        {
            get { return Serialize.BrushToString(MA1Color); }
            set { MA1Color = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "MA2 Color", Order = 4, GroupName = "Appearance")]
        public Brush MA2Color { get; set; }

        [Browsable(false)]
        public string MA2ColorSerializable
        {
            get { return Serialize.BrushToString(MA2Color); }
            set { MA2Color = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "MA Diff Positive", Order = 5, GroupName = "Appearance")]
        public Brush MADiffPositiveColor { get; set; }

        [Browsable(false)]
        public string MADiffPositiveColorSerializable
        {
            get { return Serialize.BrushToString(MADiffPositiveColor); }
            set { MADiffPositiveColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "MA Diff Negative", Order = 6, GroupName = "Appearance")]
        public Brush MADiffNegativeColor { get; set; }

        [Browsable(false)]
        public string MADiffNegativeColorSerializable
        {
            get { return Serialize.BrushToString(MADiffNegativeColor); }
            set { MADiffNegativeColor = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private LambdaLinus.COFI[] cacheCOFI;
        public LambdaLinus.COFI COFI(WeightingType weighting, double lambda, int windowSize, int resetPeriod, MAType mA1_Type, int mA1_Length, MAType mA2_Type, int mA2_Length, bool showMADifference, int lineWidth)
        {
            return COFI(Input, weighting, lambda, windowSize, resetPeriod, mA1_Type, mA1_Length, mA2_Type, mA2_Length, showMADifference, lineWidth);
        }

        public LambdaLinus.COFI COFI(ISeries<double> input, WeightingType weighting, double lambda, int windowSize, int resetPeriod, MAType mA1_Type, int mA1_Length, MAType mA2_Type, int mA2_Length, bool showMADifference, int lineWidth)
        {
            if (cacheCOFI != null)
                for (int idx = 0; idx < cacheCOFI.Length; idx++)
                    if (cacheCOFI[idx] != null && cacheCOFI[idx].Weighting == weighting && cacheCOFI[idx].Lambda == lambda && cacheCOFI[idx].WindowSize == windowSize && cacheCOFI[idx].ResetPeriod == resetPeriod && cacheCOFI[idx].MA1_Type == mA1_Type && cacheCOFI[idx].MA1_Length == mA1_Length && cacheCOFI[idx].MA2_Type == mA2_Type && cacheCOFI[idx].MA2_Length == mA2_Length && cacheCOFI[idx].ShowMADifference == showMADifference && cacheCOFI[idx].LineWidth == lineWidth && cacheCOFI[idx].EqualsInput(input))
                        return cacheCOFI[idx];
            return CacheIndicator<LambdaLinus.COFI>(new LambdaLinus.COFI() { Weighting = weighting, Lambda = lambda, WindowSize = windowSize, ResetPeriod = resetPeriod, MA1_Type = mA1_Type, MA1_Length = mA1_Length, MA2_Type = mA2_Type, MA2_Length = mA2_Length, ShowMADifference = showMADifference, LineWidth = lineWidth }, input, ref cacheCOFI);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.LambdaLinus.COFI COFI(WeightingType weighting, double lambda, int windowSize, int resetPeriod, MAType mA1_Type, int mA1_Length, MAType mA2_Type, int mA2_Length, bool showMADifference, int lineWidth)
        {
            return indicator.COFI(Input, weighting, lambda, windowSize, resetPeriod, mA1_Type, mA1_Length, mA2_Type, mA2_Length, showMADifference, lineWidth);
        }

        public Indicators.LambdaLinus.COFI COFI(ISeries<double> input, WeightingType weighting, double lambda, int windowSize, int resetPeriod, MAType mA1_Type, int mA1_Length, MAType mA2_Type, int mA2_Length, bool showMADifference, int lineWidth)
        {
            return indicator.COFI(input, weighting, lambda, windowSize, resetPeriod, mA1_Type, mA1_Length, mA2_Type, mA2_Length, showMADifference, lineWidth);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.LambdaLinus.COFI COFI(WeightingType weighting, double lambda, int windowSize, int resetPeriod, MAType mA1_Type, int mA1_Length, MAType mA2_Type, int mA2_Length, bool showMADifference, int lineWidth)
        {
            return indicator.COFI(Input, weighting, lambda, windowSize, resetPeriod, mA1_Type, mA1_Length, mA2_Type, mA2_Length, showMADifference, lineWidth);
        }

        public Indicators.LambdaLinus.COFI COFI(ISeries<double> input, WeightingType weighting, double lambda, int windowSize, int resetPeriod, MAType mA1_Type, int mA1_Length, MAType mA2_Type, int mA2_Length, bool showMADifference, int lineWidth)
        {
            return indicator.COFI(input, weighting, lambda, windowSize, resetPeriod, mA1_Type, mA1_Length, mA2_Type, mA2_Length, showMADifference, lineWidth);
        }
    }
}

#endregion

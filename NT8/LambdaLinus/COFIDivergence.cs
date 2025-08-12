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
using NinjaTrader.NinjaScript.Indicators.LambdaLinus;
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

namespace NinjaTrader.NinjaScript.Indicators.LambdaLinus
{
    public class COFIDivergence : Indicator
    {
        // Order flow calculation variables
        private double buyVolume;
        private double sellVolume;
        private double currentBid;
        private double currentAsk;
        private double lastTradePrice;
        private List<OrderFlowBar> orderFlowHistory;

        // Divergence calculation variables
        private Series<double> cofiValues;
        private Series<double> divergenceValues;
        private EMA divergenceEMA;
        private Queue<double> priceHistory;
        private Queue<double> cofiHistory;
        private bool debugMode = false; // Set to true for debugging

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"COFI Convergence/Divergence Indicator - Analyzes momentum divergence using linear regression slopes and correlation";
                Name = "COFIDivergence";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // COFI parameters
                COFIWeighting = WeightingType.Cumulative;
                COFILambda = 0.05;
                COFIWindowSize = 100;
                COFIResetPeriod = 0;

                // Linear regression and correlation parameters
                LinearRegressionPeriod = 20;
                CorrelationPeriod = 20;
                SmoothDivergence = true;
                DivergenceSmoothingPeriod = 5;

                // Correlation thresholds for background colors
                NegativeCorrelationThreshold = -0.2;
                WeakCorrelationThreshold = 0.2;
                WeakeningCorrelationThreshold = 0.6;

                // Visual parameters
                DivergencePositiveColor = Brushes.Green;
                DivergenceNegativeColor = Brushes.Red;
                DivergenceNeutralColor = Brushes.Gray;

                // Add single divergence histogram plot
                AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Bar, "DivergenceSignal");

                // Add zero line
                AddLine(Brushes.DarkGray, 0, "Zero");
            }
            else if (State == State.Configure)
            {
                // Add tick data series for order flow calculation
                AddDataSeries(Data.BarsPeriodType.Tick, 1);
                AddDataSeries(Instrument.FullName, Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Bid);
                AddDataSeries(Instrument.FullName, Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Ask);
            }
            else if (State == State.DataLoaded)
            {
                // Initialize order flow variables
                orderFlowHistory = new List<OrderFlowBar>();

                // Initialize series
                cofiValues = new Series<double>(this);
                divergenceValues = new Series<double>(this);

                // Initialize EMA for smoothing
                if (SmoothDivergence)
                {
                    divergenceEMA = EMA(divergenceValues, DivergenceSmoothingPeriod);
                }

                // Initialize history queues for correlation calculation
                priceHistory = new Queue<double>();
                cofiHistory = new Queue<double>();

                // Initialize price tracking
                buyVolume = 0;
                sellVolume = 0;
                currentBid = 0;
                currentAsk = 0;
                lastTradePrice = 0;
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
                return;
            }

            // Handle main timeframe bars (BarsInProgress == 0)
            // Need enough bars for all calculations
            int minBars = Math.Max(LinearRegressionPeriod, CorrelationPeriod);
            if (CurrentBar < minBars + 1)
            {
                if (debugMode && CurrentBar == minBars)
                    Print($"COFIDivergence: Waiting for minimum bars. Current: {CurrentBar}, Required: {minBars + 1}");
                return;
            }

            try
            {
                // Calculate current bar OFI
                double ofi = OrderFlowUtils.CalculateOFI(buyVolume, sellVolume);
                double totalVolume = buyVolume + sellVolume;

                // Create order flow bar and add to history
                var orderFlowBar = new OrderFlowBar(ofi, totalVolume, buyVolume, sellVolume, CurrentBar);
                OrderFlowUtils.UpdateOrderFlowHistory(orderFlowHistory, orderFlowBar, COFIResetPeriod);

                // Calculate weighted COFI value
                double currentCOFI = OrderFlowUtils.CalculateWeightedOFI(orderFlowHistory, COFIWeighting, COFILambda, COFIWindowSize);
                cofiValues[0] = currentCOFI;

                // Get current values for divergence calculation
                double currentPrice = Close[0];

                // Update history queues for correlation calculation
                priceHistory.Enqueue(currentPrice);
                cofiHistory.Enqueue(currentCOFI);

                if (priceHistory.Count > CorrelationPeriod)
                {
                    priceHistory.Dequeue();
                    cofiHistory.Dequeue();
                }

                // Calculate slopes using utility functions
                double[] priceArray = GetSeriesValues(Close, LinearRegressionPeriod);
                double[] cofiArray = GetSeriesValues(cofiValues, LinearRegressionPeriod);

                double priceSlope = OrderFlowUtils.CalculateLinearRegressionSlope(priceArray, LinearRegressionPeriod);
                double currentCOFINormalized = currentCOFI;

                // Calculate Pearson correlation for background coloring
                double correlation = 0;
                if (priceHistory.Count == CorrelationPeriod)
                {
                    correlation = OrderFlowUtils.CalculatePearsonCorrelation(priceHistory.ToArray(), cofiHistory.ToArray());
                }

                // Calculate divergence signal using new approach
                double signal = 0;
                Brush signalColor = DivergenceNeutralColor;

                if (priceSlope > 0 && currentCOFINormalized < 0)
                {
                    // Bearish divergence: price up + COFI down - negative bar
                    signal = -Math.Sqrt(Math.Abs(priceSlope * currentCOFINormalized));
                    signalColor = DivergenceNegativeColor; // Red
                }
                else if (priceSlope < 0 && currentCOFINormalized > 0)
                {
                    // Bullish divergence: price down + COFI up - positive bar  
                    signal = Math.Sqrt(Math.Abs(priceSlope * currentCOFINormalized));
                    signalColor = DivergencePositiveColor; // Green
                }
                // else: no divergence, signal = 0

                // Store for smoothing if enabled
                divergenceValues[0] = signal;

                // Apply smoothing if enabled
                double finalSignal = signal;
                if (SmoothDivergence && CurrentBar >= minBars + DivergenceSmoothingPeriod)
                {
                    finalSignal = divergenceEMA[0];

                    // Preserve color logic for smoothed signal
                    if (finalSignal > 0.01)
                        signalColor = DivergencePositiveColor;
                    else if (finalSignal < -0.01)
                        signalColor = DivergenceNegativeColor;
                    else
                        signalColor = DivergenceNeutralColor;
                }

                // Set background color based on correlation
                SetBackgroundColor(correlation);

                // Plot the single divergence histogram
                Values[0][0] = finalSignal;
                PlotBrushes[0][0] = signalColor;

                if (debugMode && CurrentBar % 100 == 0)
                {
                    Print($"Bar {CurrentBar}: PriceSlope={priceSlope:F6}, COFI={currentCOFINormalized:F6}, " +
                          $"Signal={finalSignal:F4}, Correlation={correlation:F4}");
                }

                // Reset volumes for next bar
                buyVolume = 0;
                sellVolume = 0;
            }
            catch (Exception ex)
            {
                Print($"COFIDivergence Error at bar {CurrentBar}: {ex.Message}");
                // Set default values on error
                Values[0][0] = 0;

                // Reset volumes for next bar
                buyVolume = 0;
                sellVolume = 0;
            }
        }

        /// <summary>
        /// Gets array of values from a series for calculation purposes
        /// </summary>
        private double[] GetSeriesValues(ISeries<double> series, int period)
        {
            if (CurrentBar < period - 1)
                return new double[0];

            double[] values = new double[period];
            for (int i = 0; i < period; i++)
            {
                values[i] = series[i];
            }
            return values;
        }

        private void SetBackgroundColor(double correlation)
        {
            Brush backgroundColor;

            if (correlation < NegativeCorrelationThreshold)
            {
                // Green for negative correlation
                backgroundColor = Brushes.Green;
            }
            else if (Math.Abs(correlation) < WeakCorrelationThreshold)
            {
                // Light orange for weak correlation
                backgroundColor = Brushes.LightSalmon;
            }
            else if (correlation < WeakeningCorrelationThreshold)
            {
                // Dark orange for weakening correlation
                backgroundColor = Brushes.Orange;
            }
            else
            {
                // Red for normal/strong positive correlation
                backgroundColor = Brushes.LightCoral;
            }

            // Apply background color with transparency to indicator panel only
            BackBrush = new SolidColorBrush(((SolidColorBrush)backgroundColor).Color) { Opacity = 0.3 };
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "COFI Weighting", Order = 1, GroupName = "COFI Settings")]
        public WeightingType COFIWeighting { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1)]
        [Display(Name = "COFI Lambda", Order = 2, GroupName = "COFI Settings")]
        public double COFILambda { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "COFI Window Size", Order = 3, GroupName = "COFI Settings")]
        public int COFIWindowSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "COFI Reset Period", Order = 4, GroupName = "COFI Settings")]
        public int COFIResetPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Linear Regression Period", Order = 1, GroupName = "Divergence Settings")]
        public int LinearRegressionPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Correlation Period", Order = 2, GroupName = "Divergence Settings")]
        public int CorrelationPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smooth Divergence", Order = 3, GroupName = "Divergence Settings")]
        public bool SmoothDivergence { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Smoothing Period", Order = 4, GroupName = "Divergence Settings")]
        public int DivergenceSmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 0.0)]
        [Display(Name = "Negative Correlation Threshold", Order = 1, GroupName = "Correlation Thresholds")]
        public double NegativeCorrelationThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Weak Correlation Threshold", Order = 2, GroupName = "Correlation Thresholds")]
        public double WeakCorrelationThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Weakening Correlation Threshold", Order = 3, GroupName = "Correlation Thresholds")]
        public double WeakeningCorrelationThreshold { get; set; }

        [XmlIgnore]
        [Display(Name = "Divergence Positive", Order = 1, GroupName = "Appearance")]
        public Brush DivergencePositiveColor { get; set; }

        [Browsable(false)]
        public string DivergencePositiveColorSerializable
        {
            get { return Serialize.BrushToString(DivergencePositiveColor); }
            set { DivergencePositiveColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Divergence Negative", Order = 2, GroupName = "Appearance")]
        public Brush DivergenceNegativeColor { get; set; }

        [Browsable(false)]
        public string DivergenceNegativeColorSerializable
        {
            get { return Serialize.BrushToString(DivergenceNegativeColor); }
            set { DivergenceNegativeColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Divergence Neutral", Order = 3, GroupName = "Appearance")]
        public Brush DivergenceNeutralColor { get; set; }

        [Browsable(false)]
        public string DivergenceNeutralColorSerializable
        {
            get { return Serialize.BrushToString(DivergenceNeutralColor); }
            set { DivergenceNeutralColor = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private LambdaLinus.COFIDivergence[] cacheCOFIDivergence;
        public LambdaLinus.COFIDivergence COFIDivergence(WeightingType cOFIWeighting, double cOFILambda, int cOFIWindowSize, int cOFIResetPeriod, int linearRegressionPeriod, int correlationPeriod, bool smoothDivergence, int divergenceSmoothingPeriod, double negativeCorrelationThreshold, double weakCorrelationThreshold, double weakeningCorrelationThreshold)
        {
            return COFIDivergence(Input, cOFIWeighting, cOFILambda, cOFIWindowSize, cOFIResetPeriod, linearRegressionPeriod, correlationPeriod, smoothDivergence, divergenceSmoothingPeriod, negativeCorrelationThreshold, weakCorrelationThreshold, weakeningCorrelationThreshold);
        }

        public LambdaLinus.COFIDivergence COFIDivergence(ISeries<double> input, WeightingType cOFIWeighting, double cOFILambda, int cOFIWindowSize, int cOFIResetPeriod, int linearRegressionPeriod, int correlationPeriod, bool smoothDivergence, int divergenceSmoothingPeriod, double negativeCorrelationThreshold, double weakCorrelationThreshold, double weakeningCorrelationThreshold)
        {
            if (cacheCOFIDivergence != null)
                for (int idx = 0; idx < cacheCOFIDivergence.Length; idx++)
                    if (cacheCOFIDivergence[idx] != null && cacheCOFIDivergence[idx].COFIWeighting == cOFIWeighting && cacheCOFIDivergence[idx].COFILambda == cOFILambda && cacheCOFIDivergence[idx].COFIWindowSize == cOFIWindowSize && cacheCOFIDivergence[idx].COFIResetPeriod == cOFIResetPeriod && cacheCOFIDivergence[idx].LinearRegressionPeriod == linearRegressionPeriod && cacheCOFIDivergence[idx].CorrelationPeriod == correlationPeriod && cacheCOFIDivergence[idx].SmoothDivergence == smoothDivergence && cacheCOFIDivergence[idx].DivergenceSmoothingPeriod == divergenceSmoothingPeriod && cacheCOFIDivergence[idx].NegativeCorrelationThreshold == negativeCorrelationThreshold && cacheCOFIDivergence[idx].WeakCorrelationThreshold == weakCorrelationThreshold && cacheCOFIDivergence[idx].WeakeningCorrelationThreshold == weakeningCorrelationThreshold && cacheCOFIDivergence[idx].EqualsInput(input))
                        return cacheCOFIDivergence[idx];
            return CacheIndicator<LambdaLinus.COFIDivergence>(new LambdaLinus.COFIDivergence() { COFIWeighting = cOFIWeighting, COFILambda = cOFILambda, COFIWindowSize = cOFIWindowSize, COFIResetPeriod = cOFIResetPeriod, LinearRegressionPeriod = linearRegressionPeriod, CorrelationPeriod = correlationPeriod, SmoothDivergence = smoothDivergence, DivergenceSmoothingPeriod = divergenceSmoothingPeriod, NegativeCorrelationThreshold = negativeCorrelationThreshold, WeakCorrelationThreshold = weakCorrelationThreshold, WeakeningCorrelationThreshold = weakeningCorrelationThreshold }, input, ref cacheCOFIDivergence);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.LambdaLinus.COFIDivergence COFIDivergence(WeightingType cOFIWeighting, double cOFILambda, int cOFIWindowSize, int cOFIResetPeriod, int linearRegressionPeriod, int correlationPeriod, bool smoothDivergence, int divergenceSmoothingPeriod, double negativeCorrelationThreshold, double weakCorrelationThreshold, double weakeningCorrelationThreshold)
        {
            return indicator.COFIDivergence(Input, cOFIWeighting, cOFILambda, cOFIWindowSize, cOFIResetPeriod, linearRegressionPeriod, correlationPeriod, smoothDivergence, divergenceSmoothingPeriod, negativeCorrelationThreshold, weakCorrelationThreshold, weakeningCorrelationThreshold);
        }

        public Indicators.LambdaLinus.COFIDivergence COFIDivergence(ISeries<double> input, WeightingType cOFIWeighting, double cOFILambda, int cOFIWindowSize, int cOFIResetPeriod, int linearRegressionPeriod, int correlationPeriod, bool smoothDivergence, int divergenceSmoothingPeriod, double negativeCorrelationThreshold, double weakCorrelationThreshold, double weakeningCorrelationThreshold)
        {
            return indicator.COFIDivergence(input, cOFIWeighting, cOFILambda, cOFIWindowSize, cOFIResetPeriod, linearRegressionPeriod, correlationPeriod, smoothDivergence, divergenceSmoothingPeriod, negativeCorrelationThreshold, weakCorrelationThreshold, weakeningCorrelationThreshold);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.LambdaLinus.COFIDivergence COFIDivergence(WeightingType cOFIWeighting, double cOFILambda, int cOFIWindowSize, int cOFIResetPeriod, int linearRegressionPeriod, int correlationPeriod, bool smoothDivergence, int divergenceSmoothingPeriod, double negativeCorrelationThreshold, double weakCorrelationThreshold, double weakeningCorrelationThreshold)
        {
            return indicator.COFIDivergence(Input, cOFIWeighting, cOFILambda, cOFIWindowSize, cOFIResetPeriod, linearRegressionPeriod, correlationPeriod, smoothDivergence, divergenceSmoothingPeriod, negativeCorrelationThreshold, weakCorrelationThreshold, weakeningCorrelationThreshold);
        }

        public Indicators.LambdaLinus.COFIDivergence COFIDivergence(ISeries<double> input, WeightingType cOFIWeighting, double cOFILambda, int cOFIWindowSize, int cOFIResetPeriod, int linearRegressionPeriod, int correlationPeriod, bool smoothDivergence, int divergenceSmoothingPeriod, double negativeCorrelationThreshold, double weakCorrelationThreshold, double weakeningCorrelationThreshold)
        {
            return indicator.COFIDivergence(input, cOFIWeighting, cOFILambda, cOFIWindowSize, cOFIResetPeriod, linearRegressionPeriod, correlationPeriod, smoothDivergence, divergenceSmoothingPeriod, negativeCorrelationThreshold, weakCorrelationThreshold, weakeningCorrelationThreshold);
        }
    }
}

#endregion

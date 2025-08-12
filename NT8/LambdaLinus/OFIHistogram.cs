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
    public class OFIHistogram : Indicator
    {
        private double buyVolume;
        private double sellVolume;
        private double currentBid;
        private double currentAsk;
        private double lastTradePrice;
        private Series<double> ofiValues;
        private SMA sma;
        private EMA ema;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"    Order Flow Imbalance (OFI) Histogram Indicator

                This indicator calculates and displays the Order Flow Imbalance for each bar based on 
                tick-by-tick trade data. OFI measures the relative strength of buying versus selling 
                pressure within each bar period.

                KEY FEATURES:
                - Real-time OFI calculation using tick data with bid/ask analysis
                - Lee-Ready algorithm for trade classification (buyer vs seller initiated)
                - Optional moving average overlay (SMA or EMA)
                - Customizable threshold levels for signal identification

                CALCULATION METHOD:
                OFI = (Buy Volume - Sell Volume) / Total Volume
                - Values range from -1 to +1
                - Positive values indicate net buying pressure
                - Negative values indicate net selling pressure
                - Zero indicates balanced order flow

                TRADE CLASSIFICATION:
                - Trades above mid-price: classified as buys
                - Trades below mid-price: classified as sells
                - Trades at mid-price: uses tick rule (compares to previous trade prices)
                - Ambiguous trades: volume split 50/50 between buy and sell";

                Name = "OFIHistogram";
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
                MA_Type = MAType.EMA;
                MA_Length = 14;
                PositiveColor = Brushes.Green;
                NegativeColor = Brushes.Red;
                MAColor = Brushes.Orange;
                ThresholdLevel1 = 0.3;
                ThresholdLevel2 = 0.6;
                ThresholdColor1 = Brushes.DarkGray;
                ThresholdColor2 = Brushes.Gray;

                // Plots
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Bar, "OFI");
                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "MA");
                AddLine(Brushes.Gray, 0, "Zero");

                // Threshold lines
                AddLine(Brushes.DarkGray, 0.3, "Threshold1+");
                AddLine(Brushes.DarkGray, -0.3, "Threshold1-");
                AddLine(Brushes.Gray, 0.6, "Threshold2+");
                AddLine(Brushes.Gray, -0.6, "Threshold2-");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Data.BarsPeriodType.Tick, 1);
                AddDataSeries(Instrument.FullName, Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Bid);
                AddDataSeries(Instrument.FullName, Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Ask);
            }
            else if (State == State.DataLoaded)
            {
                ofiValues = new Series<double>(this);

                if (MA_Type == MAType.SMA)
                    sma = SMA(ofiValues, MA_Length);
                else if (MA_Type == MAType.EMA)
                    ema = EMA(ofiValues, MA_Length);

                // Update threshold lines with user parameters
                Lines[1].Value = ThresholdLevel1;
                Lines[2].Value = -ThresholdLevel1;
                Lines[3].Value = ThresholdLevel2;
                Lines[4].Value = -ThresholdLevel2;

                // Set line colors
                Lines[1].Brush = ThresholdColor1;
                Lines[2].Brush = ThresholdColor1;
                Lines[3].Brush = ThresholdColor2;
                Lines[4].Brush = ThresholdColor2;
            }
        }

        protected override void OnBarUpdate()
        {
            // Update bid/ask prices when their series update
            if (BarsInProgress == 2) // Bid series update
            {
                currentBid = Close[0];
                return;
            }
            else if (BarsInProgress == 3) // Ask series update
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
                    // Split volume if unclassified
                    buyVolume += tradeVolume * 0.5;
                    sellVolume += tradeVolume * 0.5;
                }

                lastTradePrice = tradePrice;
            }
            // Handle main timeframe bars
            else if (BarsInProgress == 0)
            {
                if (CurrentBar < 1) return;

                // Calculate OFI using utility
                double ofi = OrderFlowUtils.CalculateOFI(buyVolume, sellVolume);

                // Store OFI value
                ofiValues[0] = ofi;
                Value[0] = ofi;

                // Set bar color
                PlotBrushes[0][0] = ofi >= 0 ? PositiveColor : NegativeColor;

                // Calculate and plot MA if enabled
                if (MA_Type != MAType.None && CurrentBar >= MA_Length)
                {
                    if (MA_Type == MAType.SMA)
                        Values[1][0] = sma[0];
                    else if (MA_Type == MAType.EMA)
                        Values[1][0] = ema[0];

                    PlotBrushes[1][0] = MAColor;
                }

                // Reset volumes for next bar
                buyVolume = 0;
                sellVolume = 0;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 1, GroupName = "Moving Average")]
        public MAType MA_Type
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MA Length", Order = 2, GroupName = "Moving Average")]
        public int MA_Length
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Threshold Level 1", Order = 1, GroupName = "Thresholds")]
        public double ThresholdLevel1
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Threshold Level 2", Order = 2, GroupName = "Thresholds")]
        public double ThresholdLevel2
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Positive Color", Order = 1, GroupName = "Colors")]
        public Brush PositiveColor
        { get; set; }

        [Browsable(false)]
        public string PositiveColorSerializable
        {
            get { return Serialize.BrushToString(PositiveColor); }
            set { PositiveColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Negative Color", Order = 2, GroupName = "Colors")]
        public Brush NegativeColor
        { get; set; }

        [Browsable(false)]
        public string NegativeColorSerializable
        {
            get { return Serialize.BrushToString(NegativeColor); }
            set { NegativeColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "MA Color", Order = 3, GroupName = "Colors")]
        public Brush MAColor
        { get; set; }

        [Browsable(false)]
        public string MAColorSerializable
        {
            get { return Serialize.BrushToString(MAColor); }
            set { MAColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Threshold Color 1", Order = 4, GroupName = "Colors")]
        public Brush ThresholdColor1
        { get; set; }

        [Browsable(false)]
        public string ThresholdColor1Serializable
        {
            get { return Serialize.BrushToString(ThresholdColor1); }
            set { ThresholdColor1 = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Threshold Color 2", Order = 5, GroupName = "Colors")]
        public Brush ThresholdColor2
        { get; set; }

        [Browsable(false)]
        public string ThresholdColor2Serializable
        {
            get { return Serialize.BrushToString(ThresholdColor2); }
            set { ThresholdColor2 = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private LambdaLinus.OFIHistogram[] cacheOFIHistogram;
        public LambdaLinus.OFIHistogram OFIHistogram(MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
        {
            return OFIHistogram(Input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
        }

        public LambdaLinus.OFIHistogram OFIHistogram(ISeries<double> input, MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
        {
            if (cacheOFIHistogram != null)
                for (int idx = 0; idx < cacheOFIHistogram.Length; idx++)
                    if (cacheOFIHistogram[idx] != null && cacheOFIHistogram[idx].MA_Type == mA_Type && cacheOFIHistogram[idx].MA_Length == mA_Length && cacheOFIHistogram[idx].ThresholdLevel1 == thresholdLevel1 && cacheOFIHistogram[idx].ThresholdLevel2 == thresholdLevel2 && cacheOFIHistogram[idx].EqualsInput(input))
                        return cacheOFIHistogram[idx];
            return CacheIndicator<LambdaLinus.OFIHistogram>(new LambdaLinus.OFIHistogram() { MA_Type = mA_Type, MA_Length = mA_Length, ThresholdLevel1 = thresholdLevel1, ThresholdLevel2 = thresholdLevel2 }, input, ref cacheOFIHistogram);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.LambdaLinus.OFIHistogram OFIHistogram(MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
        {
            return indicator.OFIHistogram(Input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
        }

        public Indicators.LambdaLinus.OFIHistogram OFIHistogram(ISeries<double> input, MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
        {
            return indicator.OFIHistogram(input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.LambdaLinus.OFIHistogram OFIHistogram(MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
        {
            return indicator.OFIHistogram(Input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
        }

        public Indicators.LambdaLinus.OFIHistogram OFIHistogram(ISeries<double> input, MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
        {
            return indicator.OFIHistogram(input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
        }
    }
}

#endregion

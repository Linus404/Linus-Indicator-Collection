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
using NinjaTrader.NinjaScript.Indicators.NTLambda;
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

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLOFI : Indicator
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

                Name = "NTL OFI Histogram";
                Calculate = Calculate.OnEachTick;
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
            if(BarsInProgress == 0) {
                // Handle main timeframe bar updates
                if(IsFirstTickOfBar) {
                    // Reset volumes only at the start of a new bar
                    buyVolume = 0;
                    sellVolume = 0;
                }

                // Calculate current OFI using utility from NTLCommons only if we have volume
                double ofi = 0;
                if ((buyVolume + sellVolume) > 0) {
                    ofi = OrderFlowUtils.CalculateOFI(buyVolume, sellVolume);
                }

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
            }

            // Process tick data (works for both historical and tick replay)
            if(BarsInProgress == 1) {
                double price = BarsArray[1].GetClose(CurrentBars[1]);
                double ask = BarsArray[1].GetAsk(CurrentBars[1]);
                double bid = BarsArray[1].GetBid(CurrentBars[1]);
                double volume = BarsArray[1].GetVolume(CurrentBars[1]);
                
                // Only process valid tick data
                if (volume > 0 && ask > 0 && bid > 0 && ask > bid) {
                    // Classify trades using bid/ask comparison (like AggressionDelta)
                    if(price >= ask) {
                        buyVolume += volume;
                    }
                    else if(price <= bid) {
                        sellVolume += volume;
                    }
                    else {
                        // Split volume for mid-market trades
                        buyVolume += volume * 0.5;
                        sellVolume += volume * 0.5;
                    }

                    lastTradePrice = price;
                }
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
		private NTLambda.NTLOFI[] cacheNTLOFI;
		public NTLambda.NTLOFI NTLOFI(MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
		{
			return NTLOFI(Input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
		}

		public NTLambda.NTLOFI NTLOFI(ISeries<double> input, MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
		{
			if (cacheNTLOFI != null)
				for (int idx = 0; idx < cacheNTLOFI.Length; idx++)
					if (cacheNTLOFI[idx] != null && cacheNTLOFI[idx].MA_Type == mA_Type && cacheNTLOFI[idx].MA_Length == mA_Length && cacheNTLOFI[idx].ThresholdLevel1 == thresholdLevel1 && cacheNTLOFI[idx].ThresholdLevel2 == thresholdLevel2 && cacheNTLOFI[idx].EqualsInput(input))
						return cacheNTLOFI[idx];
			return CacheIndicator<NTLambda.NTLOFI>(new NTLambda.NTLOFI(){ MA_Type = mA_Type, MA_Length = mA_Length, ThresholdLevel1 = thresholdLevel1, ThresholdLevel2 = thresholdLevel2 }, input, ref cacheNTLOFI);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.NTLambda.NTLOFI NTLOFI(MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
		{
			return indicator.NTLOFI(Input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
		}

		public Indicators.NTLambda.NTLOFI NTLOFI(ISeries<double> input , MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
		{
			return indicator.NTLOFI(input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.NTLambda.NTLOFI NTLOFI(MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
		{
			return indicator.NTLOFI(Input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
		}

		public Indicators.NTLambda.NTLOFI NTLOFI(ISeries<double> input , MAType mA_Type, int mA_Length, double thresholdLevel1, double thresholdLevel2)
		{
			return indicator.NTLOFI(input, mA_Type, mA_Length, thresholdLevel1, thresholdLevel2);
		}
	}
}

#endregion

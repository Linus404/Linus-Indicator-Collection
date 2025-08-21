using System;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class MoldyBars : Indicator
    {
        private double lastAussenstabHigh = double.MinValue;
        private double lastAussenstabLow = double.MaxValue;
        private int lastAussenstabIndex = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Identifies and colors Außenstäbe, Inside Bars, and Outside Bars with arrows.";
                Name = "NTL MoldyBars Clean";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;

                // Default colors
                BullishAussenstabColor = Brushes.ForestGreen;
                BearishAussenstabColor = Brushes.Crimson;
                BullishInsideBarColor = Brushes.PaleGreen;
                BearishInsideBarColor = Brushes.PaleVioletRed;
                BullishOutsideBarColor = Brushes.Green;
                BearishOutsideBarColor = Brushes.Red;
                DefaultBullishColor = Brushes.Green;
                DefaultBearishColor = Brushes.Red;
                ArrowColor = Brushes.Blue;

                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Bar, "BarTypePlot");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                lastAussenstabHigh = High[0];
                lastAussenstabLow = Low[0];
                lastAussenstabIndex = 0;
                return;
            }

            bool isAussenstab = CheckForAussenstab();
            if (!isAussenstab)
            {
                bool isInsideBar = CheckForInsideBar();
                if (!isInsideBar)
                {
                    bool isOutsideBar = CheckForOutsideBar();
                    if (!isOutsideBar)
                    {
                        // Default coloring for non-special bars
                        SetDefaultBarColor();
                    }
                }
            }
        }

        private bool CheckForAussenstab()
        {
            if (Close[0] > lastAussenstabHigh)
            {
                SetBarColor(BullishAussenstabColor, true);
                UpdateAussenstab();
                return true;
            }
            else if (Close[0] < lastAussenstabLow)
            {
                SetBarColor(BearishAussenstabColor, true);
                UpdateAussenstab();
                return true;
            }
            return false;
        }

        private void UpdateAussenstab()
        {
            lastAussenstabHigh = High[0];
            lastAussenstabLow = Low[0];
            lastAussenstabIndex = CurrentBar;

            // Clear coloring for bars between last Außenstab and current
            for (int i = lastAussenstabIndex + 1; i < CurrentBar; i++)
            {
                SetDefaultBarColor(CurrentBar - i);
            }
        }

        private bool CheckForInsideBar()
        {
            if (High[0] <= High[1] && Low[0] >= Low[1])
            {
                if (Close[0] > Close[1])
                    SetBarColor(BullishInsideBarColor, true);
                else
                    SetBarColor(BearishInsideBarColor, true);
                return true;
            }
            return false;
        }

        private bool CheckForOutsideBar()
        {
            if (High[0] > High[1] && Low[0] < Low[1])
            {
                if (Close[0] > Close[1])
                    SetBarColor(BullishOutsideBarColor, true);
                else
                    SetBarColor(BearishOutsideBarColor, true);
                return true;
            }
            return false;
        }

        private void SetDefaultBarColor(int barsAgo = 0)
        {
            Brush outlineColor = Close[barsAgo] >= Open[barsAgo] ? DefaultBullishColor : DefaultBearishColor;
            SetBarColor(outlineColor, false, barsAgo);
        }

        private void SetBarColor(Brush color, bool isFilled, int barsAgo = 0)
        {
            if (isFilled)
            {
                BarBrushes[barsAgo] = color;
                CandleOutlineBrushes[barsAgo] = color;
            }
            else
            {
                BarBrushes[barsAgo] = Brushes.Transparent;
                CandleOutlineBrushes[barsAgo] = color;
            }
        }

        #region Properties
        [XmlIgnore]
        [Display(Name = "Bullish Außenstab Color", Description = "Color for bullish Außenstäbe", Order = 1, GroupName = "Colors")]
        public Brush BullishAussenstabColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Bearish Außenstab Color", Description = "Color for bearish Außenstäbe", Order = 2, GroupName = "Colors")]
        public Brush BearishAussenstabColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Bullish Inside Bar Color", Description = "Color for bullish Inside Bars", Order = 3, GroupName = "Colors")]
        public Brush BullishInsideBarColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Bearish Inside Bar Color", Description = "Color for bearish Inside Bars", Order = 4, GroupName = "Colors")]
        public Brush BearishInsideBarColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Bullish Outside Bar Color", Description = "Color for bullish Outside Bars", Order = 5, GroupName = "Colors")]
        public Brush BullishOutsideBarColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Bearish Outside Bar Color", Description = "Color for bearish Outside Bars", Order = 6, GroupName = "Colors")]
        public Brush BearishOutsideBarColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Default Bullish Color", Description = "Outline color for bullish non-special bars", Order = 7, GroupName = "Colors")]
        public Brush DefaultBullishColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Default Bearish Color", Description = "Outline color for bearish non-special bars", Order = 8, GroupName = "Colors")]
        public Brush DefaultBearishColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Arrow Color", Description = "Color for the arrows on Inside and Outside Bars", Order = 9, GroupName = "Colors")]
        public Brush ArrowColor { get; set; }

        // Serializable color properties
        [Browsable(false)]
        public string BullishAussenstabColorSerializable
        {
            get { return Serialize.BrushToString(BullishAussenstabColor); }
            set { BullishAussenstabColor = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        public string BearishAussenstabColorSerializable
        {
            get { return Serialize.BrushToString(BearishAussenstabColor); }
            set { BearishAussenstabColor = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        public string ArrowColorSerializable
        {
            get { return Serialize.BrushToString(ArrowColor); }
            set { ArrowColor = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Moldys.MoldyBars[] cacheMoldyBars;
		public Moldys.MoldyBars MoldyBars()
		{
			return MoldyBars(Input);
		}

		public Moldys.MoldyBars MoldyBars(ISeries<double> input)
		{
			if (cacheMoldyBars != null)
				for (int idx = 0; idx < cacheMoldyBars.Length; idx++)
					if (cacheMoldyBars[idx] != null &&  cacheMoldyBars[idx].EqualsInput(input))
						return cacheMoldyBars[idx];
			return CacheIndicator<Moldys.MoldyBars>(new Moldys.MoldyBars(), input, ref cacheMoldyBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Moldys.MoldyBars MoldyBars()
		{
			return indicator.MoldyBars(Input);
		}

		public Indicators.Moldys.MoldyBars MoldyBars(ISeries<double> input )
		{
			return indicator.MoldyBars(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Moldys.MoldyBars MoldyBars()
		{
			return indicator.MoldyBars(Input);
		}

		public Indicators.Moldys.MoldyBars MoldyBars(ISeries<double> input )
		{
			return indicator.MoldyBars(input);
		}
	}
}

#endregion

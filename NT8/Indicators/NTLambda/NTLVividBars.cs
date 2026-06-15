#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLVividBars : Indicator
    {
        private double lastAussenstabHigh = double.MinValue;
        private double lastAussenstabLow = double.MaxValue;
        private int lastAussenstabIndex = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL VividBars";
                Description = "Colors Aussenstab, inside-bar, and outside-bar candles with separate bullish and bearish palettes.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;

                HighlightBars = true;
                ShowSignalDots = false;
                SignalDotOffsetTicks = 2;

                BullishAussenstabColor = Brushes.MediumSpringGreen;
                BearishAussenstabColor = Brushes.Orange;
                BullishInsideBarColor = Brushes.PaleGreen;
                BearishInsideBarColor = Brushes.LightCoral;
                BullishOutsideBarColor = Brushes.LimeGreen;
                BearishOutsideBarColor = Brushes.Crimson;

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
                Values[0][0] = 0;
                return;
            }

            if (CheckForAussenstab())
                return;

            if (CheckForInsideBar())
                return;

            if (CheckForOutsideBar())
                return;

            Values[0][0] = 0;
            SetDefaultBarColor();
        }

        private bool CheckForAussenstab()
        {
            if (Close[0] > lastAussenstabHigh)
            {
                Values[0][0] = 3;
                SetBarColor(BullishAussenstabColor, true);
                UpdateAussenstab();
                return true;
            }

            if (Close[0] < lastAussenstabLow)
            {
                Values[0][0] = -3;
                SetBarColor(BearishAussenstabColor, true);
                UpdateAussenstab();
                return true;
            }

            return false;
        }

        private void UpdateAussenstab()
        {
            int previousAussenstabIndex = lastAussenstabIndex;
            lastAussenstabHigh = High[0];
            lastAussenstabLow = Low[0];
            lastAussenstabIndex = CurrentBar;

            if (previousAussenstabIndex < 0 || previousAussenstabIndex >= CurrentBar - 1)
                return;

            for (int absoluteBar = previousAussenstabIndex + 1; absoluteBar < CurrentBar; absoluteBar++)
            {
                int barsAgo = CurrentBar - absoluteBar;
                SetDefaultBarColor(barsAgo);
            }
        }

        private bool CheckForInsideBar()
        {
            if (High[0] <= High[1] && Low[0] >= Low[1])
            {
                if (Close[0] > Close[1])
                {
                    Values[0][0] = 1;
                    SetBarColor(BullishInsideBarColor, true);
                }
                else
                {
                    Values[0][0] = -1;
                    SetBarColor(BearishInsideBarColor, true);
                }

                return true;
            }

            return false;
        }

        private bool CheckForOutsideBar()
        {
            if (High[0] > High[1] && Low[0] < Low[1])
            {
                if (Close[0] > Close[1])
                {
                    Values[0][0] = 2;
                    SetBarColor(BullishOutsideBarColor, true);
                }
                else
                {
                    Values[0][0] = -2;
                    SetBarColor(BearishOutsideBarColor, true);
                }

                return true;
            }

            return false;
        }

        private void SetDefaultBarColor()
        {
            SetDefaultBarColor(0);
        }

        private void SetDefaultBarColor(int barsAgo)
        {
            BarBrushes[barsAgo] = null;
            CandleOutlineBrushes[barsAgo] = null;
        }

        private void SetBarColor(Brush color, bool isFilled)
        {
            SetBarColor(color, isFilled, 0);
        }

        private void SetBarColor(Brush color, bool isFilled, int barsAgo)
        {
            if (!HighlightBars)
                return;

            BarBrushes[barsAgo] = color;
            CandleOutlineBrushes[barsAgo] = color;
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Highlight bars", GroupName = "Visual", Order = 0)]
        public bool HighlightBars { get; set; }

        [Browsable(false)]
        [Display(Name = "Show signal dots", GroupName = "Visual", Order = 1)]
        public bool ShowSignalDots { get; set; }

        [Browsable(false)]
        [Range(0, 20)]
        [Display(Name = "Signal dot offset (ticks)", GroupName = "Visual", Order = 2)]
        public int SignalDotOffsetTicks { get; set; }

        [XmlIgnore]
        [Display(Name = "Bullish Außenstab Color", Order = 1, GroupName = "Colors")]
        public Brush BullishAussenstabColor { get; set; }

        [Browsable(false)]
        public string BullishAussenstabColorSerializable
        {
            get { return Serialize.BrushToString(BullishAussenstabColor); }
            set { BullishAussenstabColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bearish Außenstab Color", Order = 2, GroupName = "Colors")]
        public Brush BearishAussenstabColor { get; set; }

        [Browsable(false)]
        public string BearishAussenstabColorSerializable
        {
            get { return Serialize.BrushToString(BearishAussenstabColor); }
            set { BearishAussenstabColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bullish Inside Bar Color", Order = 3, GroupName = "Colors")]
        public Brush BullishInsideBarColor { get; set; }

        [Browsable(false)]
        public string BullishInsideBarColorSerializable
        {
            get { return Serialize.BrushToString(BullishInsideBarColor); }
            set { BullishInsideBarColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bearish Inside Bar Color", Order = 4, GroupName = "Colors")]
        public Brush BearishInsideBarColor { get; set; }

        [Browsable(false)]
        public string BearishInsideBarColorSerializable
        {
            get { return Serialize.BrushToString(BearishInsideBarColor); }
            set { BearishInsideBarColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bullish Outside Bar Color", Order = 5, GroupName = "Colors")]
        public Brush BullishOutsideBarColor { get; set; }

        [Browsable(false)]
        public string BullishOutsideBarColorSerializable
        {
            get { return Serialize.BrushToString(BullishOutsideBarColor); }
            set { BullishOutsideBarColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bearish Outside Bar Color", Order = 6, GroupName = "Colors")]
        public Brush BearishOutsideBarColor { get; set; }

        [Browsable(false)]
        public string BearishOutsideBarColorSerializable
        {
            get { return Serialize.BrushToString(BearishOutsideBarColor); }
            set { BearishOutsideBarColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Browsable(false)]
        public Brush InsideBrush
        {
            get { return BullishInsideBarColor; }
            set { BullishInsideBarColor = value; }
        }

        [Browsable(false)]
        public string InsideBrushSerializable
        {
            get { return Serialize.BrushToString(InsideBrush); }
            set { InsideBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Browsable(false)]
        public Brush OutsideBrush
        {
            get { return BullishOutsideBarColor; }
            set { BullishOutsideBarColor = value; }
        }

        [Browsable(false)]
        public string OutsideBrushSerializable
        {
            get { return Serialize.BrushToString(OutsideBrush); }
            set { OutsideBrush = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

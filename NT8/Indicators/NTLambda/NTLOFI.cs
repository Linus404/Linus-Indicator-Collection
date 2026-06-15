#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLOFI : Indicator
    {
        private double buyVolume;
        private double sellVolume;
        private double lastTradePrice = double.NaN;
        private int activePrimaryBar = -1;
        private Series<double> ofiValues;
        private SMA sma;
        private EMA ema;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL OFI Histogram";
                Description = "Per-bar order-flow imbalance histogram with moving-average, threshold, and color controls.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                MA_Type = MAType.EMA;
                MA_Length = 14;
                ThresholdLevel1 = 0.3;
                ThresholdLevel2 = 0.6;
                PositiveColor = Brushes.Green;
                NegativeColor = Brushes.Red;
                MAColor = Brushes.Orange;
                ThresholdColor1 = Brushes.DarkGray;
                ThresholdColor2 = Brushes.Gray;

                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Bar, "OFI");
                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "MA");
                AddLine(Brushes.Gray, 0, "Zero");
                AddLine(Brushes.DarkGray, 0.3, "Threshold1+");
                AddLine(Brushes.DarkGray, -0.3, "Threshold1-");
                AddLine(Brushes.Gray, 0.6, "Threshold2+");
                AddLine(Brushes.Gray, -0.6, "Threshold2-");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                ofiValues = new Series<double>(this);
                if (MA_Type == MAType.SMA)
                    sma = SMA(ofiValues, MA_Length);
                else if (MA_Type == MAType.EMA)
                    ema = EMA(ofiValues, MA_Length);

                Lines[1].Value = ThresholdLevel1;
                Lines[2].Value = -ThresholdLevel1;
                Lines[3].Value = ThresholdLevel2;
                Lines[4].Value = -ThresholdLevel2;
                Lines[1].Brush = ThresholdColor1;
                Lines[2].Brush = ThresholdColor1;
                Lines[3].Brush = ThresholdColor2;
                Lines[4].Brush = ThresholdColor2;
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

            if (BarsInProgress != 0)
                return;

            if (activePrimaryBar < CurrentBar)
                ResetBar(CurrentBar);

            double totalVolume = buyVolume + sellVolume;
            double ofi = totalVolume > 0 ? NTLCore.Ofi(sellVolume, buyVolume) : 0;
            ofiValues[0] = ofi;
            Values[0][0] = ofi;
            PlotBrushes[0][0] = ofi >= 0 ? PositiveColor : NegativeColor;

            if (MA_Type != MAType.None && CurrentBar >= MA_Length)
            {
                if (MA_Type == MAType.SMA && sma != null)
                    Values[1][0] = sma[0];
                else if (MA_Type == MAType.EMA && ema != null)
                    Values[1][0] = ema[0];
                PlotBrushes[1][0] = MAColor;
            }
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume))
                return;
            if (activePrimaryBar != primaryBar)
                ResetBar(primaryBar);

            if (ask > 0 && bid > 0 && ask > bid)
            {
                if (price >= ask)
                    buyVolume += volume;
                else if (price <= bid)
                    sellVolume += volume;
                else if (!double.IsNaN(lastTradePrice) && price > lastTradePrice)
                    buyVolume += volume;
                else if (!double.IsNaN(lastTradePrice) && price < lastTradePrice)
                    sellVolume += volume;
                else
                {
                    buyVolume += volume * 0.5;
                    sellVolume += volume * 0.5;
                }
            }
            else
            {
                bool isAsk = NTLCore.IsAskTrade(price, bid, ask, lastTradePrice);
                if (isAsk) buyVolume += volume; else sellVolume += volume;
            }
            lastTradePrice = price;
        }

        private void ResetBar(int primaryBar)
        {
            activePrimaryBar = primaryBar;
            buyVolume = 0;
            sellVolume = 0;
        }

        private bool TryGetTick(out int primaryBar, out double price, out double bid, out double ask, out double volume)
        {
            primaryBar = -1; price = bid = ask = volume = 0;
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

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 1, GroupName = "Moving Average")]
        public MAType MA_Type { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MA Length", Order = 2, GroupName = "Moving Average")]
        public int MA_Length { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Threshold Level 1", Order = 1, GroupName = "Thresholds")]
        public double ThresholdLevel1 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Threshold Level 2", Order = 2, GroupName = "Thresholds")]
        public double ThresholdLevel2 { get; set; }

        [XmlIgnore]
        [Display(Name = "Positive Color", Order = 1, GroupName = "Colors")]
        public Brush PositiveColor { get; set; }

        [Browsable(false)]
        public string PositiveColorSerializable
        {
            get { return Serialize.BrushToString(PositiveColor); }
            set { PositiveColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Negative Color", Order = 2, GroupName = "Colors")]
        public Brush NegativeColor { get; set; }

        [Browsable(false)]
        public string NegativeColorSerializable
        {
            get { return Serialize.BrushToString(NegativeColor); }
            set { NegativeColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "MA Color", Order = 3, GroupName = "Colors")]
        public Brush MAColor { get; set; }

        [Browsable(false)]
        public string MAColorSerializable
        {
            get { return Serialize.BrushToString(MAColor); }
            set { MAColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Threshold Color 1", Order = 4, GroupName = "Colors")]
        public Brush ThresholdColor1 { get; set; }

        [Browsable(false)]
        public string ThresholdColor1Serializable
        {
            get { return Serialize.BrushToString(ThresholdColor1); }
            set { ThresholdColor1 = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Threshold Color 2", Order = 5, GroupName = "Colors")]
        public Brush ThresholdColor2 { get; set; }

        [Browsable(false)]
        public string ThresholdColor2Serializable
        {
            get { return Serialize.BrushToString(ThresholdColor2); }
            set { ThresholdColor2 = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

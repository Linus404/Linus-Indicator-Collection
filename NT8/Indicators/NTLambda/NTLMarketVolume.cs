#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLMarketVolume : Indicator
    {
        private readonly Dictionary<int, double> volumeByBar = new Dictionary<int, double>();
        private readonly Queue<double> maWindow = new Queue<double>();
        private double ema = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Market Volume";
                Description = "Per-bar market volume histogram.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DrawHorizontalGridLines = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                MovingAverageType = MAType.SMA;
                MovingAverageLength = 20;
                UpColor = WpfBrushes.LimeGreen;
                DownColor = WpfBrushes.IndianRed;
                MovingAverageColor = WpfBrushes.Goldenrod;
                AddPlot(new Stroke(WpfBrushes.Gray, 2), PlotStyle.Bar, "Volume");
                AddPlot(new Stroke(WpfBrushes.Goldenrod, 2), PlotStyle.Line, "Volume MA");
                AddLine(WpfBrushes.DimGray, 0, "Zero");
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
                int primaryBar;
                double volume;
                if (TryGetTick(out primaryBar, out volume))
                {
                    double current;
                    volumeByBar.TryGetValue(primaryBar, out current);
                    volumeByBar[primaryBar] = current + volume;
                    if (primaryBar == CurrentBars[0] && CurrentBars[0] >= 0)
                        Values[0][0] = volumeByBar[primaryBar];
                }
                return;
            }
            if (BarsInProgress != 0) return;

            double barVolume;
            barVolume = volumeByBar.TryGetValue(CurrentBar, out barVolume) ? barVolume : Volume[0];
            Values[0][0] = barVolume;
            Values[1][0] = CalculateMovingAverage(barVolume);
            PlotBrushes[0][0] = Close[0] >= Open[0] ? UpColor : DownColor;
            PlotBrushes[1][0] = MovingAverageColor;

            foreach (int stale in new List<int>(volumeByBar.Keys))
                if (stale < CurrentBar - 5000) volumeByBar.Remove(stale);
        }

        private double CalculateMovingAverage(double value)
        {
            if (MovingAverageType == MAType.None)
                return double.NaN;
            if (MovingAverageType == MAType.EMA)
            {
                ema = NTLCore.Ema(ema, value, MovingAverageLength);
                return ema;
            }
            return NTLCore.Sma(maWindow, value, MovingAverageLength);
        }

        private bool TryGetTick(out int primaryBar, out double volume)
        {
            primaryBar = -1;
            volume = 0;
            if (CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[0] < 0 || CurrentBars[1] < 0) return false;
            int tickIndex = CurrentBars[1];
            primaryBar = BarsArray[0].GetBar(BarsArray[1].GetTime(tickIndex));
            if (primaryBar < 0) return false;
            volume = BarsArray[1].GetVolume(tickIndex);
            return volume > 0;
        }

        [NinjaScriptProperty]
        [Display(Name = "Moving average type", GroupName = "Moving Average", Order = 0)]
        public MAType MovingAverageType { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Moving average length", GroupName = "Moving Average", Order = 1)]
        public int MovingAverageLength { get; set; }

        [System.Xml.Serialization.XmlIgnore]
        [Display(Name = "Up color", GroupName = "Colors", Order = 0)]
        public System.Windows.Media.Brush UpColor { get; set; }

        [System.ComponentModel.Browsable(false)]
        public string UpColorSerializable { get { return Serialize.BrushToString(UpColor); } set { UpColor = Serialize.StringToBrush(value); } }

        [System.Xml.Serialization.XmlIgnore]
        [Display(Name = "Down color", GroupName = "Colors", Order = 1)]
        public System.Windows.Media.Brush DownColor { get; set; }

        [System.ComponentModel.Browsable(false)]
        public string DownColorSerializable { get { return Serialize.BrushToString(DownColor); } set { DownColor = Serialize.StringToBrush(value); } }

        [System.Xml.Serialization.XmlIgnore]
        [Display(Name = "Moving average color", GroupName = "Colors", Order = 2)]
        public System.Windows.Media.Brush MovingAverageColor { get; set; }

        [System.ComponentModel.Browsable(false)]
        public string MovingAverageColorSerializable { get { return Serialize.BrushToString(MovingAverageColor); } set { MovingAverageColor = Serialize.StringToBrush(value); } }
    }
}

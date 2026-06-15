#region Using declarations
using System;
using System.Collections.Generic;
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
    public class NTLCOFI : Indicator
    {
        private double buyVolume;
        private double sellVolume;
        private double lastTradePrice = double.NaN;
        private readonly List<double> ofiHistory = new List<double>();
        private readonly List<double> volumeHistory = new List<double>();
        private int activePrimaryBar = -1;
        private int resetCounter;
        private Series<double> cofiValues;
        private SMA sma1;
        private EMA ema1;
        private SMA sma2;
        private EMA ema2;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL COFI";
                Description = "Cumulative order-flow imbalance with weighting, reset, moving-average, and difference-plot controls.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                Weighting = WeightingType.Exponential;
                Lambda = 0.05;
                WindowSize = 100;
                ResetPeriod = 0;
                ResetMode = NTLResetPeriod.NoReset;
                MA1_Type = MAType.EMA;
                MA1_Length = 12;
                MA2_Type = MAType.EMA;
                MA2_Length = 26;
                ShowMADifference = true;
                LineColor = Brushes.Blue;
                LineWidth = 2;
                MA1Color = Brushes.Orange;
                MA2Color = Brushes.Red;
                MADiffPositiveColor = Brushes.Green;
                MADiffNegativeColor = Brushes.Red;

                AddPlot(new Stroke(LineColor, LineWidth), PlotStyle.Line, "COFI");
                AddPlot(new Stroke(Brushes.Orange, 1), PlotStyle.Line, "MA1");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "MA2");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Bar, "MADiff");
                AddLine(Brushes.Gray, 0, "Zero");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                cofiValues = new Series<double>(this);
                if (MA1_Type == MAType.SMA)
                    sma1 = SMA(cofiValues, MA1_Length);
                else if (MA1_Type == MAType.EMA)
                    ema1 = EMA(cofiValues, MA1_Length);

                if (MA2_Type == MAType.SMA)
                    sma2 = SMA(cofiValues, MA2_Length);
                else if (MA2_Type == MAType.EMA)
                    ema2 = EMA(cofiValues, MA2_Length);

                ApplyPlotAppearance();
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
                FinalizeAndStartBar(CurrentBar);

            double cofi = CalculateWeightedOfi();
            cofiValues[0] = cofi;
            Values[0][0] = cofi;
            PlotBrushes[0][0] = LineColor;

            double ma1Value = 0;
            if (MA1_Type != MAType.None && CurrentBar >= MA1_Length)
            {
                if (MA1_Type == MAType.SMA && sma1 != null)
                    ma1Value = sma1[0];
                else if (MA1_Type == MAType.EMA && ema1 != null)
                    ma1Value = ema1[0];
                Values[1][0] = ma1Value;
                PlotBrushes[1][0] = MA1Color;
            }

            double ma2Value = 0;
            if (MA2_Type != MAType.None && CurrentBar >= MA2_Length)
            {
                if (MA2_Type == MAType.SMA && sma2 != null)
                    ma2Value = sma2[0];
                else if (MA2_Type == MAType.EMA && ema2 != null)
                    ma2Value = ema2[0];
                Values[2][0] = ma2Value;
                PlotBrushes[2][0] = MA2Color;
            }

            if (ShowMADifference && MA1_Type != MAType.None && MA2_Type != MAType.None && CurrentBar >= Math.Max(MA1_Length, MA2_Length))
            {
                double maDiff = ma1Value - ma2Value;
                Values[3][0] = maDiff;
                PlotBrushes[3][0] = maDiff >= 0 ? MADiffPositiveColor : MADiffNegativeColor;
            }
            else
            {
                Values[3][0] = 0;
                PlotBrushes[3][0] = Brushes.Transparent;
            }
        }

        private void ApplyPlotAppearance()
        {
            if (Plots == null || Plots.Length < 4)
                return;
            Plots[0].Brush = LineColor;
            Plots[0].Width = LineWidth;
            Plots[1].Brush = MA1Color;
            Plots[2].Brush = MA2Color;
        }
        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume))
                return;
            if (activePrimaryBar != primaryBar)
                FinalizeAndStartBar(primaryBar);

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

        private void FinalizeAndStartBar(int primaryBar)
        {
            if (activePrimaryBar >= 0)
            {
                double total = buyVolume + sellVolume;
                if (total > 0 || ofiHistory.Count == 0)
                {
                    ofiHistory.Add(NTLCore.Ofi(sellVolume, buyVolume));
                    volumeHistory.Add(total);
                    resetCounter++;
                }
            }

            activePrimaryBar = primaryBar;
            if (ShouldResetAt(primaryBar))
            {
                ofiHistory.Clear();
                volumeHistory.Clear();
                resetCounter = 0;
            }

            while (ResetPeriod > 0 && ofiHistory.Count > ResetPeriod)
            {
                ofiHistory.RemoveAt(0);
                volumeHistory.RemoveAt(0);
            }

            buyVolume = 0;
            sellVolume = 0;
        }

        private bool ShouldResetAt(int primaryBar)
        {
            if (primaryBar == 0)
                return true;
            if (ResetMode == NTLResetPeriod.NoReset)
                return false;
            DateTime current = BarsArray[0].GetTime(primaryBar);
            DateTime previous = primaryBar > 0 ? BarsArray[0].GetTime(primaryBar - 1) : current;
            if (ResetMode == NTLResetPeriod.Session)
                return BarsArray[0].IsFirstBarOfSessionByIndex(primaryBar);
            if (ResetMode == NTLResetPeriod.Daily)
                return current.Date != previous.Date;
            return current.Date.AddDays(-(int)current.DayOfWeek) != previous.Date.AddDays(-(int)previous.DayOfWeek);
        }

        private double CalculateWeightedOfi()
        {
            if (ofiHistory.Count == 0)
                return 0;

            double result = 0;
            int t = ofiHistory.Count - 1;
            switch (Weighting)
            {
                case WeightingType.Cumulative:
                    for (int i = 0; i <= t; i++)
                        result += ofiHistory[i] * volumeHistory[i];
                    break;
                case WeightingType.Linear:
                    int linearLookback = Math.Min(t, WindowSize);
                    int linearStart = Math.Max(0, t - linearLookback);
                    for (int i = linearStart; i <= t; i++)
                    {
                        double weight = LinearWeight(t - i, WindowSize);
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
                default:
                    int maxLookback = CalculateEffectiveLookback(Lambda);
                    int lookback = Math.Min(t, Math.Min(maxLookback, 500));
                    int start = Math.Max(0, t - lookback);
                    for (int i = start; i <= t; i++)
                    {
                        double weight = Math.Exp(-Math.Max(0.0001, Lambda) * (t - i));
                        result += weight * ofiHistory[i] * volumeHistory[i];
                    }
                    break;
            }
            return result;
        }

        private int CalculateEffectiveLookback(double lambda)
        {
            return Math.Max(1, (int)Math.Ceiling(5.0 / Math.Max(0.0001, lambda)));
        }

        private double LinearWeight(int age, int windowSize)
        {
            if (windowSize <= 0 || age >= windowSize)
                return 0;
            return (windowSize - age) / (double)windowSize;
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
        [Display(Name = "Reset Mode", Order = 5, GroupName = "Calculation")]
        public NTLResetPeriod ResetMode { get; set; }

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

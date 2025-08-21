/*
 * NTLCVD.cs
 * Copyright (c) 2025 https://github.com/Linus404
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
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
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLCVD : Indicator
    {
        private double buys;
        private double sells;
        private double cumulativeDelta;
        private DateTime lastResetTime;
        private int lastResetBar;
        private double barOpenCVD;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"NTLambda Cumulative Volume Delta";
                Name = "NTL CVD";
                Calculate = Calculate.OnEachTick;
                DrawOnPricePanel = false;
                IsOverlay = false;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                PositiveBrush = Brushes.Green;
                NegativeBrush = Brushes.Red;
                ResetPeriod = ResetPeriodType.NoReset;
            }
            else if (State == State.Configure)
            {
                AddLine(Brushes.Gray, 0, "Zero Line");
                var highBrush = new SolidColorBrush(Color.FromArgb(1, 0, 255, 0));
                var lowBrush = new SolidColorBrush(Color.FromArgb(1, 255, 0, 0));
                
                AddPlot(highBrush, "CVD_High");
                AddPlot(lowBrush, "CVD_Low");
                
                Plots[0].PlotStyle = PlotStyle.Line;
                Plots[0].Width = 1;
                Plots[1].PlotStyle = PlotStyle.Line;
                Plots[1].Width = 1;
                
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                cumulativeDelta = 0;
                lastResetTime = DateTime.MinValue;
                lastResetBar = -1;
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
        }

        private bool ShouldReset()
        {
            if (ResetPeriod == ResetPeriodType.NoReset)
                return false;

            DateTime currentTime = Time[0];

            switch (ResetPeriod)
            {
                case ResetPeriodType.Daily:
                    return lastResetTime.Date != currentTime.Date;

                case ResetPeriodType.Weekly:
                    DateTime startOfWeek = currentTime.AddDays(-(int)currentTime.DayOfWeek);
                    DateTime lastStartOfWeek = lastResetTime.AddDays(-(int)lastResetTime.DayOfWeek);
                    return startOfWeek.Date != lastStartOfWeek.Date;

                case ResetPeriodType.Session:
                    return Bars.IsFirstBarOfSession;

                default:
                    return false;
            }
        }

        private Dictionary<int, double> cvdOpenValues = new Dictionary<int, double>();
        private Dictionary<int, double> cvdCloseValues = new Dictionary<int, double>();


        protected override void OnBarUpdate()
        {

            if (BarsInProgress == 0)
            {
                // Handle main timeframe bar processing (works for historical, real-time, and tick replay)
                bool isBarComplete = State == State.Historical || IsTickReplays[0];
                
                if (CurrentBar == 0)
                {
                    barOpenCVD = 0;
                    cumulativeDelta = 0;
                }
                else if (IsFirstTickOfBar || isBarComplete)
                {
                    // Check for reset conditions
                    if (ShouldReset())
                    {
                        cumulativeDelta = 0;
                        barOpenCVD = 0;
                        lastResetTime = Time[0];
                        lastResetBar = CurrentBar;
                    }
                    else
                    {
                        // Set bar open to previous bar's close
                        if (cvdCloseValues.ContainsKey(CurrentBar - 1))
                        {
                            barOpenCVD = cvdCloseValues[CurrentBar - 1];
                            if (isBarComplete) cumulativeDelta = barOpenCVD;
                        }
                        else
                        {
                            barOpenCVD = cumulativeDelta;
                        }
                    }

                    // Reset tick volumes for new bar
                    if (IsFirstTickOfBar && !isBarComplete)
                    {
                        buys = 0;
                        sells = 0;
                    }
                }

                // Calculate current values
                double currentDelta = buys - sells;
                double currentCVD;
                
                if (isBarComplete)
                {
                    // For completed bars, add delta to cumulative
                    cumulativeDelta += currentDelta;
                    currentCVD = cumulativeDelta;
                    
                    // Reset for next bar
                    buys = 0;
                    sells = 0;
                }
                else
                {
                    // For real-time, show running total
                    currentCVD = cumulativeDelta + currentDelta;
                }

                // Store values and update plots
                cvdOpenValues[CurrentBar] = barOpenCVD;
                cvdCloseValues[CurrentBar] = currentCVD;

                Values[0][0] = Math.Max(barOpenCVD, currentCVD);
                Values[1][0] = Math.Min(barOpenCVD, currentCVD);
            }

            if (BarsInProgress == 1)
            {
                // Process tick data (works for historical, real-time, and tick replay)
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

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || cvdOpenValues.Count == 0)
                return;

            // Convert WPF brushes to SharpDX brushes
            SharpDX.Direct2D1.Brush upBrushDX = PositiveBrush.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush downBrushDX = NegativeBrush.ToDxBrush(RenderTarget);

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                if (idx < 0 || !cvdOpenValues.ContainsKey(idx) || !cvdCloseValues.ContainsKey(idx))
                    continue;

                double open = cvdOpenValues[idx];
                double close = cvdCloseValues[idx];

                if (double.IsNaN(open) || double.IsNaN(close))
                    continue;

                float x = chartControl.GetXByBarIndex(ChartBars, idx);
                float yOpen = chartScale.GetYByValue(open);
                float yClose = chartScale.GetYByValue(close);

                // Calculate bar width
                float barWidth = (float)chartControl.BarWidth;
                float halfWidth = barWidth / 2f;

                // Choose color based on direction (close vs open)
                var barBrush = (close >= open) ? upBrushDX : downBrushDX;

                // Create rectangle for candlestick body
                float top = Math.Min(yOpen, yClose);
                float bottom = Math.Max(yOpen, yClose);
                float height = Math.Max(1, bottom - top);

                var candleRect = new SharpDX.RectangleF(
                    x - halfWidth,
                    top,
                    barWidth,
                    height
                );

                RenderTarget.FillRectangle(candleRect, barBrush);
            }

            // Dispose brushes to prevent memory leaks
            upBrushDX.Dispose();
            downBrushDX.Dispose();
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

        [Display(Name = "Reset Period", Description = "When to reset the cumulative delta", Order = 1, GroupName = "Parameters")]
        public ResetPeriodType ResetPeriod { get; set; }
        #endregion
    }

    public enum ResetPeriodType
    {
        NoReset,
        Daily,
        Weekly,
        Session
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private NTLambda.NTLCVD[] cacheNTLCVD;
        public NTLambda.NTLCVD NTLCVD(ResetPeriodType resetPeriod)
        {
            return NTLCVD(Input, resetPeriod);
        }

        public NTLambda.NTLCVD NTLCVD(ISeries<double> input, ResetPeriodType resetPeriod)
        {
            if (cacheNTLCVD != null)
                for (int idx = 0; idx < cacheNTLCVD.Length; idx++)
                    if (cacheNTLCVD[idx] != null && cacheNTLCVD[idx].ResetPeriod == resetPeriod && cacheNTLCVD[idx].EqualsInput(input))
                        return cacheNTLCVD[idx];
            return CacheIndicator<NTLambda.NTLCVD>(new NTLambda.NTLCVD() { ResetPeriod = resetPeriod }, input, ref cacheNTLCVD);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.NTLambda.NTLCVD NTLCVD(ResetPeriodType resetPeriod)
        {
            return indicator.NTLCVD(Input, resetPeriod);
        }

        public Indicators.NTLambda.NTLCVD NTLCVD(ISeries<double> input, ResetPeriodType resetPeriod)
        {
            return indicator.NTLCVD(input, resetPeriod);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.NTLambda.NTLCVD NTLCVD(ResetPeriodType resetPeriod)
        {
            return indicator.NTLCVD(Input, resetPeriod);
        }

        public Indicators.NTLambda.NTLCVD NTLCVD(ISeries<double> input, ResetPeriodType resetPeriod)
        {
            return indicator.NTLCVD(input, resetPeriod);
        }
    }
}

#endregion
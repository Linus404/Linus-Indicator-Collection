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
    public class NTLTWAP : Indicator
    {
        private Series<double> cumPrice;
        private Series<int> cumTime;
        private Series<double> cumPrice2;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"NTLambda TWAP (Time-Weighted Average Price) with Standard Deviation Bands";
                Name = "NTL TWAP";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                AddPlot(Brushes.Purple, "TWAP");
                AddPlot(Brushes.Magenta, "UpperBand1");
                AddPlot(Brushes.Magenta, "LowerBand1");
                AddPlot(Brushes.Red, "UpperBand2");
                AddPlot(Brushes.Red, "LowerBand2");

                PriceSourceInput = PriceSource.HLC3;
                ShowFirstBands = true;
                ShowSecondBands = true;
                FirstStdDev = 1.0;
                SecondStdDev = 2.0;
                ResetPeriod = TWAPResetPeriod.Session;
            }
            else if (State == State.DataLoaded)
            {
                cumPrice = new Series<double>(this);
                cumTime = new Series<int>(this);
                cumPrice2 = new Series<double>(this);
            }
            else if (State == State.Historical)
            {
                if (!Bars.BarsType.IsIntraday && ResetPeriod == TWAPResetPeriod.Session)
                {
                    Draw.TextFixed(this, "NinjaScriptInfo", "TWAP Indicator with Session reset only supports Intraday charts", TextPosition.BottomRight);
                    Log("TWAP with Session reset only supports Intraday charts", LogLevel.Error);
                }
            }
        }

        protected override void OnBarUpdate()
        {
            bool shouldReset = false;

            if (ResetPeriod == TWAPResetPeriod.Session && Bars.IsFirstBarOfSession)
                shouldReset = true;
            else if (ResetPeriod == TWAPResetPeriod.Daily && Time[0].DayOfWeek != Time[Math.Min(1, CurrentBar)].DayOfWeek)
                shouldReset = true;

            double price = GetPrice();
            int timeWeight = GetTimeWeight();

            if (shouldReset)
            {
                // Seed from current bar and return so we don't mix with prior values
                cumPrice[0] = price;
                cumPrice2[0] = price * price;
                cumTime[0] = timeWeight;

                double twap0 = cumPrice[0] / Math.Max(1, cumTime[0]);
                Values[0][0] = twap0;

                if (ShowFirstBands || ShowSecondBands)
                {
                    double variance0 = (cumPrice2[0] / Math.Max(1, cumTime[0])) - (twap0 * twap0);
                    double stdDev0 = variance0 > 0 ? Math.Sqrt(variance0) : 0;

                    if (ShowFirstBands)
                    {
                        Values[1][0] = twap0 + (stdDev0 * FirstStdDev);
                        Values[2][0] = twap0 - (stdDev0 * FirstStdDev);
                    }

                    if (ShowSecondBands)
                    {
                        Values[3][0] = twap0 + (stdDev0 * SecondStdDev);
                        Values[4][0] = twap0 - (stdDev0 * SecondStdDev);
                    }
                }
                return;
            }

            cumPrice[0] = cumPrice[1] + price;
            cumPrice2[0] = cumPrice2[1] + (price * price);
            cumTime[0] = cumTime[1] + timeWeight;

            double twap = cumPrice[0] / Math.Max(1, cumTime[0]);
            Values[0][0] = twap;

            if (ShowFirstBands || ShowSecondBands)
            {
                double variance = (cumPrice2[0] / Math.Max(1, cumTime[0])) - (twap * twap);
                double stdDev = variance > 0 ? Math.Sqrt(variance) : 0;

                if (ShowFirstBands)
                {
                    Values[1][0] = twap + (stdDev * FirstStdDev);
                    Values[2][0] = twap - (stdDev * FirstStdDev);
                }

                if (ShowSecondBands)
                {
                    Values[3][0] = twap + (stdDev * SecondStdDev);
                    Values[4][0] = twap - (stdDev * SecondStdDev);
                }
            }
        }

        private double GetPrice()
        {
            switch (PriceSourceInput)
            {
                case PriceSource.Close:
                    return Close[0];
                case PriceSource.HLC3:
                    return (High[0] + Low[0] + Close[0]) / 3.0;
                case PriceSource.HL2:
                    return (High[0] + Low[0]) / 2.0;
                case PriceSource.OHLC4:
                    return (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
                case PriceSource.HLCC4:
                    return (High[0] + Low[0] + Close[0] + Close[0]) / 4.0;
                default:
                    return (High[0] + Low[0] + Close[0]) / 3.0;
            }
        }

        private int GetTimeWeight()
        {
            // For TWAP on time-based charts, each bar gets equal weight (1)
            return 1;
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Price Source", Description = "Source for price calculation", Order = 1, GroupName = "Parameters")]
        public PriceSource PriceSourceInput { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset Period", Description = "When to reset TWAP calculation", Order = 2, GroupName = "Parameters")]
        public TWAPResetPeriod ResetPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show First Std Dev Bands", Description = "Show first standard deviation bands", Order = 3, GroupName = "Parameters")]
        public bool ShowFirstBands { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Second Std Dev Bands", Description = "Show second standard deviation bands", Order = 4, GroupName = "Parameters")]
        public bool ShowSecondBands { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "First Std Dev Multiplier", Description = "Multiplier for first standard deviation", Order = 5, GroupName = "Parameters")]
        public double FirstStdDev { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Second Std Dev Multiplier", Description = "Multiplier for second standard deviation", Order = 6, GroupName = "Parameters")]
        public double SecondStdDev { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TWAP
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> UpperBand1
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LowerBand1
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> UpperBand2
        {
            get { return Values[3]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LowerBand2
        {
            get { return Values[4]; }
        }
        #endregion
    }

    public enum TWAPResetPeriod
    {
        Session,
        Daily
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private NTLambda.NTLTWAP[] cacheNTLTWAP;
        public NTLambda.NTLTWAP NTLTWAP(PriceSource priceSourceInput, TWAPResetPeriod resetPeriod, bool showFirstBands, bool showSecondBands, double firstStdDev, double secondStdDev)
        {
            return NTLTWAP(Input, priceSourceInput, resetPeriod, showFirstBands, showSecondBands, firstStdDev, secondStdDev);
        }

        public NTLambda.NTLTWAP NTLTWAP(ISeries<double> input, PriceSource priceSourceInput, TWAPResetPeriod resetPeriod, bool showFirstBands, bool showSecondBands, double firstStdDev, double secondStdDev)
        {
            if (cacheNTLTWAP != null)
                for (int idx = 0; idx < cacheNTLTWAP.Length; idx++)
                    if (cacheNTLTWAP[idx] != null && cacheNTLTWAP[idx].PriceSourceInput == priceSourceInput && cacheNTLTWAP[idx].ResetPeriod == resetPeriod && cacheNTLTWAP[idx].ShowFirstBands == showFirstBands && cacheNTLTWAP[idx].ShowSecondBands == showSecondBands && cacheNTLTWAP[idx].FirstStdDev == firstStdDev && cacheNTLTWAP[idx].SecondStdDev == secondStdDev && cacheNTLTWAP[idx].EqualsInput(input))
                        return cacheNTLTWAP[idx];
            return CacheIndicator<NTLambda.NTLTWAP>(new NTLambda.NTLTWAP() { PriceSourceInput = priceSourceInput, ResetPeriod = resetPeriod, ShowFirstBands = showFirstBands, ShowSecondBands = showSecondBands, FirstStdDev = firstStdDev, SecondStdDev = secondStdDev }, input, ref cacheNTLTWAP);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.NTLambda.NTLTWAP NTLTWAP(PriceSource priceSourceInput, TWAPResetPeriod resetPeriod, bool showFirstBands, bool showSecondBands, double firstStdDev, double secondStdDev)
        {
            return indicator.NTLTWAP(Input, priceSourceInput, resetPeriod, showFirstBands, showSecondBands, firstStdDev, secondStdDev);
        }

        public Indicators.NTLambda.NTLTWAP NTLTWAP(ISeries<double> input, PriceSource priceSourceInput, TWAPResetPeriod resetPeriod, bool showFirstBands, bool showSecondBands, double firstStdDev, double secondStdDev)
        {
            return indicator.NTLTWAP(input, priceSourceInput, resetPeriod, showFirstBands, showSecondBands, firstStdDev, secondStdDev);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.NTLambda.NTLTWAP NTLTWAP(PriceSource priceSourceInput, TWAPResetPeriod resetPeriod, bool showFirstBands, bool showSecondBands, double firstStdDev, double secondStdDev)
        {
            return indicator.NTLTWAP(Input, priceSourceInput, resetPeriod, showFirstBands, showSecondBands, firstStdDev, secondStdDev);
        }

        public Indicators.NTLambda.NTLTWAP NTLTWAP(ISeries<double> input, PriceSource priceSourceInput, TWAPResetPeriod resetPeriod, bool showFirstBands, bool showSecondBands, double firstStdDev, double secondStdDev)
        {
            return indicator.NTLTWAP(input, priceSourceInput, resetPeriod, showFirstBands, showSecondBands, firstStdDev, secondStdDev);
        }
    }
}

#endregion

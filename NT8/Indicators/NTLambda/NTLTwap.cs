#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLTwap : Indicator
    {
        private double sum, sumSq;
        private int count;
        private DateTime lastResetTime = DateTime.MinValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL TWAP";
                Description = "Time weighted average price with optional deviation bands.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                ResetPeriod = NTLResetPeriod.Session;
                PriceSource = NTLPriceSource.Typical;
                ShowDeviationBands = true;
                DeviationMultiplier1 = 1.0;
                DeviationMultiplier2 = 2.0;
                AddPlot(WpfBrushes.MediumPurple, "TWAP");
                AddPlot(WpfBrushes.SteelBlue, "UpperDeviation1");
                AddPlot(WpfBrushes.SteelBlue, "LowerDeviation1");
                AddPlot(WpfBrushes.DarkSlateBlue, "UpperDeviation2");
                AddPlot(WpfBrushes.DarkSlateBlue, "LowerDeviation2");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0 || NTLCore.ShouldReset(this, ResetPeriod, lastResetTime))
            {
                sum = 0; sumSq = 0; count = 0; lastResetTime = Time[0];
            }
            double price = NTLCore.Price(this, PriceSource);
            sum += price;
            sumSq += price * price;
            count++;
            double twap = sum / Math.Max(1, count);
            double variance = Math.Max(0, sumSq / Math.Max(1, count) - twap * twap);
            double sd = Math.Sqrt(variance);
            Values[0][0] = twap;
            Values[1][0] = twap + sd * DeviationMultiplier1;
            Values[2][0] = twap - sd * DeviationMultiplier1;
            Values[3][0] = twap + sd * DeviationMultiplier2;
            Values[4][0] = twap - sd * DeviationMultiplier2;
            PlotBrushes[1][0] = ShowDeviationBands ? WpfBrushes.SteelBlue : WpfBrushes.Transparent;
            PlotBrushes[2][0] = ShowDeviationBands ? WpfBrushes.SteelBlue : WpfBrushes.Transparent;
            PlotBrushes[3][0] = ShowDeviationBands ? WpfBrushes.DarkSlateBlue : WpfBrushes.Transparent;
            PlotBrushes[4][0] = ShowDeviationBands ? WpfBrushes.DarkSlateBlue : WpfBrushes.Transparent;
        }

        [NinjaScriptProperty]
        [Display(Name = "Reset period", GroupName = "Calculation", Order = 0)]
        public NTLResetPeriod ResetPeriod { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Price source", GroupName = "Calculation", Order = 1)]
        public NTLPriceSource PriceSource { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show deviation bands", GroupName = "Bands", Order = 2)]
        public bool ShowDeviationBands { get; set; }
        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Deviation multiplier 1", GroupName = "Bands", Order = 3)]
        public double DeviationMultiplier1 { get; set; }
        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Deviation multiplier 2", GroupName = "Bands", Order = 4)]
        public double DeviationMultiplier2 { get; set; }
    }
}

#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLVwap : Indicator
    {
        private double pv, volume, sumSq;
        private DateTime lastResetTime = DateTime.MinValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL VWAP";
                Description = "Volume weighted average price with optional 1/2/3 deviation bands.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                ResetPeriod = NTLResetPeriod.Session;
                PriceSource = NTLPriceSource.Typical;
                ShowDeviationBands = true;
                DeviationMultiplier1 = 1.0;
                DeviationMultiplier2 = 2.0;
                DeviationMultiplier3 = 3.0;
                AddPlot(WpfBrushes.Gold, "VWAP");
                AddPlot(WpfBrushes.SteelBlue, "UpperDeviation");
                AddPlot(WpfBrushes.SteelBlue, "LowerDeviation");
                AddPlot(WpfBrushes.DodgerBlue, "UpperDeviation2");
                AddPlot(WpfBrushes.DodgerBlue, "LowerDeviation2");
                AddPlot(WpfBrushes.DarkSlateBlue, "UpperDeviation3");
                AddPlot(WpfBrushes.DarkSlateBlue, "LowerDeviation3");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0 || NTLCore.ShouldReset(this, ResetPeriod, lastResetTime))
            {
                pv = 0; volume = 0; sumSq = 0; lastResetTime = Time[0];
            }
            double price = NTLCore.Price(this, PriceSource);
            double vol = Math.Max(1, Volume[0]);
            pv += price * vol;
            volume += vol;
            double vwap = pv / Math.Max(1, volume);
            double diff = price - vwap;
            sumSq += diff * diff * vol;
            double sd = Math.Sqrt(sumSq / Math.Max(1, volume));
            Values[0][0] = vwap;
            Values[1][0] = vwap + sd * DeviationMultiplier1;
            Values[2][0] = vwap - sd * DeviationMultiplier1;
            Values[3][0] = vwap + sd * DeviationMultiplier2;
            Values[4][0] = vwap - sd * DeviationMultiplier2;
            Values[5][0] = vwap + sd * DeviationMultiplier3;
            Values[6][0] = vwap - sd * DeviationMultiplier3;
            PlotBrushes[1][0] = ShowDeviationBands ? WpfBrushes.SteelBlue : WpfBrushes.Transparent;
            PlotBrushes[2][0] = ShowDeviationBands ? WpfBrushes.SteelBlue : WpfBrushes.Transparent;
            PlotBrushes[3][0] = ShowDeviationBands ? WpfBrushes.DodgerBlue : WpfBrushes.Transparent;
            PlotBrushes[4][0] = ShowDeviationBands ? WpfBrushes.DodgerBlue : WpfBrushes.Transparent;
            PlotBrushes[5][0] = ShowDeviationBands ? WpfBrushes.DarkSlateBlue : WpfBrushes.Transparent;
            PlotBrushes[6][0] = ShowDeviationBands ? WpfBrushes.DarkSlateBlue : WpfBrushes.Transparent;
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
        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Deviation multiplier 3", GroupName = "Bands", Order = 5)]
        public double DeviationMultiplier3 { get; set; }
    }
}

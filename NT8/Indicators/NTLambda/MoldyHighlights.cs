using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class MarkImportantLevels : Indicator
    {
        private double onh, onl, priorSettlement, dayHigh, dayLow, weekHigh, weekLow, monthHigh, monthLow, yearHigh, yearLow, allTimeHigh;
        private DateTime monthStart, yearStart;
        private bool isNewDay, isNewWeek, isNewMonth, isNewYear;
        private Dictionary<string, double> levels;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Marks important levels on the chart such as Overnight High/Low, Prior Settlement, Daily High/Low, etc.";
                Name = "NTL MoldyHighlights";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
            }
            else if (State == State.DataLoaded)
            {
                onh = double.MinValue;
                onl = double.MaxValue;
                priorSettlement = 0;
                dayHigh = double.MinValue;
                dayLow = double.MaxValue;
                weekHigh = double.MinValue;
                weekLow = double.MaxValue;
                monthHigh = double.MinValue;
                monthLow = double.MaxValue;
                yearHigh = double.MinValue;
                yearLow = double.MaxValue;
                allTimeHigh = double.MinValue;
                levels = new Dictionary<string, double>();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0)
            {
                monthStart = Time[0];
                yearStart = Time[0];
                return;
            }

            isNewDay = Time[0].Date != Time[1].Date;
            isNewWeek = isNewDay && Time[0].DayOfWeek < Time[1].DayOfWeek;
            isNewMonth = Time[0].Month != Time[1].Month;
            isNewYear = Time[0].Year != Time[1].Year;

            // Overnight session: 6 PM to 8:30 AM CT (adjust for your market)
            TimeSpan sessionStart = new TimeSpan(18, 0, 0); // 6 PM
            TimeSpan sessionEnd = new TimeSpan(8, 30, 0);   // 8:30 AM
            TimeSpan settlementTime = new TimeSpan(16, 0, 0); // 4 PM
            
            bool isOvernightSession = Time[0].TimeOfDay >= sessionStart || Time[0].TimeOfDay <= sessionEnd;
            
            if (isOvernightSession)
            {
                if (onh == double.MinValue || onl == double.MaxValue)
                {
                    onh = High[0];
                    onl = Low[0];
                }
                else
                {
                    onh = Math.Max(onh, High[0]);
                    onl = Math.Min(onl, Low[0]);
                }
            }

            if (Time[0].TimeOfDay >= settlementTime && Time[0].TimeOfDay < settlementTime.Add(TimeSpan.FromMinutes(5)))
            {
                priorSettlement = Close[0];
            }

            if (isNewDay)
            {
                dayHigh = High[0];
                dayLow = Low[0];
                // Reset overnight levels for new day
                onh = double.MinValue;
                onl = double.MaxValue;
            }
            else
            {
                dayHigh = Math.Max(dayHigh, High[0]);
                dayLow = Math.Min(dayLow, Low[0]);
            }

            if (isNewWeek)
            {
                weekHigh = High[0];
                weekLow = Low[0];
            }
            else
            {
                weekHigh = Math.Max(weekHigh, High[0]);
                weekLow = Math.Min(weekLow, Low[0]);
            }

            if (isNewMonth)
            {
                monthHigh = High[0];
                monthLow = Low[0];
                monthStart = Time[0];
            }
            else
            {
                monthHigh = Math.Max(monthHigh, High[0]);
                monthLow = Math.Min(monthLow, Low[0]);
            }

            if (isNewYear)
            {
                yearHigh = High[0];
                yearLow = Low[0];
                yearStart = Time[0];
            }
            else
            {
                yearHigh = Math.Max(yearHigh, High[0]);
                yearLow = Math.Min(yearLow, Low[0]);
            }

            allTimeHigh = Math.Max(allTimeHigh, High[0]);

            // Update levels dictionary
            levels.Clear();
            if (onh > double.MinValue) levels["ONH"] = onh;
            if (onl < double.MaxValue) levels["ONL"] = onl;
            if (priorSettlement > 0) levels["PriorSettlement"] = priorSettlement;
            if (dayHigh > double.MinValue) levels["DayHigh"] = dayHigh;
            if (dayLow < double.MaxValue) levels["DayLow"] = dayLow;
            if (weekHigh > double.MinValue) levels["WeekHigh"] = weekHigh;
            if (weekLow < double.MaxValue) levels["WeekLow"] = weekLow;
            if (monthHigh > double.MinValue) levels["MonthHigh"] = monthHigh;
            if (monthLow < double.MaxValue) levels["MonthLow"] = monthLow;
            if (yearHigh > double.MinValue) levels["YearHigh"] = yearHigh;
            if (yearLow < double.MaxValue) levels["YearLow"] = yearLow;
            if (allTimeHigh > double.MinValue) levels["ATH"] = allTimeHigh;

            // Drawing lines only every 10 bars to reduce performance impact
            if (CurrentBar % 10 == 0)
            {
                foreach (var level in levels)
                {
                    Draw.Line(this, level.Key + CurrentBar, false, 0, level.Value, 100, level.Value, GetBrushForLevel(level.Key), DashStyleHelper.Solid, 2);
                }

                // Drawing text labels
                if (levels.ContainsKey("ONH")) DrawSmartText("ONHText" + CurrentBar, "ONH", levels["ONH"], GetBrushForLevel("ONH"));
                if (levels.ContainsKey("ONL")) DrawSmartText("ONLText" + CurrentBar, "ONL", levels["ONL"], GetBrushForLevel("ONL"));
                if (levels.ContainsKey("PriorSettlement")) DrawSmartText("PriorSettlementText" + CurrentBar, "Settlement", levels["PriorSettlement"], GetBrushForLevel("PriorSettlement"));
                if (levels.ContainsKey("DayHigh")) DrawSmartText("DayHighText" + CurrentBar, "Day High", levels["DayHigh"], GetBrushForLevel("DayHigh"));
                if (levels.ContainsKey("DayLow")) DrawSmartText("DayLowText" + CurrentBar, "Day Low", levels["DayLow"], GetBrushForLevel("DayLow"));
                if (levels.ContainsKey("WeekHigh")) DrawSmartText("WeekHighText" + CurrentBar, "Week High", levels["WeekHigh"], GetBrushForLevel("WeekHigh"));
                if (levels.ContainsKey("WeekLow")) DrawSmartText("WeekLowText" + CurrentBar, "Week Low", levels["WeekLow"], GetBrushForLevel("WeekLow"));
                if (levels.ContainsKey("MonthHigh")) DrawSmartText("MonthHighText" + CurrentBar, "Month High", levels["MonthHigh"], GetBrushForLevel("MonthHigh"));
                if (levels.ContainsKey("MonthLow")) DrawSmartText("MonthLowText" + CurrentBar, "Month Low", levels["MonthLow"], GetBrushForLevel("MonthLow"));
                if (levels.ContainsKey("YearHigh")) DrawSmartText("YearHighText" + CurrentBar, "Year High", levels["YearHigh"], GetBrushForLevel("YearHigh"));
                if (levels.ContainsKey("YearLow")) DrawSmartText("YearLowText" + CurrentBar, "Year Low", levels["YearLow"], GetBrushForLevel("YearLow"));
                if (levels.ContainsKey("ATH")) DrawSmartText("ATHText" + CurrentBar, "ATH", levels["ATH"], GetBrushForLevel("ATH"));
            }
        }

        private Brush GetBrushForLevel(string levelKey)
        {
            switch (levelKey)
            {
                case "ONH":
                case "ONL":
                    return Brushes.Orange;
                case "PriorSettlement":
                    return Brushes.Yellow;
                case "DayHigh":
                case "DayLow":
                    return Brushes.Red;
                case "WeekHigh":
                case "WeekLow":
                    return Brushes.Magenta;
                case "MonthHigh":
                case "MonthLow":
                    return Brushes.Green;
                case "YearHigh":
                case "YearLow":
                    return Brushes.Blue;
                case "ATH":
                    return Brushes.Gold;
                default:
                    return Brushes.White;
            }
        }

        private void DrawSmartText(string tag, string text, double yValue, Brush color)
        {
            const int textOffset = 5;
            double minSpacing = TickSize * 10;

            // Find a suitable Y position for the text
            double textY = yValue + (TickSize * textOffset);
            bool positionFound = false;
            int maxAttempts = 20;
            int attempts = 0;

            while (!positionFound && attempts < maxAttempts)
            {
                positionFound = true;
                foreach (var level in levels)
                {
                    if (Math.Abs(level.Value - textY) < minSpacing && level.Value != yValue)
                    {
                        textY += minSpacing;
                        positionFound = false;
                        break;
                    }
                }
                attempts++;
            }

            Draw.Text(this, tag, text, 0, textY, color);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MarkImportantLevels[] cacheMarkImportantLevels;
		public MarkImportantLevels MarkImportantLevels()
		{
			return MarkImportantLevels(Input);
		}

		public MarkImportantLevels MarkImportantLevels(ISeries<double> input)
		{
			if (cacheMarkImportantLevels != null)
				for (int idx = 0; idx < cacheMarkImportantLevels.Length; idx++)
					if (cacheMarkImportantLevels[idx] != null &&  cacheMarkImportantLevels[idx].EqualsInput(input))
						return cacheMarkImportantLevels[idx];
			return CacheIndicator<MarkImportantLevels>(new MarkImportantLevels(), input, ref cacheMarkImportantLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MarkImportantLevels MarkImportantLevels()
		{
			return indicator.MarkImportantLevels(Input);
		}

		public Indicators.MarkImportantLevels MarkImportantLevels(ISeries<double> input )
		{
			return indicator.MarkImportantLevels(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MarkImportantLevels MarkImportantLevels()
		{
			return indicator.MarkImportantLevels(Input);
		}

		public Indicators.MarkImportantLevels MarkImportantLevels(ISeries<double> input )
		{
			return indicator.MarkImportantLevels(input);
		}
	}
}

#endregion

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class TPOIndicator : Indicator
    {
        private Dictionary<DateTime, Dictionary<double, int>> completedSessionTPOData;
        private Dictionary<DateTime, bool> sessionDrawnFlags;
        private Dictionary<DateTime, DateTime> sessionStartTimes;
        private Dictionary<double, int> currentSessionPriceCount;

        private DateTime currentSessionDate = DateTime.MinValue;
        private DateTime currentSessionStartTime = DateTime.MinValue;
        private DateTime sessionEndTime = DateTime.MinValue;
        private bool currentSessionDrawn = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"TPO/Market Profile Indicator - displays price distribution as horizontal rectangles for each session";
                Name = "NTL TPO Indicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;

                BlockWidthMinutes = 30;
                BlockColor = Brushes.LightBlue;
                SessionSizePercent = 100;
                ShowCurrentSessionOnly = false;
                TPOOpacity = 60;

                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TPOPlot");
            }
            else if (State == State.DataLoaded)
            {
                completedSessionTPOData = new Dictionary<DateTime, Dictionary<double, int>>();
                sessionDrawnFlags = new Dictionary<DateTime, bool>();
                sessionStartTimes = new Dictionary<DateTime, DateTime>();
                currentSessionPriceCount = new Dictionary<double, int>();
            }
            else if (State == State.Terminated)
            {
                // Finalize last session on indicator removal
                FinalizeCurrentSessionIfNeeded();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            DateTime barSessionDate = Time[0].Date;

            // Handle session change
            if (barSessionDate != currentSessionDate)
            {
                if (currentSessionDate != DateTime.MinValue && currentSessionPriceCount.Count > 0)
                    FinalizeSession(currentSessionDate, sessionEndTime);

                StartNewSession(barSessionDate);
            }

            sessionEndTime = Time[0];

            UpdateCurrentSessionPriceData();

            DrawTPOProfilesIfNeeded();

            if (State == State.Realtime && IsLastBarOfSession())
                FinalizeCurrentSessionIfNeeded();
        }

        private void StartNewSession(DateTime sessionDate)
        {
            currentSessionDate = sessionDate;
            currentSessionStartTime = Time[0];
            currentSessionPriceCount = new Dictionary<double, int>();
            currentSessionDrawn = false;

            if (ShowCurrentSessionOnly && completedSessionTPOData.Count > 0)
            {
                RemoveDrawObjectsForSessions(completedSessionTPOData.Keys.ToList());
                completedSessionTPOData.Clear();
                sessionDrawnFlags.Clear();
                sessionStartTimes.Clear();
            }
        }

        private void FinalizeSession(DateTime sessionDate, DateTime endTime)
        {
            completedSessionTPOData[sessionDate] = new Dictionary<double, int>(currentSessionPriceCount);
            sessionStartTimes[sessionDate] = currentSessionStartTime;

            DrawTPOProfileForSession(sessionDate, completedSessionTPOData[sessionDate], currentSessionStartTime, endTime);
            sessionDrawnFlags[sessionDate] = true;
        }

        private void UpdateCurrentSessionPriceData()
        {
            double tickSize = Instrument.MasterInstrument.TickSize;
            double high = High[0];
            double low = Low[0];

            for (double price = low; price <= high; price += tickSize)
            {
                double roundedPrice = Math.Round(price / tickSize) * tickSize;
                if (currentSessionPriceCount.ContainsKey(roundedPrice))
                    currentSessionPriceCount[roundedPrice]++;
                else
                    currentSessionPriceCount[roundedPrice] = 1;
            }
        }

        private void DrawTPOProfilesIfNeeded()
        {
            // Redraw current session every 20 bars to reduce load
            if (!currentSessionDrawn || CurrentBar % 20 == 0)
                DrawCurrentSession();

            foreach (var kvp in completedSessionTPOData)
            {
                if (!sessionDrawnFlags.ContainsKey(kvp.Key) || !sessionDrawnFlags[kvp.Key])
                {
                    DateTime startTime = sessionStartTimes.ContainsKey(kvp.Key) ? sessionStartTimes[kvp.Key] : kvp.Key;
                    DrawTPOProfileForSession(kvp.Key, kvp.Value, startTime, sessionEndTime);
                    sessionDrawnFlags[kvp.Key] = true;
                }
            }
        }

        private void DrawCurrentSession()
        {
            if (currentSessionPriceCount.Count == 0)
                return;

            RemoveDrawObjectsForSession(currentSessionDate);

            DrawTPOProfileForSession(currentSessionDate, currentSessionPriceCount, currentSessionStartTime, sessionEndTime);
            currentSessionDrawn = true;
        }

        private void DrawTPOProfileForSession(DateTime sessionDate, Dictionary<double, int> priceData, DateTime startTime, DateTime endTime)
        {
            if (priceData.Count == 0)
                return;

            double tickSize = Instrument.MasterInstrument.TickSize;
            bool useCandleWidthMethod = ShouldUseCandleWidth();

            foreach (var kvp in priceData.OrderBy(k => k.Key))
            {
                double price = kvp.Key;
                int count = kvp.Value;

                for (int i = 0; i < count; i++)
                {
                    string tag = $"TPO_{sessionDate:yyyyMMdd}_{price:F5}_{i}";

                    DateTime blockStartTime, blockEndTime;

                    if (useCandleWidthMethod)
                    {
                        double timeframeMinutes = GetTimeframeMinutes();
                        blockStartTime = startTime.AddMinutes(i * timeframeMinutes);
                        blockEndTime = blockStartTime.AddMinutes(timeframeMinutes);
                    }
                    else
                    {
                        double adjustedBlockWidth = BlockWidthMinutes * (SessionSizePercent / 100.0);
                        blockStartTime = startTime.AddMinutes(i * adjustedBlockWidth);
                        blockEndTime = blockStartTime.AddMinutes(adjustedBlockWidth);
                    }

                    double topPrice = price + (tickSize * 0.4);
                    double bottomPrice = price - (tickSize * 0.4);

                    var brush = BlockColor.Clone();
                    brush.Opacity = TPOOpacity / 100.0;

                    try
                    {
                        Draw.Rectangle(this, tag, false, blockStartTime, bottomPrice, blockEndTime, topPrice,
                            brush, brush, 1);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }

        private void RemoveDrawObjectsForSession(DateTime sessionDate)
        {
            string prefix = $"TPO_{sessionDate:yyyyMMdd}_";
            var tags = DrawObjects.Where(obj => obj.Tag?.StartsWith(prefix) == true)
                                  .Select(obj => obj.Tag).ToList();

            foreach (string tag in tags)
                RemoveDrawObject(tag);
        }

        private void RemoveDrawObjectsForSessions(List<DateTime> sessionDates)
        {
            foreach (var sessionDate in sessionDates)
                RemoveDrawObjectsForSession(sessionDate);
        }

        private bool ShouldUseCandleWidth()
        {
            int timeframeMinutes = GetTimeframeMinutes();
            return timeframeMinutes > 0 && timeframeMinutes <= 30;
        }

        private int GetTimeframeMinutes()
        {
            switch (BarsPeriod.BarsPeriodType)
            {
                case BarsPeriodType.Minute:
                    return BarsPeriod.Value;
                case BarsPeriodType.Second:
                    return Math.Max(1, BarsPeriod.Value / 60);
                case BarsPeriodType.Tick:
                case BarsPeriodType.Volume:
                case BarsPeriodType.Range:
                    return 1;
                default:
                    return 0; // daily, weekly, etc.
            }
        }

        private bool IsLastBarOfSession()
        {
            return DateTime.Now.Subtract(Time[0]).TotalMinutes > GetTimeframeMinutes() * 2;
        }

        private void FinalizeCurrentSessionIfNeeded()
        {
            if (currentSessionDate != DateTime.MinValue &&
                currentSessionPriceCount.Count > 0 &&
                !completedSessionTPOData.ContainsKey(currentSessionDate))
            {
                FinalizeSession(currentSessionDate, sessionEndTime);
            }
        }

        #region Properties
        [Range(5, 120)]
        [Display(Name = "Block Width (Minutes)", Description = "Width of each TPO block in minutes", Order = 1, GroupName = "Display")]
        public int BlockWidthMinutes { get; set; }

        [Range(10, 200)]
        [Display(Name = "Session Size (%)", Description = "Size of TPO blocks relative to session duration as percentage", Order = 2, GroupName = "Display")]
        public int SessionSizePercent { get; set; }

        [Display(Name = "Show Current Session Only", Description = "Only show TPO data for current session", Order = 3, GroupName = "Display")]
        public bool ShowCurrentSessionOnly { get; set; }

        [Range(10, 100)]
        [Display(Name = "TPO Opacity", Description = "Opacity of TPO blocks (10-100%)", Order = 4, GroupName = "Display")]
        public int TPOOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Block Color", Description = "Color of the TPO blocks", Order = 5, GroupName = "Display")]
        public Brush BlockColor { get; set; }

        [Browsable(false)]
        public string BlockColorSerialize
        {
            get { return Serialize.BrushToString(BlockColor); }
            set { BlockColor = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private NTLambda.TPOIndicator[] cacheTPOIndicator;
        public NTLambda.TPOIndicator TPOIndicator()
        {
            return TPOIndicator(Input);
        }

        public NTLambda.TPOIndicator TPOIndicator(ISeries<double> input)
        {
            if (cacheTPOIndicator != null)
                for (int idx = 0; idx < cacheTPOIndicator.Length; idx++)
                    if (cacheTPOIndicator[idx] != null && cacheTPOIndicator[idx].EqualsInput(input))
                        return cacheTPOIndicator[idx];
            return CacheIndicator<NTLambda.TPOIndicator>(new NTLambda.TPOIndicator(), input, ref cacheTPOIndicator);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.NTLambda.TPOIndicator TPOIndicator()
        {
            return indicator.TPOIndicator(Input);
        }

        public Indicators.NTLambda.TPOIndicator TPOIndicator(ISeries<double> input)
        {
            return indicator.TPOIndicator(input);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.NTLambda.TPOIndicator TPOIndicator()
        {
            return indicator.TPOIndicator(Input);
        }

        public Indicators.NTLambda.TPOIndicator TPOIndicator(ISeries<double> input)
        {
            return indicator.TPOIndicator(input);
        }
    }
}

#endregion

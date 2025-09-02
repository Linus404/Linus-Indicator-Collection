using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class MarketProfileStats
    {
        public double POC { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public double ValueAreaPercentage { get; set; } = 70.0;
    }

    public class TPOIndicator : Indicator
    {
        // Track which time periods have touched each price bucket
        // Traditional TPO Profile: blocks stack horizontally from session start
        // Each block represents an underlying time period (e.g., 30 minutes) that touched that price
        private Dictionary<int, HashSet<int>> bucketTimePeriods; // bucket -> set of time period indices
        private double sessionHigh = double.MinValue;
        private double sessionLow = double.MaxValue;
        private double alignedSessionLow = double.MaxValue; // Grid-aligned session low for consistent bucket positioning
        private DateTime currentSessionDate = DateTime.MinValue;
        private DateTime sessionStartTime = DateTime.MinValue;
        private int minTimePeriodIndex = int.MaxValue; // Track the minimum time period index seen

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"TPO/Market Profile Indicator - displays price distribution as horizontal rectangles for each session";
                Name = "TPO Indicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;

                UnderlyingTimeframe = 30; // Default 30-minute TPO periods
                BlockColor = Brushes.LightBlue;
                TPOOpacity = 60;
                ShowVAH = true;
                ShowVAL = true;
                ShowPOC = true;
                VAHColor = Brushes.Red;
                VALColor = Brushes.Red;
                POCColor = Brushes.Yellow;
                ValueAreaPercentage = 70;
                ShowBlocks = true;
                ShowLetters = true;
                LetterColor = Brushes.Snow;
                LetterSize = 8;
                FillBlocks = true;
                FixedBoxHeight = 10;
                LevelsPerSession = 100;
                BlockHorizontalOffset = 0.5; // Default half bar offset

                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TPOPlot");
            }
            else if (State == State.DataLoaded)
            {
                bucketTimePeriods = new Dictionary<int, HashSet<int>>();
            }
            else if (State == State.Terminated)
            {
                if (currentSessionDate != DateTime.MinValue)
                    ClearSessionDrawings();
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
                StartNewSession(barSessionDate);
            }

            ProcessCurrentBar();
        }

        private void StartNewSession(DateTime sessionDate)
        {
            currentSessionDate = sessionDate;

            SessionIterator sessionIterator = new SessionIterator(Bars);
            sessionIterator.GetTradingDay(sessionDate);

            if (sessionIterator.ActualSessionBegin != DateTime.MinValue)
            {
                sessionStartTime = sessionIterator.ActualSessionBegin;
            }
            else
            {
                sessionStartTime = Time[0];
            }

            sessionHigh = High[0];
            sessionLow = Low[0];
            alignedSessionLow = GetAlignedPrice(sessionLow, false); // Align to grid
            bucketTimePeriods = new Dictionary<int, HashSet<int>>();
            minTimePeriodIndex = int.MaxValue; // Reset minimum time period index

            ClearSessionDrawings();
        }

        private double GetAlignedPrice(double price, bool roundUp)
        {
            // Align price to the global tick grid based on box height
            double boxHeight = FixedBoxHeight * Instrument.MasterInstrument.TickSize;

            if (roundUp)
            {
                return Math.Ceiling(price / boxHeight) * boxHeight;
            }
            else
            {
                return Math.Floor(price / boxHeight) * boxHeight;
            }
        }

        private void ClearSessionDrawings()
        {
            string dateString = currentSessionDate.ToString("yyyyMMdd");

            var objectsToRemove = new List<string>();
            foreach (var drawObject in DrawObjects)
            {
                if (drawObject.Tag.StartsWith($"TPO_{dateString}_") ||
                    drawObject.Tag.StartsWith($"Letter_{dateString}_") ||
                    drawObject.Tag == $"POC_{dateString}" ||
                    drawObject.Tag == $"VAH_{dateString}" ||
                    drawObject.Tag == $"VAL_{dateString}")
                {
                    objectsToRemove.Add(drawObject.Tag);
                }
            }

            foreach (var tag in objectsToRemove)
            {
                RemoveDrawObject(tag);
            }
        }

        private int GetTimePeriodIndex(DateTime barTime)
        {
            // Calculate which underlying time period this bar belongs to
            double minutesSinceSessionStart = barTime.Subtract(sessionStartTime).TotalMinutes;
            return (int)(minutesSinceSessionStart / UnderlyingTimeframe);
        }

        private void ProcessCurrentBar()
        {
            double high = High[0];
            double low = Low[0];

            // Check for new session high/low
            bool newExtreme = false;
            if (high > sessionHigh)
            {
                sessionHigh = high;
                newExtreme = true;
            }
            if (low < sessionLow)
            {
                sessionLow = low;
                alignedSessionLow = GetAlignedPrice(sessionLow, false); // Update aligned low
                newExtreme = true;
            }

            // If new extreme, need to redraw everything
            if (newExtreme)
            {
                RedrawEntireSession();
                return;
            }

            // Get current time period index
            int currentTimePeriod = GetTimePeriodIndex(Time[0]);

            // Track minimum time period index
            if (currentTimePeriod < minTimePeriodIndex)
            {
                minTimePeriodIndex = currentTimePeriod;
            }

            // Calculate which buckets this bar touches
            double boxHeight = FixedBoxHeight * Instrument.MasterInstrument.TickSize;
            double sessionRange = sessionHigh - alignedSessionLow;
            if (sessionRange <= 0) return;

            int maxBuckets = (int)Math.Ceiling(sessionRange / boxHeight) + 1;

            for (int bucket = 0; bucket < maxBuckets; bucket++)
            {
                double bucketBottom = alignedSessionLow + (bucket * boxHeight);
                double bucketTop = bucketBottom + boxHeight;

                // Check if bar intersects with this bucket
                bool intersects = (high >= bucketBottom && low < bucketTop) ||
                                (bucket == maxBuckets - 1 && high >= bucketBottom && low <= bucketTop);

                if (intersects)
                {
                    // Initialize bucket if needed
                    if (!bucketTimePeriods.ContainsKey(bucket))
                    {
                        bucketTimePeriods[bucket] = new HashSet<int>();
                    }

                    // Check if this time period already touched this bucket
                    if (!bucketTimePeriods[bucket].Contains(currentTimePeriod))
                    {
                        bucketTimePeriods[bucket].Add(currentTimePeriod);

                        // Draw the new block (block number is the count of periods for this bucket)
                        int blockNumber = bucketTimePeriods[bucket].Count;
                        DrawSingleNewBlock(bucket, blockNumber, currentTimePeriod);
                    }
                }
            }

            UpdateValueArea();
        }

        private void RedrawEntireSession()
        {
            ClearSessionDrawings();

            double sessionRange = sessionHigh - alignedSessionLow;
            if (sessionRange <= 0) return;

            double boxHeight = FixedBoxHeight * Instrument.MasterInstrument.TickSize;
            int maxBuckets = (int)Math.Ceiling(sessionRange / boxHeight) + 1;

            // Reset bucket time periods and minimum time period index
            bucketTimePeriods = new Dictionary<int, HashSet<int>>();
            minTimePeriodIndex = int.MaxValue;

            // Go through all bars in current session and recalculate
            for (int i = 0; i <= CurrentBar; i++)
            {
                if (Time[i].Date != currentSessionDate)
                    continue;

                int timePeriod = GetTimePeriodIndex(Time[i]);

                // Track minimum time period index
                if (timePeriod < minTimePeriodIndex)
                {
                    minTimePeriodIndex = timePeriod;
                }

                for (int bucket = 0; bucket < maxBuckets; bucket++)
                {
                    double bucketBottom = alignedSessionLow + (bucket * boxHeight);
                    double bucketTop = bucketBottom + boxHeight;

                    bool intersects = (High[i] >= bucketBottom && Low[i] < bucketTop) ||
                                    (bucket == maxBuckets - 1 && High[i] >= bucketBottom && Low[i] <= bucketTop);

                    if (intersects)
                    {
                        if (!bucketTimePeriods.ContainsKey(bucket))
                        {
                            bucketTimePeriods[bucket] = new HashSet<int>();
                        }

                        bucketTimePeriods[bucket].Add(timePeriod);
                    }
                }
            }

            // Draw all blocks
            DrawAllBlocks();
            UpdateValueArea();
        }

        private double GetVisualBlockWidth()
        {
            // Calculate visual block width based on chart timeframe
            int chartMinutes = GetChartTimeframeMinutes();
            if (chartMinutes <= 0) return UnderlyingTimeframe; // Default for non-time charts

            // Visual width = how many chart bars one underlying period spans
            return (double)UnderlyingTimeframe / chartMinutes;
        }

        private int GetChartTimeframeMinutes()
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
                    return 1; // Treat as 1-minute for width calculation
                default:
                    return 0;
            }
        }

        private void DrawSingleNewBlock(int bucket, int blockNumber, int timePeriodIndex)
        {
            double boxHeight = FixedBoxHeight * Instrument.MasterInstrument.TickSize;
            double bottomPrice = alignedSessionLow + (bucket * boxHeight);
            double topPrice = bottomPrice + boxHeight;

            // Calculate visual block width
            double visualBlockWidthMinutes = GetVisualBlockWidth() * GetChartTimeframeMinutes();
            if (visualBlockWidthMinutes <= 0) visualBlockWidthMinutes = UnderlyingTimeframe;

            // Stack blocks horizontally from session start with offset
            // Block position is based on block number (1st, 2nd, 3rd, etc.), not time period
            // Add horizontal offset to shift blocks away from candle wicks
            double offsetMinutes = BlockHorizontalOffset * GetChartTimeframeMinutes();
            DateTime blockStart = sessionStartTime.AddMinutes((blockNumber - 1) * visualBlockWidthMinutes + offsetMinutes);
            DateTime blockEnd = blockStart.AddMinutes(visualBlockWidthMinutes);

            DateTime actualSessionEnd = GetSessionEndTime();
            if (blockEnd > actualSessionEnd)
            {
                blockEnd = actualSessionEnd;
            }
            if (blockStart >= actualSessionEnd)
            {
                return;
            }

            string tag = $"TPO_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNumber}";

            if (ShowBlocks)
            {
                Brush fillBrush = Brushes.Transparent;
                if (FillBlocks)
                {
                    fillBrush = BlockColor.Clone();
                    fillBrush.Opacity = TPOOpacity / 100.0;
                }

                Draw.Rectangle(this, tag, false, blockStart, bottomPrice, blockEnd, topPrice,
                    BlockColor, fillBrush, 1);
            }

            if (ShowLetters)
            {
                // Adjust the letter to account for the minimum time period offset
                string letter = GetTPOLetter(timePeriodIndex - minTimePeriodIndex);
                string letterTag = $"Letter_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNumber}";

                DateTime letterTime = blockStart.AddMinutes(visualBlockWidthMinutes / 2);
                double letterPrice = (bottomPrice + topPrice) / 2;

                Draw.Text(this, letterTag, false, letter, letterTime, letterPrice, 0,
                    LetterColor, new SimpleFont("Arial", LetterSize), TextAlignment.Center,
                    Brushes.Transparent, Brushes.Transparent, 0);
            }
        }

        private void DrawAllBlocks()
        {
            double boxHeight = FixedBoxHeight * Instrument.MasterInstrument.TickSize;
            double visualBlockWidthMinutes = GetVisualBlockWidth() * GetChartTimeframeMinutes();
            if (visualBlockWidthMinutes <= 0) visualBlockWidthMinutes = UnderlyingTimeframe;

            foreach (var kvp in bucketTimePeriods)
            {
                int bucket = kvp.Key;
                var timePeriods = kvp.Value.OrderBy(x => x).ToList();

                for (int i = 0; i < timePeriods.Count; i++)
                {
                    int timePeriodIndex = timePeriods[i];
                    int blockNumber = i + 1;

                    double bottomPrice = alignedSessionLow + (bucket * boxHeight);
                    double topPrice = bottomPrice + boxHeight;

                    // Stack blocks horizontally from session start with offset
                    double offsetMinutes = BlockHorizontalOffset * GetChartTimeframeMinutes();
                    DateTime blockStart = sessionStartTime.AddMinutes((blockNumber - 1) * visualBlockWidthMinutes + offsetMinutes);
                    DateTime blockEnd = blockStart.AddMinutes(visualBlockWidthMinutes);

                    DateTime actualSessionEnd = GetSessionEndTime();
                    if (blockEnd > actualSessionEnd)
                    {
                        blockEnd = actualSessionEnd;
                    }
                    if (blockStart >= actualSessionEnd)
                    {
                        continue;
                    }

                    string tag = $"TPO_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNumber}";

                    if (ShowBlocks)
                    {
                        Brush fillBrush = Brushes.Transparent;
                        if (FillBlocks)
                        {
                            fillBrush = BlockColor.Clone();
                            fillBrush.Opacity = TPOOpacity / 100.0;
                        }

                        Draw.Rectangle(this, tag, false, blockStart, bottomPrice, blockEnd, topPrice,
                            BlockColor, fillBrush, 1);
                    }

                    if (ShowLetters)
                    {
                        // Adjust the letter to account for the minimum time period offset
                        string letter = GetTPOLetter(timePeriodIndex - minTimePeriodIndex);
                        string letterTag = $"Letter_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNumber}";
                        DateTime letterTime = blockStart.AddMinutes(visualBlockWidthMinutes / 2);
                        double letterPrice = (bottomPrice + topPrice) / 2;

                        Draw.Text(this, letterTag, false, letter, letterTime, letterPrice, 0,
                            LetterColor, new SimpleFont("Arial", LetterSize), TextAlignment.Center,
                            Brushes.Transparent, Brushes.Transparent, 0);
                    }
                }
            }
        }

        private void UpdateValueArea()
        {
            if (bucketTimePeriods.Count == 0)
                return;

            string dateString = currentSessionDate.ToString("yyyyMMdd");
            RemoveDrawObject($"POC_{dateString}");
            RemoveDrawObject($"VAH_{dateString}");
            RemoveDrawObject($"VAL_{dateString}");

            var stats = CalculateMarketProfileStats();
            DateTime endTime = GetSessionEndTime();

            // Apply the same horizontal offset as blocks
            double offsetMinutes = BlockHorizontalOffset * GetChartTimeframeMinutes();
            DateTime offsetStartTime = sessionStartTime.AddMinutes(offsetMinutes);
            DateTime offsetEndTime = endTime.AddMinutes(offsetMinutes);

            if (ShowPOC)
            {
                Draw.Line(this, $"POC_{dateString}", false, offsetStartTime, stats.POC, offsetEndTime, stats.POC,
                    POCColor, DashStyleHelper.Solid, 2);
            }

            if (ShowVAH)
            {
                Draw.Line(this, $"VAH_{dateString}", false, offsetStartTime, stats.VAH, offsetEndTime, stats.VAH,
                    VAHColor, DashStyleHelper.Dash, 1);
            }

            if (ShowVAL)
            {
                Draw.Line(this, $"VAL_{dateString}", false, offsetStartTime, stats.VAL, offsetEndTime, stats.VAL,
                    VALColor, DashStyleHelper.Dash, 1);
            }
        }

        private string GetTPOLetter(int adjustedIndex)
        {
            // Letters based on adjusted time period index (offset by minimum)
            // This ensures first period always gets 'A'
            if (adjustedIndex < 0) adjustedIndex = 0; // Safety check

            if (adjustedIndex < 26)
                return ((char)('A' + adjustedIndex)).ToString();
            else if (adjustedIndex < 52)
                return ((char)('a' + (adjustedIndex - 26))).ToString();
            else
            {
                int repeatingIndex = (adjustedIndex - 52) % 52;
                if (repeatingIndex < 26)
                    return ((char)('A' + repeatingIndex)).ToString();
                else
                    return ((char)('a' + (repeatingIndex - 26))).ToString();
            }
        }

        private MarketProfileStats CalculateMarketProfileStats()
        {
            var stats = new MarketProfileStats();

            if (bucketTimePeriods.Count == 0)
                return stats;

            // Create array of bucket counts
            int maxBucket = bucketTimePeriods.Keys.Max();
            int[] bucketCounts = new int[maxBucket + 1];

            foreach (var kvp in bucketTimePeriods)
            {
                bucketCounts[kvp.Key] = kvp.Value.Count;
            }

            // Find POC - bucket with highest TPO count
            int pocBucket = 0;
            int maxCount = 0;
            for (int i = 0; i < bucketCounts.Length; i++)
            {
                if (bucketCounts[i] > maxCount)
                {
                    maxCount = bucketCounts[i];
                    pocBucket = i;
                }
            }

            double boxHeight = FixedBoxHeight * Instrument.MasterInstrument.TickSize;
            stats.POC = alignedSessionLow + (pocBucket * boxHeight) + (boxHeight / 2);

            // Calculate total TPO count
            int totalTPOs = bucketCounts.Sum();
            int targetValueAreaTPOs = (int)(totalTPOs * (ValueAreaPercentage / 100.0));

            // Find Value Area High and Low
            int valueAreaTPOs = bucketCounts[pocBucket];
            int upperBucket = pocBucket;
            int lowerBucket = pocBucket;

            // Expand around POC until we reach target value area percentage
            while (valueAreaTPOs < targetValueAreaTPOs && (upperBucket < bucketCounts.Length - 1 || lowerBucket > 0))
            {
                int upperTPOs = (upperBucket < bucketCounts.Length - 1) ? bucketCounts[upperBucket + 1] : 0;
                int lowerTPOs = (lowerBucket > 0) ? bucketCounts[lowerBucket - 1] : 0;

                if (upperTPOs >= lowerTPOs && upperBucket < bucketCounts.Length - 1)
                {
                    upperBucket++;
                    valueAreaTPOs += upperTPOs;
                }
                else if (lowerBucket > 0)
                {
                    lowerBucket--;
                    valueAreaTPOs += lowerTPOs;
                }
                else
                    break;
            }

            stats.VAH = alignedSessionLow + (upperBucket * boxHeight) + (boxHeight / 2);
            stats.VAL = alignedSessionLow + (lowerBucket * boxHeight) + (boxHeight / 2);

            return stats;
        }

        private DateTime GetSessionEndTime()
        {
            SessionIterator sessionIterator = new SessionIterator(Bars);
            sessionIterator.GetTradingDay(currentSessionDate);

            if (sessionIterator.ActualSessionEnd != DateTime.MinValue)
            {
                return sessionIterator.ActualSessionEnd;
            }
            else
            {
                return sessionStartTime.AddHours(8);
            }
        }

        #region Properties
        [Range(5, 120)]
        [Display(Name = "Underlying Timeframe (Minutes)", Description = "Timeframe for TPO calculation (e.g., 30 for 30-minute periods)", Order = 1, GroupName = "Display")]
        public int UnderlyingTimeframe { get; set; }

        [Range(10, 100)]
        [Display(Name = "TPO Opacity", Description = "Opacity of TPO blocks (10-100%)", Order = 2, GroupName = "Display")]
        public int TPOOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Block Color", Description = "Color of the TPO blocks", Order = 3, GroupName = "Display")]
        public Brush BlockColor { get; set; }

        [Browsable(false)]
        public string BlockColorSerialize
        {
            get { return Serialize.BrushToString(BlockColor); }
            set { BlockColor = Serialize.StringToBrush(value); }
        }

        [Range(0.0, 2.0)]
        [Display(Name = "Block Horizontal Offset", Description = "Horizontal offset for blocks in bar widths (0.5 = half bar)", Order = 4, GroupName = "Display")]
        public double BlockHorizontalOffset { get; set; }

        [Display(Name = "Show VAH", Description = "Show Value Area High line", Order = 6, GroupName = "Market Profile")]
        public bool ShowVAH { get; set; }

        [Display(Name = "Show VAL", Description = "Show Value Area Low line", Order = 7, GroupName = "Market Profile")]
        public bool ShowVAL { get; set; }

        [Display(Name = "Show POC", Description = "Show Point of Control line", Order = 8, GroupName = "Market Profile")]
        public bool ShowPOC { get; set; }

        [Range(50, 90)]
        [Display(Name = "Value Area %", Description = "Percentage of volume for Value Area calculation", Order = 9, GroupName = "Market Profile")]
        public int ValueAreaPercentage { get; set; }

        [XmlIgnore]
        [Display(Name = "VAH Color", Description = "Color of the Value Area High line", Order = 10, GroupName = "Market Profile")]
        public Brush VAHColor { get; set; }

        [Browsable(false)]
        public string VAHColorSerialize
        {
            get { return Serialize.BrushToString(VAHColor); }
            set { VAHColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "VAL Color", Description = "Color of the Value Area Low line", Order = 11, GroupName = "Market Profile")]
        public Brush VALColor { get; set; }

        [Browsable(false)]
        public string VALColorSerialize
        {
            get { return Serialize.BrushToString(VALColor); }
            set { VALColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "POC Color", Description = "Color of the Point of Control line", Order = 12, GroupName = "Market Profile")]
        public Brush POCColor { get; set; }

        [Browsable(false)]
        public string POCColorSerialize
        {
            get { return Serialize.BrushToString(POCColor); }
            set { POCColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Show Blocks", Description = "Show TPO blocks/rectangles", Order = 13, GroupName = "Display")]
        public bool ShowBlocks { get; set; }

        [Display(Name = "Show Letters", Description = "Show letters in TPO blocks", Order = 14, GroupName = "Letters")]
        public bool ShowLetters { get; set; }

        [XmlIgnore]
        [Display(Name = "Letter Color", Description = "Color of the letters in TPO blocks", Order = 15, GroupName = "Letters")]
        public Brush LetterColor { get; set; }

        [Browsable(false)]
        public string LetterColorSerialize
        {
            get { return Serialize.BrushToString(LetterColor); }
            set { LetterColor = Serialize.StringToBrush(value); }
        }

        [Range(6, 20)]
        [Display(Name = "Letter Size", Description = "Font size of letters in TPO blocks", Order = 16, GroupName = "Letters")]
        public int LetterSize { get; set; }

        [Display(Name = "Fill Blocks", Description = "Fill TPO blocks with color", Order = 17, GroupName = "Display")]
        public bool FillBlocks { get; set; }

        [Range(1, 100)]
        [Display(Name = "Box Height (Ticks)", Description = "Height of TPO boxes in ticks", Order = 18, GroupName = "Display")]
        public int FixedBoxHeight { get; set; }

        [Display(Name = "Levels Per Session", Description = "Number of horizontal TPO levels per session (no upper limit)", Order = 20, GroupName = "Display")]
        public int LevelsPerSession { get; set; }
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

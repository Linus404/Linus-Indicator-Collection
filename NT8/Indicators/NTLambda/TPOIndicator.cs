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
        // Simple bucket system
        private int[] bucketCounts; // Count of TPO blocks per bucket (index 0 to LevelsPerSession-1)
        private double sessionHigh = double.MinValue;
        private double sessionLow = double.MaxValue;
        private DateTime currentSessionDate = DateTime.MinValue;
        private DateTime sessionStartTime = DateTime.MinValue;

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

                BlockWidthMinutes = 30;
                BlockColor = Brushes.LightBlue;
                SessionSizePercent = 100;
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
                LetterColor = Brushes.Blue;
                LetterSize = 8;
                LevelsPerSession = 15;
                FillBlocks = true;

                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TPOPlot");
            }
            else if (State == State.DataLoaded)
            {
                bucketCounts = new int[LevelsPerSession];
            }
            else if (State == State.Terminated)
            {
                // Clean up on termination
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
            sessionStartTime = Time[0];
            sessionHigh = High[0];
            sessionLow = Low[0];
            bucketCounts = new int[LevelsPerSession];
            
            // Clear all previous session drawings
            ClearSessionDrawings();
        }
        
        private void ClearSessionDrawings()
        {
            string dateString = currentSessionDate.ToString("yyyyMMdd");
            
            // Remove all TPO and Letter objects for this session
            var objectsToRemove = new List<string>();
            foreach (var drawObject in DrawObjects)
            {
                if (drawObject.Tag.StartsWith($"TPO_{dateString}_") ||
                    drawObject.Tag.StartsWith($"Letter_{dateString}_") ||
                    drawObject.Tag == $"POC_{dateString}" ||
                    drawObject.Tag == $"VAH_{dateString}" ||
                    drawObject.Tag == $"VAL_{dateString}"))
                {
                    objectsToRemove.Add(drawObject.Tag);
                }
            }
            
            foreach (var tag in objectsToRemove)
            {
                RemoveDrawObject(tag);
            }
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
                newExtreme = true;
            }
            
            // If new extreme, need to redraw everything
            if (newExtreme)
            {
                RedrawEntireSession();
                return;
            }
            
            // Normal processing - check which buckets this bar touches
            double sessionRange = sessionHigh - sessionLow;
            if (sessionRange <= 0)
                sessionRange = Instrument.MasterInstrument.TickSize * LevelsPerSession;
                
            double bucketHeight = sessionRange / LevelsPerSession;
            
            for (int bucket = 0; bucket < LevelsPerSession; bucket++)
            {
                double bucketBottom = sessionLow + (bucket * bucketHeight);
                double bucketTop = sessionLow + ((bucket + 1) * bucketHeight);
                
                // Proper intersection check - last bucket includes sessionHigh
                bool intersects = (bucket == LevelsPerSession - 1) ? 
                    (high >= bucketBottom && low <= bucketTop) : 
                    (high >= bucketBottom && low < bucketTop);
                    
                if (intersects)
                {
                    bucketCounts[bucket]++; // Add 1 to bucket
                    // Draw only the NEW block for this bucket
                    DrawSingleNewBlock(bucket, bucketCounts[bucket]);
                }
            }
            
            UpdateValueArea();
        }

        private void RedrawEntireSession()
        {
            // Clear all drawings for this session
            ClearSessionDrawings();
            
            // Recalculate all buckets from scratch
            bucketCounts = new int[LevelsPerSession];
            
            double sessionRange = sessionHigh - sessionLow;
            if (sessionRange <= 0)
                sessionRange = Instrument.MasterInstrument.TickSize * LevelsPerSession;
                
            double bucketHeight = sessionRange / LevelsPerSession;
            
            // Go through all bars in current session and recalculate
            for (int i = 0; i <= CurrentBar; i++)
            {
                if (Time[i].Date != currentSessionDate)
                    continue;
                    
                for (int bucket = 0; bucket < LevelsPerSession; bucket++)
                {
                    double bucketBottom = sessionLow + (bucket * bucketHeight);
                    double bucketTop = sessionLow + ((bucket + 1) * bucketHeight);
                    
                    // Proper intersection check
                    bool intersects = (bucket == LevelsPerSession - 1) ? 
                        (High[i] >= bucketBottom && Low[i] <= bucketTop) : 
                        (High[i] >= bucketBottom && Low[i] < bucketTop);
                        
                    if (intersects)
                    {
                        bucketCounts[bucket]++;
                    }
                }
            }
            
            // Draw all blocks
            DrawAllBlocks();
            UpdateValueArea();
        }

        private void DrawSingleNewBlock(int bucket, int blockNumber)
        {
            double sessionRange = sessionHigh - sessionLow;
            if (sessionRange <= 0)
                sessionRange = Instrument.MasterInstrument.TickSize * LevelsPerSession;
                
            double bucketHeight = sessionRange / LevelsPerSession;
            double blockWidth = BlockWidthMinutes * (SessionSizePercent / 100.0);
            
            // Calculate price boundaries
            double bottomPrice = sessionLow + (bucket * bucketHeight);
            double topPrice = sessionLow + ((bucket + 1) * bucketHeight);
            
            // Calculate time position for this specific block
            DateTime blockStart = sessionStartTime.AddMinutes((blockNumber - 1) * blockWidth);
            DateTime blockEnd = blockStart.AddMinutes(blockWidth);
            
            string tag = $"TPO_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNumber}";
            
            if (ShowBlocks)
            {
                Brush fillBrush = FillBlocks ? BlockColor.Clone() : Brushes.Transparent;
                if (FillBlocks) fillBrush.Opacity = TPOOpacity / 100.0;
                
                Draw.Rectangle(this, tag, false, blockStart, bottomPrice, blockEnd, topPrice,
                    BlockColor, fillBrush, 1);
            }
            
            if (ShowLetters)
            {
                string letter = GetTPOLetter(blockNumber);
                string letterTag = $"Letter_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNumber}";
                DateTime letterTime = blockStart.AddMinutes(blockWidth / 2);
                double letterPrice = (bottomPrice + topPrice) / 2;
                
                Draw.Text(this, letterTag, false, letter, letterTime, letterPrice, 0,
                    LetterColor, new SimpleFont("Arial", LetterSize), TextAlignment.Center,
                    Brushes.Transparent, Brushes.Transparent, 0);
            }
        }

        private void DrawAllBlocks()
        {
            double bucketHeight = (sessionHigh - sessionLow) / LevelsPerSession;
            double blockWidth = BlockWidthMinutes * (SessionSizePercent / 100.0);
            
            for (int bucket = 0; bucket < LevelsPerSession; bucket++)
            {
                int count = bucketCounts[bucket];
                if (count == 0) continue;
                
                // Draw all blocks for this bucket
                for (int blockNum = 1; blockNum <= count; blockNum++)
                {
                    double bottomPrice = sessionLow + (bucket * bucketHeight);
                    double topPrice = sessionLow + ((bucket + 1) * bucketHeight);
                    
                    DateTime blockStart = sessionStartTime.AddMinutes((blockNum - 1) * blockWidth);
                    DateTime blockEnd = blockStart.AddMinutes(blockWidth);
                    
                    string tag = $"TPO_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNum}";
                    
                    if (ShowBlocks)
                    {
                        Brush fillBrush = FillBlocks ? BlockColor.Clone() : Brushes.Transparent;
                        if (FillBlocks) fillBrush.Opacity = TPOOpacity / 100.0;
                        
                        Draw.Rectangle(this, tag, false, blockStart, bottomPrice, blockEnd, topPrice,
                            BlockColor, fillBrush, 1);
                    }
                    
                    if (ShowLetters)
                    {
                        string letter = GetTPOLetter(blockNum);
                        string letterTag = $"Letter_{currentSessionDate:yyyyMMdd}_{bucket}_{blockNum}";
                        DateTime letterTime = blockStart.AddMinutes(blockWidth / 2);
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
            if (bucketCounts.Length == 0)
                return;

            // Remove old VA lines
            string dateString = currentSessionDate.ToString("yyyyMMdd");
            RemoveDrawObject($"POC_{dateString}");
            RemoveDrawObject($"VAH_{dateString}");
            RemoveDrawObject($"VAL_{dateString}");
            
            // Calculate POC and VA
            var stats = CalculateMarketProfileStats();
            
            DateTime endTime = sessionStartTime.AddHours(8); // Approximate session end
            
            if (ShowPOC)
            {
                Draw.Line(this, $"POC_{dateString}", false, sessionStartTime, stats.POC, endTime, stats.POC,
                    POCColor, DashStyleHelper.Solid, 2);
            }
            
            if (ShowVAH)
            {
                Draw.Line(this, $"VAH_{dateString}", false, sessionStartTime, stats.VAH, endTime, stats.VAH,
                    VAHColor, DashStyleHelper.Dash, 1);
            }
            
            if (ShowVAL)
            {
                Draw.Line(this, $"VAL_{dateString}", false, sessionStartTime, stats.VAL, endTime, stats.VAL,
                    VALColor, DashStyleHelper.Dash, 1);
            }
        }


        private string GetTPOLetter(int index)
        {
            // TPO convention: A-Z, then a-z, then repeating pattern
            if (index < 26)
                return ((char)('A' + index)).ToString();
            else if (index < 52)
                return ((char)('a' + (index - 26))).ToString();
            else
            {
                int repeatingIndex = (index - 52) % 52;
                if (repeatingIndex < 26)
                    return ((char)('A' + repeatingIndex)).ToString();
                else
                    return ((char)('a' + (repeatingIndex - 26))).ToString();
            }
        }

        private MarketProfileStats CalculateMarketProfileStats()
        {
            var stats = new MarketProfileStats();

            if (bucketCounts.Length == 0)
                return stats;

            // Find POC - bucket with highest TPO count
            int pocBucket = 0;
            int maxCount = 0;
            for (int i = 0; i < LevelsPerSession; i++)
            {
                if (bucketCounts[i] > maxCount)
                {
                    maxCount = bucketCounts[i];
                    pocBucket = i;
                }
            }
            
            double bucketHeight = (sessionHigh - sessionLow) / LevelsPerSession;
            stats.POC = sessionLow + (pocBucket * bucketHeight) + (bucketHeight / 2);

            // Calculate total TPO count
            int totalTPOs = bucketCounts.Sum();
            int targetValueAreaTPOs = (int)(totalTPOs * (ValueAreaPercentage / 100.0));

            // Find Value Area High and Low
            int valueAreaTPOs = bucketCounts[pocBucket];
            int upperBucket = pocBucket;
            int lowerBucket = pocBucket;

            // Expand around POC until we reach target value area percentage
            while (valueAreaTPOs < targetValueAreaTPOs && (upperBucket < LevelsPerSession - 1 || lowerBucket > 0))
            {
                int upperTPOs = (upperBucket < LevelsPerSession - 1) ? bucketCounts[upperBucket + 1] : 0;
                int lowerTPOs = (lowerBucket > 0) ? bucketCounts[lowerBucket - 1] : 0;

                if (upperTPOs >= lowerTPOs && upperBucket < LevelsPerSession - 1)
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

            stats.VAH = sessionLow + (upperBucket * bucketHeight) + (bucketHeight / 2);
            stats.VAL = sessionLow + (lowerBucket * bucketHeight) + (bucketHeight / 2);

            return stats;
        }

        private void DrawMarketProfileLines(DateTime sessionDate, MarketProfileStats stats, DateTime startTime, DateTime endTime)
        {
            string dateString = sessionDate.ToString("yyyyMMdd");

            if (ShowPOC)
            {
                string pocTag = $"POC_{dateString}";
                Draw.Line(this, pocTag, false, startTime, stats.POC, endTime, stats.POC,
                    POCColor, DashStyleHelper.Solid, 2);
            }

            if (ShowVAH)
            {
                string vahTag = $"VAH_{dateString}";
                Draw.Line(this, vahTag, false, startTime, stats.VAH, endTime, stats.VAH,
                    VAHColor, DashStyleHelper.Dash, 1);
            }

            if (ShowVAL)
            {
                string valTag = $"VAL_{dateString}";
                Draw.Line(this, valTag, false, startTime, stats.VAL, endTime, stats.VAL,
                    VALColor, DashStyleHelper.Dash, 1);
            }
        }

        private void RemoveDrawObjectsForSession(DateTime sessionDate)
        {
            string dateString = sessionDate.ToString("yyyyMMdd");

            // Remove draw objects by iterating and removing individually
            var objectsToRemove = new List<string>();

            // Collect all draw object keys that match our patterns
            foreach (var drawObject in DrawObjects)
            {
                if (drawObject.Tag.StartsWith($"TPO_{dateString}") ||
                    drawObject.Tag.StartsWith($"Letter_{dateString}") ||
                    drawObject.Tag == $"POC_{dateString}" ||
                    drawObject.Tag == $"VAH_{dateString}" ||
                    drawObject.Tag == $"VAL_{dateString}")
                {
                    objectsToRemove.Add(drawObject.Tag);
                }
            }

            // Remove collected objects
            foreach (var tag in objectsToRemove)
            {
                RemoveDrawObject(tag);
            }
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


        #region Properties
        [Range(5, 120)]
        [Display(Name = "Block Width (Minutes)", Description = "Width of each TPO block in minutes", Order = 1, GroupName = "Display")]
        public int BlockWidthMinutes { get; set; }

        [Range(10, 200)]
        [Display(Name = "Session Size (%)", Description = "Size of TPO blocks relative to session duration as percentage", Order = 2, GroupName = "Display")]
        public int SessionSizePercent { get; set; }


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

        [Display(Name = "Fill Blocks", Description = "Fill TPO blocks with color", Order = 18, GroupName = "Display")]
        public bool FillBlocks { get; set; }

        [Range(5, 200)]
        [Display(Name = "Levels Per Session", Description = "Number of horizontal TPO levels per session", Order = 18, GroupName = "Display")]
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

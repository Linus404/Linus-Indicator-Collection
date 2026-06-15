#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public class NTLVolumeFilter : Indicator
    {
        private struct VolumeMarker
        {
            public double Price;
            public bool Up;
            public double Volume;
            public int Count;
            public long Sequence;
            public double LowPrice;
            public double HighPrice;
        }

        private readonly Dictionary<int, List<VolumeMarker>> markersByPrimaryBar = new Dictionary<int, List<VolumeMarker>>();
        private double lastPrice = double.NaN;
        private long markerSequence;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Volume Filter";
                Description = "Marks trades whose volume exceeds a threshold.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                MinimumVolume = 100;
                MergeNodes = true;
                MergeDistanceTicks = 2;
                MinNodeFontSize = 12;
                MaxNodeFontSize = 30;
                UseDeltaColor = true;
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
                ProcessTickSeries();
                return;
            }
            if (BarsInProgress != 0) return;

            DrawMarkers(CurrentBar, 0);
        }

        private void ProcessTickSeries()
        {
            int primaryBar;
            double price, bid, ask, volume;
            if (!TryGetTick(out primaryBar, out price, out bid, out ask, out volume)) return;

            if (volume >= MinimumVolume)
            {
                bool up = NTLCore.IsAskTrade(price, bid, ask, lastPrice);
                AddMarker(primaryBar, price, up, volume);
            }
            lastPrice = price;
        }

        private void AddMarker(int primaryBar, double price, bool up, double volume)
        {
            List<VolumeMarker> markers;
            if (!markersByPrimaryBar.TryGetValue(primaryBar, out markers))
            {
                markers = new List<VolumeMarker>();
                markersByPrimaryBar[primaryBar] = markers;
            }

            double normalizedPrice = NTLCore.RoundToTick(this, price);
            markers.Add(new VolumeMarker { Price = normalizedPrice, Up = up, Volume = volume, Count = 1, Sequence = markerSequence++, LowPrice = normalizedPrice, HighPrice = normalizedPrice });
        }

        private void DrawMarkers(int primaryBar, int barsAgo)
        {
            List<VolumeMarker> markers;
            if (!markersByPrimaryBar.TryGetValue(primaryBar, out markers)) return;
            List<VolumeMarker> displayMarkers = BuildDisplayMarkers(markers);
            double minDisplayVolume = Math.Min(MinimumVolume, MinMarkerVolume(displayMarkers));
            double maxDisplayVolume = Math.Max(minDisplayVolume, MaxMarkerVolume(displayMarkers));
            foreach (var marker in displayMarkers)
            {
                System.Windows.Media.Brush brush = !UseDeltaColor || marker.Up ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.IndianRed;
                string tag = "NTLVF_" + primaryBar + "_" + marker.Sequence;
                int fontSize = NodeFontSize(marker, minDisplayVolume, maxDisplayVolume);
                Draw.Text(this, tag, false, "\u25CF", barsAgo, marker.Price, 0, brush, new SimpleFont("Arial", fontSize),
                    System.Windows.TextAlignment.Center, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
            }
            markersByPrimaryBar.Remove(primaryBar);
        }

        private List<VolumeMarker> BuildDisplayMarkers(List<VolumeMarker> markers)
        {
            if (!MergeNodes || markers.Count <= 1)
                return markers;

            double mergeDistance = Math.Max(0, MergeDistanceTicks) * NTLCore.TickSize(this);
            var mergedMarkers = new List<VolumeMarker>();

            foreach (bool side in new[] { true, false })
            {
                List<VolumeMarker> sideMarkers = markers.Where(m => m.Up == side).OrderBy(m => m.Price).ThenBy(m => m.Sequence).ToList();
                VolumeMarker? active = null;
                foreach (var marker in sideMarkers)
                {
                    VolumeMarker displayMarker = NormalizeMarker(marker);
                    if (!active.HasValue)
                    {
                        active = displayMarker;
                        continue;
                    }

                    VolumeMarker cluster = active.Value;
                    if (DistanceToCluster(cluster, displayMarker.Price) <= mergeDistance)
                    {
                        active = MergeMarkers(cluster, displayMarker);
                        continue;
                    }

                    mergedMarkers.Add(cluster);
                    active = displayMarker;
                }
                if (active.HasValue)
                    mergedMarkers.Add(active.Value);
            }

            return mergedMarkers.OrderBy(m => m.Sequence).ToList();
        }

        private VolumeMarker NormalizeMarker(VolumeMarker marker)
        {
            if (marker.LowPrice == 0 && marker.HighPrice == 0)
            {
                marker.LowPrice = marker.Price;
                marker.HighPrice = marker.Price;
            }
            marker.Count = Math.Max(1, marker.Count);
            return marker;
        }

        private VolumeMarker MergeMarkers(VolumeMarker merged, VolumeMarker displayMarker)
        {
            double combinedVolume = merged.Volume + displayMarker.Volume;
            merged.Price = combinedVolume > 0
                ? NTLCore.RoundToTick(this, ((merged.Price * merged.Volume) + (displayMarker.Price * displayMarker.Volume)) / combinedVolume)
                : displayMarker.Price;
            merged.Volume = combinedVolume;
            merged.Count += Math.Max(1, displayMarker.Count);
            merged.Sequence = Math.Min(merged.Sequence, displayMarker.Sequence);
            merged.LowPrice = Math.Min(merged.LowPrice, displayMarker.LowPrice);
            merged.HighPrice = Math.Max(merged.HighPrice, displayMarker.HighPrice);
            return merged;
        }

        private double DistanceToCluster(VolumeMarker marker, double price)
        {
            if (price < marker.LowPrice)
                return marker.LowPrice - price;
            if (price > marker.HighPrice)
                return price - marker.HighPrice;
            return 0;
        }

        private double MaxMarkerVolume(List<VolumeMarker> markers)
        {
            double max = MinimumVolume;
            foreach (var marker in markers)
                max = Math.Max(max, marker.Volume);
            return max;
        }

        private double MinMarkerVolume(List<VolumeMarker> markers)
        {
            if (markers == null || markers.Count == 0)
                return MinimumVolume;
            double min = double.MaxValue;
            foreach (var marker in markers)
                min = Math.Min(min, marker.Volume);
            return min == double.MaxValue ? MinimumVolume : min;
        }

        private int NodeFontSize(VolumeMarker marker, double minDisplayVolume, double maxDisplayVolume)
        {
            int minFont = Math.Max(6, MinNodeFontSize);
            int maxFont = Math.Max(minFont, MaxNodeFontSize);
            double denominator = Math.Max(1, maxDisplayVolume - minDisplayVolume);
            double volumeRatio = denominator <= 1 ? 0 : Math.Max(0, Math.Min(1, (marker.Volume - minDisplayVolume) / denominator));
            volumeRatio = Math.Sqrt(volumeRatio);
            double countBoost = Math.Min(0.35, Math.Max(0, marker.Count - 1) * 0.08);
            double sizeRatio = Math.Max(0, Math.Min(1, volumeRatio + countBoost));
            return minFont + (int)Math.Round((maxFont - minFont) * sizeRatio);
        }

        private bool TryGetTick(out int primaryBar, out double price, out double bid, out double ask, out double volume)
        {
            primaryBar = -1;
            price = bid = ask = volume = 0;
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

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Minimum volume", GroupName = "Calculation", Order = 0)]
        public int MinimumVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Merge close nodes", GroupName = "Visual", Order = 1)]
        public bool MergeNodes { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Merge distance (ticks)", GroupName = "Visual", Order = 2)]
        public int MergeDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(6, 100)]
        [Display(Name = "Min node font size", GroupName = "Visual", Order = 3)]
        public int MinNodeFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(6, 200)]
        [Display(Name = "Max node font size", GroupName = "Visual", Order = 4)]
        public int MaxNodeFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use delta color", GroupName = "Visual", Order = 5)]
        public bool UseDeltaColor { get; set; }
    }
}

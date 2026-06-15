#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using DxBrush = SharpDX.Direct2D1.Brush;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools.NTLambda
{
    public class NTLFixedRangeVolumeProfile : DrawingTool
    {
        private sealed class ProfileRow
        {
            public double Volume;
            public bool IsValueArea;
            public bool IsPoc;
        }

        private DxBrush volumeBrushDx;
        private DxBrush valueAreaBrushDx;
        private DxBrush pocBrushDx;
        private DxBrush rangeBrushDx;

        public override object Icon
        {
            get
            {
                Grid icon = new Grid { Height = 16, Width = 16, UseLayoutRounding = true, SnapsToDevicePixels = true };
                RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
                icon.Children.Add(new Path
                {
                    Stroke = Application.Current.TryFindResource("MenuBorderBrush") as WpfBrush,
                    StrokeThickness = 1,
                    Data = Geometry.Parse("M 2 2 V 14 M 13 2 V 14 M 3 4 H 10 M 3 7 H 13 M 3 10 H 8 M 3 13 H 11")
                });
                return icon;
            }
        }

        private ChartBars ChartBars
        {
            get
            {
                ChartBars chartBars = AttachedTo != null ? AttachedTo.ChartObject as ChartBars : null;
                if (chartBars == null && AttachedTo != null)
                {
                    Gui.NinjaScript.IChartBars iChartBars = AttachedTo.ChartObject as Gui.NinjaScript.IChartBars;
                    if (iChartBars != null) chartBars = iChartBars.ChartBars;
                }
                return chartBars;
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Fixed Range Volume Profile (NTL)";
                Description = "Fixed range volume profile between start/end anchors, or from start to latest bar when no end anchor is used.";
                StartAnchor = new ChartAnchor { DisplayName = "Start", IsEditing = true };
                EndAnchor = new ChartAnchor { DisplayName = "End", IsEditing = true };
                StartAnchor.IsYPropertyVisible = false;
                EndAnchor.IsYPropertyVisible = false;

                UseEndAnchor = true;
                BucketTicks = 4;
                ProfileWidth = 180;
                ValueAreaPercent = 70;
                Opacity = 45;
                ValueAreaOpacity = 75;
                ShowPoc = true;
                ShowRange = true;
                VolumeColor = WpfBrushes.CornflowerBlue;
                ValueAreaColor = WpfBrushes.RoyalBlue;
                PocColor = WpfBrushes.Goldenrod;
                RangeColor = WpfBrushes.Gray;
            }
            else if (State == State.Terminated)
            {
                DisposeBrushes();
            }
        }

        public override IEnumerable<ChartAnchor> Anchors
        {
            get { return UseEndAnchor ? new[] { StartAnchor, EndAnchor } : new[] { StartAnchor }; }
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            if (DrawingState == DrawingState.Building)
                return Cursors.Pen;
            if (DrawingState == DrawingState.Moving)
                return IsLocked ? Cursors.No : Cursors.SizeAll;
            foreach (ChartAnchor anchor in Anchors)
            {
                if (anchor == null || anchor.IsEditing) continue;
                if ((point - anchor.GetPoint(chartControl, chartPanel, chartScale)).Length <= 12)
                    return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
            }
            return null;
        }

        public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            ChartPanel panel = chartControl.ChartPanels[chartScale.PanelIndex];
            return Anchors.Where(a => a != null && !a.IsEditing).Select(a => a.GetPoint(chartControl, panel, chartScale)).ToArray();
        }

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Building)
            {
                if (StartAnchor.IsEditing)
                {
                    dataPoint.CopyDataValues(StartAnchor);
                    StartAnchor.IsEditing = false;
                    if (!UseEndAnchor)
                    {
                        dataPoint.CopyDataValues(EndAnchor);
                        EndAnchor.IsEditing = false;
                        DrawingState = DrawingState.Normal;
                        IsSelected = false;
                    }
                    return;
                }

                dataPoint.CopyDataValues(EndAnchor);
                EndAnchor.IsEditing = false;
                DrawingState = DrawingState.Normal;
                IsSelected = false;
                ForceRefresh();
                return;
            }

            if (DrawingState == DrawingState.Normal)
            {
                Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                if (GetCursor(chartControl, chartPanel, chartScale, point) != null)
                    DrawingState = DrawingState.Moving;
                else
                    IsSelected = false;
            }
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked || DrawingState != DrawingState.Moving)
                return;
            dataPoint.CopyDataValues(StartAnchor);
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing)
            {
                DrawingState = DrawingState.Normal;
                ForceRefresh();
            }
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
            ChartBars chartBars = ChartBars;
            if (!IsVisible || chartBars == null || chartBars.Bars == null || StartAnchor == null || StartAnchor.IsEditing)
                return;

            int startBar, endBar;
            if (!TryGetRange(chartBars, out startBar, out endBar))
                return;
            for (int bar = startBar; bar <= endBar; bar++)
            {
                MinValue = Math.Min(MinValue, chartBars.Bars.GetLow(bar));
                MaxValue = Math.Max(MaxValue, chartBars.Bars.GetHigh(bar));
            }
        }

        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            if (StartAnchor == null || StartAnchor.IsEditing)
                return false;
            if (!UseEndAnchor || EndAnchor == null || EndAnchor.IsEditing)
                return lastTimeOnChart >= StartAnchor.Time;
            return EndAnchor.Time >= firstTimeOnChart && StartAnchor.Time <= lastTimeOnChart;
        }

        public override void OnRenderTargetChanged()
        {
            DisposeBrushes();
            if (RenderTarget == null)
                return;
            volumeBrushDx = VolumeColor.ToDxBrush(RenderTarget);
            valueAreaBrushDx = ValueAreaColor.ToDxBrush(RenderTarget);
            pocBrushDx = PocColor.ToDxBrush(RenderTarget);
            rangeBrushDx = RangeColor.ToDxBrush(RenderTarget);
            volumeBrushDx.Opacity = Math.Max(5, Math.Min(100, Opacity)) / 100f;
            valueAreaBrushDx.Opacity = Math.Max(5, Math.Min(100, ValueAreaOpacity)) / 100f;
            rangeBrushDx.Opacity = 0.45f;
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            ChartBars chartBars = ChartBars;
            if (chartControl == null || chartScale == null || chartBars == null || chartBars.Bars == null || RenderTarget == null || StartAnchor == null || StartAnchor.IsEditing)
                return;
            if (volumeBrushDx == null || valueAreaBrushDx == null || pocBrushDx == null || rangeBrushDx == null)
                OnRenderTargetChanged();
            if (volumeBrushDx == null || valueAreaBrushDx == null || pocBrushDx == null || rangeBrushDx == null)
                return;

            int startBar, endBar;
            if (!TryGetRange(chartBars, out startBar, out endBar))
                return;

            Dictionary<double, ProfileRow> profile = BuildProfile(chartBars, startBar, endBar);
            if (profile.Count == 0)
                return;

            AnnotateProfile(profile);
            RenderProfile(chartControl, chartScale, chartBars, startBar, endBar, profile);
        }

        private bool TryGetRange(ChartBars chartBars, out int startBar, out int endBar)
        {
            startBar = endBar = -1;
            if (chartBars == null || chartBars.Bars == null || chartBars.Bars.Count == 0 || StartAnchor == null || StartAnchor.IsEditing)
                return false;

            startBar = Math.Max(0, Math.Min(chartBars.Bars.Count - 1, chartBars.Bars.GetBar(StartAnchor.Time)));
            if (UseEndAnchor && EndAnchor != null && !EndAnchor.IsEditing)
                endBar = Math.Max(0, Math.Min(chartBars.Bars.Count - 1, chartBars.Bars.GetBar(EndAnchor.Time)));
            else
                endBar = Math.Max(0, chartBars.Bars.Count - 1);

            if (endBar < startBar)
            {
                int tmp = startBar;
                startBar = endBar;
                endBar = tmp;
            }
            return startBar >= 0 && endBar >= startBar;
        }

        private Dictionary<double, ProfileRow> BuildProfile(ChartBars chartBars, int startBar, int endBar)
        {
            Dictionary<double, ProfileRow> profile = new Dictionary<double, ProfileRow>();
            double bucket = GetBucketSize(chartBars);
            for (int bar = startBar; bar <= endBar; bar++)
            {
                double low = Math.Floor(chartBars.Bars.GetLow(bar) / bucket) * bucket;
                double high = Math.Ceiling(chartBars.Bars.GetHigh(bar) / bucket) * bucket;
                int rows = Math.Max(1, (int)Math.Round((high - low) / bucket) + 1);
                double perRowVolume = Math.Max(0, chartBars.Bars.GetVolume(bar)) / rows;
                for (int i = 0; i < rows; i++)
                {
                    double price = low + i * bucket;
                    ProfileRow row;
                    if (!profile.TryGetValue(price, out row))
                    {
                        row = new ProfileRow();
                        profile[price] = row;
                    }
                    row.Volume += perRowVolume;
                }
            }
            return profile;
        }

        private void AnnotateProfile(Dictionary<double, ProfileRow> profile)
        {
            foreach (ProfileRow row in profile.Values)
            {
                row.IsPoc = false;
                row.IsValueArea = false;
            }

            var ordered = profile.OrderByDescending(kv => kv.Value.Volume).ToList();
            if (ordered.Count == 0)
                return;

            ordered[0].Value.IsPoc = true;
            double target = ordered.Sum(kv => kv.Value.Volume) * Math.Max(1, Math.Min(100, ValueAreaPercent)) / 100.0;
            double running = 0;
            foreach (var kv in ordered)
            {
                kv.Value.IsValueArea = true;
                running += kv.Value.Volume;
                if (running >= target)
                    break;
            }
        }

        private void RenderProfile(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars, int startBar, int endBar, Dictionary<double, ProfileRow> profile)
        {
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            float xStart = chartControl.GetXByBarIndex(chartBars, startBar);
            float xEnd = UseEndAnchor ? chartControl.GetXByBarIndex(chartBars, endBar) : chartControl.CanvasRight;
            if (xEnd < xStart)
            {
                float tmp = xStart;
                xStart = xEnd;
                xEnd = tmp;
            }
            float right = Math.Min(chartControl.CanvasRight, xEnd);
            float maxWidth = Math.Min(Math.Max(10, ProfileWidth), Math.Max(10, right - xStart));
            float profileRight = right;
            double maxVolume = Math.Max(1, profile.Values.Select(r => r.Volume).DefaultIfEmpty(1).Max());
            double bucket = GetBucketSize(chartBars);

            if (ShowRange)
                RenderTarget.DrawRectangle(new SharpDX.RectangleF(xStart, 0, Math.Max(1, right - xStart), chartControl.ChartPanels[chartScale.PanelIndex].H), rangeBrushDx, 1f);

            foreach (var kv in profile.OrderBy(row => row.Key))
            {
                if (!IsPriceVisible(chartScale, kv.Key))
                    continue;
                float yTop = chartScale.GetYByValue(kv.Key + bucket * 0.5);
                float yBottom = chartScale.GetYByValue(kv.Key - bucket * 0.5);
                float height = Math.Max(1, yBottom - yTop);
                float width = maxWidth * (float)(kv.Value.Volume / maxVolume);
                DxBrush brush = kv.Value.IsValueArea ? valueAreaBrushDx : volumeBrushDx;
                RenderTarget.FillRectangle(new SharpDX.RectangleF(profileRight - width, yTop, width, height), brush);
                if (ShowPoc && kv.Value.IsPoc)
                    RenderTarget.DrawLine(new SharpDX.Vector2(xStart, yTop + height * 0.5f), new SharpDX.Vector2(profileRight, yTop + height * 0.5f), pocBrushDx, 2f);
            }
        }

        private bool IsPriceVisible(ChartScale chartScale, double price)
        {
            double min = Math.Min(chartScale.MinValue, chartScale.MaxValue);
            double max = Math.Max(chartScale.MinValue, chartScale.MaxValue);
            return price >= min && price <= max;
        }

        private double GetBucketSize(ChartBars chartBars)
        {
            double tick = 1.0;
            try
            {
                if (chartBars.Bars.Instrument != null && chartBars.Bars.Instrument.MasterInstrument != null)
                    tick = chartBars.Bars.Instrument.MasterInstrument.TickSize;
            }
            catch { }
            return Math.Max(0.0000001, tick * Math.Max(1, BucketTicks));
        }

        private void DisposeBrushes()
        {
            if (volumeBrushDx != null) { volumeBrushDx.Dispose(); volumeBrushDx = null; }
            if (valueAreaBrushDx != null) { valueAreaBrushDx.Dispose(); valueAreaBrushDx = null; }
            if (pocBrushDx != null) { pocBrushDx.Dispose(); pocBrushDx = null; }
            if (rangeBrushDx != null) { rangeBrushDx.Dispose(); rangeBrushDx = null; }
        }

        public ChartAnchor StartAnchor { get; set; }
        public ChartAnchor EndAnchor { get; set; }

        [Display(Name = "Use end anchor", GroupName = "Range", Order = 0)]
        public bool UseEndAnchor { get; set; }

        [Range(1, 100)]
        [Display(Name = "Bucket ticks", GroupName = "Profile", Order = 1)]
        public int BucketTicks { get; set; }

        [Range(20, 600)]
        [Display(Name = "Profile width", GroupName = "Profile", Order = 2)]
        public int ProfileWidth { get; set; }

        [Range(1, 100)]
        [Display(Name = "Value area percent", GroupName = "Profile", Order = 3)]
        public double ValueAreaPercent { get; set; }

        [Display(Name = "Show POC", GroupName = "Visual", Order = 4)]
        public bool ShowPoc { get; set; }

        [Display(Name = "Show range box", GroupName = "Visual", Order = 5)]
        public bool ShowRange { get; set; }

        [Range(5, 100)]
        [Display(Name = "Opacity %", GroupName = "Visual", Order = 6)]
        public int Opacity { get; set; }

        [Range(5, 100)]
        [Display(Name = "Value area opacity %", GroupName = "Visual", Order = 7)]
        public int ValueAreaOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Volume color", GroupName = "Colors", Order = 0)]
        public WpfBrush VolumeColor { get; set; }
        [Browsable(false)]
        public string VolumeColorSerializable { get { return Serialize.BrushToString(VolumeColor); } set { VolumeColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Value area color", GroupName = "Colors", Order = 1)]
        public WpfBrush ValueAreaColor { get; set; }
        [Browsable(false)]
        public string ValueAreaColorSerializable { get { return Serialize.BrushToString(ValueAreaColor); } set { ValueAreaColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "POC color", GroupName = "Colors", Order = 2)]
        public WpfBrush PocColor { get; set; }
        [Browsable(false)]
        public string PocColorSerializable { get { return Serialize.BrushToString(PocColor); } set { PocColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Range color", GroupName = "Colors", Order = 3)]
        public WpfBrush RangeColor { get; set; }
        [Browsable(false)]
        public string RangeColorSerializable { get { return Serialize.BrushToString(RangeColor); } set { RangeColor = Serialize.StringToBrush(value); } }
    }
}

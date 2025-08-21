#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
using SystemBrush = System.Windows.Media.Brush;
using DxBrush = SharpDX.Direct2D1.Brush;
#endregion

namespace NinjaTrader.NinjaScript.ChartStyles
{
    /// <summary>
    /// Simple chart style that draws two lines:
    /// - One connecting the highs of the intervals
    /// - One connecting the lows of the intervals
    /// </summary>
    public class NTLSimpleHighLowChart : ChartStyle
    {
        private DxBrush highLineBrushDX;
        private DxBrush lowLineBrushDX;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL Simple High Low Chart";
                ChartStyleType = (ChartStyleType)6003; // Unique ID

                HighLineColor = System.Windows.Media.Brushes.Red;
                LowLineColor = System.Windows.Media.Brushes.Blue;
                LineWidth = 2.0f;
            }
            else if (State == State.Transition)
            {
                if (RenderTarget != null)
                {
                    OnRenderTargetChanged();
                }
            }
        }

        public override int GetBarPaintWidth(int barWidth) => barWidth;

        public override void OnRender(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars)
        {
            if (chartBars == null || chartBars.Bars == null || chartBars.Bars.Count == 0 || RenderTarget == null)
                return;

            if (highLineBrushDX == null || lowLineBrushDX == null)
                OnRenderTargetChanged();

            var bars = chartBars.Bars;
            int from = Math.Max(0, chartBars.FromIndex);
            int to = Math.Min(chartBars.ToIndex, bars.Count - 1);

            if (to <= from)
                return;

            // Draw high line
            DrawHighLine(chartControl, chartScale, chartBars, from, to);

            // Draw low line
            DrawLowLine(chartControl, chartScale, chartBars, from, to);
        }

        private void DrawHighLine(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars, int from, int to)
        {
            try
            {
                var bars = chartBars.Bars;
                Vector2? previousPoint = null;

                for (int barIdx = from; barIdx <= to; barIdx++)
                {
                    float x = chartControl.GetXByBarIndex(chartBars, barIdx);
                    float y = chartScale.GetYByValue(bars.GetHigh(barIdx));

                    Vector2 currentPoint = new Vector2(x, y);

                    if (previousPoint.HasValue)
                    {
                        // Draw line from previous point to current point
                        if (previousPoint.HasValue &&
                            IsPointVisible(chartControl, previousPoint.Value) || IsPointVisible(chartControl, currentPoint))
                        {
                            RenderTarget.DrawLine(previousPoint.Value, currentPoint, highLineBrushDX, LineWidth);
                        }
                    }

                    previousPoint = currentPoint;
                }
            }
            catch (Exception ex)
            {
                Print($"Error drawing high line: {ex}");
            }
        }

        private void DrawLowLine(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars, int from, int to)
        {
            try
            {
                var bars = chartBars.Bars;
                Vector2? previousPoint = null;

                for (int barIdx = from; barIdx <= to; barIdx++)
                {
                    float x = chartControl.GetXByBarIndex(chartBars, barIdx);
                    float y = chartScale.GetYByValue(bars.GetLow(barIdx));

                    Vector2 currentPoint = new Vector2(x, y);

                    if (previousPoint.HasValue)
                    {
                        // Draw line from previous point to current point
                        if (previousPoint.HasValue &&
                            IsPointVisible(chartControl, previousPoint.Value) || IsPointVisible(chartControl, currentPoint))
                        {
                            RenderTarget.DrawLine(previousPoint.Value, currentPoint, lowLineBrushDX, LineWidth);
                        }
                    }

                    previousPoint = currentPoint;
                }
            }
            catch (Exception ex)
            {
                // Log error but continue rendering
                Print($"Error drawing low line: {ex.Message}");
            }
        }

        private bool IsPointVisible(ChartControl chartControl, Vector2 point)
        {
            var panel = chartControl.ChartPanels[0]; // Haupt-Panel
            return point.X >= panel.X && point.X <= panel.X + panel.W &&
                   point.Y >= panel.Y && point.Y <= panel.Y + panel.H;
        }

        public override void OnRenderTargetChanged()
        {
            // Dispose existing brushes
            if (highLineBrushDX != null)
            {
                highLineBrushDX.Dispose();
                highLineBrushDX = null;
            }
            if (lowLineBrushDX != null)
            {
                lowLineBrushDX.Dispose();
                lowLineBrushDX = null;
            }

            // Create new brushes if RenderTarget is available
            if (RenderTarget != null)
            {
                try
                {
                    highLineBrushDX = HighLineColor.ToDxBrush(RenderTarget);
                    lowLineBrushDX = LowLineColor.ToDxBrush(RenderTarget);
                }
                catch (Exception ex)
                {
                    Print($"Error creating brushes: {ex.Message}");
                }
            }
        }

        #region Properties
        [Display(Name = "High Line Color", GroupName = "Colors", Order = 1)]
        public SystemBrush HighLineColor { get; set; }

        [Browsable(false)]
        public string HighLineColorSerialize
        {
            get { return Serialize.BrushToString(HighLineColor); }
            set { HighLineColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Low Line Color", GroupName = "Colors", Order = 2)]
        public SystemBrush LowLineColor { get; set; }

        [Browsable(false)]
        public string LowLineColorSerialize
        {
            get { return Serialize.BrushToString(LowLineColor); }
            set { LowLineColor = Serialize.StringToBrush(value); }
        }

        [Range(1.0, 5.0)]
        [Display(Name = "Line Width", GroupName = "Display", Order = 1)]
        public float LineWidth { get; set; }
        #endregion
    }
}
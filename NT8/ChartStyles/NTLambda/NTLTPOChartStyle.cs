#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.Gui.Tools;
using static NinjaTrader.Gui.DxExtensions;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SystemBrush = System.Windows.Media.Brush;
using DxBrush = SharpDX.Direct2D1.Brush;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.ChartStyles
{
    /// <summary>
    /// TPO/Market Profile Chart Style:
    /// - One profile per session (visible range)
    /// - Letters per sub-interval (default 30 min)
    /// - No Value Area/POC calculation
    /// </summary>
    public class NTLTPOChartStyle : ChartStyle
    {
        private Dictionary<double, int> priceToCount;
        private DxBrush blockBrushDX;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NTL TPO Chart";
                ChartStyleType = (ChartStyleType)6004; // Unique ID
                
                BlockWidth = 10f;
                BlockColor = System.Windows.Media.Brushes.LightBlue;
            }
            else if (State == State.DataLoaded)
            {
                priceToCount = new Dictionary<double, int>();
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

            if (blockBrushDX == null)
                OnRenderTargetChanged();

            if (blockBrushDX == null)
                return;

            BuildPriceData(chartBars);
            RenderBlocks(chartControl, chartScale, chartBars);
        }


        private void BuildPriceData(ChartBars chartBars)
        {
            priceToCount.Clear();

            var bars = chartBars.Bars;
            if (bars == null || bars.Count == 0)
                return;

            double tick = bars.Instrument.MasterInstrument.TickSize;
            if (tick <= 0) tick = 0.01;

            int from = Math.Max(0, chartBars.FromIndex);
            int to = Math.Min(chartBars.ToIndex, bars.Count - 1);

            for (int barIdx = from; barIdx <= to; barIdx++)
            {
                double high = bars.GetHigh(barIdx);
                double low = bars.GetLow(barIdx);

                for (double price = low; price <= high; price += tick)
                {
                    double roundedPrice = Math.Round(price / tick) * tick;
                    
                    if (priceToCount.ContainsKey(roundedPrice))
                        priceToCount[roundedPrice]++;
                    else
                        priceToCount[roundedPrice] = 1;
                }
            }
        }

        private void RenderBlocks(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars)
        {
            if (priceToCount.Count == 0)
                return;

            var panel = chartControl.ChartPanels[chartBars.Panel];
            float xCenter = panel.X + panel.W / 2;

            foreach (var kv in priceToCount)
            {
                double price = kv.Key;
                int count = kv.Value;
                
                float y = chartScale.GetYByValue(price);
                
                if (y < panel.Y || y > panel.Y + panel.H)
                    continue;

                for (int i = 0; i < count; i++)
                {
                    float x = xCenter + i * BlockWidth;
                    var rect = new RectangleF(x, y - 6f, BlockWidth - 1f, 12f);
                    RenderTarget.FillRectangle(rect, blockBrushDX);
                }
            }
        }


        public override void OnRenderTargetChanged()
        {
            if (blockBrushDX != null)
            {
                blockBrushDX.Dispose();
                blockBrushDX = null;
            }

            if (RenderTarget != null)
            {
                blockBrushDX = BlockColor.ToDxBrush(RenderTarget);
            }
        }

        #region Properties
        [Range(5, 20)]
        [Display(Name = "Block Width", GroupName = "Display", Order = 1)]
        public float BlockWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Block Color", GroupName = "Display", Order = 2)]
        public SystemBrush BlockColor { get; set; }

        [Browsable(false)]
        public string BlockColorSerialize
        {
            get { return Serialize.BrushToString(BlockColor); }
            set { BlockColor = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}
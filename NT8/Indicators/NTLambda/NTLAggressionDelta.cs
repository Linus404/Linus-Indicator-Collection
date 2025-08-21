#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
	public class NTLAggressionDelta : Indicator
	{
		private double buys;
		private double sells;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"NTLambda Aggression Delta";
				Name						= "NTL Aggression Delta";
				Calculate					= Calculate.OnEachTick;
				DrawOnPricePanel			= false;
				IsOverlay					= false;
				DisplayInDataBox			= false;
				DrawOnPricePanel			= false;
				PaintPriceMarkers			= true;
				ScaleJustification			= ScaleJustification.Right;
				PositiveBrush				= Brushes.Green;
				NegativeBrush				= Brushes.Red;
			}
			else if (State == State.Configure)
			{
				AddLine(Brushes.Gray, 0, "Zero Line");
				AddPlot(Brushes.Gray, "Delta");
				Plots[0].PlotStyle = PlotStyle.Bar;
				Plots[0].AutoWidth = true;
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
		}

		protected override void OnBarUpdate()
		{
			if(BarsInProgress == 0) {
				// Handle main timeframe bar updates
				if(IsFirstTickOfBar && State != State.Historical && !IsTickReplays[0]) {
					// Reset for real-time non-tick-replay
					buys = 0;
					sells = 0;
				}

				// Set the current delta value
				Values[0][0] = buys - sells;
				PlotBrushes[0][0] = (Values[0][0] > 0) ? PositiveBrush : NegativeBrush;

				// Reset after historical or tick replay bar completion
				if(State == State.Historical || IsTickReplays[0]) {
					buys = 0;
					sells = 0;
				}
			}

			// Process tick data (works for both historical and tick replay)
			if(BarsInProgress == 1) {
				double price = BarsArray[1].GetClose(CurrentBars[1]);
				double ask = BarsArray[1].GetAsk(CurrentBars[1]);
				double bid = BarsArray[1].GetBid(CurrentBars[1]);
				double volume = BarsArray[1].GetVolume(CurrentBars[1]);
				
				// Classify trades using bid/ask comparison
				if(price >= ask) {
					buys += volume;
				}
				else if(price <= bid) {
					sells += volume;
				}
				else {
					// Split volume for mid-market trades
					buys += volume * 0.5;
					sells += volume * 0.5;
				}
			}
		}

		#region Properties
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Positive Color", GroupName = "Visual")]
		public Brush PositiveBrush { get; set; }

		[Browsable(false)]
		public string PositiveBrushSerializable
		{
			get { return Serialize.BrushToString(PositiveBrush); }
			set { PositiveBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Negative Color", GroupName = "Visual")]
		public Brush NegativeBrush { get; set; }

		[Browsable(false)]
		public string NegativeBrushSerializable
		{
			get { return Serialize.BrushToString(NegativeBrush); }
			set { NegativeBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private NTLambda.NTLAggressionDelta[] cacheNTLAggressionDelta;
		public NTLambda.NTLAggressionDelta NTLAggressionDelta()
		{
			return NTLAggressionDelta(Input);
		}

		public NTLambda.NTLAggressionDelta NTLAggressionDelta(ISeries<double> input)
		{
			if (cacheNTLAggressionDelta != null)
				for (int idx = 0; idx < cacheNTLAggressionDelta.Length; idx++)
					if (cacheNTLAggressionDelta[idx] != null && cacheNTLAggressionDelta[idx].EqualsInput(input))
						return cacheNTLAggressionDelta[idx];
			return CacheIndicator<NTLambda.NTLAggressionDelta>(new NTLambda.NTLAggressionDelta(), input, ref cacheNTLAggressionDelta);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.NTLambda.NTLAggressionDelta NTLAggressionDelta()
		{
			return indicator.NTLAggressionDelta(Input);
		}

		public Indicators.NTLambda.NTLAggressionDelta NTLAggressionDelta(ISeries<double> input)
		{
			return indicator.NTLAggressionDelta(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.NTLambda.NTLAggressionDelta NTLAggressionDelta()
		{
			return indicator.NTLAggressionDelta(Input);
		}

		public Indicators.NTLambda.NTLAggressionDelta NTLAggressionDelta(ISeries<double> input)
		{
			return indicator.NTLAggressionDelta(input);
		}
	}
}

#endregion

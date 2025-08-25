/*
 * NTLAggressorRatio.cs
 * Copyright (c) 2025
 * 
 * Created by https://github.com/Linus404, 2025
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
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
    public class NTLAggressorRatio : Indicator
	{
        private long aggressiveBuyVolume;
        private long aggressiveSellVolume;
        private long passiveBuyVolume;
        private long passiveSellVolume;
        private long totalAggressiveVolume;
        private long totalPassiveVolume;

        private Series<double> aggressorRatio;
        private Series<double> smoothedAggressorRatio;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"NTLambda Aggressor Ratio - Measures aggressive vs passive trading activity";
				Name										= "NTL Aggressor Ratio";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;
				
				// Plots
				AddPlot(Brushes.Blue, "AggressorRatio");
				AddPlot(Brushes.Orange, "SmoothedRatio");
				AddLine(Brushes.Gray, 0.5, "Neutral");
				AddLine(Brushes.Green, 0.7, "BullishThreshold");
				AddLine(Brushes.Red, 0.3, "BearishThreshold");
				
				// Default parameters
				LookbackPeriod = 20;
				SmoothingPeriod = 5;
				ShowSmoothed = true;
				ResetOnNewSession = true;
				AggressiveThreshold = 0.6;
			}
			else if (State == State.DataLoaded)
			{
				aggressorRatio = new Series<double>(this);
				smoothedAggressorRatio = new Series<double>(this);
			}
			else if (State == State.Historical)
			{
				if (Calculate != Calculate.OnEachTick)
				{
					Draw.TextFixed(this, "NinjaScriptInfo", "Aggressor Ratio works best with Calculate.OnEachTick", TextPosition.BottomRight);
					Log("Aggressor Ratio works best with Calculate.OnEachTick", LogLevel.Warning);
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < LookbackPeriod)
				return;

			// Reset counters on new session if enabled
			if (ResetOnNewSession && Bars.IsFirstBarOfSession)
			{
				ResetCounters();
			}

			// Calculate aggressor ratio for the current bar
			double ratio = CalculateAggressorRatio();
			aggressorRatio[0] = ratio;
			Values[0][0] = ratio;

			// Calculate smoothed ratio if enabled
			if (ShowSmoothed && CurrentBar >= SmoothingPeriod)
			{
				double smoothed = CalculateSmoothedRatio();
				smoothedAggressorRatio[0] = smoothed;
				Values[1][0] = smoothed;
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			if (marketDataUpdate.MarketDataType == MarketDataType.Last)
			{
				// Classify trades as aggressive or passive based on price action
				ClassifyTrade(marketDataUpdate.Price, marketDataUpdate.Volume);
			}
		}

		private void ClassifyTrade(double price, long volume)
		{
			if (CurrentBar < 1)
				return;

			double bid = GetCurrentBid();
			double ask = GetCurrentAsk();
			double spread = ask - bid;

			if (spread <= 0)
				return; // Invalid spread

			// Trade classification logic
			if (price >= ask - (spread * 0.1)) // Trade at or near ask (aggressive buy)
			{
				aggressiveBuyVolume += volume;
				totalAggressiveVolume += volume;
			}
			else if (price <= bid + (spread * 0.1)) // Trade at or near bid (aggressive sell)
			{
				aggressiveSellVolume += volume;
				totalAggressiveVolume += volume;
			}
			else // Trade inside spread (passive)
			{
				if (price > (bid + ask) / 2) // Closer to ask
				{
					passiveBuyVolume += volume;
				}
				else // Closer to bid
				{
					passiveSellVolume += volume;
				}
				totalPassiveVolume += volume;
			}
		}

		private double GetCurrentBid()
		{
			if (Instrument?.MasterInstrument?.MarketData?.Bid != null)
				return Instrument.MasterInstrument.MarketData.Bid.Price;
			return Low[0]; // Fallback
		}

		private double GetCurrentAsk()
		{
			if (Instrument?.MasterInstrument?.MarketData?.Ask != null)
				return Instrument.MasterInstrument.MarketData.Ask.Price;
			return High[0]; // Fallback
		}

		private double CalculateAggressorRatio()
		{
			long totalVolume = totalAggressiveVolume + totalPassiveVolume;
			
			if (totalVolume == 0)
				return 0.5; // Neutral when no data

			// Ratio of aggressive volume to total volume
			double ratio = (double)totalAggressiveVolume / totalVolume;
			
			// Clamp between 0 and 1
			return Math.Max(0, Math.Min(1, ratio));
		}

		private double CalculateSmoothedRatio()
		{
			double sum = 0;
			int count = 0;

			for (int i = 0; i < Math.Min(SmoothingPeriod, CurrentBar + 1); i++)
			{
				if (aggressorRatio[i] > 0) // Only include valid values
				{
					sum += aggressorRatio[i];
					count++;
				}
			}

			return count > 0 ? sum / count : 0.5;
		}

		private void ResetCounters()
		{
			aggressiveBuyVolume = 0;
			aggressiveSellVolume = 0;
			passiveBuyVolume = 0;
			passiveSellVolume = 0;
			totalAggressiveVolume = 0;
			totalPassiveVolume = 0;
		}

		#region Properties
		[Range(1, 100)]
		[Display(Name = "Lookback Period", Description = "Number of bars to look back for calculations", Order = 1, GroupName = "Parameters")]
		public int LookbackPeriod { get; set; }

		[Range(1, 50)]
		[Display(Name = "Smoothing Period", Description = "Period for smoothing the aggressor ratio", Order = 2, GroupName = "Parameters")]
		public int SmoothingPeriod { get; set; }

		[Display(Name = "Show Smoothed", Description = "Show smoothed aggressor ratio line", Order = 3, GroupName = "Parameters")]
		public bool ShowSmoothed { get; set; }

		[Display(Name = "Reset on New Session", Description = "Reset counters at the start of each session", Order = 4, GroupName = "Parameters")]
		public bool ResetOnNewSession { get; set; }

		[Range(0.5, 1.0)]
		[Display(Name = "Aggressive Threshold", Description = "Threshold for considering high aggressive activity", Order = 5, GroupName = "Parameters")]
		public double AggressiveThreshold { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> AggressorRatio
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SmoothedRatio
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public long AggressiveBuyVolume
		{
			get { return aggressiveBuyVolume; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public long AggressiveSellVolume
		{
			get { return aggressiveSellVolume; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public long TotalAggressiveVolume
		{
			get { return totalAggressiveVolume; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public long TotalPassiveVolume
		{
			get { return totalPassiveVolume; }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private NTLambda.NTLAggressorRatio[] cacheNTLAggressorRatio;
		public NTLambda.NTLAggressorRatio NTLAggressorRatio(int lookbackPeriod, int smoothingPeriod, bool showSmoothed, bool resetOnNewSession, double aggressiveThreshold)
		{
			return NTLAggressorRatio(Input, lookbackPeriod, smoothingPeriod, showSmoothed, resetOnNewSession, aggressiveThreshold);
		}

		public NTLambda.NTLAggressorRatio NTLAggressorRatio(ISeries<double> input, int lookbackPeriod, int smoothingPeriod, bool showSmoothed, bool resetOnNewSession, double aggressiveThreshold)
		{
			if (cacheNTLAggressorRatio != null)
				for (int idx = 0; idx < cacheNTLAggressorRatio.Length; idx++)
					if (cacheNTLAggressorRatio[idx] != null && cacheNTLAggressorRatio[idx].LookbackPeriod == lookbackPeriod && cacheNTLAggressorRatio[idx].SmoothingPeriod == smoothingPeriod && cacheNTLAggressorRatio[idx].ShowSmoothed == showSmoothed && cacheNTLAggressorRatio[idx].ResetOnNewSession == resetOnNewSession && cacheNTLAggressorRatio[idx].AggressiveThreshold == aggressiveThreshold && cacheNTLAggressorRatio[idx].EqualsInput(input))
						return cacheNTLAggressorRatio[idx];
			return CacheIndicator<NTLambda.NTLAggressorRatio>(new NTLambda.NTLAggressorRatio(){ LookbackPeriod = lookbackPeriod, SmoothingPeriod = smoothingPeriod, ShowSmoothed = showSmoothed, ResetOnNewSession = resetOnNewSession, AggressiveThreshold = aggressiveThreshold }, input, ref cacheNTLAggressorRatio);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.NTLambda.NTLAggressorRatio NTLAggressorRatio(int lookbackPeriod, int smoothingPeriod, bool showSmoothed, bool resetOnNewSession, double aggressiveThreshold)
		{
			return indicator.NTLAggressorRatio(Input, lookbackPeriod, smoothingPeriod, showSmoothed, resetOnNewSession, aggressiveThreshold);
		}

		public Indicators.NTLambda.NTLAggressorRatio NTLAggressorRatio(ISeries<double> input, int lookbackPeriod, int smoothingPeriod, bool showSmoothed, bool resetOnNewSession, double aggressiveThreshold)
		{
			return indicator.NTLAggressorRatio(input, lookbackPeriod, smoothingPeriod, showSmoothed, resetOnNewSession, aggressiveThreshold);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.NTLambda.NTLAggressorRatio NTLAggressorRatio(int lookbackPeriod, int smoothingPeriod, bool showSmoothed, bool resetOnNewSession, double aggressiveThreshold)
		{
			return indicator.NTLAggressorRatio(Input, lookbackPeriod, smoothingPeriod, showSmoothed, resetOnNewSession, aggressiveThreshold);
		}

		public Indicators.NTLambda.NTLAggressorRatio NTLAggressorRatio(ISeries<double> input, int lookbackPeriod, int smoothingPeriod, bool showSmoothed, bool resetOnNewSession, double aggressiveThreshold)
		{
			return indicator.NTLAggressorRatio(input, lookbackPeriod, smoothingPeriod, showSmoothed, resetOnNewSession, aggressiveThreshold);
		}
	}
}

#endregion
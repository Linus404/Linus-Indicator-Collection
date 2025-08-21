#region Using declarations
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    /// <summary>
    /// Common types and utilities shared across NTLambda indicators
    /// </summary>

    /// <summary>
    /// Moving Average calculation types
    /// </summary>
    public enum MAType
    {
        [Description("None")]
        None,
        [Description("Simple Moving Average")]
        SMA,
        [Description("Exponential Moving Average")]
        EMA
    }

    /// <summary>
    /// Weighting methods for cumulative calculations
    /// </summary>
    public enum WeightingType
    {
        [Description("Cumulative Sum (COFI)")]
        Cumulative,      // COFI - Simple cumulative sum
        [Description("Exponential Decay (WOFI)")]
        Exponential,     // WOFI - Exponential decay weighting
        [Description("Linear Decay")]
        Linear,          // Linear decay weighting
        [Description("Fixed Window")]
        FixedWindow      // Fixed lookback window sum
    }

    /// <summary>
    /// Trade classification methods
    /// </summary>
    public enum TradeClassificationMethod
    {
        [Description("Lee-Ready Algorithm")]
        LeeReady,
        [Description("Tick Rule")]
        TickRule,
        [Description("Quote Rule")]
        QuoteRule,
        [Description("Hybrid")]
        Hybrid
    }

    /// <summary>
    /// Order flow data structure for maintaining historical data
    /// </summary>
    public class OrderFlowBar
    {
        public double OFI { get; set; }
        public double Volume { get; set; }
        public double BuyVolume { get; set; }
        public double SellVolume { get; set; }
        public int BarIndex { get; set; }

        public OrderFlowBar(double ofi, double volume, double buyVolume, double sellVolume, int barIndex)
        {
            OFI = ofi;
            Volume = volume;
            BuyVolume = buyVolume;
            SellVolume = sellVolume;
            BarIndex = barIndex;
        }
    }

    /// <summary>
    /// Utility class for common order flow calculations
    /// </summary>
    public static class OrderFlowUtils
    {
        /// <summary>
        /// Classifies a trade as buy or sell using the Lee-Ready algorithm
        /// </summary>
        /// <param name="tradePrice">The execution price of the trade</param>
        /// <param name="bidPrice">Current bid price</param>
        /// <param name="askPrice">Current ask price</param>
        /// <param name="previousPrice">Previous trade price for tick rule</param>
        /// <returns>1 for buy, -1 for sell, 0 for unclassified</returns>
        public static int ClassifyTrade(double tradePrice, double bidPrice, double askPrice, double previousPrice)
        {
            // Validate inputs
            if (bidPrice <= 0 || askPrice <= 0 || askPrice <= bidPrice)
                return 0;

            double midPrice = (askPrice + bidPrice) / 2;

            // Quote rule: Compare to midpoint
            if (tradePrice > midPrice)
                return 1; // Buy
            else if (tradePrice < midPrice)
                return -1; // Sell
            else
            {
                // Tick rule: Compare to previous trade
                if (previousPrice > 0)
                {
                    if (tradePrice > previousPrice)
                        return 1; // Buy
                    else if (tradePrice < previousPrice)
                        return -1; // Sell
                }
                return 0; // Unclassified
            }
        }

        /// <summary>
        /// Calculates Order Flow Imbalance from buy and sell volumes
        /// </summary>
        /// <param name="buyVolume">Total buy volume</param>
        /// <param name="sellVolume">Total sell volume</param>
        /// <returns>OFI value between -1 and 1</returns>
        public static double CalculateOFI(double buyVolume, double sellVolume)
        {
            double totalVolume = buyVolume + sellVolume;
            if (totalVolume <= 0)
                return 0;

            return (buyVolume - sellVolume) / totalVolume;
        }

        /// <summary>
        /// Calculates weighted order flow imbalance using specified weighting scheme
        /// </summary>
        /// <param name="orderFlowHistory">Historical order flow data</param>
        /// <param name="weighting">Weighting method to use</param>
        /// <param name="lambda">Lambda parameter for exponential weighting</param>
        /// <param name="windowSize">Window size for linear/fixed window weighting</param>
        /// <returns>Weighted COFI/WOFI value</returns>
        public static double CalculateWeightedOFI(List<OrderFlowBar> orderFlowHistory, WeightingType weighting,
            double lambda = 0.05, int windowSize = 100)
        {
            if (orderFlowHistory == null || orderFlowHistory.Count == 0)
                return 0;

            double result = 0;
            int t = orderFlowHistory.Count - 1;

            switch (weighting)
            {
                case WeightingType.Cumulative:
                    for (int i = 0; i <= t; i++)
                        result += orderFlowHistory[i].OFI * orderFlowHistory[i].Volume;
                    break;

                case WeightingType.Exponential:
                    int maxLookback = CalculateEffectiveLookback(lambda);
                    int lookback = Math.Min(t, Math.Min(maxLookback, 500));
                    int startIdx = Math.Max(0, t - lookback);

                    for (int i = startIdx; i <= t; i++)
                    {
                        double weight = ExponentialWeight(t - i, lambda);
                        result += weight * orderFlowHistory[i].OFI * orderFlowHistory[i].Volume;
                    }
                    break;

                case WeightingType.Linear:
                    int linearLookback = Math.Min(t, windowSize);
                    int linearStart = Math.Max(0, t - linearLookback);

                    for (int i = linearStart; i <= t; i++)
                    {
                        double weight = LinearWeight(t - i, windowSize);
                        if (weight > 0)
                            result += weight * orderFlowHistory[i].OFI * orderFlowHistory[i].Volume;
                    }
                    break;

                case WeightingType.FixedWindow:
                    int windowLookback = Math.Min(t, windowSize);
                    int windowStart = Math.Max(0, t - windowLookback);

                    for (int i = windowStart; i <= t; i++)
                        result += orderFlowHistory[i].OFI * orderFlowHistory[i].Volume;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Calculates exponential decay weight
        /// </summary>
        /// <param name="timeDifference">Time difference (in bars)</param>
        /// <param name="lambda">Decay parameter</param>
        /// <returns>Weight value between 0 and 1</returns>
        public static double ExponentialWeight(int timeDifference, double lambda)
        {
            return Math.Exp(-lambda * timeDifference);
        }

        /// <summary>
        /// Calculates linear decay weight
        /// </summary>
        /// <param name="timeDifference">Time difference (in bars)</param>
        /// <param name="windowSize">Maximum window size</param>
        /// <returns>Weight value between 0 and 1</returns>
        public static double LinearWeight(int timeDifference, int windowSize)
        {
            if (timeDifference >= windowSize)
                return 0;

            return 1.0 - (double)timeDifference / windowSize;
        }

        /// <summary>
        /// Calculates the optimal lookback period for exponential weighting
        /// </summary>
        /// <param name="lambda">Decay parameter</param>
        /// <param name="minWeight">Minimum weight threshold (default 0.01)</param>
        /// <returns>Number of bars for effective lookback</returns>
        public static int CalculateEffectiveLookback(double lambda, double minWeight = 0.01)
        {
            // Solve: exp(-lambda * t) = minWeight
            // t = -ln(minWeight) / lambda
            return (int)Math.Ceiling(-Math.Log(minWeight) / lambda);
        }

        /// <summary>
        /// Maintains order flow history with optional reset period
        /// </summary>
        /// <param name="history">Current history list</param>
        /// <param name="newBar">New order flow bar to add</param>
        /// <param name="resetPeriod">Maximum history length (0 = unlimited)</param>
        public static void UpdateOrderFlowHistory(List<OrderFlowBar> history, OrderFlowBar newBar, int resetPeriod = 0)
        {
            history.Add(newBar);

            if (resetPeriod > 0 && history.Count > resetPeriod)
            {
                history.RemoveAt(0);
            }
        }

        /// <summary>
        /// Calculates Pearson correlation between two data series
        /// </summary>
        /// <param name="seriesX">First data series</param>
        /// <param name="seriesY">Second data series</param>
        /// <returns>Correlation coefficient between -1 and 1</returns>
        public static double CalculatePearsonCorrelation(double[] seriesX, double[] seriesY)
        {
            if (seriesX.Length != seriesY.Length || seriesX.Length == 0)
                return 0;

            int n = seriesX.Length;
            double sumX = seriesX.Sum();
            double sumY = seriesY.Sum();
            double sumXSquared = seriesX.Select(x => x * x).Sum();
            double sumYSquared = seriesY.Select(y => y * y).Sum();
            double sumProduct = seriesX.Zip(seriesY, (x, y) => x * y).Sum();

            double numerator = n * sumProduct - sumX * sumY;
            double denominator = Math.Sqrt((n * sumXSquared - sumX * sumX) *
                                          (n * sumYSquared - sumY * sumY));

            if (Math.Abs(denominator) < 1e-10)
                return 0;

            double correlation = numerator / denominator;

            // Clamp to valid range
            return Math.Max(-1, Math.Min(1, correlation));
        }

        /// <summary>
        /// Calculates linear regression slope for a data series
        /// </summary>
        /// <param name="values">Data values (most recent first)</param>
        /// <param name="period">Number of periods to include</param>
        /// <returns>Linear regression slope</returns>
        public static double CalculateLinearRegressionSlope(double[] values, int period)
        {
            if (values.Length < period || period < 2)
                return 0;

            double sumX = 0;
            double sumY = 0;
            double sumXY = 0;
            double sumX2 = 0;

            for (int i = 0; i < period; i++)
            {
                double x = i; // Time index
                double y = values[period - 1 - i]; // Value at that time (reverse order)

                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            double denominator = period * sumX2 - sumX * sumX;
            if (Math.Abs(denominator) < 0.0001)
                return 0;

            double slope = (period * sumXY - sumX * sumY) / denominator;
            return slope;
        }

        /// <summary>
        /// Calculates standard deviation for a data series
        /// </summary>
        /// <param name="values">Data values</param>
        /// <param name="period">Number of periods to include</param>
        /// <returns>Standard deviation</returns>
        public static double CalculateStandardDeviation(double[] values, int period)
        {
            if (values.Length < period || period < 1)
                return 1; // Return 1 to avoid division by zero

            double sum = 0;
            double sumSquared = 0;

            for (int i = 0; i < period; i++)
            {
                double value = values[i];
                sum += value;
                sumSquared += value * value;
            }

            double mean = sum / period;
            double variance = (sumSquared / period) - (mean * mean);

            if (variance < 0)
                variance = 0; // Handle floating point errors

            return Math.Sqrt(variance);
        }
    }
}

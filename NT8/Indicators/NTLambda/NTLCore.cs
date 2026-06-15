#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.NTLambda
{
    public enum MAType { None, SMA, EMA }
    public enum WeightingType { Cumulative, Exponential, Linear, FixedWindow }
    public enum NTLResetPeriod { NoReset, Session, Daily, Weekly }
    public enum NTLPriceSource { Close, Open, High, Low, Median, Typical, Weighted, OHLC4 }
    public enum NTLDeltaMode { BidAsk, UpDownTick }
    public enum NTLProfileMode { Total, Delta, BidAsk }
    public enum NTLFootprintStyle { Tabular, Detailed }
    public enum NTLBidAskLayout { BidAsk, AskBid }
    public enum NTLWeightingMode { Cumulative, Exponential, Linear, FixedWindow }
    public enum NTLFootprintDisplayType { BuySell, Delta, Total, Ladder }

    internal sealed class NTLVolumeLevel
    {
        public double Bid;
        public double Ask;
        public double Total { get { return Bid + Ask; } }
        public double Delta { get { return Ask - Bid; } }
    }

    internal static class NTLCore
    {
        public static double Price(Indicator owner, NTLPriceSource source)
        {
            switch (source)
            {
                case NTLPriceSource.Open: return owner.Open[0];
                case NTLPriceSource.High: return owner.High[0];
                case NTLPriceSource.Low: return owner.Low[0];
                case NTLPriceSource.Median: return (owner.High[0] + owner.Low[0]) / 2.0;
                case NTLPriceSource.Typical: return (owner.High[0] + owner.Low[0] + owner.Close[0]) / 3.0;
                case NTLPriceSource.Weighted: return (owner.High[0] + owner.Low[0] + 2.0 * owner.Close[0]) / 4.0;
                case NTLPriceSource.OHLC4: return (owner.Open[0] + owner.High[0] + owner.Low[0] + owner.Close[0]) / 4.0;
                default: return owner.Close[0];
            }
        }

        public static bool ShouldReset(Indicator owner, NTLResetPeriod period, DateTime lastReset)
        {
            if (owner.CurrentBar == 0) return true;
            if (period == NTLResetPeriod.NoReset) return false;
            DateTime now = owner.Time[0];
            if (period == NTLResetPeriod.Session) return owner.Bars.IsFirstBarOfSession;
            if (period == NTLResetPeriod.Daily) return lastReset == DateTime.MinValue || now.Date != lastReset.Date;
            return lastReset == DateTime.MinValue || now.Date.AddDays(-(int)now.DayOfWeek) != lastReset.Date.AddDays(-(int)lastReset.DayOfWeek);
        }

        public static double TickSize(Indicator owner)
        {
            return owner.Instrument == null || owner.Instrument.MasterInstrument == null ? 1.0 : owner.Instrument.MasterInstrument.TickSize;
        }

        public static double RoundToTick(Indicator owner, double price)
        {
            double tick = TickSize(owner);
            return Math.Round(price / tick) * tick;
        }

        public static double RoundToBucket(Indicator owner, double price, int bucketTicks)
        {
            double size = TickSize(owner) * Math.Max(1, bucketTicks);
            return Math.Round(price / size) * size;
        }

        public static void AddVolume(IDictionary<double, NTLVolumeLevel> profile, double price, double bid, double ask)
        {
            NTLVolumeLevel level;
            if (!profile.TryGetValue(price, out level))
            {
                level = new NTLVolumeLevel();
                profile[price] = level;
            }
            level.Bid += bid;
            level.Ask += ask;
        }

        public static bool IsAskTrade(double price, double bid, double ask, double lastPrice)
        {
            if (ask > 0 && price >= ask) return true;
            if (bid > 0 && price <= bid) return false;
            return double.IsNaN(lastPrice) || price >= lastPrice;
        }

        public static double Ofi(double bidVolume, double askVolume)
        {
            double total = bidVolume + askVolume;
            return total <= 0 ? 0 : (askVolume - bidVolume) / total;
        }

        public static double ProfileValue(NTLVolumeLevel level, NTLProfileMode mode)
        {
            if (level == null) return 0;
            if (mode == NTLProfileMode.Delta) return level.Delta;
            if (mode == NTLProfileMode.BidAsk) return Math.Max(level.Bid, level.Ask);
            return level.Total;
        }

        public static double Ema(double previous, double value, int length)
        {
            double alpha = 2.0 / (Math.Max(1, length) + 1.0);
            return double.IsNaN(previous) ? value : previous + alpha * (value - previous);
        }

        public static double Sma(Queue<double> queue, double value, int length)
        {
            queue.Enqueue(value);
            while (queue.Count > Math.Max(1, length)) queue.Dequeue();
            return queue.Count == 0 ? value : queue.Average();
        }
    }
}

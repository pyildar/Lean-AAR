using System;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Parameters;
using QuantConnect.Indicators;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Date/parameter-driven wrapper to honor job-provided start/end dates and EMA params.
    /// Enables staggered date windows + param sweeps without code edits.
    /// </summary>
    public class AARCDateParamAlgorithm : QCAlgorithm
    {
        [Parameter("ema-fast")]
        private int _fast = 10;

        [Parameter("ema-slow")]
        private int _slow = 50;

        [Parameter("symbol")]
        private string _symbol = "SPY";

        [Parameter("resolution")]
        private string _resolution = "Minute";

        [Parameter("market")]
        private string _market = Market.USA;

        // Transform flags (renko)
        [Parameter("transforms.renko.size")]
        private decimal _renkoSize = 0m;

        // Lightweight ML-style gates (volatility / bias tuning)
        [Parameter("ml.mode")]
        private string _mlMode = "off"; // off | vol_gate | bias

        [Parameter("ml.vol_window")]
        private int _mlVolWindow = 30;

        [Parameter("ml.threshold")]
        private decimal _mlThreshold = 0.002m;

        [Parameter("ml.bias")]
        private decimal _mlBias = 0m;

        [Parameter("start-date")]
        private string _start = "2013-10-07";

        [Parameter("end-date")]
        private string _end = "2013-10-11";

        private Symbol _sym;
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RenkoConsolidator _renko;
        private decimal _lastRenkoValue;
        private bool _renkoReady;
        private StandardDeviation _vol;
        private decimal _lastPrice;
        private bool _havePrice;

        public override void Initialize()
        {
            // Parse ISO dates; fallback to defaults if parsing fails.
            DateTime startDate = ParseIsoDate(_start, new DateTime(2013, 10, 7));
            DateTime endDate = ParseIsoDate(_end, new DateTime(2013, 10, 11));
            SetStartDate(startDate);
            SetEndDate(endDate);
            SetCash(100_000);

            if (string.IsNullOrWhiteSpace(_symbol))
            {
                _symbol = "SPY";
            }
            var res = ParseResolution(_resolution);
            _sym = AddEquity(_symbol, res, _market).Symbol;

            if (_renkoSize > 0m)
            {
                _renko = new RenkoConsolidator(_renkoSize);
                _renko.DataConsolidated += OnRenko;
                SubscriptionManager.AddConsolidator(_sym, _renko);
                _emaFast = new ExponentialMovingAverage(_fast);
                _emaSlow = new ExponentialMovingAverage(_slow);
            }
            else
            {
                _emaFast = EMA(_sym, _fast);
                _emaSlow = EMA(_sym, _slow);
            }

            // Volatility gate for ML-mode if enabled
            if (_mlMode != null && _mlMode.ToLowerInvariant() != "off")
            {
                int window = Math.Max(5, Math.Min(500, _mlVolWindow));
                _vol = STD(_sym, window);
            }
        }

        public override void OnData(Slice data)
        {
            // Capture real prices from bar feeds to ensure execution is based on observed ticks/quotes.
            if (data.Bars != null && data.Bars.ContainsKey(_sym))
            {
                var bar = data.Bars[_sym];
                _lastPrice = bar.Close;
                _havePrice = true;
            }
            else if (data.QuoteBars != null && data.QuoteBars.ContainsKey(_sym))
            {
                var qb = data.QuoteBars[_sym];
                _lastPrice = qb.Close;
                _havePrice = true;
            }

            if (_renko != null)
            {
                if (!_renkoReady) return;
                TradeOn(_emaFast, _emaSlow);
                return;
            }
            if (!_emaFast.IsReady || !_emaSlow.IsReady) return;

            TradeOn(_emaFast, _emaSlow);
        }

        private void OnRenko(object sender, RenkoBar bar)
        {
            _lastRenkoValue = bar.Close;
            _emaFast.Update(bar.EndTime, bar.Close);
            _emaSlow.Update(bar.EndTime, bar.Close);
            _renkoReady = _emaFast.IsReady && _emaSlow.IsReady;
            _lastPrice = bar.Close; // brick still tracks real-ish price progression
            _havePrice = true;
        }

        private void TradeOn(ExponentialMovingAverage fast, ExponentialMovingAverage slow)
        {
            if (!fast.IsReady || !slow.IsReady) return;
            if (!_havePrice || _lastPrice <= 0m) return;
            // Optional ML gates
            if (_vol != null && !_vol.IsReady) return;
            if (_vol != null && _mlMode.Equals("vol_gate", StringComparison.OrdinalIgnoreCase))
            {
                // Skip trading when realized volatility exceeds threshold
                if (_vol.Current.Value > Math.Abs(_mlThreshold)) return;
            }

            decimal bias = _mlBias;
            if (fast > slow * (1.001m + bias))
            {
                // Size via available cash to avoid synthetic brick fill assumptions
                var price = _lastPrice;
                var targetValue = Portfolio.Cash;
                var qty = price > 0 ? Math.Floor(targetValue / price) : 0;
                if (qty > 0)
                {
                    MarketOrder(_sym, qty);
                }
            }
            else if (fast < slow * (0.999m - bias))
            {
                if (Portfolio[_sym].Invested)
                {
                    Liquidate(_sym);
                }
            }
        }

        private static DateTime ParseIsoDate(string iso, DateTime fallback)
        {
            if (DateTime.TryParse(iso, out var dt))
            {
                return new DateTime(dt.Year, dt.Month, dt.Day);
            }
            return fallback;
        }

        private static Resolution ParseResolution(string res)
        {
            if (string.IsNullOrWhiteSpace(res)) return Resolution.Minute;
            return res.ToLowerInvariant() switch
            {
                "tick" => Resolution.Tick,
                "second" => Resolution.Second,
                "minute" => Resolution.Minute,
                "hour" => Resolution.Hour,
                "daily" => Resolution.Daily,
                _ => Resolution.Minute
            };
        }
    }
}

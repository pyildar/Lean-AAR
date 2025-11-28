using System;
using System.Globalization;
using System.IO;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;

namespace QuantConnect.Algorithm.CSharp;

public class AARCImportedTickAlgorithm : QCAlgorithm
{
    private Symbol _symbol;
    private bool _entered;

    public override void Initialize()
    {
        SetStartDate(2023, 9, 27);
        SetEndDate(2023, 9, 27);
        SetCash(100_000);
        SetBenchmark(time => 0m);

        var config = AddData<FdxmImportedTick>("FDXM", Resolution.Tick);
        config.SetLeverage(1m);
        _symbol = config.Symbol;
    }

    public override void OnData(Slice data)
    {
        if (!data.ContainsKey(_symbol))
        {
            return;
        }

        var tick = data.Get<FdxmImportedTick>(_symbol);
        if (!_entered && tick != null)
        {
            MarketOrder(_symbol, 1);
            _entered = true;
        }
    }
}

public sealed class FdxmImportedTick : BaseData
{
    private static readonly string DataFile =
        Path.Combine(Globals.DataFolder, "futures", "eurex", "FDXM_2023Z", "ticks.csv");

    public decimal Bid { get; private set; }
    public decimal Ask { get; private set; }
    public decimal Last { get; private set; }

    public override DateTime EndTime { get; set; }

    public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
    {
        return new SubscriptionDataSource(
            DataFile,
            SubscriptionTransportMedium.LocalFile,
            FileFormat.Csv);
    }

    public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("time", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = line.Split(',');
        if (parts.Length < 4)
        {
            return null;
        }

        if (!DateTime.TryParseExact(parts[0],
                "yyyyMMdd HH:mm:ss.fffffffff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return null;
        }

        var tick = new FdxmImportedTick
        {
            Symbol = config.Symbol,
            Time = timestamp,
            EndTime = timestamp,
            Bid = ParseDecimal(parts[1]),
            Ask = ParseDecimal(parts[2]),
            Last = ParseDecimal(parts[3]),
        };
        tick.Value = tick.Last;
        return tick;
    }

    private static decimal ParseDecimal(string input)
    {
        if (decimal.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return 0m;
    }
}

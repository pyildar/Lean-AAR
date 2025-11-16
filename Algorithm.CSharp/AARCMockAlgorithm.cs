using System;
using QuantConnect.Data;
using QuantConnect.Algorithm;

namespace QuantConnect.Algorithm.CSharp
{
    // Minimal QCAlgorithm for testing emitters.
    public class AARCMockAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2020, 1, 1);
            SetEndDate(2020, 1, 3);
            SetCash(100000);
            AddEquity("SPY", Resolution.Daily);
        }

        public override void OnData(Slice data)
        {
            // no-op
        }
    }
}

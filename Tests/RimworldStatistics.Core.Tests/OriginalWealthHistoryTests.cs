using System.Security.Cryptography;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Two raw-DEFLATE wealth histories copied from user-supplied RimWorld 1.7 saves.
/// Only the Wealth_Total recorder bytes are preserved; no full saves, campaign IDs,
/// faction names, mod identifiers, pawn data or map data are embedded here.
/// The surrounding XML is entirely synthetic and is NOT a RimWorld-loadable save.
/// </summary>
public sealed class OriginalWealthHistoryTests
{
    // 1.7.5000, 268 observed wealth values. SHA-256 is over compressed bytes,
    // not the unrelated synthetic save XML used by the reader below.
    private const string Installed = "Dc5tUNMFHAdwOil58jg4ckp2zrvajWFIqTEBuXOs35cAQ7g8Szl8CEiRaK2DgUeMI8ABu7pIyRAL3APsAf7bHNTxMAgGrOkuj3hQBOT5oZBAHqKzUb74vP+cTD5DzTEpFMlKIZ7+Yzr+QhoNhaRR+dZFqo7LoAiliOYcUlob+ZKkAcXkmyijd9rktPSZnASCCtq+UkGKbTfpfm4ljcXfouqPfqCOK9V0+EYNBYcqyB6poE22isxyNdlkGlpy09HpKCUJubW0mlVPp90Ysv9ropoaM3m+biZlUiOJi1rII6eVKpdb6ZB/GzHrFhL3thP7Rgd1GjvI12Ql5zEb7SyxUXTAXWJpB4l1ZoCicwZoOv0BscoeEcs8TJuCx5SU85j8G6bJjzdLyl/maZWZp0mvBaqIXiDh7DJle66QvHOV5L5rJEtdoLTtf9H5jA1q6nZBVfIGVb20RbPrTipVOsnjDxc8jXPHEw93jNrdcJ3nifPVnrhzxwuzu3ZgZsgb87F+yC7ejfsjezAWz0bGw724d+01uE5wUMjiwP8ZB64J++D7ExeJcYHQKgJRFngAXtIgUMgBpBUfxvyVgwg6xgdbFYaakTBI/+ZjK44P03gI/PLexrvfHoEglY8TjjB8bg1HeakIboVZUL+ZjQs+WbCESMArEmFsOBNBnCxwSYKKfRKMiiS4VSbBicYcFL6VC7a/FE1RUky5FKB3Mg/L5flYVnyByf+k4AfmY1txPuYKCvHKpSJwPyzC2rWrKG+7iqj8Ilh3FaO5SQarvQQ5LaUQRFfDt0SBoSMqkLsaIaVq7ImohdG7FvHcWvhIVFhcrsNIohaRx3Voj9Sh96YGmWNajAXoEXtUi4wfdVhz6LG4Wg9eSj3OhTZA1FiPmq8MyN00oO6CAcltDIQ8A2TnDDjrY8CglMHwdQNOdTI4m9AAZliP9wtrYchUg/uiGjqxCpuVSrR/oEBwhhIFQUqINxTgnrqN6bTnOAqMpivBJhU4GhVUVSqcjKmDtbsOhj81cOzVYuSoBsYELVLjdNj//KX4pgE9vzGYZhgcnGAQs8nA9bIJn37SgaUd3bC92oPsmR5IJm0Q3/sVu7+3I6rZjp/bbVh0WrFfZEWn0AJzuAVBl9qRGtiFJO8uxL7RhY0VHdKf6MHtq8dcB4NQNyPypoxYe2ZCcLcRQrEJMpYZinIzlr424ZCHA1UvO+AU9eOyuA98Sx/e29kPRXg/eGH98Lf0o8V1ABsLv0PzcAALwYNQOwcR1TqIzrtDEOgeIIL9CE3/jIKfPYGLlRNIuD2OdeEEuqrGUffdOPS9U3i6MYNt8hn8Dw==";
    // 1.7.4995, 428 observed wealth values.
    private const string Legacy = "Dcx9PBWGHsdxDm1RUkRdmYfQNWzrJeFQicPv+8tDSot2b3RYnuXxDLldccNWnRxExLbETrFaylWeWh4mk+YplW7zsEo6PZAblWnW+uPz1/v1+pRoeFOE1xbyb/Mm/+Eg6m8MIve4ICoXBtHAkt00vyOKUgcS6W5rMnX8M52ydh2gn7wOEvcdpjf2h2m4QUr+VdnUniwj1Qcyij2ZQ/Nsj5LT50dpy6ocshLn0dz5fPrWp4AqgkroO/8Saqo/SicE+bTGooRCckpI2lBGosoyumVSTh4Z5WSmIaeqy3Iy1jxFctcKSsiroLvKlSSZOUvHnl+gyRMX6OahCxRhUUd5bfUkvFxHn4nq6EDaFRLfaCOHmXb6wa+FEhUdFHb1GhmlX6eFZ7rp7FQPpTn3UnpCL/2R1kv6Dv3ke/8mLTh5h56ZDFORzgg5rR2h0tPDVFA5RvKHY9TtOUY5VQpalPGYujMU1Dn2mOx6n9CXZyZo8JsJulU7Tml242TnN0HpK56QS9UE3Vg2Tg7NU8R+ryg19BVtmHxJqZGv6XDJS9p/8A9qMfiT6i6/pTeeSrB9KsBOWxWUVinhilAZ6iNK8NJ9TXv3z9BXejNUt36Gf+8ppsRv5OLzywZvxFAESlAmUCAKxJlVCxVhsd1FezrUoFoWhVFRfNwVTgfE5NqKLZejA22i9F3Wxudd5Yj7vTfsKRTD5ONH+AbMoDGMWP8Fm6Mv+sZQ01shF0rjXBt0BDiU0b4uMcIX39ujq2LbXEi3A0mzu5Q12asTCP8x8wVe6pdYZ7ihsAIN4RmiBAaKsLQv0VoLBBBM0CE6zIRNFgEoY0I69e6wTLfDc/lrmjucsWLCBFaM92ws80dNsWeWP7YEx+c9Ubr5i3I6PeBg7kvrCx2oLPeHf5m7mhP2gTxJx6I1fTAP3w90FjngRHxFtxu88asph/C7gfhtHsYguL9MOkTglqf3TAX70bmrxIc2S+B2CkGq899gZTHibDLjEH1pSh0jUUiTD8a8duj8TYgFBuyYuAqj0H2b3FQeroXd0aTsUIvAWvvJMBARwJrBwlmrRPx7YJE7JMnoT07GSm/pML6/2lYqJuBWqcM1IRlYm7dl9B1OgjrJimaBLkofJCL1OIc9BrmwOh8NkLKsqFWKcXAPik2ZUpxJvgIfjc/jMvpUkytk+L2f6VYtOwIbJ7LcKlGhtkJGc7JsrGZZNB7IcOyylxkOh7FqYxjENQU4vlPRXiYVILvgkvga1+EX5wKoSsowLkNxyB+5z/GFOHZeDH2iEtwPKcMNuXlCFh+Gp7LKtG073u4N52H0kf1yJiux/vyFmitbkXxxRZ8Ft2Ca+tbUBrVCoV3K646tqMmpBOeur3wcOnDoG0fVO/dQPazfmgNdSNluAvdvr0Ynd+LzpBeNOT1wKyiC2sUP0P3Vjv61nWgcGc3enJ7cMrvJoLDBjCn8T8Ma9xD8Kf3MFp6D6UHxyDofwTJ3BjMtymgHPcIxp1jCLV8iPzxUZyTjyFWRwFd+6dIvT8BReYUzFteIbdjCgNNU6gemYbi2jRCH7zEx4MzqFv4FgOaSjzaqMRLtquwIE+Vh9Lf469M3+eawPnc0K3GVhXqfOPkAi60WcRPFixmqcESHjXT4kumWvzEVJvPNGtxYII2Z75LZZEOdw0vZYPepRw0o8PHr+jytos6nO24gpWz9HlztTErag05vNmA8yIMuKzAhHfUGnN8+kqOW2XCGytXsZ2qBT/ysmTHQAuWBVixh7IlvyBLjvjegnvyLXjRcSsuOLSaJ/ZY89av1/DQkA3nvV3L5pb2/KeXI5c9c2D9TUI+CVuOXynkeaZCNhwUcvhxIee/s4sN63nXKyces3Nh+1rRu68LxxY7c8PTjfxJ60b2rdrIkT86c8pVESfping6wIVTDrmw0g4Rfyp35V/d3Dggm1ljtSevUN3Khc0+XGzsz1b6AaxtGM4xd8M5rTCSf1aP4kCrKDbNjeLxuShu74zm6W3R7H1kD99yjGV7nzgO7Ytnj7gEHqyVcMpHwZwTH8pqHcFc/WEwu2bFs+F4PFv9K5obyuNYKyKet2d9wc7nJay+TsKz9yU8F5vECrdkVrXay1LH/fye6gH+Cw==";

    private static void Check(string payload, string version, int ticks, int mods, int points,
        double latest, double peak, double mean, string expectedCompressedSha)
    {
        var compressed = Convert.FromBase64String(payload);
        Assert.Equal(expectedCompressedSha, Convert.ToHexStringLower(SHA256.HashData(compressed)));
        // Authentic compressed recorder bytes surrounded by deliberately fake identity/world/MOD XML.
        var bytes = new SaveXmlBuilder().Minimal().Meta(version, mods).Tick(ticksGame: ticks, start: 0)
            .WealthTotalRaw("recordsDeflate", payload).Build();
        using var source = new MemoryStream(bytes);
        var result = new SaveReader().ReadDetailed(source, "anonymous.rws", bytes.Length, DateTime.UnixEpoch);
        Assert.Equal(ObservationStatus.Ok, result.Observation.Status);
        Assert.NotNull(result.Metrics);
        Assert.Null(result.Checkpoint);
        Assert.Equal(points, result.Observation.Values.WealthPoints);
        Assert.Equal(peak, result.Observation.Values.WealthMax);
        Assert.Equal(latest, result.Observation.Values.WealthLast);
        var actualMean = result.Metrics.Scalar(MetricKeys.WealthTotalMean, MetricKeys.Revision(MetricKeys.WealthTotalMean));
        Assert.NotNull(actualMean?.RealValue);
        Assert.InRange(actualMean.RealValue.Value, mean - 0.01, mean + 0.01);
    }

    [Fact]
    public void InstalledCase_DecodesActual268PointWealthSeries()
        => Check(Installed, "1.7.5000 rev1275", 8_037_368, 183, 268,
            117528.0234375, 117741.890625, 63932.962303958724,
            "f8d3ff3061c45a52507a0dee5046685db628f49bf60535d1ecd75d16baef323b");

    [Fact]
    public void LegacyCase_DecodesActual428PointWealthSeries()
        => Check(Legacy, "1.7.4995 rev309", 12_818_036, 348, 428,
            268320.1875, 268320.1875, 93937.6574958017,
            "e2178239b3e45f1fa3e9e4c5668224608248523542203b72e6a5f666a1d911ec");
}

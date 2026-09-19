using ModelQueryTool.Helpers;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for <see cref="DownloadCaption"/> on fixed numbers, so the arithmetic behind
/// "so much of so much, so fast, so long left" is checked without any download.
/// </summary>
public class DownloadCaptionTests
{
    private const long WholeModel = 22_915_307_304L;
    private const long ThreeAndAHalfGibibytes = 3_758_096_384L;
    private const double FortyFiveMebibytesASecond = 47_185_920d;
    private const double OneMebibyteASecond = 1_048_576d;

    /// <summary>The whole caption: how much of how much, how fast, and how long is left.</summary>
    [Fact]
    public void Format_says_how_much_how_fast_and_how_long_is_left() =>
        DownloadCaption.Format(ThreeAndAHalfGibibytes, WholeModel, FortyFiveMebibytesASecond)
            .Should().Be("3.5 GiB / 21.3 GiB - 45.0 MiB/s - 6m 46s left");

    /// <summary>Until a rate has been measured, neither the rate nor the time left is invented.</summary>
    [Fact]
    public void Format_leaves_out_the_rate_and_the_time_left_until_there_is_a_rate() =>
        DownloadCaption.Format(1_073_741_824L, WholeModel, 0d)
            .Should().Be("1.0 GiB / 21.3 GiB");

    /// <summary>A download whose last byte has arrived has no time left to report.</summary>
    [Fact]
    public void Format_leaves_out_the_time_left_once_everything_has_arrived() =>
        DownloadCaption.Format(WholeModel, WholeModel, FortyFiveMebibytesASecond)
            .Should().Be("21.3 GiB / 21.3 GiB - 45.0 MiB/s");

    /// <summary>A wait under a minute is seconds alone.</summary>
    [Fact]
    public void Format_writes_a_short_wait_in_seconds() =>
        DownloadCaption.Format(WholeModel - 1_048_576L, WholeModel, OneMebibyteASecond)
            .Should().Be("21.3 GiB / 21.3 GiB - 1.0 MiB/s - 1s left");

    /// <summary>A wait over an hour is hours and minutes.</summary>
    [Fact]
    public void Format_writes_a_long_wait_in_hours_and_minutes() =>
        DownloadCaption.Format(0L, WholeModel, OneMebibyteASecond)
            .Should().Be("0.0 GiB / 21.3 GiB - 1.0 MiB/s - 6h 04m left");

    /// <summary>A small download is counted in mebibytes, so both halves agree on the unit.</summary>
    [Fact]
    public void Format_counts_a_small_download_in_mebibytes() =>
        DownloadCaption.Format(524_288L, 2_097_152L, 0d).Should().Be("0.5 MiB / 2.0 MiB");

    /// <summary>With no total there is only what has arrived.</summary>
    [Fact]
    public void Format_reports_what_has_arrived_when_the_total_is_unknown() =>
        DownloadCaption.Format(1_073_741_824L, 0L, 0d).Should().Be("1.0 GiB");
}

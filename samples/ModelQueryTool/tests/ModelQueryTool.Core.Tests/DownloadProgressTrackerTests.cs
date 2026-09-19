using System;
using ModelQueryTool.Helpers;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for <see cref="DownloadProgressTracker"/>. The clock is the test's, so a whole
/// download's worth of reports is driven through in no time at all.
/// </summary>
public class DownloadProgressTrackerTests
{
    private const long Mebibyte = 1_048_576L;
    private const long Total = 100L * Mebibyte;

    /// <summary>The very first report always produces a caption, so the bar is never captionless.</summary>
    [Fact]
    public void TryFormat_always_produces_a_caption_for_the_first_report()
    {
        //Arrange
        var tracker = new DownloadProgressTracker();

        //Act
        var produced = tracker.TryFormat(0L, Total, TimeSpan.Zero, out var caption);

        //Assert
        produced.Should().Be(true);
        caption.Should().Be("0.0 MiB / 100.0 MiB");
    }

    /// <summary>A report that arrives before the interval has passed leaves the caption alone.</summary>
    [Fact]
    public void TryFormat_holds_the_caption_until_the_interval_has_passed()
    {
        //Arrange
        var tracker = new DownloadProgressTracker(TimeSpan.FromMilliseconds(500));
        tracker.TryFormat(0L, Total, TimeSpan.Zero, out _);

        //Act
        var produced = tracker.TryFormat(Mebibyte, Total, TimeSpan.FromMilliseconds(100), out var caption);

        //Assert
        produced.Should().Be(false);
        caption.Should().BeNull();
    }

    /// <summary>The first measured rate is taken whole; the ones after it are blended into it.</summary>
    [Fact]
    public void TryFormat_smooths_the_rate_rather_than_following_every_report()
    {
        //Arrange
        var tracker = new DownloadProgressTracker(TimeSpan.FromMilliseconds(500));
        tracker.TryFormat(0L, Total, TimeSpan.Zero, out _);

        //Act
        tracker.TryFormat(Mebibyte, Total, TimeSpan.FromSeconds(1), out var first);
        tracker.TryFormat(3L * Mebibyte, Total, TimeSpan.FromSeconds(2), out var second);

        //Assert
        first.Should().Be("1.0 MiB / 100.0 MiB - 1.0 MiB/s - 1m 39s left");
        second.Should().Be("3.0 MiB / 100.0 MiB - 1.3 MiB/s - 1m 15s left");
    }

    /// <summary>The report that completes the download always produces a caption, whenever it lands.</summary>
    [Fact]
    public void TryFormat_always_produces_a_caption_for_the_last_report()
    {
        //Arrange
        var tracker = new DownloadProgressTracker(TimeSpan.FromMilliseconds(500));
        tracker.TryFormat(0L, Total, TimeSpan.Zero, out _);

        //Act
        var produced = tracker.TryFormat(Total, Total, TimeSpan.FromMilliseconds(10), out var caption);

        //Assert
        produced.Should().Be(true);
        caption.Should().StartWith("100.0 MiB / 100.0 MiB");
    }

    /// <summary>A rate is only reported once one has been measured.</summary>
    [Fact]
    public void BytesPerSecond_is_nothing_until_two_reports_have_arrived()
    {
        //Arrange
        var tracker = new DownloadProgressTracker();

        //Act
        tracker.TryFormat(0L, Total, TimeSpan.Zero, out _);

        //Assert
        tracker.BytesPerSecond.Should().Be(0d);
    }
}

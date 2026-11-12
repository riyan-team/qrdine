using System.Text.Json;
using QrDine.Application.Common;
using Xunit;

namespace QrDine.Tests;

public class TimeTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    [Fact]
    public void A_time_read_back_from_the_database_still_says_it_is_utc()
    {
        var fromDb = new DateTime(2026, 11, 9, 15, 26, 5, DateTimeKind.Unspecified);
        Assert.Equal("\"2026-11-09T15:26:05Z\"", JsonSerializer.Serialize(fromDb, Options));
    }

    [Fact]
    public void A_time_just_made_is_unchanged()
    {
        var now = new DateTime(2026, 11, 9, 15, 26, 5, DateTimeKind.Utc);
        Assert.Equal("\"2026-11-09T15:26:05Z\"", JsonSerializer.Serialize(now, Options));
    }

    [Fact]
    public void An_optional_time_works_too()
    {
        DateTime? none = null;
        DateTime? some = new DateTime(2026, 11, 9, 15, 26, 5, DateTimeKind.Unspecified);
        Assert.Equal("null", JsonSerializer.Serialize(none, Options));
        Assert.Equal("\"2026-11-09T15:26:05Z\"", JsonSerializer.Serialize(some, Options));
    }

    [Fact]
    public void A_late_bill_belongs_to_the_pakistani_day()
    {
        var eightPmUtc = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 10, 1, 0, 0), BusinessDay.ToPkt(eightPmUtc));
    }

    [Fact]
    public void A_report_day_is_the_pakistani_day()
    {
        // "13 November" runs from 00:00 to 23:59:59 in Pakistan: 19:00 UTC on the 12th to just before 19:00 UTC on the 13th
        var (from, to) = BusinessDay.RangeUtc(new DateTime(2026, 11, 13), new DateTime(2026, 11, 13));
        Assert.Equal(new DateTime(2026, 11, 12, 19, 0, 0), from);
        Assert.True(to < new DateTime(2026, 11, 13, 19, 0, 0) && to > new DateTime(2026, 11, 13, 18, 59, 59));
        var oneAmBill = new DateTime(2026, 11, 12, 20, 0, 0);   // 1 am on the 13th in Pakistan
        Assert.InRange(oneAmBill, from, to);
        var elevenPmBill = new DateTime(2026, 11, 13, 18, 0, 0); // 11 pm on the 13th
        Assert.InRange(elevenPmBill, from, to);
    }

    [Fact]
    public void A_report_with_times_or_no_dates_keeps_its_window()
    {
        var now = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
        var (from, to) = BusinessDay.RangeUtc(null, null, now);
        Assert.Equal(now.AddDays(-31), from);
        Assert.Equal(now, to);
        var exactFrom = new DateTime(2026, 11, 13, 8, 30, 0);
        var exactTo = new DateTime(2026, 11, 13, 9, 15, 0);
        Assert.Equal((exactFrom, exactTo), BusinessDay.RangeUtc(exactFrom, exactTo));
    }
}

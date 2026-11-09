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
}

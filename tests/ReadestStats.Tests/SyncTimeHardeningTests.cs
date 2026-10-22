using Microsoft.Data.Sqlite;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class SyncTimeHardeningTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "SyncTimeTests-UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");

    private static DateTimeOffset Local(int year, int month, int day, int hour = 12, int minute = 0) =>
        new(year, month, day, hour, minute, 0, Zone.BaseUtcOffset);

    private static ReadingEvent Event(DateTimeOffset start, double seconds, long book = 1, string source = "Readest") =>
        new(book, 10, start.ToUnixTimeSeconds(), seconds, 200, source);

    [Fact]
    public void DailyTrendSplitsAtLocalMidnightAndCountsTheBookOnBothTouchedDays()
    {
        var analytics = new AnalyticsEngine(Zone);
        var range = new DateRangeService(Zone).Resolve("Custom", Local(2026, 9, 15),
            customStart: new DateOnly(2026, 9, 13), customEnd: new DateOnly(2026, 9, 14));
        var events = new[] { Event(Local(2026, 9, 13, 23, 50), 1200) };

        var trend = analytics.AggregateTrend(events, range, "Reading time", "Day", 5);
        var books = analytics.AggregateTrend(events, range, "Active books", "Day", 5);
        var sessions = analytics.AggregateTrend(events, range, "Sessions", "Day", 5);
        var metrics = analytics.Metrics(events, range, 5);

        Assert.Equal(new[] { 10d, 10d }, trend.Select(point => point.Value));
        Assert.Equal(new[] { 1d, 1d }, books.Select(point => point.Value));
        // A session is attributed once, to its start; reading time belongs to each touched day.
        Assert.Equal(new[] { 1d, 0d }, sessions.Select(point => point.Value));
        Assert.Equal(1200, metrics.TotalSeconds);
        Assert.Equal(2, metrics.ActiveDays);
        Assert.Equal(1, metrics.SessionCount);
    }

    [Fact]
    public void MonthlyTrendClipsOutsideTheRangeBeforeSplittingMonthBoundaries()
    {
        var analytics = new AnalyticsEngine(Zone);
        var range = new DateRangeService(Zone).Resolve("Custom", Local(2026, 9, 2),
            customStart: new DateOnly(2026, 8, 31), customEnd: new DateOnly(2026, 9, 1));
        var events = new[]
        {
            Event(Local(2026, 8, 30, 23, 50), 1200),
            Event(Local(2026, 8, 31, 23, 50), 1200),
            Event(Local(2026, 9, 1, 23, 50), 1200)
        };

        var daily = analytics.AggregateTrend(events, range, "Reading time", "Day", 5);
        var monthly = analytics.AggregateTrend(events, range, "Reading time", "Month", 5);

        Assert.Equal(new[] { 20d, 20d }, daily.Select(point => point.Value));
        Assert.Equal(2, monthly.Count);
        Assert.All(monthly, point =>
        {
            Assert.Equal("h", point.Unit);
            Assert.Equal(1d / 3, point.Value, 8);
        });
        Assert.Equal(2400, analytics.Metrics(events, range, 5).TotalSeconds);
        Assert.Equal(daily.Sum(point => point.Value), monthly.Sum(point => point.Value) * 60, 8);
    }

    [Theory]
    [InlineData(true, 21, 23)]
    [InlineData(false, 20, 22)]
    public void PartialWeeklyRecapClipsBothWeeksAndUsesTheConfiguredLocalWeekStart(bool mondayStart, int startDay, int todayDay)
    {
        var events = new[]
        {
            Event(Local(2026, 9, startDay - 1, 23, 50), 1200),
            Event(Local(2026, 9, startDay + 1, 23, 50), 1200, 2, "Manual"),
            Event(Local(2026, 9, todayDay, 23, 50), 1200, 2, "Manual"),
            Event(Local(2026, 9, todayDay + 1), 7200),
            Event(Local(2026, 9, startDay - 8, 23, 50), 1200),
            Event(Local(2026, 9, todayDay - 7, 23, 50), 1200)
        };

        var recap = WeeklyRecapEngine.Build(events,
            [new Book(1, "Digital", "A"), new Book(2, "Physical", "B")],
            new DateOnly(2026, 9, todayDay), mondayStart, 60, Zone);

        Assert.Equal(new DateOnly(2026, 9, startDay), recap.Start);
        Assert.Equal(recap.Start.AddDays(6), recap.End);
        Assert.Equal(2400, recap.TotalSeconds);
        Assert.Equal(1200, recap.PreviousSeconds);
        Assert.Equal(1200, recap.DeltaSeconds);
        Assert.Equal(3, recap.ActiveDays);
        Assert.Equal(600, recap.ReadestSeconds);
        Assert.Equal(1800, recap.ManualSeconds);
        Assert.Equal("Physical", recap.TopBook);
        Assert.Equal(new[] { 10d, 10d, 20d, 0d, 0d, 0d, 0d }, recap.Days.Select(point => point.Value));
        Assert.Equal(recap.TotalSeconds, recap.Days.Sum(point => point.Value) * 60);
    }

    [Fact]
    public void YearTotalsClipSessionsAcrossLocalNewYearInsteadOfUsingTheirStartYear()
    {
        var analytics = new AnalyticsEngine(Zone);
        var events = new[]
        {
            Event(Local(2025, 12, 31, 23, 50), 1200),
            Event(Local(2026, 12, 31, 23, 50), 1200)
        };

        var year = analytics.Year(events, [new Book(1, "Book", "Author")], 2026, 5);

        Assert.Equal(1200, year.TotalSeconds);
        Assert.Equal(2, year.ActiveDays);
        Assert.Equal(1d / 6, year.Months[0].Value, 8);
        Assert.Equal(1d / 6, year.Months[11].Value, 8);
        Assert.Equal(10, year.Heatmap[0].Value);
        Assert.Equal(10, year.Heatmap[^1].Value);
        Assert.Equal(year.TotalSeconds, year.Heatmap.Sum(point => point.Value) * 60);
    }

    [Fact]
    public void MidnightReadingHasConsistentBookDaysHourlyWeekdayAndMatrixValues()
    {
        var statistics = new StatisticsEngine(Zone);
        var analytics = new AnalyticsEngine(Zone);
        var infographic = new InfographicEngine(Zone);
        var range = new DateRangeService(Zone).Resolve("Custom", Local(2026, 9, 15),
            customStart: new DateOnly(2026, 9, 13), customEnd: new DateOnly(2026, 9, 14));
        var events = new[] { Event(Local(2026, 9, 13, 23, 50), 2400) };
        var books = new[] { new Book(1, "Book", "Author") };

        var overview = statistics.OverviewForRange(events, books, range, TimeSpan.FromMinutes(5));
        var rank = Assert.Single(statistics.RankBooks(events, books));
        var hourMatrix = infographic.WeekHourMatrix(events, range, 5, false);
        var sessionMatrix = infographic.WeekHourMatrix(events, range, 5, true);
        var bookMatrix = infographic.BookPeriodMatrix(events, books, range);
        var dumbbells = infographic.PeriodDumbbells(events, range, 5);
        var commonHour = analytics.CommonWindow(events, range, 1);
        var year = analytics.Year(events, books, 2026, 5);
        var story = new VisualizationEngine(Zone).Build(events, range, analytics.Compare(events, range), 5, range.End);

        Assert.Equal(2, rank.ActiveDays);
        Assert.Equal(10, overview.Hourly[23].Value);
        Assert.Equal(30, overview.Hourly[0].Value);
        Assert.Equal(10, overview.Weekdays.Single(point => point.Label == "Sun").Value);
        Assert.Equal(30, overview.Weekdays.Single(point => point.Label == "Mon").Value);
        Assert.Equal(10, hourMatrix.Single(cell => cell.RowLabel == "Sun" && cell.ColumnLabel == "23").Value);
        Assert.Equal(30, hourMatrix.Single(cell => cell.RowLabel == "Mon" && cell.ColumnLabel == "00").Value);
        Assert.Equal(40, hourMatrix.Sum(cell => cell.Value));
        Assert.Equal(1, sessionMatrix.Sum(cell => cell.Value));
        Assert.Equal(new[] { 10d, 30d }, bookMatrix.Select(cell => cell.Value));
        Assert.Equal(2, dumbbells.Single(item => item.Label == "Active days").Current);
        Assert.Equal(2400, dumbbells.Single(item => item.Label == "Reading time").Current);
        Assert.Equal(1, dumbbells.Single(item => item.Label == "Sessions").Current);
        Assert.Equal(0, commonHour.StartHour);
        Assert.Equal(1800, commonHour.Seconds);
        Assert.Equal(.75, commonHour.Share);
        Assert.StartsWith("00:00", year.FavoriteHour!.Label);
        Assert.Equal("Monday", year.FavoriteWeekday!.Label);
        Assert.Contains("Monday", story.PeakWeekday);
        Assert.Equal(40d / 60, story.CumulativeJourney[^1].Value, 8);
    }

    [Theory]
    [InlineData(3, 8, -300, 1, 1800, 3, 3600, 4, 1800)]
    [InlineData(11, 1, -240, 1, 5400, 2, 1800, 3, 0)]
    public void HourlyChartsConserveRealDurationThroughSkippedAndRepeatedDstHours(
        int month, int day, int offsetMinutes,
        int firstHour, double firstSeconds, int secondHour, double secondSeconds, int thirdHour, double thirdSeconds)
    {
        var zone = DstZone(TimeSpan.FromHours(-5), TimeSpan.FromHours(1));
        var start = new DateTimeOffset(2026, month, day, 1, 30, 0, TimeSpan.FromMinutes(offsetMinutes));
        var events = new[] { Event(start, 7200) };
        var range = new DateRangeService(zone).Resolve("Custom", start.AddDays(2),
            customStart: new DateOnly(2026, month, day), customEnd: new DateOnly(2026, month, day));
        var statistics = new StatisticsEngine(zone);
        var overview = statistics.OverviewForRange(events, [new Book(1, "Book", "Author")], range, TimeSpan.FromMinutes(5));
        var matrix = new InfographicEngine(zone).WeekHourMatrix(events, range, 5, false);

        Assert.Equal(firstSeconds, overview.Hourly[firstHour].Value * 60, 8);
        Assert.Equal(secondSeconds, overview.Hourly[secondHour].Value * 60, 8);
        Assert.Equal(thirdSeconds, overview.Hourly[thirdHour].Value * 60, 8);
        Assert.Equal(7200, overview.Hourly.Sum(point => point.Value) * 60, 8);
        Assert.Equal(7200, overview.Weekdays.Sum(point => point.Value) * 60, 8);
        Assert.Equal(7200, matrix.Sum(cell => cell.Value) * 60, 8);
        Assert.Equal(7200, statistics.Daily(events, TimeSpan.FromMinutes(5)).Sum(item => item.Seconds));
        Assert.Equal(1, new InfographicEngine(zone).WeekHourMatrix(events, range, 5, true).Sum(cell => cell.Value));
    }

    [Fact]
    public void HourlySlicesHandleHalfHourDstTransitionAndFractionalSeconds()
    {
        var zone = DstZone(TimeSpan.FromMinutes(630), TimeSpan.FromMinutes(30));
        var start = new DateTimeOffset(2026, 3, 8, 1, 50, 0, TimeSpan.FromMinutes(630));
        var events = new[] { Event(start, 4200) };
        var range = new DateRangeService(zone).Resolve("Custom", start.AddDays(2),
            customStart: new DateOnly(2026, 3, 8), customEnd: new DateOnly(2026, 3, 8));
        var overview = new StatisticsEngine(zone).OverviewForRange(events, [], range, TimeSpan.FromMinutes(5));

        Assert.Equal(10, overview.Hourly[1].Value);
        Assert.Equal(30, overview.Hourly[2].Value);
        Assert.Equal(30, overview.Hourly[3].Value);
        Assert.Equal(4200, overview.Hourly.Sum(point => point.Value) * 60);

        var fractionalRange = new DateRangeService(Zone).Resolve("Custom", Local(2026, 9, 14),
            customStart: new DateOnly(2026, 9, 13), customEnd: new DateOnly(2026, 9, 13));
        var fractional = new StatisticsEngine(Zone).OverviewForRange(
            [Event(Local(2026, 9, 13, 10, 59), 123.5)], [], fractionalRange, TimeSpan.FromMinutes(5));
        Assert.Equal(60, fractional.Hourly[10].Value * 60);
        Assert.Equal(63.5, fractional.Hourly[11].Value * 60, 8);
        Assert.Equal(123.5, fractional.Hourly.Sum(point => point.Value) * 60, 8);
    }

    [Fact]
    public void StoryAndInfographicsClipEventsAtPeriodAndYearBoundaries()
    {
        var events = new[]
        {
            Event(Local(2025, 12, 31, 23, 50), 1200),
            Event(Local(2026, 12, 31, 23, 50), 1200)
        };
        var range = new DateRangeService(Zone).Resolve("Custom", Local(2026, 1, 2),
            customStart: new DateOnly(2026, 1, 1), customEnd: new DateOnly(2026, 1, 1));
        var comparison = new AnalyticsEngine(Zone).Compare(events, range);
        var infographic = new InfographicEngine(Zone);
        var story = new VisualizationEngine(Zone).Build(events, range, comparison, 5, Local(2026, 1, 2));

        Assert.Equal(600, comparison.CurrentValue);
        Assert.Equal(600, infographic.PeriodDumbbells(events, range, 5).Single(item => item.Label == "Reading time").Current);
        Assert.Equal(10, infographic.WeekHourMatrix(events, range, 5, false).Sum(cell => cell.Value));
        Assert.Equal(10d / 60, Assert.Single(story.CumulativeJourney).Value, 8);
        Assert.Equal(1d / 6, story.MonthlyJourney[0].Value, 8);
        Assert.Equal(1d / 6, story.MonthlyJourney[11].Value, 8);
        Assert.Equal(20, infographic.BestDays(events, 2026, 5).Sum(point => point.Value));
    }

    private static TimeZoneInfo DstZone(TimeSpan baseOffset, TimeSpan daylightDelta)
    {
        var transitionTime = new DateTime(1, 1, 1, 2, 0, 0);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), daylightDelta,
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(transitionTime, 3, 8),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(transitionTime, 11, 1));
        return TimeZoneInfo.CreateCustomTimeZone("SyncTimeTests-DST-" + daylightDelta.Ticks,
            baseOffset, "Test DST zone", "Standard", "Daylight", [rule]);
    }

    [Fact]
    public async Task FullRepositoryReloadSeesOlderEditsDeletesAndNewRowsWithoutCollapsingIdenticalEvents()
    {
        var root = CreateTemporaryRoot();
        var database = Path.Combine(root, "statistics.db");
        try
        {
            await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = database, Pooling = false }.ToString());
            await writer.OpenAsync();
            await using var command = writer.CreateCommand();
            command.CommandText = """
                CREATE TABLE page_stat_data(id_book INTEGER,page INTEGER,start_time INTEGER,duration REAL,total_pages INTEGER);
                INSERT INTO page_stat_data VALUES(1,1,1700000000,120,100);
                INSERT INTO page_stat_data VALUES(1,1,1700000000,120,100);
                INSERT INTO page_stat_data VALUES(1,2,1800000000,60,100);
                """;
            await command.ExecuteNonQueryAsync();
            var repository = new SqliteReadestRepository(database);
            var initial = await repository.GetEventsAsync();
            Assert.Equal(3, initial.Count);
            Assert.Equal(2, initial.Count(item => item.StartTime == 1700000000 && item.DurationSeconds == 120));

            command.CommandText = "UPDATE page_stat_data SET page=8,duration=300,total_pages=200 WHERE rowid=1";
            await command.ExecuteNonQueryAsync();
            var edited = await repository.GetEventsAsync();
            Assert.Contains(edited, item => item.Page == 8 && item.DurationSeconds == 300 && item.TotalPages == 200);
            Assert.Equal(120, initial[0].DurationSeconds);

            command.CommandText = "DELETE FROM page_stat_data WHERE rowid=2";
            await command.ExecuteNonQueryAsync();
            var deleted = await repository.GetEventsAsync();
            Assert.Equal(2, deleted.Count);
            Assert.DoesNotContain(deleted, item => item.StartTime == 1700000000 && item.DurationSeconds == 120);

            command.CommandText = "INSERT INTO page_stat_data VALUES(1,3,1600000000,90,200)";
            await command.ExecuteNonQueryAsync();
            var added = await repository.GetEventsAsync();
            Assert.Equal(3, added.Count);
            Assert.Equal(1600000000, added[0].StartTime);
            Assert.Equal(450, added.Sum(item => item.DurationSeconds));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775808")]
    [InlineData("\"9223372036854775807\"")]
    [InlineData("\"not a date\"")]
    public async Task InvalidNoteEpochDoesNotAbortValidNotesInTheSameFile(string invalidDateJson)
    {
        var root = CreateTemporaryRoot();
        var directory = Directory.CreateDirectory(Path.Combine(root, "Books", "hash-a")).FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Books", "library.json"),
                """[{"hash":"hash-a","title":"Fixture Book","author":"Author"}]""");
            await File.WriteAllTextAsync(Path.Combine(directory, "config.json"), $$"""
                {"booknotes":[
                  {"id":"invalid-date","text":"Keep this note","createdAt":{{invalidDateJson}},"updatedAt":{{invalidDateJson}}},
                  {"id":"valid","text":"Valid dated note","createdAt":1700000000}
                ]}
                """);
            var repository = new ReadestNotesRepository(Path.Combine(root, "statistics.db"));

            var notes = await repository.LoadAsync();

            Assert.Equal(2, notes.Count);
            var invalid = Assert.Single(notes.Where(note => note.Id == "invalid-date"));
            Assert.Null(invalid.CreatedAt);
            Assert.Null(invalid.UpdatedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000),
                Assert.Single(notes.Where(note => note.Id == "valid")).CreatedAt);
            Assert.Equal(2, repository.LastDiagnostics.NotesLoaded);
            Assert.Equal(0, repository.LastDiagnostics.FilesFailed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTemporaryRoot() => Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "readest-sync-time-" + Guid.NewGuid().ToString("N"))).FullName;
}

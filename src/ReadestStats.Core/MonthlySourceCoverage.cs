using System.Globalization;

namespace ReadestStats.Core;

public enum ReadestCoverageState { AvailableSnapshot, LastKnownSnapshot, Unconfirmed, Unavailable }

/// <summary>
/// Describes the evidence available to a report. It deliberately does not infer how much
/// unrecorded reading happened, or promise that an imported source is a complete history.
/// </summary>
public sealed record MonthlySourceCoverage(string SourceScope, ReadestCoverageState ReadestState,
    DateTimeOffset? LastSuccessfulRefresh)
{
    public static MonthlySourceCoverage Create(string sourceScope, bool connected, bool refreshFailed,
        bool hasSnapshot, DateTimeOffset? lastSuccessfulRefresh)
    {
        var scope = sourceScope is "Manual" or "Readest" ? sourceScope : "All sources";
        var state = connected && !refreshFailed && lastSuccessfulRefresh is not null
            ? ReadestCoverageState.AvailableSnapshot
            : hasSnapshot || lastSuccessfulRefresh is not null
                ? ReadestCoverageState.LastKnownSnapshot
                : connected ? ReadestCoverageState.Unconfirmed : ReadestCoverageState.Unavailable;
        return new(scope, state, lastSuccessfulRefresh);
    }

    public string ToText(bool vietnamese = false)
    {
        var parts = new List<string>();
        if (SourceScope != "Manual")
        {
            parts.Add(ReadestState switch
            {
                ReadestCoverageState.AvailableSnapshot => vietnamese ? "Readest: dữ liệu nhập từ lần làm mới thành công gần nhất." : "Readest: imported data from the last successful refresh.",
                ReadestCoverageState.LastKnownSnapshot => vietnamese ? "Readest: đang dùng bản dữ liệu gần nhất; nguồn đang ngoại tuyến hoặc lần làm mới đã lỗi." : "Readest: last known snapshot; the source is offline or a refresh failed.",
                ReadestCoverageState.Unconfirmed => vietnamese ? "Readest: chưa làm mới thành công; chưa xác nhận được dữ liệu sách điện tử." : "Readest: no successful refresh yet; digital coverage is unconfirmed.",
                _ => vietnamese ? "Readest: nguồn không khả dụng; có thể thiếu hoạt động đọc sách điện tử." : "Readest: source unavailable; digital reading may be missing."
            });
            if (LastSuccessfulRefresh is { } refreshed)
            {
                var stamp = refreshed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
                parts.Add(vietnamese ? $"Làm mới thành công: {stamp}." : $"Last successful refresh: {stamp}.");
            }
        }
        if (SourceScope != "Readest")
            parts.Add(vietnamese ? "Sách giấy: dữ liệu cục bộ do ứng dụng lưu." : "Physical reading: app-owned local records.");
        parts.Add(vietnamese
            ? "Chỉ tính hoạt động đã ghi lại. Nguồn không khả dụng không đồng nghĩa với không đọc; số liệu không chứng minh toàn bộ lịch sử đọc."
            : "Recorded activity only. An unavailable source is not zero reading; these totals do not prove a complete reading history.");
        return string.Join(" ", parts);
    }
}

using System.Windows;
using ReadestStats.Core;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public sealed class ExportOptions : ObservableObject
{
    private bool _allItems, _includePrivate, _archiveMode;
    private readonly int _filteredCount, _totalCount;
    private readonly Func<ExportOptions, ExportScopeCounts>? _countScope;
    private ExportScopeCounts _counts = new();
    private string _unavailableReason = "";
    private static string Text(string en, string vi) => Localization.Localizer.Instance.IsVietnamese ? vi : en;
    public ExportOptions(bool physicalLibrary, int filteredCount, int totalCount,
        Func<ExportOptions, ExportScopeCounts>? countScope = null)
    {
        AllowArchive = physicalLibrary; _filteredCount = filteredCount; _totalCount = totalCount;
        _countScope = countScope;
        RefreshCounts();
    }
    public bool AllowArchive { get; }
    public string Heading => Text("Choose what to export", "Chọn dữ liệu cần xuất");
    public string Description => AllowArchive
        ? Text("Share a reading summary, or explicitly choose a full portable archive for importing elsewhere.", "Chia sẻ tóm tắt đọc sách, hoặc chủ động chọn bản lưu trữ đầy đủ để nhập sang máy khác.")
        : Text("Only the selected scope is exported. Your private writing is excluded by default.", "Chỉ xuất phạm vi được chọn. Nội dung riêng tư được loại trừ theo mặc định.");
    public string ScopeLabel => Text("EXPORT SCOPE", "PHẠM VI XUẤT");
    public string AllItemsLabel => Text("Entire library instead of current filters", "Toàn bộ thư viện thay vì bộ lọc hiện tại");
    public string PrivateLabel => AllowArchive ? Text("Include private session notes", "Bao gồm ghi chú riêng trong phiên đọc") : Text("Include Readest notes, personal writing and physical session notes", "Bao gồm ghi chú Readest, nội dung riêng và ghi chú phiên đọc sách giấy");
    public string ArchiveLabel => Text("Full portable archive (all books, sessions and private notes)", "Bản lưu trữ đầy đủ (mọi sách, phiên đọc và ghi chú riêng)");
    public string CancelLabel => Text("Cancel", "Hủy");
    public string ExportLabel => Text("Choose file and export", "Chọn tệp và xuất");
    public bool AllItems { get => _allItems; set { if (Set(ref _allItems, value)) RefreshCounts(); } }
    public bool IncludePrivate { get => _includePrivate; set { if (Set(ref _includePrivate, value)) { RefreshCounts(); Raise(nameof(Warning)); } } }
    public bool ArchiveMode { get => _archiveMode; set { if (Set(ref _archiveMode, value)) { Raise(nameof(IsSharing)); RefreshCounts(); Raise(nameof(Warning)); } } }
    public bool IsSharing => !ArchiveMode;
    public ExportScopeCounts Counts => _counts;
    public string UnavailableReason
    {
        get => _unavailableReason;
        set { if (Set(ref _unavailableReason, value ?? "")) { Raise(nameof(CanExport)); Raise(nameof(EmptyHint)); } }
    }
    public bool CanExport => string.IsNullOrWhiteSpace(UnavailableReason) && Counts.CanExport;
    public string CountLabel => AllowArchive
        ? Text($"{Counts.Books} books · {Counts.Sessions} sessions will be exported. " + (ArchiveMode ? "Full archive ignores current filters." : AllItems ? "Includes unlogged books from the entire library." : "Only books with sessions in the current journal filters."),
            $"Sẽ xuất {Counts.Books} sách · {Counts.Sessions} phiên đọc. " + (ArchiveMode ? "Bản lưu trữ đầy đủ không áp dụng bộ lọc hiện tại." : AllItems ? "Bao gồm cả sách chưa có phiên đọc trong toàn bộ thư viện." : "Chỉ gồm sách có phiên đọc trong bộ lọc nhật ký hiện tại."))
        : Text($"{Counts.Notes} notes will be exported · {Counts.Omitted} empty/private-only notes omitted.", $"Sẽ xuất {Counts.Notes} ghi chú · bỏ {Counts.Omitted} ghi chú trống/chỉ có nội dung riêng.");
    public string EmptyHint => !string.IsNullOrWhiteSpace(UnavailableReason) ? UnavailableReason : CanExport ? "" : AllowArchive
        ? Text("Nothing to export in this scope. Choose the entire library to include unlogged books, or add a book/session first.", "Phạm vi này không có dữ liệu để xuất. Chọn toàn bộ thư viện để gồm cả sách chưa đọc, hoặc thêm sách/phiên đọc trước.")
        : Counts.Omitted > 0
            ? Text("No shareable highlights in this scope. Change the scope, or include private writing only if you intend to share it.", "Phạm vi này không có đoạn trích để chia sẻ. Đổi phạm vi, hoặc chỉ bật nội dung riêng nếu bạn chủ động muốn chia sẻ.")
            : Text("No notes in this scope. Change the scope or add a note first.", "Phạm vi này chưa có ghi chú. Đổi phạm vi hoặc thêm ghi chú trước.");
    public void RefreshCounts()
    {
        var fallbackCount = Math.Max(0, AllItems || ArchiveMode ? _totalCount : _filteredCount);
        _counts = _countScope?.Invoke(this) ?? (AllowArchive ? new(Sessions: fallbackCount) : new(Notes: fallbackCount));
        foreach (var property in new[] { nameof(Counts), nameof(CountLabel), nameof(CanExport), nameof(EmptyHint) }) Raise(property);
    }
    public string Warning => ArchiveMode
        ? Text("This full archive contains private writing and may contain local file paths. It is not encrypted. Keep it private.", "Bản lưu trữ đầy đủ có nội dung riêng và có thể có đường dẫn tệp trên máy. Tệp không được mã hóa; hãy giữ riêng tư.")
        : IncludePrivate
            ? Text("Private writing will be readable in this unencrypted file. Local cover paths are removed from shareable library exports.", "Nội dung riêng sẽ đọc được trong tệp không mã hóa này. Đường dẫn bìa trên máy được bỏ khỏi bản xuất chia sẻ.")
            : Text("Private writing is excluded. Exported book titles, highlights and reading dates are still personal information. This file is not encrypted.", "Loại trừ nội dung riêng. Tên sách, đoạn trích và ngày đọc vẫn là thông tin cá nhân. Tệp này không được mã hóa.");
}

public partial class ExportOptionsWindow : Window
{
    public ExportOptionsWindow(ExportOptions options) { InitializeComponent(); DataContext = options; }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportOptions options) return;
        options.RefreshCounts();
        if (options.CanExport) DialogResult = true;
    }
}

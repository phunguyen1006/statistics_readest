using System.Windows;
using ReadestStats.Core;

namespace ReadestStats.Views;

public sealed class BackupPreviewPresentation
{
    private static string Text(string en, string vi) => Localization.Localizer.Instance.IsVietnamese ? vi : en;
    public sealed record DomainRow(string Name, string Counts, string Action);
    public string Heading => Text("Preview backup restore", "Xem trước khôi phục sao lưu");
    public string DomainHeader => Text("Data", "Dữ liệu");
    public string CountHeader => Text("Current → Backup", "Hiện tại → Sao lưu");
    public string ActionHeader => Text("After restore", "Sau khôi phục");
    public string CancelLabel => Text("Cancel — keep current data", "Hủy — giữ dữ liệu hiện tại");
    public string RestoreLabel => Text("Replace with this backup", "Thay bằng bản sao lưu này");
    public string Description => Text("Restore replaces the included app-owned data; it does not merge libraries. Counts below come from the actual backup contents. Readest books, notes and statistics remain untouched.", "Khôi phục thay thế dữ liệu thuộc app có trong bản sao lưu; không gộp thư viện. Số lượng bên dưới được đọc từ nội dung thực. Sách, ghi chú và thống kê của Readest không bị sửa.");
    public string RecoveryLabel => Text("A recovery copy is created before replacement. API keys are not portable. No private note text is displayed here.", "Tạo bản phục hồi trước khi thay thế. API key không chuyển giữa máy. Không hiển thị nội dung ghi chú riêng ở đây.");
    public string Verification { get; }
    public string Warnings { get; }
    public IReadOnlyList<DomainRow> Domains { get; }
    public BackupPreviewPresentation(BackupRestorePreview preview)
    {
        Verification = $"v{preview.Manifest.AppVersion} · {preview.Manifest.CreatedAtUtc.ToLocalTime():g} · " + Text(preview.VerificationStatus, preview.Manifest.SchemaVersion == 2 ? "Đã kiểm tra checksum" : "Sao lưu cũ · không có checksum");
        Warnings = Text(string.Join("\n", preview.Warnings), preview.Warnings.Count == 0 ? "" : "Có lưu ý về dữ liệu hiện tại hoặc nội dung bản sao lưu:\n" + string.Join("\n", preview.Warnings.Select(Localization.Localizer.Instance.Translate)));
        Domains = preview.Domains.Select(domain => new DomainRow(Text(domain.Name, domain.Name switch
        {
            "Physical books" => "Sách giấy", "Manual sessions" => "Phiên đọc sách giấy", "Session notes" => "Ghi chú phiên đọc", "Active timer" => "Phiên đang đọc", "Settings" => "Cài đặt", "Goals" => "Mục tiêu", "Note curation" => "Tùy chọn ghi chú", "Cached covers" => "Bìa lưu trên máy", "Readest source data" => "Dữ liệu nguồn Readest", _ => domain.Name
        }), domain.CountComparison.Replace("Unknown", Text("Unknown", "Chưa rõ")).Replace("Unchanged", Text("Unchanged", "Giữ nguyên")), Text(domain.Action, domain.Action switch
        {
            "Replace (not merge)" => "Thay thế (không gộp)", "Keep current" => "Giữ hiện tại", "Not modified" => "Không sửa", "Overlay (other covers kept)" => "Chép đè bìa trùng, giữ bìa khác", _ => domain.Action
        }))).ToArray();
    }
}

public partial class BackupPreviewWindow : Window
{
    public BackupPreviewWindow(BackupRestorePreview preview)
    {
        InitializeComponent();
        var presentation = new BackupPreviewPresentation(preview);
        DataContext = presentation;
        DomainGrid.Columns[0].Header = presentation.DomainHeader;
        DomainGrid.Columns[1].Header = presentation.CountHeader;
        DomainGrid.Columns[2].Header = presentation.ActionHeader;
    }
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Localization.UiDialog.Show(Localization.Localizer.Instance.IsVietnamese ? "Thay dữ liệu hiện tại bằng bản sao lưu này? Đây không phải thao tác gộp thư viện." : "Replace current app-owned data with this backup? This is not a library merge.",
            Localization.Localizer.Instance.IsVietnamese ? "Xác nhận khôi phục" : "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) DialogResult = true;
    }
}

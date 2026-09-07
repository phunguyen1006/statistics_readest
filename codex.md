Tôi muốn bạn xây dựng hoàn chỉnh một ứng dụng Windows desktop độc lập tên tạm thời là **Readest Stats**.

Mục tiêu của ứng dụng:

- Đọc trực tiếp dữ liệu reading statistics mà ứng dụng Readest đã lưu trên máy.
- Không sửa source code của Readest.
- Không inject vào Readest.
- Không cần Readest API.
- Không cần server.
- Không cần localhost.
- Không cần trình duyệt.
- Không Electron.
- Không Tauri/WebView.
- App phải là desktop app Windows native, nhẹ RAM/CPU.
- Cuối cùng build thành `.exe` để chỉ cần double-click là dùng.
- Readest có thể đang chạy đồng thời với Readest Stats.
- Readest Stats chỉ được phép READ dữ liệu của Readest, tuyệt đối không được thay đổi database của Readest.

## 1. Công nghệ bắt buộc

Sử dụng:

- C#
- .NET 8
- WPF
- MVVM
- Microsoft.Data.Sqlite hoặc SQLite provider phù hợp với database SQLite/libSQL hiện tại của Readest.

Ưu tiên UI WPF native.

Không sử dụng:

- Electron
- Chromium
- WebView2 cho UI
- Tauri
- React
- Node.js runtime
- local HTTP server
- Flask/FastAPI
- Python + PyInstaller
- browser dashboard

Nếu cần chart, ưu tiên tự render bằng WPF Canvas/Shapes/ItemsControl để giữ app nhẹ.

Chỉ thêm chart library nếu thực sự cần thiết. Nếu dùng library thì phải là library native .NET/WPF nhẹ, không kéo browser runtime.

## 2. Nguồn dữ liệu Readest

Readest hiện có database reading statistics:

`statistics.db`

Trên một bản cài Windows mặc định, trước tiên thử:

`%APPDATA%\com.bilingify.readest\Readest\statistics.db`

Không được giả định đây luôn là đường dẫn duy nhất.

Readest có hỗ trợ custom root directory, vì vậy phải xây dựng `ReadestDatabaseLocator`.

Thứ tự tìm database:

1. Kiểm tra:

`%APPDATA%\com.bilingify.readest\Readest\statistics.db`

2. Kiểm tra settings của Readest nếu tồn tại, đặc biệt:

`%APPDATA%\com.bilingify.readest\settings.json`

Tìm `customRootDir`.

Nếu `customRootDir` tồn tại, kiểm tra:

`<customRootDir>\Readest\statistics.db`

3. Hỗ trợ trường hợp portable/custom installation.

Không scan toàn bộ ổ C: một cách bừa bãi.

4. Nếu vẫn không tìm thấy, hiển thị màn hình:

"Không tìm thấy dữ liệu Readest"

với nút:

"Chọn statistics.db"

Cho phép người dùng browse trực tiếp đến file.

5. Lưu đường dẫn mà người dùng chọn vào config riêng của Readest Stats.

Config của app phải nằm trong folder riêng, ví dụ:

`%LOCALAPPDATA%\ReadestStats\`

Không được ghi config vào folder Readest.

## 3. Database schema cần hỗ trợ

Readest hiện sử dụng schema tương thích KOReader.

Các bảng quan trọng:

### `book`

Các field chính:

- id
- title
- authors
- notes
- last_open
- highlights
- pages
- series
- language
- md5
- total_read_time
- total_read_pages

### `page_stat_data`

Các field:

- id_book
- page
- start_time
- duration
- total_pages

Ý nghĩa:

- `id_book`: foreign key tới `book.id`
- `page`: trang được đọc
- `start_time`: Unix timestamp tính bằng giây
- `duration`: số giây đọc event đó
- `total_pages`: tổng số trang theo pagination tại thời điểm event

Unique key hiện tại tương ứng:

`(id_book, page, start_time)`

Ngoài ra có thể có:

- `numbers`
- `page_stat` VIEW
- các bảng extension của Readest trong tương lai.

App phải tolerant với việc database có thêm tables/columns mới.

Không hard-code kiểu "database chỉ được phép có chính xác từng này bảng".

Chỉ validate rằng các field tối thiểu cần thiết tồn tại.

## 4. Quy tắc an toàn database — RẤT QUAN TRỌNG

Đây là yêu cầu bắt buộc.

Readest Stats KHÔNG ĐƯỢC:

- INSERT
- UPDATE
- DELETE
- CREATE
- DROP
- ALTER
- VACUUM
- REINDEX
- migration database Readest
- thay đổi journal mode
- checkpoint WAL của Readest
- sửa pragma gây thay đổi DB

Chỉ được SELECT.

Mở database ở `ReadOnly` mode.

Sau khi mở connection, nếu SQLite provider hỗ trợ:

`PRAGMA query_only = ON`

nhưng không thực hiện bất kỳ PRAGMA nào có thể thay đổi database.

Readest sử dụng WAL.

Có thể tồn tại đồng thời:

- statistics.db
- statistics.db-wal
- statistics.db-shm

Không được bỏ qua WAL.

Không sử dụng `immutable=1` đối với database đang được Readest cập nhật vì dữ liệu trong WAL có thể không được nhìn thấy.

Phải cho phép Readest tiếp tục ghi DB trong khi Readest Stats đang mở.

Nếu database tạm thời busy/locked:

- không crash
- retry nhẹ
- ví dụ 200ms -> 500ms -> 1000ms
- sau đó hiển thị trạng thái thân thiện nếu vẫn thất bại.

Không được giải quyết lỗi lock bằng cách đóng hoặc kill Readest.

## 5. Kiểm tra database khi kết nối

Khi tìm thấy database:

1. Kiểm tra file tồn tại.
2. Mở read-only.
3. Kiểm tra `sqlite_master`.
4. Xác nhận có:
   - `book`
   - `page_stat_data`
5. Kiểm tra các columns tối thiểu.
6. Query thử:
   `SELECT COUNT(*) FROM page_stat_data`
7. Nếu hợp lệ:
   hiển thị trạng thái "Connected to Readest".
8. Nếu sai:
   không sửa database.
   Báo rõ đây không phải database statistics tương thích.

Trong Settings phải có diagnostic view:

- database path
- database size
- số books
- số page events
- first event
- latest event
- connection state
- schema version nếu đọc được
- nút "Open folder"
- nút "Change database"
- nút "Reconnect"

## 6. Timezone

`start_time` là Unix timestamp seconds.

Toàn bộ statistics theo ngày phải sử dụng LOCAL TIME của Windows, không coi timestamps là UTC calendar date.

Ví dụ:

- Today
- Yesterday
- streak
- weekday
- monthly charts
- heatmap

đều phải tính theo timezone local của OS.

Ưu tiên convert bằng `DateTimeOffset.FromUnixTimeSeconds(...).ToLocalTime()` hoặc cơ chế tương đương.

Nếu query khoảng thời gian lớn, hãy tính local day boundaries rồi convert boundaries về Unix timestamp để tận dụng index `start_time`.

Không query toàn DB lại một cách ngu ngốc mỗi lần UI redraw.

## 7. Kiến trúc project

Tách rõ các layer.

Gợi ý:

`ReadestStats/`

- `App.xaml`
- `MainWindow.xaml`

`Models/`
- BookStat.cs
- PageEvent.cs
- ReadingSession.cs
- DailyStat.cs
- HourlyStat.cs
- WeekdayStat.cs
- OverviewStats.cs
- PeriodComparison.cs

`Services/`
- ReadestDatabaseLocator.cs
- ReadestDatabaseService.cs
- StatisticsService.cs
- SessionBuilder.cs
- StatisticsCache.cs
- DatabaseWatcher.cs
- ExportService.cs
- AppSettingsService.cs

`ViewModels/`
- MainViewModel.cs
- DashboardViewModel.cs
- ActivityViewModel.cs
- BooksViewModel.cs
- BookDetailViewModel.cs
- SessionsViewModel.cs
- SettingsViewModel.cs

`Views/`
- DashboardView.xaml
- ActivityView.xaml
- BooksView.xaml
- BookDetailView.xaml
- SessionsView.xaml
- SettingsView.xaml

`Controls/`
- StatCard.xaml
- BarChart.xaml
- LineChart.xaml
- HeatmapCalendar.xaml
- HourDistribution.xaml
- StreakCard.xaml
- EmptyState.xaml

Nếu một cấu trúc tốt hơn phù hợp với WPF/MVVM thì được phép thay đổi, nhưng phải giữ separation of concerns.

Không nhét toàn bộ SQL/UI/code-behind vào MainWindow.

## 8. Giao diện tổng thể

Thiết kế UI hiện đại, tối giản, giống một reading analytics application.

Có sidebar bên trái:

- Overview
- Activity
- Books
- Sessions
- Settings

Header trên cùng:

`Readest Stats`

Bên phải:

- khoảng thời gian đang xem
- Refresh button
- trạng thái database

Hỗ trợ:

- Light mode
- Dark mode
- Follow system

Không cần animation phức tạp.

Ưu tiên mượt và nhẹ.

Window mặc định khoảng:

1200 x 760

Cho phép resize.

Minimum size hợp lý khoảng:

900 x 600

## 9. OVERVIEW DASHBOARD

Overview phải là màn hình quan trọng nhất.

### Row 1: Summary Cards

Hiển thị ít nhất:

#### Total Reading Time

Tính chính xác bằng:

`SUM(page_stat_data.duration)`

Format:

- < 60 phút -> `42 min`
- >= 60 phút -> `12h 34m`
- rất lớn -> `4d 12h`

Có thể dùng `book.total_read_time` để cross-check, nhưng nguồn canonical cho analytics là page events.

#### Reading Today

Tổng duration từ 00:00 local hôm nay đến hiện tại.

#### Current Streak

Số ngày liên tiếp có ít nhất một reading event.

Quy tắc:

- nếu hôm nay có đọc: streak kết thúc hôm nay
- nếu hôm nay chưa đọc nhưng hôm qua có đọc: vẫn giữ streak
- nếu cả hôm nay và hôm qua không đọc: streak = 0

#### Longest Streak

Chuỗi ngày đọc liên tục dài nhất lịch sử.

#### Active Reading Days

Số distinct local calendar dates có reading event.

#### Books Read

Số distinct books có ít nhất một `page_stat_data` event.

Không gọi đây là "Books Finished".

Readest DB không đảm bảo rằng có event nghĩa là đã đọc xong.

UI label nên là:

`Books Read`

hoặc:

`Books With Activity`

Nếu cần tooltip giải thích.

## 10. Reading Trend Chart

Dashboard có biểu đồ thời gian đọc.

Cho phép chọn:

- 7 days
- 30 days
- 90 days
- 1 year
- All time

30 days là mặc định.

Mỗi ngày:

`SUM(duration)`

Y-axis:

reading minutes/hours

X-axis:

date

Hover hoặc click point/bar hiển thị:

- date
- reading time
- books read that day
- sessions

Nếu khoảng 1 year:

aggregate theo tuần khi cần để chart không có hàng trăm label.

## 11. Period Comparison

Tính:

- thời gian đọc kỳ hiện tại
- kỳ trước cùng độ dài
- % tăng/giảm

Ví dụ 30 days:

current = last 30 days

previous = 30 days ngay trước đó

Hiển thị kiểu:

`+18% vs previous 30 days`

Nếu previous = 0 thì xử lý tránh divide-by-zero.

## 12. GitHub-style Reading Heatmap

Tạo heatmap 365 ngày gần nhất.

Giống contribution heatmap.

Mỗi cell = một ngày.

Intensity dựa trên tổng duration ngày đó.

Không nên normalize kiểu một ngày cực lớn khiến mọi ngày khác biến mất hoàn toàn.

Có thể dùng percentile/log scale hoặc bucket:

- 0
- very low
- low
- medium
- high

Hover/click:

`Sep 7, 2026`
`1h 24m`
`2 books`

Hiển thị month labels.

Có weekday labels gọn:

Mon
Wed
Fri

hoặc equivalent.

## 13. Time of Day Distribution

Biểu đồ 24 giờ:

0h -> 23h

Tổng hợp duration theo giờ local của `start_time`.

Hiển thị:

- tổng duration mỗi giờ
- percentage of total

Đánh dấu:

`Most active hour`

Ví dụ:

`21:00–22:00`

Ngoài 24-hour chart, chia thành:

- Morning: 05–11
- Afternoon: 12–16
- Evening: 17–21
- Night: 22–04

Hiển thị:

`You read most in the Evening`

## 14. Day of Week Statistics

Biểu đồ:

- Monday
- Tuesday
- Wednesday
- Thursday
- Friday
- Saturday
- Sunday

Hai chế độ:

- Total time
- Average per occurrence

Tính:

`Most active weekday`

Ví dụ:

`Sunday — avg 58m`

## 15. Monthly Statistics

Activity page phải có monthly view.

Hiển thị tháng hiện tại và cho chuyển:

`< Previous Month    September 2026    Next Month >`

Summary:

- total reading time
- active days
- average per active day
- books
- sessions
- longest reading day

Chart từng ngày trong tháng.

Có calendar heatmap.

## 16. Yearly Statistics

Cho phép chọn year.

Hiển thị:

- total hours
- active days
- average/month
- average active day
- books with activity
- current/longest streak trong năm
- best month
- best day

Bar chart 12 tháng.

Có yearly heatmap.

## 17. Sessions

Readest không lưu "session" trực tiếp mà lưu page events.

Phải reconstruct reading sessions.

Algorithm mặc định:

1. Sort event theo `start_time`.
2. Một event có:
   - start = start_time
   - end = start_time + duration
3. Event kế tiếp thuộc cùng session nếu gap giữa previous end và next start <= configurable threshold.
4. Threshold mặc định:
   `5 minutes`
5. Nếu gap lớn hơn threshold:
   tạo session mới.

Trong Settings cho thay đổi session gap:

- 2 minutes
- 5 minutes
- 10 minutes
- 15 minutes

Default 5.

Có hai khái niệm:

### Global Reading Session

Nếu người dùng đổi sách rất nhanh nhưng gap <= threshold thì vẫn có thể coi là cùng reading session.

### Per-book Session

Trong Book Detail, session phải group riêng theo book.

Không được sửa DB để lưu session. Tất cả derived in memory/query.

## 18. Sessions page

Hiển thị summary:

- total sessions
- average session
- median session
- longest session
- sessions this week
- average sessions/day

Danh sách recent sessions:

Ví dụ:

`Sep 7`
`21:03 – 21:48`
`45 min`
`Harry Potter`
`23 page events`

Nếu session gồm nhiều books:

`3 books`

Click mở details.

Session detail:

- start/end
- duration
- books
- event count
- reading timeline

## 19. Books page

Query tất cả books có statistics.

Mỗi row/card:

- Title
- Authors
- Total read time
- Active days
- Last read
- Estimated pages/activity
- Relative share of total reading time

Sort:

- Most read
- Recently read
- Title
- Reading time ascending
- Reading time descending

Search:

- title
- author

Không cần search mạng.

Book không có cover thì tạo placeholder đẹp bằng title initials.

KHÔNG download cover trên internet trong v1.

## 20. Book detail page

Click book mở detail.

Header:

- title
- author
- total reading time
- last read
- active days

Statistics:

- total time
- first read
- last read
- number of reading days
- current book streak
- longest book streak
- total sessions
- average session
- longest session
- median page-event duration

Readest hiện cũng có logic median page duration dựa trên recent page events; app có thể cung cấp:

`Median time per page event`

nhưng phải ghi rõ đây là page-event metric.

Charts:

- daily reading time
- 30/90/all
- heatmap
- hour distribution
- weekday distribution
- session history

## 21. Page statistics phải được ghi nhãn cẩn thận

Readest và KOReader/reflow EPUB có thể paginate khác nhau.

Vì vậy:

`duration` là metric đáng tin cậy nhất.

Không được quảng cáo page count như tuyệt đối chính xác.

Có thể hiển thị:

`Estimated pages`
`Page events`
`Recorded pages`

với tooltip:

"Page-based metrics may vary when pagination or reader layout changes."

Không lấy:

`COUNT(page_stat_data)` = "pages read"

vì mỗi row là event, không nhất thiết một trang mới.

Có thể hiển thị riêng:

- page events = COUNT(*)
- distinct recorded pages = COUNT(DISTINCT page)
- latest recorded page / total_pages

nhưng label phải chính xác.

## 22. Reading Insights

Overview có một card "Insights".

Các insight được tạo bằng rule-based logic local, không AI, không internet.

Ví dụ:

- `You read 24% more this week than last week.`
- `Your most active reading time is 9–10 PM.`
- `Sunday is your most consistent reading day.`
- `Your longest reading session this month was 1h 42m.`
- `You have read on 18 of the last 30 days.`
- `Your current streak is 7 days.`
- `This is your most-read book this month.`

Chỉ hiển thị insight có đủ dữ liệu.

Không bịa insight khi dữ liệu quá ít.

## 23. Personal Records

Tạo section:

`Personal Records`

Bao gồm:

- Longest streak
- Longest single reading session
- Most reading in one day
- Most reading in one week
- Most reading in one month
- Most active month
- Most-read book
- Most active hour
- Most active weekday

Click record nếu phù hợp để drill down.

## 24. Goals

Goals KHÔNG được lưu vào database Readest.

Lưu riêng trong:

`%LOCALAPPDATA%\ReadestStats\`

Cho phép tạo time-based goals:

- daily minutes
- weekly minutes
- monthly hours

Ví dụ:

Daily goal: 30 min

Progress:

`24 / 30 min`

Không bắt buộc feature "books finished per year", vì Readest stats DB không đáng tin để suy ra finished status.

Có thể để books-finished goal cho future version.

## 25. Auto Refresh

App phải cập nhật khi Readest ghi data mới.

Sử dụng `FileSystemWatcher` theo dõi folder chứa:

- statistics.db
- statistics.db-wal
- statistics.db-shm

Không refresh ngay mỗi filesystem event vì WAL có thể tạo rất nhiều events.

Debounce khoảng 1–2 giây.

Sau debounce:

- query lại dữ liệu cần thiết
- update ViewModel
- không rebuild toàn bộ UI không cần thiết.

Có nút:

`Refresh`

Có option Settings:

`Auto refresh when Readest data changes`

default ON.

Khi user đang đọc trong Readest và mở Readest Stats ở bên cạnh, stats nên tự tăng/cập nhật sau khi Readest flush event.

## 26. Performance

Máy mục tiêu có thể là laptop Windows cấu hình thấp.

Yêu cầu:

- CPU idle gần như 0%
- không polling DB liên tục mỗi 100ms/1s
- không background browser
- không web server
- không reload toàn bộ DB vô lý
- debounce FileSystemWatcher
- async database queries
- cancellation token khi đổi filter nhanh
- không block UI thread
- virtualization cho danh sách books/sessions nếu dài

Cache derived statistics theo period.

Invalidate cache khi database thay đổi.

Target thực tế:

- startup nhanh
- UI responsive
- idle memory càng thấp càng tốt
- không ưu tiên animation hơn performance.

## 27. SQL/query design

Tạo repository/service riêng.

Không đặt raw SQL lung tung trong ViewModels.

Các query chính nên có method riêng:

- GetOverviewAsync()
- GetDailyStatsAsync(start, end)
- GetBookStatsAsync(bookId)
- GetBookDailyStatsAsync(bookId, start, end)
- GetHourlyDistributionAsync(...)
- GetWeekdayDistributionAsync(...)
- GetBooksRankingAsync(...)
- GetEventsForSessionsAsync(...)
- GetActiveDatesAsync(...)
- GetFirstLastEventAsync(...)

Đối với period:

ưu tiên:

`WHERE start_time >= @start AND start_time < @end`

để tận dụng index `page_stat_data_start_time`.

Không wrap `start_time` trong `date()` ở mọi WHERE query nếu nó khiến SQLite full-scan.

Group calendar dates có thể thực hiện ở C# sau khi convert local timezone hoặc dùng SQL hợp lý sau khi đã giới hạn range.

## 28. Derived metric formulas

### Total reading time

`SUM(duration)`

### Reading time/day

Sum duration grouped by LOCAL calendar date.

### Active day

Local date có ít nhất 1 event với duration > 0.

### Current streak

Distinct active dates sorted descending.

Nếu latest active date không phải Today hoặc Yesterday:

0.

Sau đó count backwards từng ngày liên tục.

### Longest streak

Sort unique active dates ascending và tìm longest consecutive sequence.

### Average active-day reading time

`total duration / active days`

### Books with activity

`COUNT(DISTINCT id_book)` trên page events.

### Most-read book

Book có `SUM(duration)` lớn nhất.

### Most active hour

Local hour có `SUM(duration)` lớn nhất.

### Most active weekday

Cần có cả:

- total
- normalized average

để tránh bias đơn giản do số Mondays khác số Sundays trong custom period.

### Median

Phải tính median thật, không dùng average rồi gọi là median.

## 29. Empty states

Nếu database tồn tại nhưng chưa có page events:

Không hiển thị một đống dashboard `0 0 0 0` trông như lỗi.

Hiển thị:

`No reading activity yet`

`Read a book in Readest and your statistics will appear here automatically.`

Nếu có ít dữ liệu cho một chart:

hiển thị empty/insufficient state.

## 30. Error handling

Tất cả lỗi phải friendly.

Các case:

- statistics.db not found
- invalid database
- permission denied
- database locked/busy
- corrupted database
- schema incompatible
- database moved/deleted
- custom root changed
- settings malformed
- no reading events

Không để app crash.

Có optional diagnostic log riêng:

`%LOCALAPPDATA%\ReadestStats\logs\`

Không log nội dung sách.

Không log toàn bộ user file paths nếu không cần.

## 31. Settings page

Settings gồm:

### Data Source

- Database path
- Change
- Re-detect
- Open folder
- Connection status

### Statistics

- Session gap threshold
- First day of week: Monday/Sunday
- Default dashboard range
- Minimum activity duration nếu cần

### Appearance

- System
- Light
- Dark

### Refresh

- Auto refresh ON/OFF

### Export

- Export current statistics CSV
- Export summary JSON

Export chỉ xuất derived statistics của Readest Stats.

Không modify source DB.

## 32. About / Database note

Có thể có About nhỏ:

`Readest Stats is an independent local statistics viewer for Readest reading data.`

Không claim đây là official Readest app.

Không cần Readest logo nếu có vấn đề trademark/assets.

## 33. Testing bắt buộc

Tạo automated tests cho statistics engine.

Tạo temporary SQLite fixture theo Readest schema.

Seed data với timestamps đã biết.

Test:

### Total time

Nhiều rows -> SUM duration đúng.

### Day grouping

Event gần midnight -> đúng local date.

### Streak

- today only
- today + yesterday
- yesterday but not today
- missing 1 day
- month boundary
- year boundary

### Longest streak

Nhiều chuỗi khác nhau.

### Sessions

- gap 1 phút -> same session
- gap 4 phút -> same session với threshold 5
- gap 6 phút -> new session
- overlapping/adjacent event
- switching books

### Period comparison

- normal
- previous period zero

### Books ranking

Ordering đúng.

### Hourly

Unix timestamp -> đúng local hour.

### Weekday

Đúng weekday local.

### Empty database

UI/service không crash.

## 34. Database safety tests

Đây là acceptance criterion.

Viết test hoặc code guard để đảm bảo app không có code path ghi vào Readest database.

Repository interface cho Readest DB chỉ expose SELECT methods.

Không expose generic:

`ExecuteNonQuery(string sql)`

ra ViewModel.

Nếu cần SQLite connection nội bộ, giữ encapsulated.

Sau khi chạy app, database source không được bị thay đổi bởi Readest Stats.

Không được tạo:

- new table
- own metadata
- settings
- goals

trong `statistics.db`.

## 35. Live database testing

Nếu trên máy hiện tại tìm thấy database thật:

Trước tiên chỉ inspect read-only.

Không sửa nó.

In log:

- detected path
- tables
- column names
- book count
- event count

Nếu schema khác với assumption trên, thích nghi code với schema thực tế.

Database thật luôn là source of truth cao hơn assumption trong prompt.

Nếu có Readest source code/repository access, hãy tham khảo implementation hiện tại của:

`apps/readest-app/src/services/statistics/statisticsDb.ts`

và migration schema statistics.

Không copy UI unfinished của Readest; chỉ dùng schema/data semantics.

## 36. Build thành EXE

Khi hoàn thiện:

Build Release cho:

`win-x64`

Ưu tiên self-contained single-file build để máy khác không cần tự cài .NET Runtime.

Ví dụ cấu hình phù hợp:

- `PublishSingleFile=true`
- `SelfContained=true`
- `RuntimeIdentifier=win-x64`
- Release

Nếu SQLite native dependency khiến single-file cần extraction, cấu hình đúng để người dùng vẫn chỉ cần chạy một `.exe`.

Có thể dùng:

`IncludeNativeLibrariesForSelfExtract=true`

nếu cần.

Không để user phải chạy:

- npm install
- dotnet run
- python
- localhost server

sau khi build.

Output cuối phải có một file dễ thấy:

`ReadestStats.exe`

Tạo thêm script:

`build-release.ps1`

để lần sau tôi chỉ cần chạy một command là rebuild.

## 37. README

Tạo README ngắn gồm:

### What it does

Reads local Readest statistics.

### Data safety

Source DB is read-only.

### Default database location

`%APPDATA%\com.bilingify.readest\Readest\statistics.db`

### Build

Command cụ thể.

### Release

Đường dẫn `.exe`.

### Troubleshooting

Database not found.

## 38. Điều tôi KHÔNG muốn

Không làm prototype giả.

Không tạo UI với fake data rồi dừng lại.

Không chỉ viết architecture document.

Không chỉ tạo SQL queries.

Không chỉ tạo mockup.

Không yêu cầu tôi tự nối các phần code.

Không mở UI qua browser.

Không dùng Electron.

Không sửa Readest.

Không tạo fork Readest.

Không yêu cầu user export statistics thủ công.

Không copy database thủ công mỗi lần.

App phải tự đọc database Readest.

## 39. Quy trình bạn phải thực hiện

Hãy tự thực hiện toàn bộ công việc theo thứ tự:

1. Inspect workspace hiện tại.
2. Tạo solution/project nếu chưa có.
3. Xây database locator.
4. Xây read-only database layer.
5. Validate schema.
6. Test với fixture.
7. Xây statistics engine.
8. Test formulas.
9. Xây session reconstruction.
10. Xây WPF MVVM UI.
11. Nối UI với dữ liệu thật.
12. Thêm auto-refresh.
13. Thêm settings.
14. Thêm export.
15. Test empty/error states.
16. Test app khi Readest đang mở nếu có thể.
17. Build Release.
18. Publish self-contained `.exe`.
19. Chạy app build cuối để kiểm tra nó thực sự launch được.
20. Kiểm tra không có code path write vào statistics.db.
21. Sửa mọi compile/runtime error gặp phải.
22. Chỉ kết thúc khi có bản usable.

Không hỏi tôi xác nhận sau mỗi bước.

Nếu gặp vấn đề kỹ thuật, tự điều tra source Readest và chọn giải pháp an toàn nhất.

## 40. Definition of Done

Chỉ coi task hoàn thành khi:

- app mở bằng native Windows window
- không browser
- tự detect Readest statistics.db
- đọc được data thật
- Overview hiển thị đúng
- daily trend hoạt động
- heatmap hoạt động
- current streak hoạt động
- longest streak hoạt động
- hourly distribution hoạt động
- weekday statistics hoạt động
- monthly/yearly view hoạt động
- books ranking hoạt động
- book detail hoạt động
- session reconstruction hoạt động
- personal records hoạt động
- auto refresh hoạt động khi DB thay đổi
- light/dark mode hoạt động
- không ghi vào Readest DB
- chạy được khi Readest đang chạy
- tests pass
- Release build pass
- có `ReadestStats.exe`

Cuối task, báo cho tôi:

1. File `.exe` nằm ở đâu.
2. Database Readest được detect ở đường dẫn nào.
3. Bao nhiêu books/events được đọc từ database thật.
4. Các statistics đã implement.
5. RAM idle thực tế đo được nếu có thể.
6. Có chạy song song với Readest thành công không.
7. Những limitation còn lại.
8. Các file chính đã tạo/thay đổi.

Quan trọng nhất: ưu tiên **correctness + database safety + nhẹ máy** hơn animation hay UI cầu kỳ.
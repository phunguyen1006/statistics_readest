# Readest statistics data map

The source is opened with SQLite `ReadOnly` mode plus `PRAGMA query_only = ON`. No analytics path writes to Readest.

| Statistic | Required fields | Readest source | Transformation | Edge cases | Accuracy / fallback |
|---|---|---|---|---|---|
| Reading time | `start_time`, `duration` | `page_stat_data` | sum positive active seconds | overlaps and extreme events | Exact for recorded active duration; quality check flags >8h |
| Reading day | timestamp | `page_stat_data` | convert UTC timestamp to local date; distinct dates | midnight/timezone boundary | Exact under current Windows timezone |
| Logical session | start, duration, book | `page_stat_data` | sort events; merge overlap; join when next start ≤ prior end + configured gap | fragmented events, book switches | Inferred; default gap 5 minutes and user-configurable |
| Streak | local reading dates | normalized events | consecutive distinct local dates; current may end today or yesterday | accidental seconds | Minimum valid duration setting filters noise before analytics |
| Heatmap | daily duration/session/book totals | events + reconstructed sessions | 365 local days; quantile intensity | no activity, leap year | Exact totals; intensity is relative |
| Hour/weekday | local start time, duration | `page_stat_data` | group active seconds by local hour/day | event spanning hour boundary | Assigned by event start; documented inference |
| Time block | hour, duration | normalized events | morning 05–12, afternoon 12–17, evening 17–22, night 22–05 | midnight wrap | Deterministic inference |
| Session distribution | logical session duration | reconstructed sessions | `<5`, `5–10`, `10–20`, `20–30`, `30–60`, `60+` minutes | zero sessions | Inferred from configured gap |
| Top books | `id_book`, duration, timestamp | `page_stat_data` joined to `book` | rank by time; also show session count and active days | missing book | Orphans flagged in Data Quality |
| Finished books | user-selected status/date | app settings JSON | count one explicit `Finished` timestamp per tracked book | Readest lacks completion history | App-owned, clearly labelled; never inferred from import |
| Goal progress | duration, completion dates, target | normalized analytics + settings JSON | actual / target, elapsed-period pace and projection | zero goal / little data | Finite values; projection withheld before two elapsed days |
| Personal records | daily/session/book aggregates | common statistics engine | max day, week, month, session, streak, book, hour, weekday | ties/empty data | First deterministic maximum; empty list when unavailable |
| Book cover/open action | `md5` | `book` + `Books/<md5>` | resolve `cover.*` and supported ebook file | missing hash/file | Placeholder/disabled action; source file never copied |

Normalization currently rejects non-positive duration at query and engine boundaries, merges overlaps for session duration, preserves the last good dataset on transient locks, uses local calendar grouping, and reports duplicates/orphans/future timestamps/extreme durations through Data Quality.

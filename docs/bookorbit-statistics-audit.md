# BookOrbit statistics audit

Reference: official `bookorbit/bookorbit` repository at commit `3d6afb314957a6c52c9242c1ed3f8a35a35821e7` (inspected 2026-09-08). BookOrbit is AGPL-3.0. Readest Stats reimplements observable analytics behavior against Readest data; no BookOrbit source, component, query, or artwork is copied.

BookOrbit exposes 33 configurable Statistics charts: 19 library charts and 14 personal-reading charts. Its useful structural pattern is a compact summary followed by configurable, progressively loaded chart tiles. Readest Stats adopts the information hierarchy, not BookOrbit's visual design or runtime architecture.

| Metric / feature | BookOrbit UI / logic | Data required | Existing before 1.2? | Feasible from Readest | Classification | v1.2 action |
|---|---|---|---|---|---|---|
| Statistics summary | Summary card | book/status aggregates | Partial | Yes | A | Consolidated KPIs |
| Reading heatmap | `reading-heatmap` / daily aggregate | timestamp, duration | Yes | Yes | A | Included with drill-down |
| Peak reading hours | 24-hour chart | local hour, duration | Yes | Yes | A | Included |
| Favorite reading days | weekday chart | local weekday, duration | Yes | Yes | A | Included |
| Reading clock | circular time distribution | local hour, duration | Partial | Yes | A | Four blocks + 24h chart |
| Reading session timeline | weekly session items | reconstructed sessions | Yes | Yes | B | Kept in Sessions |
| Session archetypes | hour × duration × weekday | session start/duration | Partial | Yes | B | Distribution + time charts |
| Reading pace | duration vs progress delta | stable progress delta | No | Limited | C | Not shown |
| Reading source distribution | device/source | source field | No | No | C | Not shown |
| Books completed | completion dates | trustworthy completion event | App-owned | Yes, explicit | B | 12-month timeline |
| Completion timeline | monthly completion counts | completion dates | Partial | Yes, explicit | B | Included |
| Goal trajectory | cumulative actual vs target | completion dates + goal | Partial | Yes | B | Goal progress retained |
| Progress funnel | 25/50/75/100 progress | stable progress history | No | No | C | Not shown |
| Completion latency | started→finished duration | start + finish dates | Partial | Approximate | C | Not shown |
| Genre reading time | duration + genres | genre metadata | No | No | C | Not shown |
| Format distribution | book format | reliable format | No | Derivable from file | B | Deferred |
| Language distribution | language | book language | No | Yes | A | Deferred to library analytics |
| Books added over time | imported timestamp | added-at | No | No | C | Not shown |
| Storage by format | file path/size/format | file metadata | No | Yes | B | Deferred |
| Publication decade/year | publication year | publication metadata | No | No | C | Not shown |
| Top authors | author/book count | author | No | Yes | A | Deferred |
| Metadata completeness | metadata fields | title/author/etc. | Data Quality | Yes | A | Settings quality checks |
| Metadata score distribution | rich metadata score | many metadata fields | No | Limited | C | Not shown |
| Metadata freshness | fetch timestamp | metadata refresh time | No | No | C | Not shown |
| Library integrity | file + metadata presence | hashes/files | Yes | Yes | A | Settings quality score |
| Largest books | file size | resolved files | No | Yes | B | Deferred |
| Genre distribution/co-occurrence | genres | genre relations | No | No | C | Not shown |
| Top series | series | series metadata | No | Yes | A | Deferred |
| Acquisition lag | added vs publication year | both dates | No | No | C | Not shown |
| Reading-time summary | session duration sum | events/sessions | Yes | Yes | A | Consolidated |
| Session distribution | duration buckets | sessions | Yes | Yes | B | Included |
| Top books | duration/session/day ranks | events + books | Yes | Yes | A | Unified ranking |
| Personal records | maxima over daily/session aggregates | events + sessions | Yes | Yes | A | Eight records included |

Classification: **A** exact from Readest; **B** clean deterministic inference; **C** insufficient Readest data; **D** BookOrbit-only/library-server concerns. BookOrbit-specific multi-library, user, device-source, storage-server and metadata-provider analytics are intentionally excluded.

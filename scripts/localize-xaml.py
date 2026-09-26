"""Mechanical migration of static UI labels; never rewrites model values or user content."""
from pathlib import Path
import hashlib, re, html
root = Path(__file__).resolve().parents[1] / 'src/ReadestStats'
translations = {}
for line in (root / 'Localization/UiStrings.tsv').read_text(encoding='utf-8').splitlines():
    if '\t' in line:
        en, vi = line.split('\t', 1)
        translations[en.lower()] = vi
ignored = {'Readest Stats', 'CSV', 'JSON', 'Markdown', 'ISBN-13', '  Ctrl K'}
missing = set()
for path in [root / 'MainWindow.xaml', *sorted((root / 'Views').glob('*.xaml'))]:
    text = path.read_text(encoding='utf-8-sig')
    if 'xmlns:loc=' not in text: text = text.replace(' xmlns:x=', ' xmlns:loc="clr-namespace:ReadestStats.Localization" xmlns:x=', 1)
    # ComboBoxItem values are identifiers: localize their presentation, never their Content.
    text = re.sub(r'<ComboBoxItem Content="(Monday|Sunday)"\s*/>', r'<ComboBoxItem Content="\1" ContentTemplate="{StaticResource LocalizedOption}"/>', text)
    def label(m):
        attr, value = m.groups()
        if value.startswith('{') or not re.search('[A-Za-z]', value): return m.group()
        source = html.unescape(value)
        if source in ignored: return m.group()
        # Keys used by SelectedValuePath must remain language-independent.
        if attr == 'Content' and source in ['Monday', 'Sunday']: return m.group()
        if source.lower() not in translations:
            missing.add(source); return m.group()
        key = 's' + hashlib.sha256(source.lower().encode()).hexdigest()[:16].upper()
        return f'{attr}="{{loc:Loc Key={key}}}"'
    text = re.sub(r'\b(Text|Content|Header|ToolTip|Title|Label|Help|Detail|AutomationProperties.Name|AutomationProperties.HelpText)="([^"]*)"', label, text)
    # These bindings represent interface-generated text. Titles, quotations, authors and personal notes stay untouched.
    properties = {'Status','Error','FinishError','DraftError','SearchStatus','PauseResumeLabel','ActiveSessionState','LibrarySummary','SelectedBookProgressLabel','MergePreview','ComparisonText','ComparisonLegend','ComparisonHeading','TrendSummary','ConsistencyDetail','ReadingPeriodSummary','PageMetricReliability','LastSyncLabel','AppDatabaseSummary','SelectedBookStatusDetail','SelectedBookCycleSummary','NoteLibrarySummary','RandomNoteStatus','OpenBookStatus','NoteDiscoverySummary','NotesSummary','SourceCompositionSummary','CurrentStreakLabel','LongestStreakLabel','StreakContinuation','SelectedBookPlanSummary','BookEditorTitle','Action','Detail','Summary','Evidence','StateLabel','SourceLabel','DateLabel','ProgressLabel','TimeLabel','NotesStatus','NotesCountLabel','NotesEmptyMessage','UpdateStatus','PinBookLabel','SelectedBookOpenLabel','LinkEditionLabel','ReadingPlanPauseLabel','PatternSummary','PatternWeekdaySummary','YearLongestStreakLabel','MonthSummary','TypeLabel','PageLabel','SnoozeLabel','RandomEligibilityLabel'}
    def display(m):
        prop = m.group(2).split('.')[-1]
        if prop not in properties or 'Converter=' in m.group(3): return m.group()
        return f'{m.group(1)}="{{Binding {m.group(2)}{m.group(3)},Converter={{StaticResource UiText}}}}"'
    text = re.sub(r'(Text|Content|ToolTip|Detail|Value)="\{Binding ([\w.]+)([^"}]*)\}"', display, text)
    # String option controls retain their original SelectedItem while displaying translated labels.
    def combo(m):
        tag = m.group()
        if 'ItemsSource="{Binding ' in tag and 'DisplayMemberPath=' not in tag and 'ItemTemplate=' not in tag and any(x in tag for x in ['Options}', 'Modes}', 'Filters}', 'Sorts}', 'Metrics}', 'Granularities}', 'Pools}', 'Durations}']):
            return tag.replace('<ComboBox ', '<ComboBox ItemTemplate="{StaticResource LocalizedOption}" ', 1)
        return tag
    text = re.sub(r'<ComboBox\s[^>]*>', combo, text)
    path.write_text(text, encoding='utf-8')
for source in sorted(missing): print(source)

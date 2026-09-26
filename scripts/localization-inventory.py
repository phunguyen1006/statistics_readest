from pathlib import Path
import re, html
root = Path(__file__).resolve().parents[1] / 'src/ReadestStats'
values = set()
for p in [root / 'MainWindow.xaml', *sorted((root / 'Views').glob('*.xaml'))]:
    for key, value in re.findall(r'\b(Text|Content|Header|ToolTip|Title|Label|Help|AutomationProperties.Name|AutomationProperties.HelpText)="([^"]*)"', p.read_text(encoding='utf-8-sig')):
        if value and not value.startswith('{') and re.search('[A-Za-z]', value): values.add(html.unescape(value))
for value in sorted(values): print(value)

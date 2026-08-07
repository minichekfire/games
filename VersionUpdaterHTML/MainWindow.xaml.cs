using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace InfinitariumManager
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _filePath = "";
        private string _originalHtml = "";

        // Full ru/en dictionaries parsed from the file, in original order, BEFORE any edits.
        // Used at save-time to preserve every translation key that this tool doesn't manage
        // (site chrome strings like changelog_link, eyebrow_text, etc).
        private List<KeyValuePair<string, string>> _ruOriginal = new List<KeyValuePair<string, string>>();
        private List<KeyValuePair<string, string>> _enOriginal = new List<KeyValuePair<string, string>>();

        public string[] SectionTypes { get; } = { "Additions", "Improvements", "Bug Fixes", "Patches", "Custom" };

        private static readonly Dictionary<string, (string Ru, string En)> FixedSectionTitles =
            new Dictionary<string, (string, string)>
            {
                { "additions",    ("Добавления:", "Additions:") },
                { "improvements", ("Улучшения:", "Improvements:") },
                { "bug_fixes",    ("Исправления ошибок:", "Bug Fixes:") },
                { "patches",      ("Патчи:", "Patches:") },
            };

        private static readonly Dictionary<string, string> TypeToKey = new Dictionary<string, string>
        {
            { "Additions", "additions" },
            { "Improvements", "improvements" },
            { "Bug Fixes", "bug_fixes" },
            { "Patches", "patches" },
        };

        // Matches: const translations = { ru: { ... }, en: { ... } };
        private static readonly Regex TranslationsBlockRegex = new Regex(
            @"const translations = \{\s*\r?\n\s*ru:\s*\{([\s\S]*?)\r?\n\s*\},\s*\r?\n\s*en:\s*\{([\s\S]*?)\r?\n\s*\}\s*\r?\n\s*\};",
            RegexOptions.Compiled);

        private static readonly Regex KeyValueRegex = new Regex(
            "(\\w+):\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,?",
            RegexOptions.Compiled);

        public class VersionItem : INotifyPropertyChanged
        {
            private string _id = "";
            private string _label = "";
            private bool _isUnreleased;

            public string Id
            {
                get => _id;
                set { _id = value; OnPropertyChanged(nameof(Id)); OnPropertyChanged(nameof(VersionTag)); }
            }
            public string Label
            {
                get => _label;
                set { _label = value; OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(ButtonText)); }
            }
            public ObservableCollection<ChangelogSection> Sections { get; set; } = new ObservableCollection<ChangelogSection>();
            public bool IsUnreleased
            {
                get => _isUnreleased;
                set { _isUnreleased = value; OnPropertyChanged(nameof(IsUnreleased)); }
            }

            public string VersionTag => Id;
            public string ButtonText => Label;

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class ChangelogSection : INotifyPropertyChanged
        {
            private string _type = "Additions";
            private string _customTitleRu = "";
            private string _customTitleEn = "";
            private string _itemsRuText = "";
            private string _itemsEnText = "";

            public string Type
            {
                get => _type;
                set { _type = value; OnPropertyChanged(nameof(Type)); OnPropertyChanged(nameof(IsCustomVisible)); }
            }
            public string CustomTitleRu
            {
                get => _customTitleRu;
                set { _customTitleRu = value; OnPropertyChanged(nameof(CustomTitleRu)); }
            }
            public string CustomTitleEn
            {
                get => _customTitleEn;
                set { _customTitleEn = value; OnPropertyChanged(nameof(CustomTitleEn)); }
            }
            public string ItemsRuText
            {
                get => _itemsRuText;
                set { _itemsRuText = value; OnPropertyChanged(nameof(ItemsRuText)); }
            }
            public string ItemsEnText
            {
                get => _itemsEnText;
                set { _itemsEnText = value; OnPropertyChanged(nameof(ItemsEnText)); }
            }

            public Visibility IsCustomVisible => _type == "Custom" ? Visibility.Visible : Visibility.Collapsed;

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private ObservableCollection<VersionItem> _versions = new ObservableCollection<VersionItem>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            LstVersions.ItemsSource = _versions;
            ListSections.ItemsSource = new ObservableCollection<ChangelogSection>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "HTML Files|*.html", Title = "Выберите файл" };
            if (dialog.ShowDialog() == true)
            {
                _filePath = dialog.FileName;
                LblFilePath.Text = System.IO.Path.GetFileName(_filePath);
                BtnSave.IsEnabled = true;
                try
                {
                    _originalHtml = File.ReadAllText(_filePath);
                    ParseHtml();
                    LblStatus.Text = $"Загружено {_versions.Count} версий.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ================= PARSING =================

        private static string SanitizeId(string id) => Regex.Replace(id ?? "", "[^a-zA-Z0-9]", "");

        private static string UnescapeJs(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

        private static string EscapeJs(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void ParseTranslations(string html, out List<KeyValuePair<string, string>> ruList, out List<KeyValuePair<string, string>> enList)
        {
            ruList = new List<KeyValuePair<string, string>>();
            enList = new List<KeyValuePair<string, string>>();

            var blockMatch = TranslationsBlockRegex.Match(html);
            if (!blockMatch.Success) return;

            string ruBlock = blockMatch.Groups[1].Value;
            string enBlock = blockMatch.Groups[2].Value;

            foreach (Match m in KeyValueRegex.Matches(ruBlock))
                ruList.Add(new KeyValuePair<string, string>(m.Groups[1].Value, UnescapeJs(m.Groups[2].Value)));

            foreach (Match m in KeyValueRegex.Matches(enBlock))
                enList.Add(new KeyValuePair<string, string>(m.Groups[1].Value, UnescapeJs(m.Groups[2].Value)));
        }

        private void ParseHtml()
        {
            _versions.Clear();

            ParseTranslations(_originalHtml, out _ruOriginal, out _enOriginal);
            var ruDict = _ruOriginal.GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
            var enDict = _enOriginal.GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);

            var buttonPattern = new Regex(@"<button\s+[^>]*?class=""tab-btn""[^>]*?data-tab=""(.*?)""[^>]*?>(.*?)</button>", RegexOptions.Singleline);
            var buttonMatches = buttonPattern.Matches(_originalHtml);

            foreach (Match match in buttonMatches)
            {
                string id = match.Groups[1].Value.Trim();
                string label = match.Groups[2].Value.Trim();

                // A version is "unreleased" if the stylesheet has a dedicated
                // .tab-btn[data-tab="ID"] rule targeting it (see ReplaceUnreleasedCss).
                bool isUnreleased = _originalHtml.Contains($".tab-btn[data-tab=\"{id}\"]");

                string contentPattern = $@"<div\s+[^>]*?id=""{Regex.Escape(id)}""[^>]*?class=""tab-content[^""]*""[^>]*?>([\s\S]*?)</div>\s*(?=<div\s+[^>]*?id=""|</div>\s*<button class=""close-modal"")";
                Match contentMatch = Regex.Match(_originalHtml, contentPattern);

                string rawContent = contentMatch.Success ? contentMatch.Groups[1].Value.Trim() : "";

                var sections = HtmlToSections(rawContent, ruDict, enDict);

                _versions.Add(new VersionItem
                {
                    Id = id,
                    Label = label,
                    Sections = sections,
                    IsUnreleased = isUnreleased
                });
            }
        }

        private ObservableCollection<ChangelogSection> HtmlToSections(string html, Dictionary<string, string> ruDict, Dictionary<string, string> enDict)
        {
            var sections = new ObservableCollection<ChangelogSection>();

            string sectionPattern = @"<h3\s+data-lang-key=""(.*?)"">.*?</h3>\s*<ul\s+class=""changelog-list"">([\s\S]*?)</ul>";
            MatchCollection matches = Regex.Matches(html, sectionPattern, RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                string h3Key = m.Groups[1].Value.Trim();
                string listHtml = m.Groups[2].Value;

                var ruLines = new List<string>();
                var enLines = new List<string>();

                MatchCollection liMatches = Regex.Matches(listHtml, @"<li\s+data-lang-key=""(.*?)"">([\s\S]*?)</li>", RegexOptions.Singleline);
                foreach (Match li in liMatches)
                {
                    string itemKey = li.Groups[1].Value.Trim();
                    string fallback = li.Groups[2].Value.Trim();
                    ruLines.Add(ruDict.TryGetValue(itemKey, out var rv) ? rv : fallback);
                    enLines.Add(enDict.TryGetValue(itemKey, out var ev) ? ev : fallback);
                }

                var section = new ChangelogSection
                {
                    ItemsRuText = string.Join(Environment.NewLine, ruLines),
                    ItemsEnText = string.Join(Environment.NewLine, enLines)
                };

                switch (h3Key)
                {
                    case "additions": section.Type = "Additions"; break;
                    case "improvements": section.Type = "Improvements"; break;
                    case "bug_fixes": section.Type = "Bug Fixes"; break;
                    case "patches": section.Type = "Patches"; break;
                    default:
                        section.Type = "Custom";
                        string ruTitle = ruDict.TryGetValue(h3Key, out var rt) ? rt : h3Key;
                        string enTitle = enDict.TryGetValue(h3Key, out var et) ? et : h3Key;
                        section.CustomTitleRu = ruTitle.TrimEnd(':').Trim();
                        section.CustomTitleEn = enTitle.TrimEnd(':').Trim();
                        break;
                }

                sections.Add(section);
            }

            return sections;
        }

        // ================= EDITOR WIRING =================

        private void LstVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstVersions.SelectedItem is VersionItem item)
            {
                TxtId.Text = item.Id;
                TxtButtonLabel.Text = item.Label;
                ChkUnreleased.IsChecked = item.IsUnreleased;
                ListSections.ItemsSource = item.Sections;
            }
            else
            {
                ClearEditor();
            }
        }

        private void BtnAddSection_Click(object sender, RoutedEventArgs e)
        {
            if (ListSections.ItemsSource is ObservableCollection<ChangelogSection> collection)
            {
                collection.Add(new ChangelogSection { Type = "Additions" });
            }
        }

        private void BtnRemoveSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChangelogSection section)
            {
                if (ListSections.ItemsSource is ObservableCollection<ChangelogSection> collection)
                {
                    collection.Remove(section);
                }
            }
        }

        private void BtnUpdateVersion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtId.Text))
            {
                MessageBox.Show("Введите ID версии!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string id = TxtId.Text.Trim();
            string label = TxtButtonLabel.Text.Trim();
            bool isUnreleased = ChkUnreleased.IsChecked == true;

            var currentSections = ListSections.ItemsSource as ObservableCollection<ChangelogSection>
                                   ?? new ObservableCollection<ChangelogSection>();

            var existing = _versions.FirstOrDefault(v => v.Id == id);

            if (existing != null)
            {
                existing.Label = label;
                existing.Sections = currentSections;
                existing.IsUnreleased = isUnreleased;
                LblStatus.Text = $"Версия {id} обновлена.";
            }
            else
            {
                var newItem = new VersionItem
                {
                    Id = id,
                    Label = label,
                    Sections = new ObservableCollection<ChangelogSection>(currentSections),
                    IsUnreleased = isUnreleased
                };
                _versions.Insert(0, newItem);
                LstVersions.SelectedItem = newItem;
                LblStatus.Text = $"Версия {id} добавлена.";
            }
        }

        private void BtnNewVersion_Click(object sender, RoutedEventArgs e)
        {
            LstVersions.SelectedIndex = -1;
            ClearEditor();
            LblStatus.Text = "Готов к созданию новой версии.";
        }

        private void BtnDeleteVersion_Click(object sender, RoutedEventArgs e)
        {
            if (LstVersions.SelectedItem is VersionItem item)
            {
                if (MessageBox.Show($"Удалить версию {item.Id}?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _versions.Remove(item);
                    ClearEditor();
                    LblStatus.Text = $"Версия {item.Id} удалена.";
                }
            }
            else
            {
                MessageBox.Show("Сначала выберите версию для удаления.", "Подсказка", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearEditor()
        {
            TxtId.Clear();
            TxtButtonLabel.Clear();
            ChkUnreleased.IsChecked = false;
            ListSections.ItemsSource = new ObservableCollection<ChangelogSection>();
        }

        // ================= SAVING =================

        private static int FindMatchingBrace(string s, int openBraceIndex)
        {
            int depth = 0;
            for (int i = openBraceIndex; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private string BuildUnreleasedCss(List<string> ids)
        {
            if (ids.Count == 0)
                return "        /* unreleased tabs — managed automatically, do not edit manually */";

            var sb = new StringBuilder();
            sb.AppendLine("        /* unreleased / beta-in-dev versions shown red — managed automatically, do not edit manually */");
            foreach (var id in ids)
            {
                sb.AppendLine($"        .tab-btn[data-tab=\"{id}\"] {{");
                sb.AppendLine("            color: var(--accent-red-bright);");
                sb.AppendLine("            border-color: rgba(194,46,58,0.4);");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine($"            .tab-btn[data-tab=\"{id}\"].active {{");
                sb.AppendLine("                background: linear-gradient(135deg, var(--accent-red), #7a1b24);");
                sb.AppendLine("                color: #fff;");
                sb.AppendLine("                border-color: var(--accent-red-bright);");
                sb.AppendLine("            }");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private string ReplaceUnreleasedCss(string html, List<string> unreleasedIds)
        {
            int activeIdx = html.IndexOf(".tab-btn.active", StringComparison.Ordinal);
            if (activeIdx < 0) return html; // stylesheet shape not recognized, leave untouched

            int braceOpen = html.IndexOf('{', activeIdx);
            if (braceOpen < 0) return html;
            int braceClose = FindMatchingBrace(html, braceOpen);
            if (braceClose < 0) return html;

            int nextSelectorIdx = html.IndexOf(".changelog-content-area", braceClose, StringComparison.Ordinal);
            if (nextSelectorIdx < 0) return html;

            string before = html.Substring(0, braceClose + 1);
            string after = html.Substring(nextSelectorIdx);
            string managed = BuildUnreleasedCss(unreleasedIds);

            return before + "\r\n\r\n" + managed + "\r\n\r\n        " + after;
        }

        private static void EnsureDefault(List<KeyValuePair<string, string>> list, string key, string value)
        {
            if (!list.Any(kv => kv.Key == key))
                list.Add(new KeyValuePair<string, string>(key, value));
        }

        private static bool IsManagedKey(string key) => key.StartsWith("cl_") || key.StartsWith("custom_");

        private string BuildTranslationsReplacement(List<KeyValuePair<string, string>> ruFinal, List<KeyValuePair<string, string>> enFinal)
        {
            var sb = new StringBuilder();
            sb.Append("const translations = {\r\n        ru: {\r\n");
            foreach (var kv in ruFinal)
                sb.Append($"        {kv.Key}: \"{EscapeJs(kv.Value)}\",\r\n");
            sb.Append("        },\r\n        en: {\r\n");
            foreach (var kv in enFinal)
                sb.Append($"        {kv.Key}: \"{EscapeJs(kv.Value)}\",\r\n");
            sb.Append("        }\r\n        };");
            return sb.ToString();
        }

        private void BtnMoveSectionUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChangelogSection section)
            {
                if (ListSections.ItemsSource is ObservableCollection<ChangelogSection> collection)
                {
                    int index = collection.IndexOf(section);
                    if (index > 0)
                    {
                        collection.Move(index, index - 1);
                        ListSections.UpdateLayout();
                    }
                }
            }
        }

        private void BtnMoveSectionDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChangelogSection section)
            {
                if (ListSections.ItemsSource is ObservableCollection<ChangelogSection> collection)
                {
                    int index = collection.IndexOf(section);
                    if (index < collection.Count - 1)
                    {
                        collection.Move(index, index + 1);
                        ListSections.UpdateLayout();
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            try
            {
                // Preserve every translation key this tool doesn't own (site chrome, UI labels, etc).
                var ruFinal = _ruOriginal.Where(kv => !IsManagedKey(kv.Key)).ToList();
                var enFinal = _enOriginal.Where(kv => !IsManagedKey(kv.Key)).ToList();
                foreach (var kv in FixedSectionTitles)
                {
                    EnsureDefault(ruFinal, kv.Key, kv.Value.Ru);
                    EnsureDefault(enFinal, kv.Key, kv.Value.En);
                }

                string newTabsHtml = "";
                string newContentHtml = "";
                var unreleasedIds = new List<string>();

                foreach (var v in _versions)
                {
                    if (v.IsUnreleased) unreleasedIds.Add(v.Id);

                    newTabsHtml += $@"                    <button class=""tab-btn"" data-tab=""{v.Id}"">{v.Label}</button>" + Environment.NewLine;

                    string activeClass = (v == _versions[0]) ? " active" : "";
                    newContentHtml += $@"                    <div id=""{v.Id}"" class=""tab-content{activeClass}"">" + Environment.NewLine;

                    string sanitizedId = SanitizeId(v.Id);
                    int itemCounter = 0;
                    int sectionCounter = 0;

                    foreach (var sec in v.Sections)
                    {
                        sectionCounter++;

                        string h3Key;
                        string h3TextEn;
                        if (sec.Type == "Custom")
                        {
                            h3Key = $"custom_{sanitizedId}_{sectionCounter}";
                            string ruTitle = (sec.CustomTitleRu ?? "").Trim();
                            string enTitle = (sec.CustomTitleEn ?? "").Trim();
                            if (!ruTitle.EndsWith(":")) ruTitle += ":";
                            if (!enTitle.EndsWith(":")) enTitle += ":";
                            ruFinal.Add(new KeyValuePair<string, string>(h3Key, ruTitle));
                            enFinal.Add(new KeyValuePair<string, string>(h3Key, enTitle));
                            h3TextEn = enTitle;
                        }
                        else
                        {
                            h3Key = TypeToKey[sec.Type];
                            h3TextEn = FixedSectionTitles[h3Key].En;
                        }

                        var ruLines = (sec.ItemsRuText ?? "").Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None)
                                        .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                        var enLines = (sec.ItemsEnText ?? "").Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None)
                                        .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                        int lineCount = Math.Max(ruLines.Count, enLines.Count);
                        while (ruLines.Count < lineCount) ruLines.Add("");
                        while (enLines.Count < lineCount) enLines.Add("");

                        string itemsHtml = "";
                        for (int i = 0; i < lineCount; i++)
                        {
                            itemCounter++;
                            string itemKey = $"cl_{sanitizedId}_{itemCounter}";
                            ruFinal.Add(new KeyValuePair<string, string>(itemKey, ruLines[i]));
                            enFinal.Add(new KeyValuePair<string, string>(itemKey, enLines[i]));
                            itemsHtml += $@"<li data-lang-key=""{itemKey}"">{enLines[i]}</li>" + Environment.NewLine;
                        }

                        newContentHtml += $@"                        <div class=""changelog-section"">" + Environment.NewLine;
                        newContentHtml += $@"                            <h3 data-lang-key=""{h3Key}"">{h3TextEn}</h3>" + Environment.NewLine;
                        newContentHtml += $@"                            <ul class=""changelog-list"">" + Environment.NewLine;
                        newContentHtml += itemsHtml;
                        newContentHtml += $@"                            </ul>" + Environment.NewLine;
                        newContentHtml += $@"                        </div>" + Environment.NewLine;
                    }

                    newContentHtml += $@"                    </div>" + Environment.NewLine;
                }

                string updatedHtml = _originalHtml;

                string tabsPattern = @"(<div class=""changelog-tabs"">)([\s\S]*?)(</div>)";
                updatedHtml = Regex.Replace(updatedHtml, tabsPattern, m =>
                    m.Groups[1].Value + Environment.NewLine + newTabsHtml + "                " + m.Groups[3].Value,
                    RegexOptions.Singleline);

                string contentPattern = @"(<div class=""changelog-content-area"">)[\s\S]*(</div>\s*<button class=""close-modal"")";
                updatedHtml = Regex.Replace(updatedHtml, contentPattern, m =>
                    m.Groups[1].Value + Environment.NewLine + newContentHtml + "                " + m.Groups[2].Value,
                    RegexOptions.Singleline);

                updatedHtml = ReplaceUnreleasedCss(updatedHtml, unreleasedIds);

                string translationsReplacement = BuildTranslationsReplacement(ruFinal, enFinal);
                updatedHtml = TranslationsBlockRegex.Replace(updatedHtml, m => translationsReplacement);

                File.WriteAllText(_filePath, updatedHtml);
                _originalHtml = updatedHtml;
                ParseTranslations(_originalHtml, out _ruOriginal, out _enOriginal);

                MessageBox.Show("Сохранено успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                LblStatus.Text = "Файл сохранён.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= EXPORT =================

        private void BtnExportSingleTxt_Click(object sender, RoutedEventArgs e)
        {
            if (!(LstVersions.SelectedItem is VersionItem selectedVersion))
            {
                MessageBox.Show("Сначала выберите версию для экспорта.", "Подсказка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files|*.txt",
                Title = "Сохранить Changelog версии",
                FileName = $"{selectedVersion.Id}_changelog.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string content = GenerateVersionTxt(selectedVersion);
                    File.WriteAllText(dialog.FileName, content);
                    LblStatus.Text = $"Версия {selectedVersion.Id} сохранена в TXT.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportAllTxt_Click(object sender, RoutedEventArgs e)
        {
            if (_versions.Count == 0)
            {
                MessageBox.Show("Нет версий для экспорта.", "Подсказка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files|*.txt",
                Title = "Сохранить полный Changelog",
                FileName = "TheLateLight_Full_Changelog.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string content = GenerateFullChangelogTxt();
                    File.WriteAllText(dialog.FileName, content);
                    LblStatus.Text = "Полный Changelog сохранен в TXT.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string GenerateVersionTxt(VersionItem version)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Version: {version.Id}");
            sb.AppendLine($"Label: {version.Label}");
            sb.AppendLine($"Status: {(version.IsUnreleased ? "UNRELEASED" : "Released")}");
            sb.AppendLine(new string('-', 50));
            sb.AppendLine();

            foreach (var section in version.Sections)
            {
                string title = section.Type == "Custom" ? section.CustomTitleEn : section.Type;
                if (string.IsNullOrWhiteSpace(title)) continue;

                sb.AppendLine($"[{title}]");

                var ruLines = (section.ItemsRuText ?? "").Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var enLines = (section.ItemsEnText ?? "").Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < Math.Max(ruLines.Length, enLines.Length); i++)
                {
                    string en = i < enLines.Length ? enLines[i].Trim() : "";
                    string ru = i < ruLines.Length ? ruLines[i].Trim() : "";
                    if (!string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(ru))
                        sb.AppendLine($"- {en}  |  {ru}");
                    else
                        sb.AppendLine($"- {(string.IsNullOrEmpty(en) ? ru : en)}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GenerateFullChangelogTxt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("THE LATE LIGHT — FULL CHANGELOG");
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            foreach (var version in _versions)
            {
                sb.Append(GenerateVersionTxt(version));
                sb.AppendLine(new string('.', 50));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
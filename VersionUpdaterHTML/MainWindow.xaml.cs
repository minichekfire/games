using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        public class WikiArticle : INotifyPropertyChanged
        {
            private string _id = "";
            private string _titleRu = "";
            private string _titleEn = "";
            private string _contentRu = "";
            private string _contentEn = "";

            public string Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }
            public string TitleRu { get => _titleRu; set { _titleRu = value; OnPropertyChanged(nameof(TitleRu)); OnPropertyChanged(nameof(DisplayTitle)); } }
            public string TitleEn { get => _titleEn; set { _titleEn = value; OnPropertyChanged(nameof(TitleEn)); OnPropertyChanged(nameof(DisplayTitle)); } }
            public string ContentRu { get => _contentRu; set { _contentRu = value; OnPropertyChanged(nameof(ContentRu)); } }
            public string ContentEn { get => _contentEn; set { _contentEn = value; OnPropertyChanged(nameof(ContentEn)); } }
            public string DisplayTitle => !string.IsNullOrWhiteSpace(TitleRu) ? TitleRu : TitleEn;

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class WikiCategory : INotifyPropertyChanged
        {
            private string _id = "";
            private string _nameRu = "";
            private string _nameEn = "";

            public string Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }

            public string NameRu
            {
                get => _nameRu;
                set { _nameRu = value; OnPropertyChanged(nameof(NameRu)); OnPropertyChanged(nameof(DisplayName)); }
            }

            public string NameEn
            {
                get => _nameEn;
                set { _nameEn = value; OnPropertyChanged(nameof(NameEn)); OnPropertyChanged(nameof(DisplayName)); }
            }

            public string DisplayName => !string.IsNullOrWhiteSpace(NameRu) ? NameRu : NameEn;

            public ObservableCollection<WikiArticle> Articles { get; set; } = new ObservableCollection<WikiArticle>();

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private class WikiArticleDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("titleRu")] public string TitleRu { get; set; } = "";
            [JsonPropertyName("titleEn")] public string TitleEn { get; set; } = "";
            [JsonPropertyName("contentRu")] public string ContentRu { get; set; } = "";
            [JsonPropertyName("contentEn")] public string ContentEn { get; set; } = "";
            [JsonPropertyName("title")] public string LegacyTitle { set { if (string.IsNullOrEmpty(TitleRu)) TitleRu = value; } }
            [JsonPropertyName("content")] public string LegacyContent { set { if (string.IsNullOrEmpty(ContentRu)) ContentRu = value; } }
        }

        private class WikiCategoryDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("nameRu")] public string NameRu { get; set; } = "";
            [JsonPropertyName("nameEn")] public string NameEn { get; set; } = "";
            [JsonPropertyName("articles")] public List<WikiArticleDto> Articles { get; set; } = new List<WikiArticleDto>();
            [JsonPropertyName("name")] public string LegacyName { set { if (string.IsNullOrEmpty(NameRu)) NameRu = value; } }
        }

        private class WikiRootDto
        {
            [JsonPropertyName("categories")] public List<WikiCategoryDto> Categories { get; set; } = new List<WikiCategoryDto>();
        }

        private ObservableCollection<VersionItem> _versions = new ObservableCollection<VersionItem>();

        private string _wikiFilePath = "";
        private ObservableCollection<WikiCategory> _wikiCategories = new ObservableCollection<WikiCategory>();
        private WikiArticle _selectedWikiArticle = null;
        private bool _suppressArticleTextEvents = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            LstVersions.ItemsSource = _versions;
            ListSections.ItemsSource = new ObservableCollection<ChangelogSection>();
            LstCategories.ItemsSource = _wikiCategories;
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
            if (activeIdx < 0) return html;

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

        private static readonly JsonSerializerOptions WikiWriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string GenerateWikiId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        private void LoadWikiFromDto(WikiRootDto dto)
        {
            _wikiCategories.Clear();
            foreach (var catDto in dto.Categories)
            {
                var cat = new WikiCategory
                {
                    Id = string.IsNullOrWhiteSpace(catDto.Id) ? GenerateWikiId() : catDto.Id,
                    NameRu = catDto.NameRu,
                    NameEn = catDto.NameEn
                };
                foreach (var artDto in catDto.Articles)
                {
                    cat.Articles.Add(new WikiArticle
                    {
                        Id = string.IsNullOrWhiteSpace(artDto.Id) ? GenerateWikiId() : artDto.Id,
                        TitleRu = artDto.TitleRu,
                        TitleEn = artDto.TitleEn,
                        ContentRu = artDto.ContentRu,
                        ContentEn = artDto.ContentEn
                    });
                }
                _wikiCategories.Add(cat);
            }
        }

        private WikiRootDto BuildWikiDto()
        {
            var dto = new WikiRootDto();
            foreach (var cat in _wikiCategories)
            {
                var catDto = new WikiCategoryDto
                {
                    Id = cat.Id,
                    NameRu = cat.NameRu,
                    NameEn = cat.NameEn
                };
                foreach (var art in cat.Articles)
                {
                    catDto.Articles.Add(new WikiArticleDto
                    {
                        Id = art.Id,
                        TitleRu = art.TitleRu,
                        TitleEn = art.TitleEn,
                        ContentRu = art.ContentRu,
                        ContentEn = art.ContentEn
                    });
                }
                dto.Categories.Add(catDto);
            }
            return dto;
        }

        private void BtnWikiOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "JSON Files|*.json", Title = "Выберите wiki-data.json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    var dto = JsonSerializer.Deserialize<WikiRootDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                              ?? new WikiRootDto();
                    LoadWikiFromDto(dto);

                    _wikiFilePath = dialog.FileName;
                    LblWikiFilePath.Text = System.IO.Path.GetFileName(_wikiFilePath);
                    BtnWikiSave.IsEnabled = true;
                    ClearWikiEditor();
                    LblStatus.Text = $"Загружено {_wikiCategories.Count} категорий вики.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось прочитать файл: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnWikiNew_Click(object sender, RoutedEventArgs e)
        {
            if (_wikiCategories.Count > 0)
            {
                if (MessageBox.Show("Начать новый файл вики? Несохранённые изменения текущего файла будут потеряны из памяти (сам файл на диске не тронется).",
                        "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            _wikiCategories.Clear();
            _wikiFilePath = "";
            LblWikiFilePath.Text = "Файл не выбран (будет создан при сохранении)";
            BtnWikiSave.IsEnabled = true;
            ClearWikiEditor();
            LblStatus.Text = "Создан новый пустой файл вики. Добавьте категории и статьи, затем сохраните.";
        }

        private void BtnWikiSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_wikiFilePath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON Files|*.json",
                    Title = "Сохранить wiki-data.json",
                    FileName = "wiki-data.json"
                };
                if (dialog.ShowDialog() != true) return;
                _wikiFilePath = dialog.FileName;
                LblWikiFilePath.Text = System.IO.Path.GetFileName(_wikiFilePath);
            }

            try
            {
                var dto = BuildWikiDto();
                string json = JsonSerializer.Serialize(dto, WikiWriteOptions);
                File.WriteAllText(_wikiFilePath, json);

                MessageBox.Show("Wiki сохранена успешно!\n\nНе забудьте загрузить этот файл на хостинг рядом с wiki.html, чтобы изменения увидели все игроки.",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                LblStatus.Text = "wiki-data.json сохранён.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private WikiCategory SelectedCategory => LstCategories.SelectedItem as WikiCategory;

        private void LstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cat = SelectedCategory;
            LstArticles.ItemsSource = cat?.Articles;
            BtnAddArticle.IsEnabled = cat != null;
            ClearWikiEditor();
        }

        private void LstArticles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedWikiArticle = LstArticles.SelectedItem as WikiArticle;
            if (_selectedWikiArticle == null)
            {
                ClearWikiEditor();
                return;
            }

            _suppressArticleTextEvents = true;
            TxtArticleTitleRu.Text = _selectedWikiArticle.TitleRu;
            TxtArticleTitleEn.Text = _selectedWikiArticle.TitleEn;
            TxtArticleContentRu.Text = _selectedWikiArticle.ContentRu;
            TxtArticleContentEn.Text = _selectedWikiArticle.ContentEn;
            _suppressArticleTextEvents = false;

            ScrollArticleEditor.Visibility = Visibility.Visible;
            LblWikiEmptyState.Visibility = Visibility.Collapsed;
        }

        private void ClearWikiEditor()
        {
            _selectedWikiArticle = null;
            _suppressArticleTextEvents = true;
            TxtArticleTitleRu.Text = "";
            TxtArticleTitleEn.Text = "";
            TxtArticleContentRu.Text = "";
            TxtArticleContentEn.Text = "";
            _suppressArticleTextEvents = false;
            ScrollArticleEditor.Visibility = Visibility.Collapsed;
            LblWikiEmptyState.Visibility = Visibility.Visible;
        }

        private void TxtArticle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressArticleTextEvents || _selectedWikiArticle == null || !(sender is TextBox tb)) return;

            string tag = tb.Tag as string;
            switch (tag)
            {
                case "TitleRu": _selectedWikiArticle.TitleRu = tb.Text; break;
                case "TitleEn": _selectedWikiArticle.TitleEn = tb.Text; break;
                case "ContentRu": _selectedWikiArticle.ContentRu = tb.Text; break;
                case "ContentEn": _selectedWikiArticle.ContentEn = tb.Text; break;
            }
        }

        private void BtnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            var cat = new WikiCategory
            {
                Id = GenerateWikiId(),
                NameRu = "Новая категория",
                NameEn = "New Category"
            };
            _wikiCategories.Add(cat);
            LstCategories.SelectedItem = cat;
            LblStatus.Text = "Категория добавлена. Переименуйте её и добавьте статьи.";
        }

        private void BtnDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WikiCategory cat)
            {
                if (MessageBox.Show($"Удалить категорию «{cat.DisplayName}» вместе со всеми статьями в ней?",
                        "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _wikiCategories.Remove(cat);
                    LstArticles.ItemsSource = null;
                    ClearWikiEditor();
                }
            }
        }

        private void BtnAddArticle_Click(object sender, RoutedEventArgs e)
        {
            var cat = SelectedCategory;
            if (cat == null) return;

            var art = new WikiArticle { Id = GenerateWikiId(), TitleRu = "Новая статья", TitleEn = "New Article", ContentRu = "Текст статьи...", ContentEn = "Article content..." };
            cat.Articles.Add(art);
            LstArticles.SelectedItem = art;
            LblStatus.Text = "Статья добавлена.";
        }

        private void BtnDeleteArticle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WikiArticle art)
            {
                var cat = SelectedCategory;
                if (cat == null) return;
                if (MessageBox.Show($"Удалить статью «{art.DisplayTitle}»?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    cat.Articles.Remove(art);
                    if (_selectedWikiArticle == art) ClearWikiEditor();
                }
            }
        }
    }
}
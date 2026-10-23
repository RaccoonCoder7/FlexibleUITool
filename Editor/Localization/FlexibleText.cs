using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    public enum FlexibleLanguage
    {
        Auto,
        Korean,
        Japanese,
        English,
    }

    // 에디터 UI 문구 번역 (키 = 영어 원문, 표 = Strings.tsv의 en/ko/ja 열). 언어는 사용자별 EditorPrefs
    public static class FlexibleText
    {
        public static string TablePath => Path.Combine(FlexibleSettingsUtility.ToolFullPath, "Editor", "Localization", "Strings.tsv");
        private const string LanguageKey = "FlexibleUITool.Language";

        private static Dictionary<string, string[]> table;
        private static DateTime tableTime;
        private static double nextCheck;
        private static int forcedEnglish;

        public static event Action Changed;

        public static FlexibleLanguage Setting
        {
            get => (FlexibleLanguage)EditorPrefs.GetInt(LanguageKey, (int)FlexibleLanguage.Auto);
            set
            {
                if (value == Setting)
                    return;
                EditorPrefs.SetInt(LanguageKey, (int)value);
                Changed?.Invoke();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        // 실제로 쓰는 언어 (Auto면 Unity 에디터 언어, 못 읽으면 OS 언어)
        public static FlexibleLanguage Current
        {
            get
            {
                if (forcedEnglish > 0)
                    return FlexibleLanguage.English;
                FlexibleLanguage setting = Setting;
                return setting != FlexibleLanguage.Auto ? setting : FromSystem(GetEditorLanguage());
            }
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            int column = Current switch
            {
                FlexibleLanguage.Korean => 1,
                FlexibleLanguage.Japanese => 2,
                _ => 0,
            };
            if (column == 0)
                return key;

            Dictionary<string, string[]> map = GetTable();
            return map.TryGetValue(key, out string[] row) && row.Length > column && !string.IsNullOrEmpty(row[column]) ? row[column] : key;
        }

        // 번역된 서식 문자열에 값을 넣는다 ({0}, {1:0.00} ...)
        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        // 프로필 표시 이름 (설정 애셋의 이름은 영어 키, 표시할 때 번역)
        public static string Label(FlexibleProfile profile)
        {
            if (profile == null)
                return "-";
            return string.IsNullOrEmpty(profile.displayName) ? profile.id : $"{T(profile.displayName)} ({profile.id})";
        }

        // CLI 결과처럼 언어와 무관하게 영어여야 하는 구간
        public static IDisposable ForceEnglish()
        {
            forcedEnglish++;
            return new Scope();
        }

        // 표에 없는 키 (번역 누락 점검용)
        public static bool Has(string key)
        {
            return GetTable().ContainsKey(key);
        }

        public static IEnumerable<string> Keys => GetTable().Keys;

        private static Dictionary<string, string[]> GetTable()
        {
            double now = EditorApplication.timeSinceStartup;
            if (table != null && now < nextCheck)
                return table;
            nextCheck = now + 2.0;

            DateTime time = File.Exists(TablePath) ? File.GetLastWriteTimeUtc(TablePath) : DateTime.MinValue;
            if (table != null && time == tableTime)
                return table;

            tableTime = time;
            table = Load(TablePath);
            return table;
        }

        // 한 줄 = 영어\t한국어\t일본어. #으로 시작하면 주석, 칸 안의 줄바꿈은 \n 으로 적는다
        private static Dictionary<string, string[]> Load(string path)
        {
            var map = new Dictionary<string, string[]>();
            if (!File.Exists(path))
                return map;

            foreach (string line in File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] cells = line.Split('\t');
                for (int i = 0; i < cells.Length; i++)
                    cells[i] = cells[i].Replace("\\n", "\n");
                if (cells[0].Length > 0)
                    map[cells[0]] = cells;
            }
            return map;
        }

        private static SystemLanguage GetEditorLanguage()
        {
            try
            {
                Type database = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LocalizationDatabase");
                object value = database?.GetProperty("currentEditorLanguage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null);
                if (value is SystemLanguage language)
                    return language;
            }
            catch (Exception)
            {
                // 내부 API가 바뀌었으면 OS 언어로
            }
            return Application.systemLanguage;
        }

        private static FlexibleLanguage FromSystem(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Korean: return FlexibleLanguage.Korean;
                case SystemLanguage.Japanese: return FlexibleLanguage.Japanese;
                default: return FlexibleLanguage.English;
            }
        }

        private sealed class Scope : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                forcedEnglish--;
            }
        }
    }

    // Tools > FlexibleUITool > Language 메뉴
    public static class FlexibleLanguageMenu
    {
        private const string Root = "Tools/FlexibleUITool/Language/";
        private const string AutoItem = Root + "Auto (Unity editor language)";
        private const string KoreanItem = Root + "한국어";
        private const string JapaneseItem = Root + "日本語";
        private const string EnglishItem = Root + "English";

        [MenuItem(AutoItem, false, 100)] private static void SetAuto() => FlexibleText.Setting = FlexibleLanguage.Auto;
        [MenuItem(KoreanItem, false, 101)] private static void SetKorean() => FlexibleText.Setting = FlexibleLanguage.Korean;
        [MenuItem(JapaneseItem, false, 102)] private static void SetJapanese() => FlexibleText.Setting = FlexibleLanguage.Japanese;
        [MenuItem(EnglishItem, false, 103)] private static void SetEnglish() => FlexibleText.Setting = FlexibleLanguage.English;

        [MenuItem(AutoItem, true)] private static bool CheckAuto() => Check(AutoItem, FlexibleLanguage.Auto);
        [MenuItem(KoreanItem, true)] private static bool CheckKorean() => Check(KoreanItem, FlexibleLanguage.Korean);
        [MenuItem(JapaneseItem, true)] private static bool CheckJapanese() => Check(JapaneseItem, FlexibleLanguage.Japanese);
        [MenuItem(EnglishItem, true)] private static bool CheckEnglish() => Check(EnglishItem, FlexibleLanguage.English);

        private static bool Check(string item, FlexibleLanguage language)
        {
            Menu.SetChecked(item, FlexibleText.Setting == language);
            return true;
        }
    }
}

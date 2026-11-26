// Campfire Linux port — 基于 Xocio/Campfire v1.1.32 (GPL-3.0)，新增文件（非改写）。
// 作用：Linux GUI 的三语本地化（简体中文 / 繁體中文 / English）。
//   设计对齐上游 src/Campfire/Localization/Text.cs：Dictionary 词表 + LanguageChanged
//   事件 + 索引器绑定。上游的 TextExtension 依赖 System.Windows.Data / Markup（WPF），
//   在 Avalonia 用不了，改为在 ViewModelBase 暴露 LocSource 索引器，XAML 写 {Binding L[Key]}。
//   术语沿用上游四语词表的译法；Linux 移植专属的说明文案另见 Strings.cs。
//   语言切换时：静态文案由 LocSource 的索引器通知刷新；ViewModel 通过弱引用订阅
//   RaiseAll() 刷新带参数的动态文案（子页 VM 每次新建，用弱引用避免静态事件泄漏）。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Campfire.Avalonia.Localization
{
    public enum UiLanguage
    {
        Chinese,
        TraditionalChinese,
        English,
    }

    public static class Loc
    {
        private const string SettingsKey = "ui.language";
        private static UiLanguage _language = DetectSystemLanguage();
        private static bool _loaded;
        private static readonly List<WeakReference<Action>> Subscribers =
            new List<WeakReference<Action>>();

        /// <summary>供 LocSource 订阅；长生命周期单例使用，不会泄漏。</summary>
        public static event Action LanguageChanged;

        public static UiLanguage Language => _language;

        /// <summary>标题栏语言按钮上显示的短码。</summary>
        public static string Label => _language == UiLanguage.English
            ? "EN"
            : (_language == UiLanguage.TraditionalChinese ? "繁" : "简");

        public static string Tooltip => Get("LangTooltip");

        /// <summary>应用启动时调用：读 settings.ini，没设过就跟随系统语言。</summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            UiLanguage? saved = Parse(AppSettings.Get(SettingsKey));
            if (saved.HasValue)
                _language = saved.Value;
        }

        public static void SetLanguage(UiLanguage value)
        {
            _loaded = true;
            if (_language == value) return;
            _language = value;
            AppSettings.Set(SettingsKey, Code(value));
            Notify();
        }

        public static UiLanguage NextLanguage()
        {
            switch (_language)
            {
                case UiLanguage.Chinese: return UiLanguage.TraditionalChinese;
                case UiLanguage.TraditionalChinese: return UiLanguage.English;
                default: return UiLanguage.Chinese;
            }
        }

        /// <summary>取词；缺译时退回简体，仍缺则返回键名（便于发现漏翻）。</summary>
        public static string Get(string key)
        {
            Dictionary<string, string> table =
                _language == UiLanguage.English ? Strings.English
                : _language == UiLanguage.TraditionalChinese ? Strings.TraditionalChinese
                : Strings.Chinese;
            string value;
            if (table.TryGetValue(key, out value)) return value;
            if (Strings.Chinese.TryGetValue(key, out value)) return value;
            return key;
        }

        public static string Format(string key, params object[] args)
            => args == null || args.Length == 0
                ? Get(key)
                : string.Format(CultureInfo.CurrentCulture, Get(key), args);

        /// <summary>弱引用订阅：调用方须自己持有该委托的强引用（ViewModelBase 就是这么做的）。</summary>
        public static void Subscribe(Action callback)
        {
            if (callback == null) return;
            Subscribers.RemoveAll(w => !w.TryGetTarget(out _));
            Subscribers.Add(new WeakReference<Action>(callback));
        }

        private static void Notify()
        {
            foreach (WeakReference<Action> weak in Subscribers.ToArray())
            {
                Action callback;
                if (weak.TryGetTarget(out callback))
                    callback();
            }
            LanguageChanged?.Invoke();
        }

        private static UiLanguage DetectSystemLanguage()
        {
            CultureInfo culture = CultureInfo.CurrentUICulture;
            if (culture.TwoLetterISOLanguageName != "zh")
                return UiLanguage.English;
            string name = culture.Name;
            return name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0
                || name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.TraditionalChinese
                : UiLanguage.Chinese;
        }

        private static UiLanguage? Parse(string code)
        {
            switch ((code ?? "").ToLowerInvariant())
            {
                case "zh-hans": case "zh-cn": return UiLanguage.Chinese;
                case "zh-hant": case "zh-tw": case "zh-hk": return UiLanguage.TraditionalChinese;
                case "en": case "en-us": return UiLanguage.English;
                default: return null;
            }
        }

        private static string Code(UiLanguage language)
        {
            switch (language)
            {
                case UiLanguage.TraditionalChinese: return "zh-Hant";
                case UiLanguage.English: return "en";
                default: return "zh-Hans";
            }
        }
    }

    /// <summary>XAML 索引器绑定源：{Binding L[Key]}。</summary>
    public sealed class LocSource : INotifyPropertyChanged
    {
        public static LocSource Instance { get; } = new LocSource();

        private LocSource()
        {
            Loc.LanguageChanged += Raise;
        }

        public string this[string key] => Loc.Get(key);

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise()
        {
            // Avalonia 的索引器绑定监听 "Item[]"；同时发 "Item" 兼容不同版本实现。
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        }
    }
}

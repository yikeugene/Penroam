using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Data;
using Moye.ViewModels;

namespace Moye.Localization;

/// <summary>Interface language only; document content and formatting cultures stay unchanged.</summary>
public sealed class LocalizationService : ObservableObject
{
    private static readonly ResourceManager Resources = new("Moye.Localization.Strings", typeof(LocalizationService).Assembly);
    public static LocalizationService Current { get; } = new();
    private string _languageCode = "en";
    public string LanguageCode => _languageCode;
    public string this[string key] => GetText(key, _languageCode);

    public static string NormalizeLanguage(string? language) => language == "zh-TW" ? "zh-TW" : "en";

    public static void SetLanguage(string? language)
    {
        var code = NormalizeLanguage(language);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is { HasShutdownStarted: false } && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => Current.ChangeLanguage(code));
        else Current.ChangeLanguage(code);
    }

    private void ChangeLanguage(string code)
    {
        if (!Set(ref _languageCode, code, nameof(LanguageCode))) return;
        Notify("Item[]");
    }

    public static string GetText(string key, string language) =>
        Resources.GetString(key, CultureInfo.GetCultureInfo(NormalizeLanguage(language))) ?? key;

    public static string Text(string key, params object?[] arguments)
    {
        var template = Current[key];
        return arguments.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }

    public static LocalizedMessage M(string key, params object?[] arguments) => new(key, arguments);

    public static Binding Binding(string key) => new($"[{key}]") { Source = Current, Mode = BindingMode.OneWay };

    public static void Bind(DependencyObject target, DependencyProperty property, string key, params object?[] arguments)
    {
        if (arguments.Length == 0) BindingOperations.SetBinding(target, property, Binding(key));
        else BindingOperations.SetBinding(target, property, new Binding(nameof(LanguageCode))
        {
            Source = Current, Mode = BindingMode.OneWay, Converter = new FixedArgumentsConverter(key, arguments.ToArray())
        });
    }

    public static MultiBinding FormatBinding(string key, params BindingBase[] arguments)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = new ArgumentsConverter(key) };
        binding.Bindings.Add(new Binding(nameof(LanguageCode)) { Source = Current, Mode = BindingMode.OneWay });
        foreach (var argument in arguments) binding.Bindings.Add(argument);
        return binding;
    }

    public static MultiBinding CountBinding(string key, string singularKey, BindingBase count)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = new ArgumentsConverter(key, singularKey) };
        binding.Bindings.Add(new Binding(nameof(LanguageCode)) { Source = Current, Mode = BindingMode.OneWay });
        binding.Bindings.Add(count);
        return binding;
    }

    private sealed class FixedArgumentsConverter(string key, object?[] arguments) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Text(key, arguments);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }

    private sealed class ArgumentsConverter(string key, string? singularKey = null) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Skip(1).Any(value => value == DependencyProperty.UnsetValue)
                ? DependencyProperty.UnsetValue : Text(singularKey is not null && values[1] is int count && count == 1 ? singularKey : key, values.Skip(1).ToArray());
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            targetTypes.Select(_ => System.Windows.Data.Binding.DoNothing).ToArray();
    }
}

/// <summary>A message retains its identity so an existing status can change language without losing its meaning.</summary>
public sealed record LocalizedMessage
{
    public string Key { get; }
    public object?[] Arguments { get; }
    public LocalizedMessage(string key, params object?[] arguments) { Key = key; Arguments = arguments.ToArray(); }
    public override string ToString() => LocalizationService.Text(Key, Arguments);
}

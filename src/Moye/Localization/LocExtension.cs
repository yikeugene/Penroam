using System.Windows.Data;
using System.Windows.Markup;

namespace Moye.Localization;

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationService.Binding(Key).ProvideValue(serviceProvider);
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class FormatExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public BindingBase Value { get; set; } = new Binding();
    public string? SingularKey { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        (SingularKey is null ? LocalizationService.FormatBinding(Key, Value)
            : LocalizationService.CountBinding(Key, SingularKey, Value)).ProvideValue(serviceProvider);
}

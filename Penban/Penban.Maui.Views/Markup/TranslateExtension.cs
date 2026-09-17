using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Penban.Maui.Views.Markup;

/// <summary>
/// XAML markup extension for localized strings: <c>{markup:Translate StringKey}</c>. Reads
/// values from <see cref="Penban.Util.Strings"/> via its <see cref="ResourceManager"/> (so this
/// project doesn't need a hard reference per key) and refreshes bound labels whenever
/// <see cref="LocalizationBroadcaster.Changed"/> fires.
/// </summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    private static readonly ResourceManager ResourceManager =
        new("Penban.Util.Strings", typeof(Penban.Util.Strings).Assembly);

    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var source = new TranslationSource(Key);
        return new Binding
        {
            Path = nameof(TranslationSource.Value),
            Source = source,
            Mode = BindingMode.OneWay,
        };
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);

    /// <summary>Small helper object that re-raises PropertyChanged whenever the UI culture changes.</summary>
    private sealed class TranslationSource : INotifyPropertyChanged
    {
        private readonly string key;

        public TranslationSource(string key)
        {
            this.key = key;
            LocalizationBroadcaster.Changed += OnLanguageChanged;
        }

        public string Value => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnLanguageChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}

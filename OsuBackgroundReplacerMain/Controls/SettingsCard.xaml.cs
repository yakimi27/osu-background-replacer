using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace OsuBackgroundReplacerMain.Controls
{
    [ContentProperty(Name = nameof(ActionContent))]
    public sealed partial class SettingsCard : UserControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard),
                new PropertyMetadata(string.Empty, (d, e) => ((SettingsCard)d).HeaderTextBlock.Text = (string)e.NewValue));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard),
                new PropertyMetadata(string.Empty, (d, e) =>
                {
                    var card = (SettingsCard)d;
                    var text = (string)e.NewValue;
                    card.DescriptionTextBlock.Text = text;
                    card.DescriptionTextBlock.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
                }));

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsCard),
                new PropertyMetadata(string.Empty, (d, e) =>
                {
                    var card = (SettingsCard)d;
                    var glyph = (string)e.NewValue;
                    card.IconElement.Glyph = glyph;
                    card.IconContainer.Visibility = string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
                }));

        public static readonly DependencyProperty ActionContentProperty =
            DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(SettingsCard),
                new PropertyMetadata(null, (d, e) => ((SettingsCard)d).ActionPresenter.Content = e.NewValue));

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public object ActionContent
        {
            get => GetValue(ActionContentProperty);
            set => SetValue(ActionContentProperty, value);
        }

        public SettingsCard()
        {
            this.InitializeComponent();
        }
    }
}

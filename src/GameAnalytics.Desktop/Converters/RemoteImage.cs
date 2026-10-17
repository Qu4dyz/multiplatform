using Avalonia;
using Avalonia.Controls;

namespace GameAnalytics.Desktop.Converters;

/// <summary>
/// <c>RemoteImage.Url</c> sets <see cref="Image.Source"/> from the icon cache and fills it in
/// once a missing Data Dragon icon finishes downloading. A plain converter binding never
/// re-evaluates, so a cold cache left those images blank.
/// </summary>
public static class RemoteImage
{
    public static readonly AttachedProperty<string?> UrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Url", typeof(RemoteImage));

    static RemoteImage()
    {
        UrlProperty.Changed.AddClassHandler<Image>((image, e) => OnUrlChanged(image, e.NewValue as string));
    }

    public static string? GetUrl(Image image) => image.GetValue(UrlProperty);

    public static void SetUrl(Image image, string? value) => image.SetValue(UrlProperty, value);

    private static void OnUrlChanged(Image image, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            image.Source = null;
            return;
        }

        image.Source = BitmapAssetValueConverter.GetOrLoadBitmap(url, bmp =>
        {
            // The image may have been recycled for another row while the download ran.
            if (string.Equals(GetUrl(image), url, StringComparison.Ordinal))
                image.Source = bmp;
        });
    }
}

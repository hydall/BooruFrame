using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BooruFrame;

/// <summary>
/// The two-layer crossfade shared by the main window and the wallpaper surface: the new
/// picture is placed on the lower image and the upper one fades away to reveal it.
/// </summary>
public static class ImageCrossfade
{
    public static void Show(Image high, Image low, BitmapSource bmp, bool animate)
    {
        if (!animate || high.Source is null)
        {
            high.BeginAnimation(UIElement.OpacityProperty, null);
            high.Opacity = 1;
            high.Source = bmp;
            low.Source = bmp;
            return;
        }

        low.Source = bmp;
        low.Opacity = 1;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        fade.Completed += (_, _) =>
        {
            high.BeginAnimation(UIElement.OpacityProperty, null);
            high.Source = bmp;
            high.Opacity = 1;
        };
        high.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

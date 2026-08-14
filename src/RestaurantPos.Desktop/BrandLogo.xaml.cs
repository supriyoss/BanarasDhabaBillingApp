using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace RestaurantPos.Desktop;

public partial class BrandLogo : UserControl
{
    public BrandLogo()
    {
        InitializeComponent();
        Loaded += AnimateLogoOnce;
    }

    private void AnimateLogoOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= AnimateLogoOnce;

        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            LogoScale.ScaleX = 1;
            LogoScale.ScaleY = 1;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)));
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = easing });
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = easing });
        GlowEffect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.18, 0.52, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                BeginTime = TimeSpan.FromMilliseconds(120),
                EasingFunction = easing
            });
    }
}

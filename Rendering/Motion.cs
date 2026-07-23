using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DeskBoard.Rendering;

/// <summary>
/// Code-side motion tokens (house values from the design system) and a spline-easing
/// animation helper. Storyboard-in-XAML covers styles; everything code-driven routes
/// through here so no default-eased animation slips in.
/// </summary>
internal static class Motion
{
    public static readonly KeySpline EaseSmooth = Frozen(new KeySpline(0.22, 1, 0.36, 1));
    public static readonly KeySpline EaseOut    = Frozen(new KeySpline(0.17, 1, 0.32, 1));
    public static readonly KeySpline EaseInOut  = Frozen(new KeySpline(0.66, 0, 0.34, 1));

    public const int Fast = 150;
    public const int Normal = 200;
    public const int Slow = 280;

    /// <summary>Honors the system animation preference: when off, snap to end state.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    private static KeySpline Frozen(KeySpline s) { s.Freeze(); return s; }

    /// <summary>
    /// Animate a double DP with a single spline keyframe. When system animations are
    /// off (or ms is 0) the value is set directly.
    /// </summary>
    public static void Animate(UIElement target, DependencyProperty property, double to,
        int ms, KeySpline? spline = null, Action? completed = null)
    {
        if (!Enabled || ms <= 0)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            completed?.Invoke();
            return;
        }

        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(
            to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), spline ?? EaseSmooth));
        if (completed is not null)
            anim.Completed += (_, _) => completed();
        target.BeginAnimation(property, anim);
    }

    /// <summary>Same as <see cref="Animate"/> but for any Animatable (e.g. transforms).</summary>
    public static void Animate(System.Windows.Media.Animation.Animatable target,
        DependencyProperty property, double to, int ms, KeySpline? spline = null)
    {
        if (!Enabled || ms <= 0)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }

        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(
            to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), spline ?? EaseSmooth));
        target.BeginAnimation(property, anim);
    }
}

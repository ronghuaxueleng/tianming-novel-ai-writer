using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    public class ThemeTransitionService
    {
        public ThemeTransitionService() { }

        public void PlayTransition(FrameworkElement container, ThemeTransitionSettings settings, Action? onComplete = null)
        {
            try
            {
                bool useCombinedEffects = settings.CombinedEffects != null && settings.CombinedEffects.Count > 0;

                if (!useCombinedEffects && (settings.Effect == TransitionEffect.None || settings.Duration <= 0))
                {
                    TM.App.Log("[ThemeTransition] 无动画或时长为0，直接执行回调");
                    onComplete?.Invoke();
                    return;
                }

                if (useCombinedEffects)
                {
                    TM.App.Log($"[ThemeTransition] 开始播放组合过渡动画: {string.Join("+", settings.CombinedEffects!)}, 时长: {settings.Duration}ms, 缓动: {settings.EasingType}, 强度: {settings.IntensityMultiplier}");
                }
                else
                {
                    TM.App.Log($"[ThemeTransition] 开始播放过渡动画: {settings.Effect}, 时长: {settings.Duration}ms, 缓动: {settings.EasingType}, 强度: {settings.IntensityMultiplier}");
                }

                var storyboard = useCombinedEffects 
                    ? CreateCombinedTransitionStoryboard(settings, container) 
                    : CreateTransitionStoryboard(settings, container);

                ApplyEasingAndIntensity(storyboard, settings);

                storyboard.Completed += (s, e) =>
                {
                    TM.App.Log($"[ThemeTransition] 动画完成");
                    ResetElementState(container);
                    onComplete?.Invoke();
                };

                container.BeginStoryboard(storyboard);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 播放动画失败: {ex.Message}");
                onComplete?.Invoke();
            }
        }

        private Storyboard CreateTransitionStoryboard(ThemeTransitionSettings settings, FrameworkElement container)
        {
            var duration = TimeSpan.FromMilliseconds(settings.Duration);
            double containerWidth = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
            double containerHeight = container.ActualHeight > 0 ? container.ActualHeight : container.Height;

            return settings.Effect switch
            {
                TransitionEffect.Rotate => CreateRotateAnimation(duration),
                TransitionEffect.Blur => CreateBlurAnimation(duration),
                TransitionEffect.SlideLeft => CreateSlideAnimation(duration, -1, 0, containerWidth, containerHeight),
                TransitionEffect.SlideRight => CreateSlideAnimation(duration, 1, 0, containerWidth, containerHeight),
                TransitionEffect.SlideUp => CreateSlideAnimation(duration, 0, -1, containerWidth, containerHeight),
                TransitionEffect.SlideDown => CreateSlideAnimation(duration, 0, 1, containerWidth, containerHeight),
                TransitionEffect.FlipHorizontal => CreateFlipAnimation(duration, true),
                TransitionEffect.FlipVertical => CreateFlipAnimation(duration, false),
                _ => new Storyboard()
            };
        }

        private Storyboard CreateSlideAnimation(TimeSpan duration, double xDirection, double yDirection, double containerWidth, double containerHeight)
        {
            var storyboard = new Storyboard();
            var halfDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2);

            var width = containerWidth > 0 ? containerWidth : 1500;
            var height = containerHeight > 0 ? containerHeight : 1000;

            var slideOut = new ThicknessAnimation
            {
                From = new Thickness(0),
                To = new Thickness(-xDirection * width * 1.2, -yDirection * height * 1.2, xDirection * width * 1.2, yDirection * height * 1.2),
                Duration = new Duration(halfDuration),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.4 }
            };
            Storyboard.SetTargetProperty(slideOut, new PropertyPath("Margin"));
            storyboard.Children.Add(slideOut);

            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 2 }
            };
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeOut);

            var scaleOut = new DoubleAnimation(1.0, 0.85, new Duration(halfDuration))
            {
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 }
            };
            Storyboard.SetTargetProperty(scaleOut, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleOut);

            var scaleOutY = new DoubleAnimation(1.0, 0.85, new Duration(halfDuration))
            {
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 }
            };
            Storyboard.SetTargetProperty(scaleOutY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleOutY);

            var slideIn = new ThicknessAnimation
            {
                From = new Thickness(xDirection * width * 1.2, yDirection * height * 1.2, -xDirection * width * 1.2, -yDirection * height * 1.2),
                To = new Thickness(0),
                Duration = new Duration(halfDuration),
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };
            Storyboard.SetTargetProperty(slideIn, new PropertyPath("Margin"));
            storyboard.Children.Add(slideIn);

            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 2 }
            };
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeIn);

            var scaleIn = new DoubleAnimation(0.85, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            Storyboard.SetTargetProperty(scaleIn, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleIn);

            var scaleInY = new DoubleAnimation(0.85, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            Storyboard.SetTargetProperty(scaleInY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleInY);

            return storyboard;
        }

        private Storyboard CreateFlipAnimation(TimeSpan duration, bool horizontal)
        {
            var storyboard = new Storyboard();
            var halfDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2);

            var propertyPath = horizontal 
                ? "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"
                : "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)";

            var crossScale = horizontal
                ? "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"
                : "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)";

            var flipOut = new DoubleAnimation(1.0, -0.2, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 3 }
            };
            Storyboard.SetTargetProperty(flipOut, new PropertyPath(propertyPath));
            storyboard.Children.Add(flipOut);

            var crossScaleOut = new DoubleAnimation(1.0, 0.8, new Duration(halfDuration))
            {
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 }
            };
            Storyboard.SetTargetProperty(crossScaleOut, new PropertyPath(crossScale));
            storyboard.Children.Add(crossScaleOut);

            var fadeOut = new DoubleAnimation(1.0, 0.1, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 2 }
            };
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeOut);

            var flipIn = new DoubleAnimation(-0.2, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };
            Storyboard.SetTargetProperty(flipIn, new PropertyPath(propertyPath));
            storyboard.Children.Add(flipIn);

            var crossScaleIn = new DoubleAnimation(0.8, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
            };
            Storyboard.SetTargetProperty(crossScaleIn, new PropertyPath(crossScale));
            storyboard.Children.Add(crossScaleIn);

            var fadeIn = new DoubleAnimation(0.1, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 2 }
            };
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeIn);

            return storyboard;
        }

        private Storyboard CreateRotateAnimation(TimeSpan duration)
        {
            var storyboard = new Storyboard();
            var halfDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2);

            var rotate = new DoubleAnimation(0, 720, new Duration(duration))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseInOut, Amplitude = 0.5 }
            };
            Storyboard.SetTargetProperty(rotate, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(RotateTransform.Angle)"));
            storyboard.Children.Add(rotate);

            var scaleOut = new DoubleAnimation(1.0, 0.3, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 3 }
            };
            Storyboard.SetTargetProperty(scaleOut, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleOut);

            var scaleOutY = new DoubleAnimation(1.0, 0.3, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 3 }
            };
            Storyboard.SetTargetProperty(scaleOutY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleOutY);

            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 2 }
            };
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeOut);

            var scaleIn = new DoubleAnimation(0.3, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };
            Storyboard.SetTargetProperty(scaleIn, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleIn);

            var scaleInY = new DoubleAnimation(0.3, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };
            Storyboard.SetTargetProperty(scaleInY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleInY);

            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 2 }
            };
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeIn);

            return storyboard;
        }

        private Storyboard CreateBlurAnimation(TimeSpan duration)
        {
            var storyboard = new Storyboard();
            var halfDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2);

            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(halfDuration))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 3 }
            };
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeOut);

            var scaleOut = new DoubleAnimation(1.0, 1.15, new Duration(halfDuration))
            {
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 }
            };
            Storyboard.SetTargetProperty(scaleOut, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleOut);

            var scaleOutY = new DoubleAnimation(1.0, 1.15, new Duration(halfDuration))
            {
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 }
            };
            Storyboard.SetTargetProperty(scaleOutY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleOutY);

            var rotateOut = new DoubleAnimation(0, 5, new Duration(halfDuration))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTargetProperty(rotateOut, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(RotateTransform.Angle)"));
            storyboard.Children.Add(rotateOut);

            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 }
            };
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeIn);

            var scaleIn = new DoubleAnimation(0.85, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            Storyboard.SetTargetProperty(scaleIn, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleIn);

            var scaleInY = new DoubleAnimation(0.85, 1.0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            Storyboard.SetTargetProperty(scaleInY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleInY);

            var rotateIn = new DoubleAnimation(5, 0, new Duration(halfDuration))
            {
                BeginTime = halfDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
            };
            Storyboard.SetTargetProperty(rotateIn, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(RotateTransform.Angle)"));
            storyboard.Children.Add(rotateIn);

            return storyboard;
        }

        private void ResetElementState(FrameworkElement element)
        {
            try
            {
                element.Opacity = 1.0;
                element.Margin = new Thickness(0);

                if (element.RenderTransform is TransformGroup transformGroup)
                {
                    foreach (var transform in transformGroup.Children)
                    {
                        if (transform is ScaleTransform scale)
                        {
                            scale.ScaleX = 1.0;
                            scale.ScaleY = 1.0;
                        }
                        else if (transform is TranslateTransform translate)
                        {
                            translate.X = 0;
                            translate.Y = 0;
                        }
                        else if (transform is RotateTransform rotate)
                        {
                            rotate.Angle = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 重置元素状态失败: {ex.Message}");
            }
        }

        public void PrepareElement(FrameworkElement element)
        {
            try
            {
                if (element.RenderTransform == null || element.RenderTransform is MatrixTransform)
                {
                    var transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(1, 1));
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                    transformGroup.Children.Add(new RotateTransform(0));
                    element.RenderTransform = transformGroup;
                    element.RenderTransformOrigin = new Point(0.5, 0.5);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 准备元素失败: {ex.Message}");
            }
        }

        private Storyboard CreateCombinedTransitionStoryboard(ThemeTransitionSettings settings, FrameworkElement container)
        {
            var masterStoryboard = new Storyboard();
            var duration = TimeSpan.FromMilliseconds(settings.Duration);
            double containerWidth = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
            double containerHeight = container.ActualHeight > 0 ? container.ActualHeight : container.Height;

            foreach (var effect in settings.CombinedEffects)
            {
                if (effect == TransitionEffect.None) continue;

                var effectStoryboard = effect switch
                {
                    TransitionEffect.Rotate => CreateRotateAnimation(duration),
                    TransitionEffect.Blur => CreateBlurAnimation(duration),
                    TransitionEffect.SlideLeft => CreateSlideAnimation(duration, -1, 0, containerWidth, containerHeight),
                    TransitionEffect.SlideRight => CreateSlideAnimation(duration, 1, 0, containerWidth, containerHeight),
                    TransitionEffect.SlideUp => CreateSlideAnimation(duration, 0, -1, containerWidth, containerHeight),
                    TransitionEffect.SlideDown => CreateSlideAnimation(duration, 0, 1, containerWidth, containerHeight),
                    TransitionEffect.FlipHorizontal => CreateFlipAnimation(duration, true),
                    TransitionEffect.FlipVertical => CreateFlipAnimation(duration, false),
                    _ => new Storyboard()
                };

                if (effectStoryboard != null)
                {
                    foreach (Timeline timeline in effectStoryboard.Children)
                    {
                        masterStoryboard.Children.Add(timeline);
                    }
                }
            }

            return masterStoryboard;
        }

        private void ApplyEasingAndIntensity(Storyboard storyboard, ThemeTransitionSettings settings)
        {
            try
            {
                var customEasing = ConvertToWpfEasing(settings.EasingType);

                foreach (Timeline timeline in storyboard.Children)
                {
                    if (settings.EasingType != EasingFunctionType.Linear && timeline is AnimationTimeline animTimeline)
                    {
                        var easingProperty = animTimeline.GetType().GetProperty("EasingFunction");
                        if (easingProperty != null && easingProperty.CanWrite)
                        {
                            easingProperty.SetValue(animTimeline, customEasing);
                        }
                    }

                    if (Math.Abs(settings.IntensityMultiplier - 1.0) > 0.01)
                    {
                        ApplyIntensityToAnimation(timeline, settings.IntensityMultiplier);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 应用缓动和强度失败: {ex.Message}");
            }
        }

        private IEasingFunction? ConvertToWpfEasing(EasingFunctionType type)
        {
            return type switch
            {
                EasingFunctionType.Linear => null,
                EasingFunctionType.EaseInQuad => new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 },
                EasingFunctionType.EaseOutQuad => new PowerEase { EasingMode = EasingMode.EaseOut, Power = 2 },
                EasingFunctionType.EaseInOutQuad => new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 2 },
                EasingFunctionType.EaseInCubic => new PowerEase { EasingMode = EasingMode.EaseIn, Power = 3 },
                EasingFunctionType.EaseOutCubic => new PowerEase { EasingMode = EasingMode.EaseOut, Power = 3 },
                EasingFunctionType.EaseInOutCubic => new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 3 },
                EasingFunctionType.EaseInElastic => new ElasticEase { EasingMode = EasingMode.EaseIn, Oscillations = 3, Springiness = 3 },
                EasingFunctionType.EaseOutElastic => new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 3, Springiness = 3 },
                EasingFunctionType.EaseInBounce => new BounceEase { EasingMode = EasingMode.EaseIn, Bounces = 3, Bounciness = 2 },
                EasingFunctionType.EaseOutBounce => new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 3, Bounciness = 2 },
                EasingFunctionType.EaseInOutBounce => new BounceEase { EasingMode = EasingMode.EaseInOut, Bounces = 3, Bounciness = 2 },
                _ => null
            };
        }

        private void ApplyIntensityToAnimation(Timeline timeline, double intensity)
        {
            try
            {
                if (timeline is DoubleAnimation doubleAnim && doubleAnim.To.HasValue)
                {
                    var targetPath = Storyboard.GetTargetProperty(timeline).Path;
                    if (targetPath.Contains("Angle") || targetPath.Contains("Scale"))
                    {
                        doubleAnim.To = doubleAnim.To.Value * intensity;
                    }
                }
                else if (timeline is ThicknessAnimation thicknessAnim && thicknessAnim.To.HasValue)
                {
                    var to = thicknessAnim.To.Value;
                    thicknessAnim.To = new Thickness(
                        to.Left * intensity,
                        to.Top * intensity,
                        to.Right * intensity,
                        to.Bottom * intensity
                    );
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 应用强度到动画失败: {ex.Message}");
            }
        }
    }
}


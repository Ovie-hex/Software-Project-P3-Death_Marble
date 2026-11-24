using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Software_Project_P3_Death_Marble.Views
{
    public class PixelCharacter : Canvas
    {
        private const int PixelSize = 3;
        private const double BaseScale = 0.72;

        private readonly DispatcherTimer walkTimer;

        private readonly ScaleTransform scaleTransform;
        private readonly TranslateTransform effectTransform;
        private readonly DropShadowEffect effectGlow;

        private bool isWalking;
        private bool walkFrame;

        public int PlayerNumber { get; }

        private readonly string[] idleSprite =
        {
            "0001000000001000",
            "0001100000011000",
            "0001110000111000",
            "0000111111110000",
            "0001111111111000",
            "0011111111111100",
            "0011121111211100",
            "0011111111111100",
            "0001111331111000",
            "0000111111110000",
            "0401111111111040",
            "0441111111111440",
            "0041111111114000",
            "0001111111111000",
            "0000111111110000",
            "0000110011000000",
            "0001110011100000",
            "0000000000000000"
        };

        private readonly string[] walkSpriteA =
        {
            "0001000000001000",
            "0001100000011000",
            "0001110000111000",
            "0000111111110000",
            "0001111111111000",
            "0011111111111100",
            "0011121111211100",
            "0011111111111100",
            "0001111331111000",
            "0000111111110000",
            "0401111111111040",
            "0441111111111440",
            "0041111111114000",
            "0001111111111000",
            "0000111111110000",
            "0000110001100000",
            "0001110000110000",
            "0011000000011000"
        };

        private readonly string[] walkSpriteB =
        {
            "0001000000001000",
            "0001100000011000",
            "0001110000111000",
            "0000111111110000",
            "0001111111111100",
            "0011111111111100",
            "0011121111211100",
            "0011111111111100",
            "0001111331111000",
            "0000111111110000",
            "0401111111111040",
            "0441111111111440",
            "0041111111114000",
            "0001111111111000",
            "0000111111110000",
            "0001100011000000",
            "0011000011100000",
            "0110000000110000"
        };

        public PixelCharacter(int playerNumber)
        {
            PlayerNumber = playerNumber;

            Width = 16 * PixelSize;
            Height = 18 * PixelSize;

            IsHitTestVisible = false;
            SnapsToDevicePixels = true;

            scaleTransform = new ScaleTransform(BaseScale, BaseScale);
            effectTransform = new TranslateTransform();

            TransformGroup group = new TransformGroup();
            group.Children.Add(scaleTransform);
            group.Children.Add(effectTransform);

            RenderTransform = group;

            effectGlow = new DropShadowEffect
            {
                Color = Colors.LimeGreen,
                BlurRadius = 0,
                ShadowDepth = 0,
                Opacity = 0
            };

            Effect = effectGlow;

            walkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };

            walkTimer.Tick += WalkTimer_Tick;

            DrawSprite(idleSprite);
        }

        private Brush GetMainColor()
        {
            return PlayerNumber switch
            {
                1 => Brushes.IndianRed,
                2 => Brushes.DodgerBlue,
                3 => Brushes.LimeGreen,
                4 => Brushes.Gold,
                _ => Brushes.White
            };
        }

        private Brush GetPixelColor(char pixel)
        {
            return pixel switch
            {
                '1' => GetMainColor(),
                '2' => Brushes.White,
                '3' => Brushes.DarkRed,
                '4' => Brushes.DarkSlateGray,
                _ => Brushes.Transparent
            };
        }

        private void DrawSprite(string[] sprite)
        {
            Children.Clear();

            for (int row = 0; row < sprite.Length; row++)
            {
                for (int col = 0; col < sprite[row].Length; col++)
                {
                    char pixel = sprite[row][col];

                    if (pixel == '0')
                        continue;

                    Rectangle rectangle = new Rectangle
                    {
                        Width = PixelSize,
                        Height = PixelSize,
                        Fill = GetPixelColor(pixel),
                        StrokeThickness = 0
                    };

                    Canvas.SetLeft(rectangle, col * PixelSize);
                    Canvas.SetTop(rectangle, row * PixelSize);

                    Children.Add(rectangle);
                }
            }
        }

        // =====================================
        // 걷기 애니메이션
        // =====================================

        private void WalkTimer_Tick(object? sender, EventArgs e)
        {
            if (!isWalking)
                return;

            walkFrame = !walkFrame;

            DrawSprite(walkFrame ? walkSpriteA : walkSpriteB);
        }

        public void StartWalking()
        {
            if (isWalking)
                return;

            isWalking = true;
            walkFrame = false;

            walkTimer.Start();
        }

        public void StopWalking()
        {
            isWalking = false;
            walkTimer.Stop();

            DrawSprite(idleSprite);
        }

        // =====================================
        // 피해 이펙트
        // =====================================

        public void PlayDamageEffect()
        {
            ResetEffects();

            effectGlow.Color = Colors.Red;
            effectGlow.BlurRadius = 18;
            effectGlow.Opacity = 0.8;

            DoubleAnimation flash = new DoubleAnimation
            {
                From = 1,
                To = 0.2,
                Duration = TimeSpan.FromMilliseconds(90),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            BeginAnimation(OpacityProperty, flash);

            DoubleAnimationUsingKeyFrames shake =
                new DoubleAnimationUsingKeyFrames();

            shake.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    -6,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromMilliseconds(60))));

            shake.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    6,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromMilliseconds(120))));

            shake.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    -5,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromMilliseconds(180))));

            shake.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    5,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromMilliseconds(240))));

            shake.KeyFrames.Add(
                new LinearDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(
                        TimeSpan.FromMilliseconds(350))));

            effectTransform.BeginAnimation(
                TranslateTransform.XProperty, shake);

            DoubleAnimation glowFade = new DoubleAnimation
            {
                From = 0.8,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(600)
            };

            effectGlow.BeginAnimation(
                DropShadowEffect.OpacityProperty, glowFade);
        }

        // =====================================
        // 회복 이펙트
        // =====================================

        public void PlayHealEffect()
        {
            ResetEffects();

            effectGlow.Color = Colors.LimeGreen;
            effectGlow.BlurRadius = 25;
            effectGlow.Opacity = 1;

            DoubleAnimation floatAnimation = new DoubleAnimation
            {
                From = 0,
                To = -12,
                Duration = TimeSpan.FromMilliseconds(250),
                AutoReverse = true
            };

            effectTransform.BeginAnimation(
                TranslateTransform.YProperty, floatAnimation);

            DoubleAnimation scaleAnimation = new DoubleAnimation
            {
                From = BaseScale,
                To = 0.9,
                Duration = TimeSpan.FromMilliseconds(250),
                AutoReverse = true
            };

            scaleTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty, scaleAnimation);

            scaleTransform.BeginAnimation(
                ScaleTransform.ScaleYProperty, scaleAnimation);

            DoubleAnimation glowFade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(800)
            };

            effectGlow.BeginAnimation(
                DropShadowEffect.OpacityProperty, glowFade);
        }

        // =====================================
        // 애니메이션 초기화
        // =====================================

        private void ResetEffects()
        {
            BeginAnimation(OpacityProperty, null);

            effectTransform.BeginAnimation(
                TranslateTransform.XProperty, null);

            effectTransform.BeginAnimation(
                TranslateTransform.YProperty, null);

            scaleTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty, null);

            scaleTransform.BeginAnimation(
                ScaleTransform.ScaleYProperty, null);

            effectGlow.BeginAnimation(
                DropShadowEffect.OpacityProperty, null);

            Opacity = 1;

            effectTransform.X = 0;
            effectTransform.Y = 0;

            scaleTransform.ScaleX = BaseScale;
            scaleTransform.ScaleY = BaseScale;

            effectGlow.Opacity = 0;
        }

        public void StopAllAnimations()
        {
            StopWalking();
            ResetEffects();
        }
    }
}
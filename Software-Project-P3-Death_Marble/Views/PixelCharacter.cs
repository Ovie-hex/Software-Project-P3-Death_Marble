using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Software_Project_P3_Death_Marble.Views
{
    public class PixelCharacter : Canvas
    {
        private const int PixelSize = 3;

        private readonly DispatcherTimer walkTimer;

        private bool isWalking = false;
        private bool walkFrame = false;

        public int PlayerNumber { get; }

        // 기본 대기 자세
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
            "0001111221111000",
            "0000111111110000",
            "0011111111111100",
            "0111111111111110",
            "1111111111111111",
            "0011111111111100",
            "0001111111111000",
            "0000110011000000",
            "0001110011100000",
            "0000000000000000"
        };

        // 걷기 프레임 A
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
            "0001111221111000",
            "0000111111110000",
            "0011111111111100",
            "0111111111111110",
            "1111111111111111",
            "0011111111111100",
            "0001111111111000",
            "0000110001100000",
            "0001110000110000",
            "0011000000011000"
        };

        // 걷기 프레임 B
        private readonly string[] walkSpriteB =
        {
            "0001000000001000",
            "0001100000011000",
            "0001110000111000",
            "0000111111110000",
            "0001111111111000",
            "0011111111111100",
            "0011121111211100",
            "0011111111111100",
            "0001111221111000",
            "0000111111110000",
            "0011111111111100",
            "0111111111111110",
            "1111111111111111",
            "0011111111111100",
            "0001111111111000",
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

        private void DrawSprite(string[] sprite)
        {
            Children.Clear();

            Brush mainColor = GetMainColor();

            for (int row = 0; row < sprite.Length; row++)
            {
                for (int col = 0; col < sprite[row].Length; col++)
                {
                    char pixel = sprite[row][col];

                    if (pixel == '0')
                        continue;

                    Brush color = pixel switch
                    {
                        '1' => mainColor,
                        '2' => Brushes.White,
                        _ => Brushes.Black
                    };

                    Rectangle rectangle = new Rectangle
                    {
                        Width = PixelSize,
                        Height = PixelSize,
                        Fill = color,
                        StrokeThickness = 0
                    };

                    Canvas.SetLeft(rectangle, col * PixelSize);
                    Canvas.SetTop(rectangle, row * PixelSize);

                    Children.Add(rectangle);
                }
            }
        }

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
    }
}
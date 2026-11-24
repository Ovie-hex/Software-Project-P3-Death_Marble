using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Software_Project_P3_Death_Marble.Views
{
    public class Dice3D : Viewport3D
    {
        private readonly AxisAngleRotation3D rotationX;
        private readonly AxisAngleRotation3D rotationY;

        private readonly Random random = new Random();

        private bool isRolling = false;
        private int currentResult = 1;

        public event EventHandler<int>? RollCompleted;

        public Dice3D()
        {
            Width = 160;
            Height = 160;
            ClipToBounds = true;

            // 카메라는 주사위 정면을 바라봅니다.
            Camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0, 5.5),
                LookDirection = new Vector3D(0, 0, -1),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 45
            };

            rotationX = new AxisAngleRotation3D(
                new Vector3D(1, 0, 0), 0);

            rotationY = new AxisAngleRotation3D(
                new Vector3D(0, 1, 0), 0);

            Transform3DGroup transforms = new Transform3DGroup();

            transforms.Children.Add(
                new RotateTransform3D(rotationX));

            transforms.Children.Add(
                new RotateTransform3D(rotationY));

            Model3DGroup diceModel = CreateDice();
            diceModel.Transform = transforms;

            Model3DGroup scene = new Model3DGroup();

            // 전체 조명
            scene.Children.Add(
                new AmbientLight(
                    Color.FromRgb(190, 190, 190)));

            // 정면 조명
            scene.Children.Add(
                new DirectionalLight(
                    Colors.White,
                    new Vector3D(-1, -1, -2)));

            scene.Children.Add(diceModel);

            Children.Add(new ModelVisual3D
            {
                Content = scene
            });

            ShowFace(1);
        }

        // =====================================
        // 주사위 6개 면 생성
        // =====================================

        private Model3DGroup CreateDice()
        {
            Model3DGroup dice = new Model3DGroup();

            /*
             * 좌표계
             *
             * X : 오른쪽 +
             * Y : 위쪽 +
             * Z : 카메라 방향 +
             *
             * 각 면은 바깥쪽을 향하도록
             * 꼭짓점 순서를 통일합니다.
             */

            // 앞면 : 1 (+Z)
            AddFace(
                dice, 1,
                new Point3D(-1, -1, 1),
                new Point3D(1, -1, 1),
                new Point3D(1, 1, 1),
                new Point3D(-1, 1, 1));

            // 뒷면 : 6 (-Z)
            AddFace(
                dice, 6,
                new Point3D(1, -1, -1),
                new Point3D(-1, -1, -1),
                new Point3D(-1, 1, -1),
                new Point3D(1, 1, -1));

            // 오른쪽 : 3 (+X)
            AddFace(
                dice, 3,
                new Point3D(1, -1, -1),
                new Point3D(1, 1, -1),
                new Point3D(1, 1, 1),
                new Point3D(1, -1, 1));

            // 왼쪽 : 4 (-X)
            AddFace(
                dice, 4,
                new Point3D(-1, -1, 1),
                new Point3D(-1, 1, 1),
                new Point3D(-1, 1, -1),
                new Point3D(-1, -1, -1));

            // 위쪽 : 2 (+Y)
            AddFace(
                dice, 2,
                new Point3D(-1, 1, -1),
                new Point3D(-1, 1, 1),
                new Point3D(1, 1, 1),
                new Point3D(1, 1, -1));

            // 아래쪽 : 5 (-Y)
            AddFace(
                dice, 5,
                new Point3D(-1, -1, 1),
                new Point3D(-1, -1, -1),
                new Point3D(1, -1, -1),
                new Point3D(1, -1, 1));

            return dice;
        }

        // =====================================
        // 면 생성
        // =====================================

        private void AddFace(
            Model3DGroup group,
            int number,
            Point3D p0,
            Point3D p1,
            Point3D p2,
            Point3D p3)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();

            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);

            // 삼각형 2개로 사각형 생성
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(1);
            mesh.TriangleIndices.Add(2);

            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(3);

            mesh.TextureCoordinates.Add(new Point(0, 1));
            mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));

            // 256 x 256 텍스처
            const int size = 256;

            DrawingVisual visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                // 검은색 면
                dc.DrawRectangle(
                    new SolidColorBrush(
                        Color.FromRgb(27, 22, 30)),
                    null,
                    new Rect(0, 0, size, size));

                // 금색 테두리
                dc.DrawRectangle(
                    null,
                    new Pen(
                        new SolidColorBrush(
                            Color.FromRgb(180, 125, 35)),
                        4),
                    new Rect(2, 2, 252, 252));

                // 빨간 눈금
                DrawDots(dc, number);
            }

            RenderTargetBitmap bitmap =
                new RenderTargetBitmap(
                    size,
                    size,
                    96,
                    96,
                    PixelFormats.Pbgra32);

            bitmap.Render(visual);
            bitmap.Freeze();

            ImageBrush brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.Fill
            };

            DiffuseMaterial material =
                new DiffuseMaterial(brush);

            // 면의 앞쪽만 렌더링
            GeometryModel3D face = new GeometryModel3D
            {
                Geometry = mesh,
                Material = material
            };

            group.Children.Add(face);
        }

        // =====================================
        // 주사위 눈금
        // =====================================

        private void DrawDots(DrawingContext dc, int number)
        {
            Brush dotBrush = Brushes.OrangeRed;

            Pen dotPen = new Pen(
                Brushes.DarkRed, 2);

            const double left = 65;
            const double center = 128;
            const double right = 191;

            const double top = 65;
            const double middle = 128;
            const double bottom = 191;

            void Dot(double x, double y)
            {
                dc.DrawEllipse(
                    dotBrush,
                    dotPen,
                    new Point(x, y),
                    19,
                    19);
            }

            switch (number)
            {
                case 1:
                    Dot(center, middle);
                    break;

                case 2:
                    Dot(left, top);
                    Dot(right, bottom);
                    break;

                case 3:
                    Dot(left, top);
                    Dot(center, middle);
                    Dot(right, bottom);
                    break;

                case 4:
                    Dot(left, top);
                    Dot(right, top);
                    Dot(left, bottom);
                    Dot(right, bottom);
                    break;

                case 5:
                    Dot(left, top);
                    Dot(right, top);
                    Dot(center, middle);
                    Dot(left, bottom);
                    Dot(right, bottom);
                    break;

                case 6:
                    Dot(left, top);
                    Dot(left, middle);
                    Dot(left, bottom);

                    Dot(right, top);
                    Dot(right, middle);
                    Dot(right, bottom);
                    break;
            }
        }

        // =====================================
        // 숫자별 최종 회전 각도
        // =====================================

        private (double x, double y) GetFaceAngles(int number)
        {
            return number switch
            {
                1 => (0, 0),

                // 위쪽 면을 정면으로
                2 => (90, 0),

                // 오른쪽 면을 정면으로
                3 => (0, -90),

                // 왼쪽 면을 정면으로
                4 => (0, 90),

                // 아래쪽 면을 정면으로
                5 => (-90, 0),

                // 뒷면을 정면으로
                6 => (0, 180),

                _ => (0, 0)
            };
        }

        // =====================================
        // 특정 숫자 표시
        // =====================================

        private void ShowFace(int number)
        {
            (double x, double y) =
                GetFaceAngles(number);

            rotationX.Angle = x;
            rotationY.Angle = y;

            currentResult = number;
        }

        // =====================================
        // 주사위 굴리기
        // =====================================

        public void Roll()
        {
            Roll(random.Next(1, 7));
        }

        // 실제 Game_Manager 연동용
        public void Roll(int result)
        {
            if (isRolling)
                return;

            if (result < 1 || result > 6)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(result),
                    "주사위 결과는 1~6이어야 합니다.");
            }

            isRolling = true;

            (double finalX, double finalY) =
                GetFaceAngles(result);

            // 기존 회전 제거
            rotationX.BeginAnimation(
                AxisAngleRotation3D.AngleProperty,
                null);

            rotationY.BeginAnimation(
                AxisAngleRotation3D.AngleProperty,
                null);

            rotationX.Angle = 0;
            rotationY.Angle = 0;

            // X축 회전
            DoubleAnimation spinX = new DoubleAnimation
            {
                From = 0,
                To = 720 + finalX,
                Duration = TimeSpan.FromSeconds(1.5),
                DecelerationRatio = 0.65,
                FillBehavior = FillBehavior.Stop
            };

            // Y축 회전
            DoubleAnimation spinY = new DoubleAnimation
            {
                From = 0,
                To = 720 + finalY,
                Duration = TimeSpan.FromSeconds(1.5),
                DecelerationRatio = 0.65,
                FillBehavior = FillBehavior.Stop
            };

            spinX.Completed += (sender, args) =>
            {
                rotationX.BeginAnimation(
                    AxisAngleRotation3D.AngleProperty,
                    null);

                rotationY.BeginAnimation(
                    AxisAngleRotation3D.AngleProperty,
                    null);

                ShowFace(result);

                isRolling = false;

                RollCompleted?.Invoke(this, result);
            };

            rotationX.BeginAnimation(
                AxisAngleRotation3D.AngleProperty,
                spinX);

            rotationY.BeginAnimation(
                AxisAngleRotation3D.AngleProperty,
                spinY);
        }

        // =====================================
        // 회전 중지
        // =====================================

        public void StopRolling()
        {
            rotationX.BeginAnimation(
                AxisAngleRotation3D.AngleProperty,
                null);

            rotationY.BeginAnimation(
                AxisAngleRotation3D.AngleProperty,
                null);

            isRolling = false;

            ShowFace(currentResult);
        }
    }
}
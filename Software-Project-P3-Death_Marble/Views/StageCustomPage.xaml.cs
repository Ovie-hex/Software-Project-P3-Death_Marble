using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class StageCustomPage : UserControl
    {
        private const int GRID_SIZE = 50;
        private const int TILE_SIZE = 42;

        private enum EditorMode
        {
            Place,
            Connect,
            SetStart,
            Delete
        }

        private EditorMode currentMode = EditorMode.Place;

        private readonly Dictionary<(int X, int Y), EditorTile> tiles = new();

        private EditorTile? connectionStart;
        private EditorTile? startTile;

        public event EventHandler? BackRequested;

        public StageCustomPage()
        {
            InitializeComponent();
            DrawGrid();
        }

        // 편집용 타일 데이터
        private class EditorTile
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Type { get; set; }

            public List<EditorTile> Next { get; } = new();
        }

        // 격자 그리기
        private void DrawGrid()
        {
            MapCanvas.Children.Clear();

            for (int x = 0; x <= 700; x += GRID_SIZE)
            {
                Line line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = 500,
                    Stroke = new SolidColorBrush(
                        Color.FromRgb(62, 33, 35)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };

                MapCanvas.Children.Add(line);
            }

            for (int y = 0; y <= 500; y += GRID_SIZE)
            {
                Line line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = 700,
                    Y2 = y,
                    Stroke = new SolidColorBrush(
                        Color.FromRgb(62, 33, 35)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };

                MapCanvas.Children.Add(line);
            }

            DrawConnections();
            DrawTiles();
        }

        // 경로 표시
        private void DrawConnections()
        {
            foreach (EditorTile tile in tiles.Values)
            {
                foreach (EditorTile next in tile.Next)
                {
                    Line line = new Line
                    {
                        X1 = tile.X * GRID_SIZE + GRID_SIZE / 2,
                        Y1 = tile.Y * GRID_SIZE + GRID_SIZE / 2,
                        X2 = next.X * GRID_SIZE + GRID_SIZE / 2,
                        Y2 = next.Y * GRID_SIZE + GRID_SIZE / 2,
                        Stroke = Brushes.DarkOrange,
                        StrokeThickness = 6,
                        IsHitTestVisible = false
                    };

                    MapCanvas.Children.Add(line);
                }
            }
        }

        // 타일 표시
        private void DrawTiles()
        {
            foreach (EditorTile tile in tiles.Values)
            {
                Brush tileColor = tile.Type switch
                {
                    1 => Brushes.DarkRed,
                    2 => Brushes.SeaGreen,
                    3 => Brushes.MediumPurple,
                    4 => Brushes.OrangeRed,
                    _ => Brushes.DimGray
                };

                Border border = new Border
                {
                    Width = TILE_SIZE,
                    Height = TILE_SIZE,
                    Background = tileColor,
                    BorderBrush =
                        tile == startTile
                        ? Brushes.Gold
                        : Brushes.SandyBrown,
                    BorderThickness =
                        new Thickness(tile == startTile ? 4 : 2),
                    Child = new TextBlock
                    {
                        Text = tile == startTile ? "S" : "■",
                        Foreground = Brushes.White,
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    },
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(
                    border,
                    tile.X * GRID_SIZE +
                    (GRID_SIZE - TILE_SIZE) / 2);

                Canvas.SetTop(
                    border,
                    tile.Y * GRID_SIZE +
                    (GRID_SIZE - TILE_SIZE) / 2);

                MapCanvas.Children.Add(border);
            }
        }

        // 격자 클릭
        private void MapCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(MapCanvas);

            int x = (int)(point.X / GRID_SIZE);
            int y = (int)(point.Y / GRID_SIZE);

            if (x < 0 || x >= 14 ||
                y < 0 || y >= 10)
                return;

            var position = (x, y);

            tiles.TryGetValue(position, out EditorTile? tile);

            switch (currentMode)
            {
                case EditorMode.Place:

                    if (tile == null)
                    {
                        tile = new EditorTile
                        {
                            X = x,
                            Y = y,
                            Type = TileTypeCombo.SelectedIndex
                        };

                        tiles[position] = tile;

                        EditorStatusText.Text =
                            $"타일 배치: ({x}, {y})";
                    }
                    else
                    {
                        tile.Type = TileTypeCombo.SelectedIndex;

                        EditorStatusText.Text =
                            $"타일 종류 변경: ({x}, {y})";
                    }

                    break;

                case EditorMode.Connect:

                    if (tile == null)
                        return;

                    if (connectionStart == null)
                    {
                        connectionStart = tile;

                        EditorStatusText.Text =
                            "연결할 다음 타일을 선택하세요.";
                    }
                    else
                    {
                        if (connectionStart != tile &&
                            !connectionStart.Next.Contains(tile))
                        {
                            connectionStart.Next.Add(tile);

                            EditorStatusText.Text =
                                "경로가 연결되었습니다.";
                        }

                        connectionStart = null;
                    }

                    break;

                case EditorMode.SetStart:

                    if (tile != null)
                    {
                        startTile = tile;

                        EditorStatusText.Text =
                            $"시작 타일 지정: ({x}, {y})";
                    }

                    break;

                case EditorMode.Delete:

                    if (tile != null)
                    {
                        tiles.Remove(position);

                        foreach (EditorTile other in tiles.Values)
                        {
                            other.Next.Remove(tile);
                        }

                        if (startTile == tile)
                            startTile = null;

                        if (connectionStart == tile)
                            connectionStart = null;

                        EditorStatusText.Text =
                            $"타일 삭제: ({x}, {y})";
                    }

                    break;
            }

            DrawGrid();
        }

        // 타일 배치 모드
        private void PlaceTileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            currentMode = EditorMode.Place;
            connectionStart = null;

            EditorStatusText.Text =
                "타일 배치 모드: 격자를 클릭하세요.";
        }

        // 경로 연결 모드
        private void ConnectTileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            currentMode = EditorMode.Connect;
            connectionStart = null;

            EditorStatusText.Text =
                "경로 연결 모드: 시작 타일을 선택하세요.";
        }

        // 시작 타일 지정
        private void SetStartTileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            currentMode = EditorMode.SetStart;
            connectionStart = null;

            EditorStatusText.Text =
                "시작 타일로 지정할 타일을 선택하세요.";
        }

        // 타일 삭제 모드
        private void DeleteTileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            currentMode = EditorMode.Delete;
            connectionStart = null;

            EditorStatusText.Text =
                "삭제할 타일을 선택하세요.";
        }

        // 맵 초기화
        private void ClearMapButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "모든 타일을 삭제하시겠습니까?",
                "DEATH MARBLE",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            tiles.Clear();
            startTile = null;
            connectionStart = null;

            DrawGrid();

            EditorStatusText.Text =
                "맵이 초기화되었습니다.";
        }

        // 저장 버튼
        private void SaveStageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                StageNameTextBox.Text))
            {
                MessageBox.Show(
                    "스테이지 이름을 입력하세요.");
                return;
            }

            if (tiles.Count == 0)
            {
                MessageBox.Show(
                    "최소 하나 이상의 타일을 배치하세요.");
                return;
            }

            if (startTile == null)
            {
                MessageBox.Show(
                    "시작 타일을 지정하세요.");
                return;
            }

            MessageBox.Show(
                "스테이지 편집이 완료되었습니다.\n" +
                "데이터베이스 저장 기능은 다음 단계에서 연결합니다.",
                "DEATH MARBLE");
        }

        // 뒤로 가기
        private void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            BackRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
    }
}
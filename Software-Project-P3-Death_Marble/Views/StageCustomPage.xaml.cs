using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly Connector.BindableEvent customizerEvent;
        private readonly Guid? stageIdToEdit;
        private readonly Guid editorSessionId = Guid.NewGuid();
        private Guid pendingStageLoadId;
        private Guid pendingStageSaveId;
        private Guid pendingMapActionId;
        private Guid pendingCardActionId;

        private const double GRID_SIZE = 112;
        private const double TILE_SIZE = 88;
        private const int EMPTY_GRID_MARGIN = 4;
        private const double MINIMUM_VISIBLE_TILE_SIZE = 28;
        private const double MINIMUM_ZOOM = MINIMUM_VISIBLE_TILE_SIZE / TILE_SIZE;
        private const double MAXIMUM_ZOOM = 3.0;

        private readonly ScaleTransform mapScale = new(1, 1);
        private readonly TranslateTransform mapTranslation = new();
        private readonly TransformGroup mapTransform = new();

        private int gridMinX;
        private int gridMinY;
        private int gridColumns;
        private int gridRows;
        private bool hasInitialMapView;
        private bool pointerIsDown;
        private bool mapIsBeingDragged;
        private bool updatingTileControls;
        private int lastValidTileAmount = 1;
        private bool updatingCardEditor;
        private Point pointerDownPosition;
        private Point lastPointerPosition;

        private enum EditorMode
        {
            Place,
            Connect
        }

        private EditorMode currentMode = EditorMode.Place;

        private readonly Dictionary<(int X, int Y), EditorTile> tiles = new();

        private EditorTile? connectionStart;
        private EditorTile? startTile;
        private EditorTile? selectedTile;
        private readonly ObservableCollection<CardEntry> cardEntries = new();
        private CardEntry? selectedCardEntry;
        private EffectData? selectedEffect;

        public event EventHandler? BackRequested;

        public StageCustomPage(StageData? stageToEdit = null)
        {
            InitializeComponent();

            stageIdToEdit = stageToEdit?.Id;
            CardsListBox.ItemsSource = cardEntries;

            customizerEvent = Connector.GetBindableEvent(
                StageConnectorNames.StageCustomizerCommands);
            customizerEvent.Name = StageConnectorNames.StageCustomPage;
            customizerEvent.OnInvoke = HandleCustomizerResponse;
            Unloaded += (_, _) =>
            {
                customizerEvent.Fire(
                    StageConnectorNames.StageCustomizer,
                    new CloseStageEditorRequest { SessionId = editorSessionId });
                customizerEvent.Destroy();
            };

            mapTransform.Children.Add(mapScale);
            mapTransform.Children.Add(mapTranslation);
            MapCanvas.RenderTransform = mapTransform;
            Loaded += (_, _) => FitMapToViewport();

            RedrawMap();
            UpdateCardEditorFromSelection();
            RequestEditorStageLoad();
        }

        // 편집용 타일 데이터
        private class EditorTile
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Type { get; set; }
            public int Amount { get; set; } = 1;

            public List<EditorTile> Next { get; } = new();
        }

        private void RequestEditorStageLoad()
        {
            pendingStageLoadId = Guid.NewGuid();
            customizerEvent.Fire(
                StageConnectorNames.StageCustomizer,
                new LoadStageEditorRequest
                {
                    RequestId = pendingStageLoadId,
                    SessionId = editorSessionId,
                    StageId = stageIdToEdit
                });
        }

        private void ApplyEditorDraft(StageEditorDraft draft)
        {
            StageNameTextBox.Text = draft.Name;
            ApplyEditorMapDraft(draft, null);
            LoadCardsForEditing(draft.Cards);
            EditorStatusText.Text = draft.StageId.HasValue
                ? $"'{draft.Name}' 스테이지 편집 중입니다."
                : "새 스테이지를 편집하고 있습니다.";
            hasInitialMapView = false;
            RedrawMap();
            FitMapToViewport();
        }

        private void ApplyEditorMapDraft(
            StageEditorDraft draft,
            StageEditorPosition? selectedPosition)
        {
            tiles.Clear();
            startTile = null;
            selectedTile = null;
            connectionStart = null;

            Dictionary<(int X, int Y), EditorTile> editorTiles = new();
            foreach (StageEditorTileDraft tile in draft.Tiles)
            {
                EditorTile editorTile = new()
                {
                    X = tile.X,
                    Y = tile.Y,
                    Type = (int)tile.Type,
                    Amount = tile.Amount
                };
                tiles[(editorTile.X, editorTile.Y)] = editorTile;
                editorTiles[(tile.X, tile.Y)] = editorTile;
            }

            foreach (StageEditorTileDraft tile in draft.Tiles)
            {
                EditorTile editorTile = editorTiles[(tile.X, tile.Y)];
                foreach (StageEditorPosition next in tile.Next)
                {
                    if (!editorTiles.TryGetValue((next.X, next.Y), out EditorTile? nextEditorTile))
                        continue;

                    if (!editorTile.Next.Contains(nextEditorTile))
                        editorTile.Next.Add(nextEditorTile);
                }
            }

            if (draft.Start != null &&
                editorTiles.TryGetValue((draft.Start.X, draft.Start.Y), out EditorTile? editorStart))
            {
                startTile = editorStart;
            }

            if (selectedPosition != null &&
                editorTiles.TryGetValue(
                    (selectedPosition.X, selectedPosition.Y),
                    out EditorTile? editorSelected))
                SelectTile(editorSelected);
        }

        private void RequestMapAction(
            StageEditorMapAction action,
            StageEditorPosition? first = null,
            StageEditorPosition? second = null,
            TileType tileType = TileType.Zone,
            int amount = 1)
        {
            pendingMapActionId = Guid.NewGuid();
            customizerEvent.Fire(
                StageConnectorNames.StageCustomizer,
                new StageEditorMapActionRequest
                {
                    RequestId = pendingMapActionId,
                    SessionId = editorSessionId,
                    Action = action,
                    First = first,
                    Second = second,
                    TileType = tileType,
                    Amount = amount
                });
        }

        private void RedrawMap()
        {
            UpdateGridBounds();
            MapCanvas.Children.Clear();
            MapCanvas.Width = gridColumns * GRID_SIZE;
            MapCanvas.Height = gridRows * GRID_SIZE;

            for (int column = 0; column <= gridColumns; column++)
            {
                double x = column * GRID_SIZE;
                MapCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = MapCanvas.Height,
                    Stroke = new SolidColorBrush(
                        Color.FromRgb(62, 33, 35)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                });
            }

            for (int row = 0; row <= gridRows; row++)
            {
                double y = row * GRID_SIZE;
                MapCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = MapCanvas.Width,
                    Y2 = y,
                    Stroke = new SolidColorBrush(
                        Color.FromRgb(62, 33, 35)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                });
            }

            DrawConnections();
            DrawTiles();
        }

        private void RedrawMapPreservingCamera(
            int coordinateShiftX = 0,
            int coordinateShiftY = 0)
        {
            int previousMinX = gridMinX;
            int previousMinY = gridMinY;
            bool preserveCamera = hasInitialMapView;

            RedrawMap();

            if (!preserveCamera)
                return;

            mapTranslation.X += (gridMinX - previousMinX - coordinateShiftX) *
                GRID_SIZE * mapScale.ScaleX;
            mapTranslation.Y += (gridMinY - previousMinY - coordinateShiftY) *
                GRID_SIZE * mapScale.ScaleY;
        }

        private void UpdateGridBounds()
        {
            if (tiles.Count == 0)
            {
                gridMinX = -EMPTY_GRID_MARGIN;
                gridMinY = -EMPTY_GRID_MARGIN;
                gridColumns = EMPTY_GRID_MARGIN * 2 + 1;
                gridRows = EMPTY_GRID_MARGIN * 2 + 1;
                return;
            }

            int minTileX = tiles.Keys.Min(position => position.X);
            int maxTileX = tiles.Keys.Max(position => position.X);
            int minTileY = tiles.Keys.Min(position => position.Y);
            int maxTileY = tiles.Keys.Max(position => position.Y);

            gridMinX = minTileX - EMPTY_GRID_MARGIN;
            gridMinY = minTileY - EMPTY_GRID_MARGIN;
            gridColumns = maxTileX - minTileX + 1 + EMPTY_GRID_MARGIN * 2;
            gridRows = maxTileY - minTileY + 1 + EMPTY_GRID_MARGIN * 2;
        }

        private Point GetCellCenter(EditorTile tile)
        {
            return new Point(
                (tile.X - gridMinX + 0.5) * GRID_SIZE,
                (tile.Y - gridMinY + 0.5) * GRID_SIZE);
        }

        // 경로 표시
        private void DrawConnections()
        {
            HashSet<(int FromX, int FromY, int ToX, int ToY)> drawnEdges = new();

            foreach (EditorTile tile in tiles.Values)
            {
                foreach (EditorTile next in tile.Next)
                {
                    var edge = (tile.X, tile.Y, next.X, next.Y);
                    if (!drawnEdges.Add(edge))
                        continue;

                    DrawConnectionArrow(
                        MapCanvas,
                        GetCellCenter(tile),
                        GetCellCenter(next),
                        TILE_SIZE,
                        Brushes.DarkGoldenrod,
                        7,
                        next.Next.Contains(tile) ? 5 : 0);
                }
            }
        }

        private static void DrawConnectionArrow(
            Canvas canvas,
            Point fromCenter,
            Point toCenter,
            double tileSize,
            Brush brush,
            double strokeThickness,
            double laneOffset)
        {
            double deltaX = toCenter.X - fromCenter.X;
            double deltaY = toCenter.Y - fromCenter.Y;
            double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance <= 0)
                return;

            double directionX = deltaX / distance;
            double directionY = deltaY / distance;
            double normalX = -directionY;
            double normalY = directionX;
            double edgeDistance = tileSize / 2 + 1;

            Point lineStart = new(
                fromCenter.X + directionX * edgeDistance + normalX * laneOffset,
                fromCenter.Y + directionY * edgeDistance + normalY * laneOffset);
            Point arrowTip = new(
                toCenter.X - directionX * edgeDistance + normalX * laneOffset,
                toCenter.Y - directionY * edgeDistance + normalY * laneOffset);

            const double arrowLength = 13;
            const double arrowHalfWidth = 6;
            Point arrowBase = new(
                arrowTip.X - directionX * arrowLength,
                arrowTip.Y - directionY * arrowLength);
            Point arrowSide1 = new(
                arrowBase.X + normalX * arrowHalfWidth,
                arrowBase.Y + normalY * arrowHalfWidth);
            Point arrowSide2 = new(
                arrowBase.X - normalX * arrowHalfWidth,
                arrowBase.Y - normalY * arrowHalfWidth);

            canvas.Children.Add(new Line
            {
                X1 = lineStart.X,
                Y1 = lineStart.Y,
                X2 = arrowBase.X,
                Y2 = arrowBase.Y,
                Stroke = brush,
                StrokeThickness = strokeThickness,
                IsHitTestVisible = false
            });
            canvas.Children.Add(new Polygon
            {
                Points = new PointCollection { arrowTip, arrowSide1, arrowSide2 },
                Fill = brush,
                IsHitTestVisible = false
            });
        }

        // 타일 표시
        private void DrawTiles()
        {
            foreach (EditorTile tile in tiles.Values)
            {
                bool isStart = tile == startTile;
                bool isSelected = tile == selectedTile;
                (Brush tileColor, string label) = tile.Type switch
                {
                    1 => (Brushes.Maroon, $"-{tile.Amount} HP"),
                    2 => (Brushes.DarkGreen, $"+{tile.Amount} HP"),
                    3 => (Brushes.DarkSlateBlue, "CARD"),
                    4 => (Brushes.SaddleBrown, "PRISON"),
                    5 => (Brushes.DarkCyan, "PORTAL"),
                    6 => (Brushes.Black, "HOLE"),
                    _ => (new SolidColorBrush(Color.FromRgb(53, 35, 41)), "ZONE")
                };
                if (isStart)
                    label = "START";

                Border border = new Border
                {
                    Width = TILE_SIZE,
                    Height = TILE_SIZE,
                    Background = tileColor,
                    BorderBrush = isSelected
                        ? Brushes.LimeGreen
                        : isStart ? Brushes.Gold : Brushes.Goldenrod,
                    BorderThickness = new Thickness(isSelected || isStart ? 4 : 3),
                    Child = new TextBlock
                    {
                        Text = label,
                        Foreground = Brushes.White,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(4)
                    },
                    IsHitTestVisible = false
                };

                Point center = GetCellCenter(tile);
                Canvas.SetLeft(border, center.X - TILE_SIZE / 2);
                Canvas.SetTop(border, center.Y - TILE_SIZE / 2);

                MapCanvas.Children.Add(border);
            }
        }

        private void FitMapToViewport()
        {
            if (hasInitialMapView ||
                MapViewport.ActualWidth <= 1 ||
                MapViewport.ActualHeight <= 1 ||
                MapCanvas.Width <= 1 ||
                MapCanvas.Height <= 1)
                return;

            double fitScale = Math.Min(
                1,
                Math.Min(
                    MapViewport.ActualWidth / MapCanvas.Width,
                    MapViewport.ActualHeight / MapCanvas.Height));
            double scale = Math.Max(fitScale, MINIMUM_ZOOM);
            mapScale.ScaleX = scale;
            mapScale.ScaleY = scale;

            Point focus = tiles.Count == 0
                ? new Point(MapCanvas.Width / 2, MapCanvas.Height / 2)
                : GetCellCenter(startTile ?? tiles.Values.First());
            mapTranslation.X = MapViewport.ActualWidth / 2 - focus.X * scale;
            mapTranslation.Y = MapViewport.ActualHeight / 2 - focus.Y * scale;
            hasInitialMapView = true;
        }

        private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitMapToViewport();
        }

        private void MapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            pointerIsDown = true;
            mapIsBeingDragged = false;
            pointerDownPosition = e.GetPosition(MapViewport);
            lastPointerPosition = pointerDownPosition;
            MapViewport.CaptureMouse();
            e.Handled = true;
        }

        private void MapViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!pointerIsDown)
                return;

            Point current = e.GetPosition(MapViewport);
            if (!mapIsBeingDragged &&
                (Math.Abs(current.X - pointerDownPosition.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(current.Y - pointerDownPosition.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                mapIsBeingDragged = true;
                MapViewport.Cursor = Cursors.SizeAll;
            }

            if (mapIsBeingDragged)
            {
                mapTranslation.X += current.X - lastPointerPosition.X;
                mapTranslation.Y += current.Y - lastPointerPosition.Y;
            }

            lastPointerPosition = current;
        }

        private void MapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!pointerIsDown)
                return;

            Point releasePosition = e.GetPosition(MapViewport);
            bool shouldPlaceOrEdit = !mapIsBeingDragged;
            pointerIsDown = false;
            MapViewport.ReleaseMouseCapture();
            MapViewport.Cursor = Cursors.Hand;

            if (shouldPlaceOrEdit)
                HandleGridClick(releasePosition);
        }

        private void MapViewport_MouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ClickCount != 2 ||
                !TryGetTileAt(e.GetPosition(MapViewport), out EditorTile? tile) ||
                tile == null)
                return;

            RequestMapAction(
                StageEditorMapAction.RemoveTile,
                new StageEditorPosition { X = tile.X, Y = tile.Y });
        }

        private void MapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point pointer = e.GetPosition(MapViewport);
            double oldScale = mapScale.ScaleX;
            double newScale = Math.Clamp(
                oldScale * (e.Delta > 0 ? 1.12 : 1 / 1.12),
                MINIMUM_ZOOM,
                MAXIMUM_ZOOM);
            if (Math.Abs(newScale - oldScale) < 0.0001)
                return;

            double contentX = (pointer.X - mapTranslation.X) / oldScale;
            double contentY = (pointer.Y - mapTranslation.Y) / oldScale;
            mapScale.ScaleX = newScale;
            mapScale.ScaleY = newScale;
            mapTranslation.X = pointer.X - contentX * newScale;
            mapTranslation.Y = pointer.Y - contentY * newScale;
            e.Handled = true;
        }

        private void HandleGridClick(Point viewportPoint)
        {
            if (!TryGetTilePosition(viewportPoint, out int x, out int y))
                return;

            var position = (x, y);

            tiles.TryGetValue(position, out EditorTile? tile);

            switch (currentMode)
            {
                case EditorMode.Place:

                    if (tile == null)
                    {
                        RequestMapAction(
                            StageEditorMapAction.AddTile,
                            new StageEditorPosition { X = x, Y = y },
                            tileType: TileTypeCombo.SelectedIndex >= 0
                                ? (TileType)TileTypeCombo.SelectedIndex
                                : TileType.Zone,
                            amount: GetTileAmountInput());
                        return;
                    }
                    else
                    {
                        SelectTile(tile);

                        EditorStatusText.Text =
                            $"타일 선택: ({x}, {y}) · 유형과 Amount를 편집하세요.";
                    }

                    break;

                case EditorMode.Connect:

                    if (tile == null)
                        return;

                    SelectTile(tile);

                    if (connectionStart == null)
                    {
                        connectionStart = tile;

                        EditorStatusText.Text =
                            "바로 옆에 있는 연결 대상 타일을 선택하세요.";
                    }
                    else
                    {
                        RequestMapAction(
                            StageEditorMapAction.ConnectTiles,
                            new StageEditorPosition
                            {
                                X = connectionStart.X,
                                Y = connectionStart.Y
                            },
                            new StageEditorPosition { X = tile.X, Y = tile.Y });
                        connectionStart = null;
                        return;
                    }

                    break;
            }

            RedrawMapPreservingCamera();
        }

        private bool TryGetTileAt(Point viewportPoint, out EditorTile? tile)
        {
            tile = null;
            if (!TryGetTilePosition(viewportPoint, out int x, out int y))
                return false;

            return tiles.TryGetValue((x, y), out tile);
        }

        private bool TryGetTilePosition(
            Point viewportPoint,
            out int x,
            out int y)
        {
            Matrix inverse = mapTransform.Value;
            inverse.Invert();
            Point contentPoint = inverse.Transform(viewportPoint);

            int column = (int)Math.Floor(contentPoint.X / GRID_SIZE);
            int row = (int)Math.Floor(contentPoint.Y / GRID_SIZE);
            if (column < 0 || column >= gridColumns ||
                row < 0 || row >= gridRows)
            {
                x = 0;
                y = 0;
                return false;
            }

            x = gridMinX + column;
            y = gridMinY + row;
            return true;
        }

        private void FitMapAfterLastTileRemoval()
        {
            if (tiles.Count != 0)
                return;

            hasInitialMapView = false;
            FitMapToViewport();
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

        private void TileTypeCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (updatingTileControls ||
                selectedTile == null ||
                TileTypeCombo.SelectedIndex < 0)
                return;

            if (selectedTile.Type == TileTypeCombo.SelectedIndex)
                return;

            RequestMapAction(
                StageEditorMapAction.UpdateTile,
                new StageEditorPosition { X = selectedTile.X, Y = selectedTile.Y },
                tileType: (TileType)TileTypeCombo.SelectedIndex,
                amount: selectedTile.Amount);
        }

        private void TileAmountTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (updatingTileControls ||
                !int.TryParse(TileAmountTextBox.Text, out int amount) ||
                amount < 0)
                return;

            lastValidTileAmount = amount;
            if (selectedTile == null || selectedTile.Amount == amount)
                return;

            RequestMapAction(
                StageEditorMapAction.UpdateTile,
                new StageEditorPosition { X = selectedTile.X, Y = selectedTile.Y },
                tileType: (TileType)selectedTile.Type,
                amount: amount);
        }

        private void TileAmountTextBox_LostKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            if (int.TryParse(TileAmountTextBox.Text, out int amount) && amount >= 0)
                return;

            int amountToRestore = selectedTile?.Amount ?? Math.Max(0, lastValidTileAmount);
            updatingTileControls = true;
            TileAmountTextBox.Text = amountToRestore.ToString();
            updatingTileControls = false;
        }

        private int GetTileAmountInput()
        {
            return int.TryParse(TileAmountTextBox.Text, out int amount) && amount >= 0
                ? amount
                : Math.Max(0, lastValidTileAmount);
        }

        private void SelectTile(EditorTile tile)
        {
            selectedTile = tile;
            updatingTileControls = true;
            TileTypeCombo.SelectedIndex = tile.Type;
            TileAmountTextBox.Text = tile.Amount.ToString();
            lastValidTileAmount = tile.Amount;
            updatingTileControls = false;
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

            RequestMapAction(StageEditorMapAction.ClearMap);
        }

        private void LoadCardsForEditing(
            IEnumerable<CardEntry> cards,
            int selectedCardIndex = 0,
            int selectedEffectIndex = 0)
        {
            updatingCardEditor = true;
            cardEntries.Clear();

            foreach (CardEntry entry in cards ?? Enumerable.Empty<CardEntry>())
            {
                CardData card = entry.Card ?? new CardData();
                cardEntries.Add(new CardEntry
                {
                    Count = entry.Count,
                    Card = new CardData
                    {
                        Name = card.Name ?? string.Empty,
                        Description = card.Description ?? string.Empty,
                        Usable = card.Usable,
                        Effects = card.Effects?.Select(effect => new EffectData
                        {
                            Type = effect.Type,
                            Target = effect.Target,
                            Count = effect.Count,
                            Amount = effect.Amount
                        }).ToList() ?? new List<EffectData>()
                    }
                });
            }

            CardsListBox.SelectedIndex = selectedCardIndex >= 0 &&
                selectedCardIndex < cardEntries.Count
                    ? selectedCardIndex
                    : cardEntries.Count > 0 ? 0 : -1;
            updatingCardEditor = false;
            selectedCardEntry = CardsListBox.SelectedItem as CardEntry;
            UpdateCardEditorFromSelection();
            if (selectedCardEntry != null &&
                selectedEffectIndex >= 0 &&
                selectedEffectIndex < selectedCardEntry.Card.Effects.Count)
                CardEffectsListBox.SelectedIndex = selectedEffectIndex;
        }

        private void RequestCardAction(
            StageEditorCardAction action,
            string text = "",
            int value = 0,
            bool isChecked = false,
            EffectType effectType = EffectType.None,
            TargetType targetType = TargetType.Self)
        {
            pendingCardActionId = Guid.NewGuid();
            customizerEvent.Fire(
                StageConnectorNames.StageCustomizer,
                new StageEditorCardActionRequest
                {
                    RequestId = pendingCardActionId,
                    SessionId = editorSessionId,
                    Action = action,
                    CardIndex = CardsListBox.SelectedIndex,
                    EffectIndex = CardEffectsListBox.SelectedIndex,
                    Text = text,
                    Value = value,
                    IsChecked = isChecked,
                    EffectType = effectType,
                    TargetType = targetType
                });
        }

        private void ApplyCardActionResult(StageEditorCardActionResult result)
        {
            if (!result.Success || result.Draft == null)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "카드 편집 요청을 처리하지 못했습니다.",
                    "DEATH MARBLE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool cardCollectionChanged = result.Action is
                StageEditorCardAction.AddCard or
                StageEditorCardAction.DeleteCard or
                StageEditorCardAction.AddEffect or
                StageEditorCardAction.DeleteEffect;
            bool effectCollectionChanged = result.Draft.Cards.Count == cardEntries.Count &&
                result.Draft.Cards.Where((entry, index) =>
                    entry.Card.Effects.Count != cardEntries[index].Card.Effects.Count).Any();

            if (cardCollectionChanged || effectCollectionChanged ||
                result.Draft.Cards.Count != cardEntries.Count)
            {
                LoadCardsForEditing(
                    result.Draft.Cards,
                    result.SelectedCardIndex,
                    result.SelectedEffectIndex);
                return;
            }

            for (int i = 0; i < cardEntries.Count; i++)
            {
                CardEntry source = result.Draft.Cards[i];
                CardEntry target = cardEntries[i];
                target.Count = source.Count;
                target.Card.Name = source.Card.Name;
                target.Card.Description = source.Card.Description;
                target.Card.Usable = source.Card.Usable;

                for (int effectIndex = 0; effectIndex < target.Card.Effects.Count; effectIndex++)
                {
                    EffectData sourceEffect = source.Card.Effects[effectIndex];
                    EffectData targetEffect = target.Card.Effects[effectIndex];
                    targetEffect.Type = sourceEffect.Type;
                    targetEffect.Target = sourceEffect.Target;
                    targetEffect.Count = sourceEffect.Count;
                    targetEffect.Amount = sourceEffect.Amount;
                }
            }

            selectedCardEntry = CardsListBox.SelectedItem as CardEntry;
            selectedEffect = CardEffectsListBox.SelectedItem as EffectData;
            CardsListBox.Items.Refresh();
            CardEffectsListBox.Items.Refresh();
            if (result.Action == StageEditorCardAction.SetEffectType)
                UpdateEffectEditorFromSelection();
        }

        private void UpdateCardEditorFromSelection()
        {
            selectedCardEntry = CardsListBox.SelectedItem as CardEntry;
            updatingCardEditor = true;

            bool hasCard = selectedCardEntry != null;
            CardNameTextBox.IsEnabled = hasCard;
            CardCountTextBox.IsEnabled = hasCard;
            CardUsableCheckBox.IsEnabled = hasCard;
            CardDescriptionTextBox.IsEnabled = hasCard;
            AddEffectButton.IsEnabled = hasCard;
            DeleteCardButton.IsEnabled = hasCard;

            if (selectedCardEntry == null)
            {
                CardNameTextBox.Text = string.Empty;
                CardCountTextBox.Text = string.Empty;
                CardUsableCheckBox.IsChecked = false;
                CardDescriptionTextBox.Text = string.Empty;
                CardEffectsListBox.ItemsSource = null;
                CardEffectsListBox.SelectedIndex = -1;
            }
            else
            {
                CardNameTextBox.Text = selectedCardEntry.Card.Name;
                CardCountTextBox.Text = selectedCardEntry.Count.ToString();
                CardUsableCheckBox.IsChecked = selectedCardEntry.Card.Usable;
                CardDescriptionTextBox.Text = selectedCardEntry.Card.Description;
                CardEffectsListBox.ItemsSource = selectedCardEntry.Card.Effects;
                CardEffectsListBox.SelectedIndex =
                    selectedCardEntry.Card.Effects.Count > 0 ? 0 : -1;
            }

            updatingCardEditor = false;
            UpdateEffectEditorFromSelection();
        }

        private void UpdateEffectEditorFromSelection()
        {
            selectedEffect = CardEffectsListBox.SelectedItem as EffectData;
            updatingCardEditor = true;

            bool hasEffect = selectedEffect != null;
            EffectTypeCombo.IsEnabled = hasEffect;
            EffectTargetCombo.IsEnabled = hasEffect;
            EffectCountTextBox.IsEnabled = hasEffect;
            EffectAmountTextBox.IsEnabled = hasEffect &&
                selectedEffect!.Type != EffectType.Teleport &&
                selectedEffect.Type != EffectType.Swap;
            DeleteEffectButton.IsEnabled = hasEffect;

            if (selectedEffect == null)
            {
                EffectTypeCombo.SelectedIndex = -1;
                EffectTargetCombo.SelectedIndex = -1;
                EffectCountTextBox.Text = string.Empty;
                EffectAmountTextBox.Text = string.Empty;
            }
            else
            {
                SelectEnumComboItem(EffectTypeCombo, selectedEffect.Type);
                SelectEnumComboItem(EffectTargetCombo, selectedEffect.Target);
                EffectCountTextBox.Text = selectedEffect.Count.ToString();
                EffectAmountTextBox.Text = selectedEffect.Amount.ToString();
            }

            updatingCardEditor = false;
        }

        private static void SelectEnumComboItem<TEnum>(
            ComboBox comboBox,
            TEnum value)
            where TEnum : struct, Enum
        {
            comboBox.SelectedItem = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == value.ToString());
        }

        private static bool TryReadEnumComboItem<TEnum>(
            ComboBox comboBox,
            out TEnum value)
            where TEnum : struct, Enum
        {
            value = default;
            return comboBox.SelectedItem is ComboBoxItem selectedItem &&
                Enum.TryParse<TEnum>(selectedItem.Tag?.ToString(), out value);
        }

        private void CardsListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!updatingCardEditor)
                UpdateCardEditorFromSelection();
        }

        private void CardNameTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (updatingCardEditor || selectedCardEntry == null)
                return;

            RequestCardAction(
                StageEditorCardAction.SetName,
                text: CardNameTextBox.Text);
        }

        private void CardDescriptionTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (updatingCardEditor || selectedCardEntry == null)
                return;

            RequestCardAction(
                StageEditorCardAction.SetDescription,
                text: CardDescriptionTextBox.Text);
        }

        private void CardCountTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (updatingCardEditor || selectedCardEntry == null ||
                !int.TryParse(CardCountTextBox.Text, out int count) || count < 0)
                return;

            RequestCardAction(StageEditorCardAction.SetCount, value: count);
        }

        private void CardCountTextBox_LostKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            if (selectedCardEntry == null ||
                (int.TryParse(CardCountTextBox.Text, out int count) && count >= 0))
                return;

            updatingCardEditor = true;
            CardCountTextBox.Text = selectedCardEntry.Count.ToString();
            updatingCardEditor = false;
        }

        private void CardUsableCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (updatingCardEditor || selectedCardEntry == null)
                return;

            RequestCardAction(
                StageEditorCardAction.SetUsable,
                isChecked: CardUsableCheckBox.IsChecked == true);
        }

        private void AddCardButton_Click(object sender, RoutedEventArgs e)
        {
            RequestCardAction(StageEditorCardAction.AddCard);
            CardNameTextBox.Focus();
            CardNameTextBox.SelectAll();
        }

        private void DeleteCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCardEntry == null)
                return;

            RequestCardAction(StageEditorCardAction.DeleteCard);
        }

        private void CardEffectsListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!updatingCardEditor)
                UpdateEffectEditorFromSelection();
        }

        private void AddEffectButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCardEntry != null)
                RequestCardAction(StageEditorCardAction.AddEffect);
        }

        private void DeleteEffectButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCardEntry != null && selectedEffect != null)
                RequestCardAction(StageEditorCardAction.DeleteEffect);
        }

        private void EffectTypeCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (updatingCardEditor || selectedEffect == null ||
                !TryReadEnumComboItem(EffectTypeCombo, out EffectType effectType))
                return;

            RequestCardAction(
                StageEditorCardAction.SetEffectType,
                effectType: effectType);
        }

        private void EffectTargetCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (updatingCardEditor || selectedEffect == null ||
                !TryReadEnumComboItem(EffectTargetCombo, out TargetType targetType))
                return;

            RequestCardAction(
                StageEditorCardAction.SetEffectTarget,
                targetType: targetType);
        }

        private void EffectValueTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (updatingCardEditor || selectedEffect == null)
                return;

            if (ReferenceEquals(sender, EffectCountTextBox))
            {
                if (!int.TryParse(EffectCountTextBox.Text, out int count) || count < 1)
                    return;

                RequestCardAction(
                    StageEditorCardAction.SetEffectCount,
                    value: count);
            }
            else if (ReferenceEquals(sender, EffectAmountTextBox))
            {
                if (!int.TryParse(EffectAmountTextBox.Text, out int amount) || amount < 0)
                    return;

                RequestCardAction(
                    StageEditorCardAction.SetEffectAmount,
                    value: amount);
            }
        }

        private void EffectValueTextBox_LostKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            if (selectedEffect == null)
                return;

            if (ReferenceEquals(sender, EffectCountTextBox) &&
                (!int.TryParse(EffectCountTextBox.Text, out int count) || count < 1))
                EffectCountTextBox.Text = selectedEffect.Count.ToString();
            else if (ReferenceEquals(sender, EffectAmountTextBox) &&
                (!int.TryParse(EffectAmountTextBox.Text, out int amount) || amount < 0))
                EffectAmountTextBox.Text = selectedEffect.Amount.ToString();
        }

        // 저장 버튼
        private void SaveStageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                pendingStageSaveId = Guid.NewGuid();
                customizerEvent.Fire(
                    StageConnectorNames.StageCustomizer,
                    new SaveStageEditorRequest
                    {
                        RequestId = pendingStageSaveId,
                        SessionId = editorSessionId,
                        Name = StageNameTextBox.Text
                    });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"스테이지를 저장하지 못했습니다.\n{exception.Message}",
                    "DEATH MARBLE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void HandleCustomizerResponse(string senderName, dynamic? payload)
        {
            switch (payload)
            {
                case StageEditorCardActionResult cardResult when
                    cardResult.RequestId == pendingCardActionId:
                    ApplyCardActionResult(cardResult);
                    break;

                case StageEditorMapActionResult mapResult when
                    mapResult.RequestId == pendingMapActionId:
                    if (!mapResult.Success || mapResult.Draft == null)
                    {
                        EditorStatusText.Text = mapResult.Message ??
                            "맵 편집 요청을 처리하지 못했습니다.";
                        return;
                    }

                    ApplyEditorMapDraft(mapResult.Draft, mapResult.SelectedTile);
                    EditorStatusText.Text = mapResult.Message ?? string.Empty;
                    RedrawMapPreservingCamera(
                        mapResult.CoordinateShiftX,
                        mapResult.CoordinateShiftY);
                    FitMapAfterLastTileRemoval();
                    break;

                case StageEditorLoadResult loadResult when
                    loadResult.RequestId == pendingStageLoadId &&
                    loadResult.SessionId == editorSessionId:
                    if (!loadResult.Success || loadResult.Draft == null)
                    {
                        MessageBox.Show(
                            $"스테이지를 불러오지 못했습니다.\n{loadResult.ErrorMessage}",
                            "DEATH MARBLE",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    ApplyEditorDraft(loadResult.Draft);
                    break;

                case StageEditorSaveResult saveResult when
                    saveResult.RequestId == pendingStageSaveId:
                    if (!saveResult.Success)
                    {
                        if (saveResult.InvalidCardIndex is int cardIndex &&
                            cardIndex >= 0 && cardIndex < CardsListBox.Items.Count)
                            CardsListBox.SelectedIndex = cardIndex;

                        MessageBox.Show(
                            $"스테이지를 저장하지 못했습니다.\n{saveResult.ErrorMessage}",
                            "DEATH MARBLE",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    MessageBox.Show(
                        "스테이지가 저장되었습니다.",
                        "DEATH MARBLE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;
            }
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Software_Project_P3_Death_Marble
{
    internal sealed class Stage_Customizer
    {
        private readonly Connector.BindableEvent pageEvent;
        private readonly Connector.BindableEvent loadStagesEvent;
        private readonly Connector.BindableEvent saveStageEvent;
        private readonly Dictionary<Guid, (Guid SessionId, Guid StageId)> pendingStageLoads = new();
        private readonly Dictionary<Guid, Guid> pendingStageSaves = new();
        private readonly Dictionary<Guid, StageEditorDraft> editorSessions = new();

        public Stage_Customizer()
        {
            pageEvent = Connector.GetBindableEvent(
                StageConnectorNames.StageCustomizerCommands);
            pageEvent.Name = StageConnectorNames.StageCustomizer;
            pageEvent.OnInvoke = HandlePageRequest;

            loadStagesEvent = Connector.GetBindableEvent(
                StageConnectorNames.LoadStages);
            loadStagesEvent.Name = StageConnectorNames.StageCustomizer;
            loadStagesEvent.OnInvoke = HandleStageLoadResult;

            saveStageEvent = Connector.GetBindableEvent(
                StageConnectorNames.SaveStage);
            saveStageEvent.Name = StageConnectorNames.StageCustomizer;
            saveStageEvent.OnInvoke = HandleStageSaveResult;
        }

        private void HandlePageRequest(string senderName, dynamic? payload)
        {
            switch (payload)
            {
                case LoadStageEditorRequest request:
                    HandleLoadStageEditor(senderName, request);
                    break;

                case SaveStageEditorRequest request:
                    HandleSaveStageEditor(senderName, request);
                    break;

                case StageEditorMapActionRequest request:
                    HandleMapAction(senderName, request);
                    break;

                case StageEditorCardActionRequest request:
                    HandleCardAction(senderName, request);
                    break;

                case CloseStageEditorRequest request:
                    editorSessions.Remove(request.SessionId);
                    foreach (Guid requestId in pendingStageLoads
                        .Where(item => item.Value.SessionId == request.SessionId)
                        .Select(item => item.Key)
                        .ToList())
                        pendingStageLoads.Remove(requestId);
                    foreach (Guid requestId in pendingStageSaves
                        .Where(item => item.Value == request.SessionId)
                        .Select(item => item.Key)
                        .ToList())
                        pendingStageSaves.Remove(requestId);
                    break;

                default:
                    Reply(senderName, new StageEditorSaveResult
                    {
                        Success = false,
                        ErrorMessage = "알 수 없는 스테이지 편집 요청입니다."
                    });
                    break;
            }
        }

        private void HandleMapAction(
            string senderName,
            StageEditorMapActionRequest request)
        {
            if (!editorSessions.TryGetValue(request.SessionId, out StageEditorDraft? draft))
            {
                Reply(senderName, new StageEditorMapActionResult
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Message = "편집 세션이 만료되었습니다. 스테이지 편집을 다시 열어주세요."
                });
                return;
            }

            StageEditorMapActionResult result = new()
            {
                RequestId = request.RequestId,
                Success = true,
                Draft = draft
            };

            switch (request.Action)
            {
                case StageEditorMapAction.AddTile:
                    if (request.First == null || request.Amount < 0 ||
                        !Enum.IsDefined(request.TileType))
                    {
                        result.Success = false;
                        result.Message = "타일의 위치, 유형 또는 Amount 값이 올바르지 않습니다.";
                        break;
                    }

                    int tileX = draft.Tiles.Count == 0 ? 0 : request.First.X;
                    int tileY = draft.Tiles.Count == 0 ? 0 : request.First.Y;
                    if (FindTile(draft, tileX, tileY) != null)
                    {
                        result.Success = false;
                        result.Message = "해당 위치에 이미 타일이 있습니다.";
                        break;
                    }

                    draft.Tiles.Add(new StageEditorTileDraft
                    {
                        X = tileX,
                        Y = tileY,
                        Type = request.TileType,
                        Amount = request.Amount
                    });
                    draft.Start ??= new StageEditorPosition { X = tileX, Y = tileY };
                    result.SelectedTile = new StageEditorPosition { X = tileX, Y = tileY };
                    result.Message = draft.Tiles.Count == 1
                        ? $"첫 타일을 시작 타일로 배치했습니다: ({tileX}, {tileY})"
                        : $"타일 배치: ({tileX}, {tileY})";
                    break;

                case StageEditorMapAction.UpdateTile:
                    if (request.First == null || request.Amount < 0 ||
                        !Enum.IsDefined(request.TileType))
                    {
                        result.Success = false;
                        result.Message = "타일 유형 또는 Amount 값이 올바르지 않습니다.";
                        break;
                    }

                    StageEditorTileDraft? tileToUpdate = FindTile(
                        draft,
                        request.First.X,
                        request.First.Y);
                    if (tileToUpdate == null)
                    {
                        result.Success = false;
                        result.Message = "편집할 타일을 찾을 수 없습니다.";
                        break;
                    }

                    tileToUpdate.Type = request.TileType;
                    tileToUpdate.Amount = request.Amount;
                    result.SelectedTile = new StageEditorPosition
                    {
                        X = tileToUpdate.X,
                        Y = tileToUpdate.Y
                    };
                    result.Message = "선택한 타일 정보를 변경했습니다.";
                    break;

                case StageEditorMapAction.ConnectTiles:
                    if (request.First == null || request.Second == null)
                    {
                        result.Success = false;
                        result.Message = "연결할 두 타일을 선택하세요.";
                        break;
                    }

                    (int fromX, int fromY, int toX, int toY) = OrientConnection(
                        draft,
                        (request.First.X, request.First.Y),
                        (request.Second.X, request.Second.Y));
                    if (fromX == toX && fromY == toY)
                    {
                        result.Success = false;
                        result.Message = "서로 다른 타일을 선택하세요.";
                        break;
                    }

                    if (!AreAdjacent(fromX, fromY, toX, toY))
                    {
                        result.Success = false;
                        result.Message = "상하좌우로 바로 인접한 타일끼리만 연결할 수 있습니다.";
                        break;
                    }

                    StageEditorTileDraft? fromTile = FindTile(draft, fromX, fromY);
                    StageEditorTileDraft? toTile = FindTile(draft, toX, toY);
                    if (fromTile == null || toTile == null)
                    {
                        result.Success = false;
                        result.Message = "연결할 타일을 찾을 수 없습니다.";
                        break;
                    }

                    if (ContainsPosition(fromTile.Next, toX, toY))
                    {
                        result.Success = false;
                        result.Message = "해당 방향의 경로가 이미 연결되어 있습니다.";
                        break;
                    }

                    bool reversedExistingConnection = RemovePosition(toTile.Next, fromX, fromY);
                    fromTile.Next.Add(new StageEditorPosition { X = toX, Y = toY });
                    result.SelectedTile = new StageEditorPosition
                    {
                        X = request.Second.X,
                        Y = request.Second.Y
                    };
                    result.Message = reversedExistingConnection
                        ? $"경로 방향 변경: ({fromX}, {fromY}) → ({toX}, {toY})"
                        : $"경로 연결: ({fromX}, {fromY}) → ({toX}, {toY})";
                    break;

                case StageEditorMapAction.RemoveTile:
                    if (request.First == null)
                    {
                        result.Success = false;
                        result.Message = "삭제할 타일을 찾을 수 없습니다.";
                        break;
                    }

                    RemoveEditorTile(draft, request.First, result);
                    break;

                case StageEditorMapAction.ClearMap:
                    draft.Tiles.Clear();
                    draft.Start = null;
                    result.Message = "맵이 초기화되었습니다.";
                    break;

                default:
                    result.Success = false;
                    result.Message = "지원하지 않는 맵 편집 요청입니다.";
                    break;
            }

            Reply(senderName, result);
        }

        private void HandleCardAction(
            string senderName,
            StageEditorCardActionRequest request)
        {
            if (!editorSessions.TryGetValue(request.SessionId, out StageEditorDraft? draft))
            {
                Reply(senderName, new StageEditorCardActionResult
                {
                    RequestId = request.RequestId,
                    Success = false,
                    ErrorMessage = "편집 세션이 만료되었습니다. 스테이지 편집을 다시 열어주세요."
                });
                return;
            }

            int selectedCardIndex = request.CardIndex;
            int selectedEffectIndex = request.EffectIndex;
            CardEntry? entry = selectedCardIndex >= 0 &&
                selectedCardIndex < draft.Cards.Count
                    ? draft.Cards[selectedCardIndex]
                    : null;
            EffectData? effect = entry?.Card.Effects != null &&
                selectedEffectIndex >= 0 &&
                selectedEffectIndex < entry.Card.Effects.Count
                    ? entry.Card.Effects[selectedEffectIndex]
                    : null;

            string? errorMessage = null;
            switch (request.Action)
            {
                case StageEditorCardAction.AddCard:
                    draft.Cards.Add(new CardEntry
                    {
                        Count = 1,
                        Card = new CardData
                        {
                            Name = "새 카드",
                            Description = string.Empty,
                            Effects = new List<EffectData>()
                        }
                    });
                    selectedCardIndex = draft.Cards.Count - 1;
                    selectedEffectIndex = -1;
                    break;

                case StageEditorCardAction.DeleteCard:
                    if (entry == null)
                    {
                        errorMessage = "삭제할 카드를 선택하세요.";
                        break;
                    }

                    draft.Cards.RemoveAt(selectedCardIndex);
                    selectedCardIndex = draft.Cards.Count == 0
                        ? -1
                        : Math.Min(selectedCardIndex, draft.Cards.Count - 1);
                    selectedEffectIndex = 0;
                    break;

                case StageEditorCardAction.SetName:
                    if (entry == null)
                        errorMessage = "편집할 카드를 선택하세요.";
                    else
                        entry.Card.Name = request.Text;
                    break;

                case StageEditorCardAction.SetDescription:
                    if (entry == null)
                        errorMessage = "편집할 카드를 선택하세요.";
                    else
                        entry.Card.Description = request.Text;
                    break;

                case StageEditorCardAction.SetCount:
                    if (entry == null || request.Value < 0)
                        errorMessage = "카드 개수는 0 이상으로 입력하세요.";
                    else
                        entry.Count = request.Value;
                    break;

                case StageEditorCardAction.SetUsable:
                    if (entry == null)
                        errorMessage = "편집할 카드를 선택하세요.";
                    else
                        entry.Card.Usable = request.IsChecked;
                    break;

                case StageEditorCardAction.AddEffect:
                    if (entry == null)
                    {
                        errorMessage = "효과를 추가할 카드를 선택하세요.";
                        break;
                    }

                    entry.Card.Effects ??= new List<EffectData>();
                    entry.Card.Effects.Add(new EffectData());
                    selectedEffectIndex = entry.Card.Effects.Count - 1;
                    break;

                case StageEditorCardAction.DeleteEffect:
                    if (effect == null || entry?.Card.Effects == null)
                    {
                        errorMessage = "삭제할 효과를 선택하세요.";
                        break;
                    }

                    entry.Card.Effects.RemoveAt(selectedEffectIndex);
                    selectedEffectIndex = entry.Card.Effects.Count == 0
                        ? -1
                        : Math.Min(selectedEffectIndex, entry.Card.Effects.Count - 1);
                    break;

                case StageEditorCardAction.SetEffectType:
                    if (effect == null || !Enum.IsDefined(request.EffectType))
                        errorMessage = "효과 유형을 선택하세요.";
                    else
                        effect.Type = request.EffectType;
                    break;

                case StageEditorCardAction.SetEffectTarget:
                    if (effect == null || !Enum.IsDefined(request.TargetType))
                        errorMessage = "효과 대상을 선택하세요.";
                    else
                        effect.Target = request.TargetType;
                    break;

                case StageEditorCardAction.SetEffectCount:
                    if (effect == null || request.Value < 1)
                        errorMessage = "효과 발동 횟수는 1 이상이어야 합니다.";
                    else
                        effect.Count = request.Value;
                    break;

                case StageEditorCardAction.SetEffectAmount:
                    if (effect == null || request.Value < 0)
                        errorMessage = "효과 Amount는 0 이상이어야 합니다.";
                    else
                        effect.Amount = request.Value;
                    break;

                default:
                    errorMessage = "지원하지 않는 카드 편집 요청입니다.";
                    break;
            }

            Reply(senderName, new StageEditorCardActionResult
            {
                RequestId = request.RequestId,
                Action = request.Action,
                Success = errorMessage == null,
                Draft = draft,
                SelectedCardIndex = selectedCardIndex,
                SelectedEffectIndex = selectedEffectIndex,
                ErrorMessage = errorMessage
            });
        }

        private static void RemoveEditorTile(
            StageEditorDraft draft,
            StageEditorPosition position,
            StageEditorMapActionResult result)
        {
            StageEditorTileDraft? tile = FindTile(draft, position.X, position.Y);
            if (tile == null)
            {
                result.Success = false;
                result.Message = "삭제할 타일을 찾을 수 없습니다.";
                return;
            }

            bool wasStart = draft.Start != null &&
                draft.Start.X == position.X && draft.Start.Y == position.Y;
            StageEditorPosition? nextStart = null;
            if (wasStart)
            {
                StageEditorPosition? outgoing = tile.Next.FirstOrDefault(next =>
                    FindTile(draft, next.X, next.Y) != null &&
                    (next.X != position.X || next.Y != position.Y));
                if (outgoing != null)
                {
                    nextStart = new StageEditorPosition
                    {
                        X = outgoing.X,
                        Y = outgoing.Y
                    };
                }
                else if (draft.Tiles.FirstOrDefault(candidate =>
                    candidate.X != position.X || candidate.Y != position.Y) is StageEditorTileDraft candidate)
                {
                    nextStart = new StageEditorPosition
                    {
                        X = candidate.X,
                        Y = candidate.Y
                    };
                }
            }

            draft.Tiles.Remove(tile);
            foreach (StageEditorTileDraft other in draft.Tiles)
                other.Next.RemoveAll(next => next.X == position.X && next.Y == position.Y);

            if (wasStart)
            {
                draft.Start = nextStart;
                if (nextStart != null)
                {
                    result.CoordinateShiftX = -nextStart.X;
                    result.CoordinateShiftY = -nextStart.Y;
                    foreach (StageEditorTileDraft remaining in draft.Tiles)
                    {
                        remaining.X += result.CoordinateShiftX;
                        remaining.Y += result.CoordinateShiftY;
                        foreach (StageEditorPosition next in remaining.Next)
                        {
                            next.X += result.CoordinateShiftX;
                            next.Y += result.CoordinateShiftY;
                        }
                    }

                    draft.Start = new StageEditorPosition { X = 0, Y = 0 };
                }

                result.Message = nextStart == null
                    ? $"시작 타일을 삭제했습니다: ({position.X}, {position.Y})"
                    : "시작 타일을 삭제하고 다음 타일을 (0, 0) 시작 타일로 지정했습니다.";
            }
            else
            {
                result.Message = $"타일 삭제: ({position.X}, {position.Y})";
            }
        }

        private static (int FromX, int FromY, int ToX, int ToY) OrientConnection(
            StageEditorDraft draft,
            (int X, int Y) first,
            (int X, int Y) second)
        {
            if (draft.Start == null)
                return (first.X, first.Y, second.X, second.Y);

            (int X, int Y) start = (draft.Start.X, draft.Start.Y);
            if (first == start || second == start)
            {
                (int X, int Y) other = first == start ? second : first;
                StageEditorTileDraft? otherTile = FindTile(draft, other.X, other.Y);
                bool otherLeadsToStart = otherTile != null &&
                    ContainsPosition(otherTile.Next, start.X, start.Y);

                if (otherLeadsToStart ||
                    TryGetStartPathDistance(draft, other, start, other) ||
                    HasDirectedPath(draft, other, start))
                    return (other.X, other.Y, start.X, start.Y);

                return (start.X, start.Y, other.X, other.Y);
            }

            bool firstOnStartPath = TryGetStartPathDistance(draft, first, null, null, out int firstDistance);
            bool secondOnStartPath = TryGetStartPathDistance(draft, second, null, null, out int secondDistance);
            if (firstOnStartPath && !secondOnStartPath)
                return (first.X, first.Y, second.X, second.Y);
            if (!firstOnStartPath && secondOnStartPath)
                return (second.X, second.Y, first.X, first.Y);

            if (firstOnStartPath && secondOnStartPath)
            {
                StageEditorTileDraft? firstTile = FindTile(draft, first.X, first.Y);
                StageEditorTileDraft? secondTile = FindTile(draft, second.X, second.Y);
                int firstNextCount = firstTile?.Next.Count ?? 0;
                int secondNextCount = secondTile?.Next.Count ?? 0;

                if (firstNextCount == 0 && secondNextCount > 0)
                    return (first.X, first.Y, second.X, second.Y);
                if (secondNextCount == 0 && firstNextCount > 0)
                    return (second.X, second.Y, first.X, first.Y);
                if (firstNextCount == 0 && secondNextCount == 0 &&
                    firstDistance != secondDistance)
                {
                    return firstDistance > secondDistance
                        ? (first.X, first.Y, second.X, second.Y)
                        : (second.X, second.Y, first.X, first.Y);
                }

                bool firstLeadsToSecond = HasDirectedPath(draft, first, second);
                bool secondLeadsToFirst = HasDirectedPath(draft, second, first);
                if (firstLeadsToSecond != secondLeadsToFirst)
                {
                    return firstLeadsToSecond
                        ? (first.X, first.Y, second.X, second.Y)
                        : (second.X, second.Y, first.X, first.Y);
                }

                if (firstDistance != secondDistance)
                {
                    return firstDistance < secondDistance
                        ? (first.X, first.Y, second.X, second.Y)
                        : (second.X, second.Y, first.X, first.Y);
                }
            }

            return (first.X, first.Y, second.X, second.Y);
        }

        private static bool TryGetStartPathDistance(
            StageEditorDraft draft,
            (int X, int Y) target,
            (int X, int Y)? excludedFrom,
            (int X, int Y)? excludedTo)
        {
            return TryGetStartPathDistance(
                draft,
                target,
                excludedFrom,
                excludedTo,
                out _);
        }

        private static bool TryGetStartPathDistance(
            StageEditorDraft draft,
            (int X, int Y) target,
            (int X, int Y)? excludedFrom,
            (int X, int Y)? excludedTo,
            out int distance)
        {
            distance = 0;
            if (draft.Start == null)
                return false;

            (int X, int Y) start = (draft.Start.X, draft.Start.Y);
            Queue<((int X, int Y) Position, int Distance)> pending = new();
            HashSet<(int X, int Y)> visited = new() { start };
            pending.Enqueue((start, 0));

            while (pending.Count > 0)
            {
                ((int X, int Y) current, int currentDistance) = pending.Dequeue();
                if (current == target)
                {
                    distance = currentDistance;
                    return true;
                }

                StageEditorTileDraft? currentTile = FindTile(draft, current.X, current.Y);
                if (currentTile == null)
                    continue;

                foreach (StageEditorPosition next in currentTile.Next)
                {
                    (int X, int Y) nextPosition = (next.X, next.Y);
                    if (excludedFrom == current && excludedTo == nextPosition)
                        continue;

                    if (FindTile(draft, next.X, next.Y) != null && visited.Add(nextPosition))
                        pending.Enqueue((nextPosition, currentDistance + 1));
                }
            }

            return false;
        }

        private static bool HasDirectedPath(
            StageEditorDraft draft,
            (int X, int Y) source,
            (int X, int Y) destination)
        {
            Queue<(int X, int Y)> pending = new();
            HashSet<(int X, int Y)> visited = new() { source };
            pending.Enqueue(source);

            while (pending.Count > 0)
            {
                (int X, int Y) current = pending.Dequeue();
                StageEditorTileDraft? currentTile = FindTile(draft, current.X, current.Y);
                if (currentTile == null)
                    continue;

                foreach (StageEditorPosition next in currentTile.Next)
                {
                    (int X, int Y) nextPosition = (next.X, next.Y);
                    if (nextPosition == destination)
                        return true;

                    if (FindTile(draft, next.X, next.Y) != null && visited.Add(nextPosition))
                        pending.Enqueue(nextPosition);
                }
            }

            return false;
        }

        private static bool AreAdjacent(int firstX, int firstY, int secondX, int secondY)
        {
            return Math.Abs(firstX - secondX) + Math.Abs(firstY - secondY) == 1;
        }

        private static StageEditorTileDraft? FindTile(
            StageEditorDraft draft,
            int x,
            int y)
        {
            return draft.Tiles.FirstOrDefault(tile => tile.X == x && tile.Y == y);
        }

        private static bool ContainsPosition(
            IEnumerable<StageEditorPosition> positions,
            int x,
            int y)
        {
            return positions.Any(position => position.X == x && position.Y == y);
        }

        private static bool RemovePosition(
            List<StageEditorPosition> positions,
            int x,
            int y)
        {
            int index = positions.FindIndex(position => position.X == x && position.Y == y);
            if (index < 0)
                return false;

            positions.RemoveAt(index);
            return true;
        }

        private void HandleLoadStageEditor(
            string senderName,
            LoadStageEditorRequest request)
        {
            if (request.StageId == null)
            {
                StageEditorDraft draft = new()
                {
                    Name = "새로운 지옥"
                };
                editorSessions[request.SessionId] = draft;
                Reply(senderName, new StageEditorLoadResult
                {
                    RequestId = request.RequestId,
                    SessionId = request.SessionId,
                    Success = true,
                    Draft = draft
                });
                return;
            }

            pendingStageLoads[request.RequestId] = (request.SessionId, request.StageId.Value);
            loadStagesEvent.Fire(
                StageConnectorNames.DataManager,
                new LoadStagesRequest { RequestId = request.RequestId });
        }

        private void HandleStageLoadResult(string senderName, dynamic? payload)
        {
            if (payload is not StageEventResult result ||
                !pendingStageLoads.Remove(result.RequestId, out var pendingLoad))
                return;

            if (!result.Success)
            {
                Reply(StageConnectorNames.StageCustomPage,
                    new StageEditorLoadResult
                    {
                        RequestId = result.RequestId,
                        SessionId = pendingLoad.SessionId,
                        Success = false,
                        ErrorMessage = result.ErrorMessage
                    });
                return;
            }

            StageData? stage = result.Stages?.FirstOrDefault(candidate =>
                candidate.Id == pendingLoad.StageId);
            if (stage == null)
            {
                Reply(StageConnectorNames.StageCustomPage,
                    new StageEditorLoadResult
                    {
                        RequestId = result.RequestId,
                        SessionId = pendingLoad.SessionId,
                        Success = false,
                        ErrorMessage = "편집할 스테이지를 저장 데이터에서 찾지 못했습니다."
                    });
                return;
            }

            StageEditorDraft draft = CreateEditorDraft(stage);
            editorSessions[pendingLoad.SessionId] = draft;
            Reply(StageConnectorNames.StageCustomPage,
                new StageEditorLoadResult
                {
                    RequestId = result.RequestId,
                    SessionId = pendingLoad.SessionId,
                    Success = true,
                    Draft = draft
                });
        }

        private void HandleSaveStageEditor(
            string senderName,
            SaveStageEditorRequest request)
        {
            if (!editorSessions.TryGetValue(request.SessionId, out StageEditorDraft? draft))
            {
                Reply(senderName, new StageEditorSaveResult
                {
                    RequestId = request.RequestId,
                    Success = false,
                    ErrorMessage = "편집 세션이 만료되었습니다. 스테이지 편집을 다시 열어주세요."
                });
                return;
            }

            draft.Name = request.Name;
            if (!TryCreateStage(
                    draft,
                    out StageData? stage,
                    out string? errorMessage,
                    out int? invalidCardIndex))
            {
                Reply(senderName, new StageEditorSaveResult
                {
                    RequestId = request.RequestId,
                    Success = false,
                    ErrorMessage = errorMessage,
                    InvalidCardIndex = invalidCardIndex
                });
                return;
            }

            draft.StageId = stage!.Id;
            pendingStageSaves[request.RequestId] = request.SessionId;
            saveStageEvent.Fire(
                StageConnectorNames.DataManager,
                new SaveStageRequest
                {
                    RequestId = request.RequestId,
                    Stage = stage
                });
        }

        private void HandleStageSaveResult(string senderName, dynamic? payload)
        {
            if (payload is not StageEventResult result ||
                !pendingStageSaves.Remove(result.RequestId, out _))
                return;

            Reply(StageConnectorNames.StageCustomPage,
                new StageEditorSaveResult
                {
                    RequestId = result.RequestId,
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage
                });
        }

        private void Reply(string receiverName, object payload)
        {
            pageEvent.Fire(receiverName, payload);
        }

        private static StageEditorDraft CreateEditorDraft(StageData stage)
        {
            MapData map = stage.Map ?? new MapData();
            List<TileData> sourceTiles = map.Tiles?.ToList() ?? new List<TileData>();
            TileData? sourceStart = ResolveTile(sourceTiles, map.Start);
            if (sourceStart == null && map.Start != null)
            {
                sourceStart = map.Start;
                sourceTiles.Add(sourceStart);
            }

            float originX = sourceStart?.Position.X ??
                (sourceTiles.Count > 0 ? sourceTiles.Min(tile => tile.Position.X) : 0);
            float originY = sourceStart?.Position.Y ??
                (sourceTiles.Count > 0 ? sourceTiles.Min(tile => tile.Position.Y) : 0);

            Dictionary<TileData, StageEditorPosition> positions = sourceTiles
                .ToDictionary(
                    tile => tile,
                    tile => new StageEditorPosition
                    {
                        X = (int)Math.Round(tile.Position.X - originX),
                        Y = (int)Math.Round(tile.Position.Y - originY)
                    });

            StageEditorDraft draft = new()
            {
                StageId = stage.Id,
                Name = stage.Name ?? string.Empty,
                Start = sourceStart != null && positions.TryGetValue(sourceStart, out StageEditorPosition? start)
                    ? new StageEditorPosition { X = start.X, Y = start.Y }
                    : null,
                Cards = CloneCards(stage.Cards)
            };

            foreach (TileData tile in sourceTiles)
            {
                StageEditorPosition position = positions[tile];
                StageEditorTileDraft tileDraft = new()
                {
                    X = position.X,
                    Y = position.Y,
                    Type = tile.Type,
                    Amount = tile.Amount
                };

                foreach (TileData next in tile.Next ?? new List<TileData>())
                {
                    TileData? resolvedNext = ResolveTile(sourceTiles, next);
                    if (resolvedNext == null || !positions.TryGetValue(resolvedNext, out StageEditorPosition? nextPosition))
                        continue;

                    tileDraft.Next.Add(new StageEditorPosition
                    {
                        X = nextPosition.X,
                        Y = nextPosition.Y
                    });
                }

                draft.Tiles.Add(tileDraft);
            }

            return draft;
        }

        private static TileData? ResolveTile(
            List<TileData> tiles,
            TileData? target)
        {
            if (target == null)
                return null;

            return tiles.FirstOrDefault(tile => ReferenceEquals(tile, target))
                ?? tiles.FirstOrDefault(tile => tile.Position == target.Position);
        }

        private static bool TryCreateStage(
            StageEditorDraft draft,
            out StageData? stage,
            out string? errorMessage,
            out int? invalidCardIndex)
        {
            stage = null;
            errorMessage = null;
            invalidCardIndex = null;

            string name = (draft.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                errorMessage = "스테이지 이름을 입력하세요.";
                return false;
            }

            if (draft.Tiles == null || draft.Tiles.Count == 0)
            {
                errorMessage = "최소 하나 이상의 타일을 배치하세요.";
                return false;
            }

            if (draft.Start == null)
            {
                errorMessage = "시작 타일을 지정하세요.";
                return false;
            }

            Dictionary<(int X, int Y), TileData> mapTiles = new();
            foreach (StageEditorTileDraft tile in draft.Tiles)
            {
                if (!Enum.IsDefined(tile.Type) || tile.Amount < 0)
                {
                    errorMessage = "타일 유형 또는 Amount 값이 올바르지 않습니다.";
                    return false;
                }

                TileData mapTile = new()
                {
                    Position = new Vector2(tile.X - draft.Start.X, tile.Y - draft.Start.Y),
                    Type = tile.Type,
                    Amount = tile.Amount
                };

                if (!mapTiles.TryAdd((tile.X, tile.Y), mapTile))
                {
                    errorMessage = "같은 위치에 타일이 중복되어 있습니다.";
                    return false;
                }
            }

            if (!mapTiles.TryGetValue((draft.Start.X, draft.Start.Y), out TileData? startTile))
            {
                errorMessage = "시작 타일을 찾을 수 없습니다.";
                return false;
            }

            foreach (StageEditorTileDraft source in draft.Tiles)
            {
                TileData sourceTile = mapTiles[(source.X, source.Y)];
                foreach (StageEditorPosition next in source.Next ?? new List<StageEditorPosition>())
                {
                    if (Math.Abs(source.X - next.X) + Math.Abs(source.Y - next.Y) != 1)
                    {
                        errorMessage = "상하좌우로 바로 인접한 타일끼리만 연결할 수 있습니다.";
                        return false;
                    }

                    if (!mapTiles.TryGetValue((next.X, next.Y), out TileData? nextTile))
                    {
                        errorMessage = "연결 대상 타일을 찾을 수 없습니다.";
                        return false;
                    }

                    if (!sourceTile.Next.Contains(nextTile))
                    {
                        sourceTile.Next.Add(nextTile);
                        nextTile.Prev.Add(sourceTile);
                    }
                }
            }

            List<CardEntry> cards = new();
            List<CardEntry> draftCards = draft.Cards ?? new List<CardEntry>();
            for (int i = 0; i < draftCards.Count; i++)
            {
                CardEntry? sourceEntry = draftCards[i];
                if (sourceEntry == null)
                {
                    invalidCardIndex = i;
                    errorMessage = "카드 데이터가 올바르지 않습니다.";
                    return false;
                }

                CardEntry entry = sourceEntry;
                CardData card = entry.Card ?? new CardData();
                string cardName = (card.Name ?? string.Empty).Trim();
                string description = (card.Description ?? string.Empty).Trim();
                if (entry.Count < 0)
                {
                    invalidCardIndex = i;
                    errorMessage = "카드 개수는 0 이상으로 입력하세요.";
                    return false;
                }

                if (cardName.Length == 0 || description.Length == 0)
                {
                    invalidCardIndex = i;
                    errorMessage = "모든 카드의 이름과 설명을 입력하세요.";
                    return false;
                }

                if (card.Effects?.Any(effect => effect.Count < 1 || effect.Amount < 0) == true)
                {
                    invalidCardIndex = i;
                    errorMessage = "효과의 발동 횟수와 Amount 값을 확인하세요.";
                    return false;
                }

                cards.Add(new CardEntry
                {
                    Count = entry.Count,
                    Card = CloneCard(card, cardName, description)
                });
            }

            MapData map = new()
            {
                Start = startTile,
                Tiles = mapTiles.Values.ToList()
            };
            map.IsLinked = map.HasConnectedPaths();

            stage = new StageData
            {
                Id = draft.StageId ?? Guid.NewGuid(),
                Name = name,
                Map = map,
                Cards = cards
            };
            return true;
        }

        private static List<CardEntry> CloneCards(List<CardEntry>? cards)
        {
            return cards?.Select(entry => new CardEntry
            {
                Count = entry.Count,
                Card = CloneCard(
                    entry.Card ?? new CardData(),
                    entry.Card?.Name ?? string.Empty,
                    entry.Card?.Description ?? string.Empty)
            }).ToList() ?? new List<CardEntry>();
        }

        private static CardData CloneCard(
            CardData card,
            string name,
            string description)
        {
            return new CardData
            {
                Name = name,
                Description = description,
                Usable = card.Usable,
                Effects = card.Effects?.Select(effect => new EffectData
                {
                    Type = effect.Type,
                    Target = effect.Target,
                    Count = effect.Count,
                    Amount = effect.Amount
                }).ToList() ?? new List<EffectData>()
            };
        }
    }
}

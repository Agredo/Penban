namespace Penban.ViewModels;

public sealed record CardMoveRequest(Guid CardId, Guid SourceColumnId, Guid TargetColumnId, int NewIndex);

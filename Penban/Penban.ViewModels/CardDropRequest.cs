namespace Penban.ViewModels;

public sealed record CardDropRequest(Guid CardId, Guid SourceColumnId, Guid TargetColumnId, int NewIndex);

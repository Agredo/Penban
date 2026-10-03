namespace Penban.ViewModels;

/// <summary>
/// A move of several notes at once, as the board's selection mode asks for it. The ids are the notes
/// that were picked, in the order they appear on the board - that order is what they keep in the
/// column they land in.
/// </summary>
public sealed record CardSelectionMoveRequest(IReadOnlyList<Guid> CardIds, Guid TargetColumnId);

namespace Penban.ViewModels;

/// <summary>
/// A project the way a picker offers it. The entry that stands for "no project" carries no id, which
/// is exactly what a board without a project holds - so the picker's selection can be written into
/// <c>BoardDetails.ProjectId</c> without a second case for it.
/// </summary>
/// <param name="Id">The project, or <c>null</c> for the entry that means "no project".</param>
/// <param name="Title">What the picker shows for it.</param>
public sealed record ProjectOption(Guid? Id, string Title);

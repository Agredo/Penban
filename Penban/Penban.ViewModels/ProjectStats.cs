// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;

namespace Penban.ViewModels;

/// <summary>
/// What a project's boards say about it: how many hang under it and when the latest of them was
/// written to. Read once for the whole list rather than asked per project, because a project's card
/// needs both numbers and the boards are one read.
/// </summary>
internal static class ProjectStats
{
    /// <summary>
    /// How many boards hang under each project, and the latest write to one of them. A board carries
    /// the work, so a project whose board was just written to is not a project that has stood still.
    /// Boards without a project are left out - they hang under none.
    /// </summary>
    public static Dictionary<Guid, (int Count, DateTimeOffset LastEdited)> CountByProject(IReadOnlyList<Board> boards)
    {
        var byProject = new Dictionary<Guid, (int Count, DateTimeOffset LastEdited)>();

        foreach (var board in boards)
        {
            if (board.ProjectId is not { } projectId)
            {
                continue;
            }

            var current = byProject.TryGetValue(projectId, out var found)
                ? found
                : (Count: 0, LastEdited: DateTimeOffset.MinValue);

            byProject[projectId] = (
                current.Count + 1,
                board.UpdatedAtUtc > current.LastEdited ? board.UpdatedAtUtc : current.LastEdited);
        }

        return byProject;
    }
}

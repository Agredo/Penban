// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;

namespace Penban.ViewModels;

/// <summary>
/// Keeps an observable list of rows in step with what was just read from the database.
/// </summary>
/// <remarks>
/// Emptying the list and filling it again raises a reset, and a reset sends the list back to the top:
/// the reader would lose the place they had scrolled to every time a row was opened and closed. Only
/// the rows that really moved, appeared or went away raise an event here, and a row that stays where
/// it is raises none at all.
/// </remarks>
internal static class RowList
{
    /// <summary>
    /// Puts the freshly read rows into <paramref name="target"/> without taking out the ones that are
    /// already there. The rows have to be the same instances across loads - that is what lets a row
    /// that did not change stay where it is.
    /// </summary>
    public static void Merge<T>(ObservableCollection<T> target, IReadOnlyList<T> ordered)
        where T : class
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var current = target.IndexOf(item);

            if (current < 0)
            {
                target.Insert(index, item);
            }
            else if (current != index)
            {
                target.Move(current, index);
            }
        }

        while (target.Count > ordered.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}

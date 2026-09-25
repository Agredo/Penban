using System.Diagnostics;
using Penban.ViewModels;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Keeps the copy of the overview the widget shows up to date.
/// </summary>
public static class WidgetSnapshotWriter
{
    /// <summary>
    /// Writes the snapshot and its previews into the shared folder, but only when the boards actually
    /// changed: drawing six images per board is not work to repeat on every visit to the overview.
    /// <para>
    /// Never throws. The widget is a convenience next to the app, and a stale picture on the home
    /// screen is a far smaller nuisance than a failure in the overview.
    /// </para>
    /// </summary>
    public static Task UpdateAsync(IReadOnlyList<BoardViewModel> boards) => UpdateAsync(boards, WidgetSharedStorage.Directory);

    /// <summary>
    /// The same against an explicit folder: the shared one where the app calls this, any folder where
    /// the snapshot is written without a device - and none at all, which does nothing.
    /// </summary>
    public static async Task UpdateAsync(IReadOnlyList<BoardViewModel> boards, string? folder)
    {
        try
        {
            if (folder is null)
            {
                return;
            }

            var snapshot = WidgetSnapshotFactory.Create(boards);
            var document = Path.Combine(folder, WidgetSnapshot.FileName);

            // The signature says whether the boards changed; the previews say whether the widget can
            // still show them at all, which a shared container that was recreated in the meantime
            // does not.
            var current = WidgetSnapshotFactory.ReadSignature(document) == snapshot.Signature && HasPreviews(folder, snapshot);

            if (!current)
            {
                Directory.CreateDirectory(folder);

                // Drawing is plain CPU work on a thread of its own: the overview must not wait for it,
                // and the widget only needs the files once it is drawn again.
                await Task.Run(() =>
                {
                    foreach (var board in snapshot.Boards)
                    {
                        board.Images = WidgetImageRenderer.Render(board, folder);
                    }
                });

                WidgetSnapshotFactory.Write(snapshot, document);
            }

            // Leftovers are cleared on every visit, not only when something changed: a board that was
            // deleted while the app was closed would otherwise keep its previews forever.
            RemoveStaleImages(folder, snapshot);

            if (!current)
            {
                IosWidgetRefresh.ReloadAllTimelines();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Widget snapshot not updated: {exception}");
        }
    }

    /// <summary>Whether every preview the snapshot names is actually on disk.</summary>
    private static bool HasPreviews(string folder, WidgetSnapshot snapshot)
    {
        foreach (var board in snapshot.Boards)
        {
            foreach (var file in board.Images.All)
            {
                if (!File.Exists(Path.Combine(folder, file)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Deletes the previews of boards that are gone, or of sizes that are no longer written.</summary>
    private static void RemoveStaleImages(string folder, WidgetSnapshot snapshot)
    {
        try
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var board in snapshot.Boards)
            {
                foreach (var file in board.Images.All)
                {
                    wanted.Add(file);
                }
            }

            foreach (var file in Directory.EnumerateFiles(folder, "w-*.png"))
            {
                if (!wanted.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception)
        {
            // Leftovers only cost space, so a failed cleanup must not keep the widget from refreshing.
            Debug.WriteLine($"Stale widget images not removed: {exception.Message}");
        }
    }
}

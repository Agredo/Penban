using System.Globalization;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.Maui.Views.Services;

/// <summary>
/// Runs the export and import flows end to end: asks the user what they want, calls
/// <see cref="IDataTransferService"/>, and reports back through <see cref="IDialogService"/>.
/// Lives in the Views assembly because it is the part that has to talk to the user, and keeps the
/// pages free of anything but the click handlers that start a flow.
/// </summary>
public class TransferCoordinator(
    IDataTransferService transferService,
    IFileShareService fileShareService,
    IDialogService dialogService)
{
    /// <summary>
    /// Offers the whole-database actions. Returns whether the database changed, so that the caller
    /// knows it has to reload what it shows.
    /// </summary>
    public async Task<bool> ShowDatabaseMenuAsync()
    {
        var choice = await dialogService.DisplayActionSheetAsync(
            Strings.TransferTitle,
            Strings.Cancel,
            Strings.ExportBackup,
            Strings.ImportFile);

        switch (choice)
        {
            case 0:
                await ExportAsync(ExportScope.Backup, null);
                return false;
            case 1:
                return await ImportAsync();
            default:
                return false;
        }
    }

    /// <summary>Offers only the export actions for one board, for the overview where nothing can be imported into.</summary>
    public async Task ShowBoardExportMenuAsync(Guid boardId)
    {
        var choice = await dialogService.DisplayActionSheetAsync(
            Strings.TransferTitle,
            Strings.Cancel,
            Strings.ExportBoard,
            Strings.ExportCards);

        switch (choice)
        {
            case 0:
                await ExportAsync(ExportScope.Board, boardId);
                break;
            case 1:
                await ExportAsync(ExportScope.Cards, boardId);
                break;
        }
    }

    /// <summary>
    /// Offers everything that can be done from inside a board. The columns are only needed when an
    /// imported file turns out to hold cards, which is the one case that needs a destination.
    /// Returns whether the database changed, so that the caller knows it has to reload the board.
    /// </summary>
    public async Task<bool> ShowBoardMenuAsync(Guid boardId, IReadOnlyList<ColumnChoice> columns)
    {
        var choice = await dialogService.DisplayActionSheetAsync(
            Strings.TransferTitle,
            Strings.Cancel,
            Strings.ExportBoard,
            Strings.ExportCards,
            Strings.ImportFile);

        switch (choice)
        {
            case 0:
                await ExportAsync(ExportScope.Board, boardId);
                return false;
            case 1:
                await ExportAsync(ExportScope.Cards, boardId);
                return false;
            case 2:
                return await ImportAsync(() => ChooseColumnAsync(columns));
            default:
                return false;
        }
    }

    /// <summary>Asks which column an imported card file should be appended to; <c>null</c> means the user backed out.</summary>
    private async Task<Guid?> ChooseColumnAsync(IReadOnlyList<ColumnChoice> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var choice = await dialogService.DisplayActionSheetAsync(
            Strings.ImportCardsInto,
            Strings.Cancel,
            columns.Select(column => column.Title).ToArray());

        return choice >= 0 && choice < columns.Count ? columns[choice].Id : null;
    }

    /// <summary>
    /// Exports the given scope and hands the file straight to the share sheet. The share sheet is
    /// also the only confirmation the user needs, so a successful export says nothing extra.
    /// </summary>
    private async Task ExportAsync(ExportScope scope, Guid? boardId)
    {
        try
        {
            var result = await transferService.ExportAsync(scope, boardId);
            await fileShareService.ShareAsync(result.FilePath, TitleFor(scope));
        }
        catch (Exception exception)
        {
            await dialogService.DisplayAlertAsync(
                Strings.TransferExport,
                string.Format(CultureInfo.CurrentCulture, Strings.ExportFailedFormat, exception.Message),
                Strings.Done);
        }
    }

    /// <summary>
    /// Imports a file the user picks. What the file holds decides what happens; only a backup leaves
    /// a real choice, so only a backup asks. Returns whether the database changed.
    /// </summary>
    private async Task<bool> ImportAsync(Func<Task<Guid?>>? chooseTargetColumn = null)
    {
        var filePath = await PickAsync();
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        ImportFileInfo? info;
        try
        {
            info = await transferService.ReadAsync(filePath);
        }
        catch (Exception exception)
        {
            await ReportImportFailureAsync(exception);
            return false;
        }

        if (info is null)
        {
            await dialogService.DisplayAlertAsync(Strings.TransferImport, Strings.ImportFailed, Strings.Done);
            return false;
        }

        if (info.Scope == ExportScope.Cards)
        {
            if (chooseTargetColumn is null)
            {
                await dialogService.DisplayAlertAsync(Strings.TransferImport, Strings.ImportCardsNeedColumn, Strings.Done);
                return false;
            }

            // A cancelled column choice is not an error, so it says nothing.
            return await chooseTargetColumn() is { } columnId
                && await RunImportAsync(filePath, ImportMode.Merge, columnId);
        }

        if (info.Scope == ExportScope.Board)
        {
            return await RunImportAsync(filePath, ImportMode.Merge, null);
        }

        // "Merge" is offered first because it is the safe one: it can never lose what is already
        // there, and a full replace is always an extra click away.
        var choice = await dialogService.DisplayActionSheetAsync(
            Strings.ImportModeTitle,
            Strings.Cancel,
            Strings.ImportModeMerge,
            Strings.ImportModeReplace);

        if (choice == 0)
        {
            return await RunImportAsync(filePath, ImportMode.Merge, null);
        }

        if (choice == 1)
        {
            var confirmed = await dialogService.DisplayConfirmationAsync(
                Strings.ImportModeReplace,
                Strings.ImportModeReplaceWarning,
                Strings.ImportModeReplace,
                Strings.Cancel);

            if (confirmed)
            {
                return await RunImportAsync(filePath, ImportMode.Replace, null);
            }
        }

        return false;
    }

    private async Task<string?> PickAsync()
    {
        try
        {
            return await fileShareService.PickAsync(Strings.ImportFile, PenbanFile.FileExtension);
        }
        catch (Exception exception)
        {
            await ReportImportFailureAsync(exception);
            return null;
        }
    }

    private async Task<bool> RunImportAsync(string filePath, ImportMode mode, Guid? targetColumnId)
    {
        try
        {
            var result = await transferService.ImportAsync(filePath, mode, targetColumnId);
            await dialogService.DisplayAlertAsync(
                Strings.TransferImport,
                string.Format(CultureInfo.CurrentCulture, Strings.ImportDoneFormat, result.BoardCount, result.CardCount),
                Strings.Done);
            return true;
        }
        catch (Exception exception)
        {
            await ReportImportFailureAsync(exception);
            return false;
        }
    }

    private Task ReportImportFailureAsync(Exception exception)
        => dialogService.DisplayAlertAsync(
            Strings.TransferImport,
            string.Format(CultureInfo.CurrentCulture, Strings.ImportFailedFormat, exception.Message),
            Strings.Done);

    private static string TitleFor(ExportScope scope) => scope switch
    {
        ExportScope.Board => Strings.ExportBoard,
        ExportScope.Cards => Strings.ExportCards,
        _ => Strings.ExportBackup,
    };
}

/// <summary>A column a card import can be appended to, as offered to the user.</summary>
public sealed record ColumnChoice(Guid Id, string Title);

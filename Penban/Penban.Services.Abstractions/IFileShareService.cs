namespace Penban.Services.Abstractions;

/// <summary>
/// The platform's file plumbing: where an export is written, how it reaches the share sheet, and
/// how the user picks a file to import. Kept apart from <see cref="IDataTransferService"/> so that
/// the file format can be exercised without a share sheet in the way.
/// </summary>
public interface IFileShareService
{
    /// <summary>Path in a writable, temporary folder that <paramref name="fileName"/> can be written to.</summary>
    string CreateExportPath(string fileName);

    /// <summary>Hands the file to the operating system's share sheet.</summary>
    Task ShareAsync(string filePath, string title);

    /// <summary>Lets the user pick a file, or returns <c>null</c> when the picker was cancelled.</summary>
    Task<string?> PickAsync(string title, string fileExtension);
}

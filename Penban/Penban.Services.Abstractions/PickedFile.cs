namespace Penban.Services.Abstractions;

/// <summary>
/// A file the user picked, held as the picker handed it out - not as a bare path. On iOS a picked
/// file lies outside the app's sandbox and may only be read while the security scope the picker
/// granted is open; <c>File.OpenRead</c> on <see cref="FullPath"/> is refused by the system with
/// <c>UnauthorizedAccessException</c>. <see cref="OpenRead"/> is the only way in, and it opens a
/// fresh stream every time it is called, so a picked file stays usable after the picker is gone.
/// </summary>
/// <param name="FullPath">Where the file lies, for showing and naming it - not for opening it.</param>
/// <param name="OpenRead">Opens a fresh stream over the file.</param>
public sealed record PickedFile(string FullPath, Func<Task<Stream>> OpenRead);

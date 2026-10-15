namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A row that stands for a thing the drive manages rather than a file or a folder — a Docker
/// container at the root of <c>docker://</c>. Enter still opens it, but no file operation may act
/// on it: <see cref="Commands.FocusedEntry"/> refuses it, so F5, Shift+F6, F8 and Alt+F5 never see one.
///
/// <para>A container row's URL is the container's root folder, and that folder exists, so without
/// this F8 on a container offered to delete it — "Recursively delete folder" ticked — and the delete
/// walked the container's file system removing <c>/etc</c>, <c>/usr</c> and the rest one by one.</para>
/// </summary>
public interface IDriveObjectEntry
{
}

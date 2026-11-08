using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MoVibeSkua.Android.Platform;
using MoVibeSkua.Android.Views;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// Android has no file paths for picked documents, so picked files are copied into the app's Skua
/// folder and that path is returned. Saves go to the app's public exports folder.
/// </summary>
public class MobileFileDialogService : IFileDialogService
{
    private static string ImportDirectory => Path.Combine(ClientFileSources.SkuaDIR, "Imported");

    public static string ExportDirectory
    {
        get
        {
            string? external = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
            string dir = Path.Combine(external ?? ClientFileSources.SkuaDIR, "Exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public string? OpenFile() => PickFiles(false)?.FirstOrDefault();

    public string? OpenFile(string filters) => OpenFile();

    public string? OpenFile(string initialDirectory, string filters) => OpenFile();

    public string? OpenFolder() => ImportFolder();

    public string? OpenFolder(string initialDirectory) => ImportFolder();

    public Task<string?> OpenFolderAsync() => Task.Run(ImportFolder);

    public Task<string?> OpenFolderAsync(string initialDirectory) => Task.Run(ImportFolder);

    public IEnumerable<string>? OpenText()
    {
        string? path = OpenFile();
        return path is null ? null : File.ReadAllLines(path);
    }

    public string? Save() => Save("Text|*.txt");

    public string? Save(string filters) => Save(ExportDirectory, filters);

    public string? Save(string initialDirectory, string filters)
    {
        string ext = ExtensionFromFilter(filters);
        string path = Path.Combine(ExportDirectory, $"skua-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
        Notify(path);
        return path;
    }

    public void SaveText(string contents)
    {
        string? path = Save();
        if (path is not null)
            File.WriteAllText(path, contents);
    }

    public void SaveText(IEnumerable<string> contents)
    {
        string? path = Save();
        if (path is not null)
            File.WriteAllLines(path, contents);
    }

    /// <summary>
    /// Folders cannot be opened by path, so ask for files and import them into one app folder.
    /// </summary>
    private static string? ImportFolder()
    {
        List<string>? files = PickFiles(true);
        if (files is null || files.Count == 0)
            return null;
        return Path.GetDirectoryName(files[0]);
    }

    private static List<string>? PickFiles(bool multiple)
    {
        TopLevel? top = MobileShellView.Instance is { } shell ? TopLevel.GetTopLevel(shell) : null;
        if (top is null)
            return null;

        return Modal.Await(async () =>
        {
            IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = multiple,
                Title = multiple ? "Choose files to import" : "Choose a file"
            });
            if (picked.Count == 0)
                return null;

            Directory.CreateDirectory(ImportDirectory);
            List<string> paths = new();
            foreach (IStorageFile file in picked)
            {
                string destination = Path.Combine(ImportDirectory, file.Name);
                await using Stream input = await file.OpenReadAsync();
                await using FileStream output = File.Create(destination);
                await input.CopyToAsync(output);
                paths.Add(destination);
            }
            return paths;
        });
    }

    private static string ExtensionFromFilter(string filters)
    {
        int star = filters.IndexOf("*.", StringComparison.Ordinal);
        if (star < 0)
            return ".txt";
        string ext = new(filters[(star + 1)..].TakeWhile(c => c != ';' && c != '|' && c != ')').ToArray());
        return ext == ".*" || ext.Length < 2 ? ".txt" : ext;
    }

    private static void Notify(string path)
    {
        global::Android.App.Application.SynchronizationContext.Post(_ =>
            global::Android.Widget.Toast.MakeText(global::Android.App.Application.Context, $"Saved: {System.IO.Path.GetFileName(path)}", global::Android.Widget.ToastLength.Long)?.Show(), null);
    }
}

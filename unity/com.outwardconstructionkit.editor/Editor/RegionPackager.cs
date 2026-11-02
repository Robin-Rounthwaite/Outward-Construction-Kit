using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the open scene into a zip ready to upload to Thunderstore: the exported region, with the files Thunderstore
/// asks for, depending on the Construction Kit's own package so a mod manager installs it too.
/// </summary>
internal static class RegionPackager
{
    public const string DEFAULT_ICON = "Packages/com.outwardconstructionkit.editor/Tools~/default-region-icon.png";
    public const string OUTPUT_FOLDER = "Build/Upload";
    private const string KIT_PACKAGE = "Blue_Chamber_Collective-Outward_Construction_Kit-";
    private const int MAX_DESCRIPTION = 250;
    private const int MAX_NAME = 128;
    private const int ICON_SIZE = 256;

    /// <summary>What has to change before this can be uploaded. Empty means it's ready.</summary>
    public static List<string> Problems(RegionInfo info)
    {
        var problems = new List<string>();
        if (string.IsNullOrEmpty(info.PackageTeam) || !Regex.IsMatch(info.PackageTeam, "^[a-zA-Z0-9_]+$"))
            problems.Add("Add your Thunderstore team's name, using letters, numbers and underscores.");
        if (string.IsNullOrEmpty(info.PackageName) || !Regex.IsMatch(info.PackageName, "^[a-zA-Z0-9_]+$") || info.PackageName.Length > MAX_NAME)
            problems.Add($"The mod name can only use letters, numbers and underscores, up to {MAX_NAME} characters. Underscores show as spaces on Thunderstore.");
        if (string.IsNullOrEmpty(info.PackageVersion) || !Regex.IsMatch(info.PackageVersion, @"^\d+\.\d+\.\d+$"))
            problems.Add("The version needs to look like 1.0.0.");
        if (string.IsNullOrEmpty(info.Description))
            problems.Add("Add a short description.");
        else if (info.Description.Length > MAX_DESCRIPTION)
            problems.Add($"The description is {info.Description.Length} characters, and Thunderstore allows {MAX_DESCRIPTION}.");
        if (!string.IsNullOrEmpty(info.WebsiteUrl) && !Regex.IsMatch(info.WebsiteUrl, "^https?://"))
            problems.Add("The website needs to start with https://, or be left empty.");
        string iconProblem = IconProblem(IconFor(info));
        if (iconProblem != null)
            problems.Add(iconProblem);
        return problems;
    }

    private static string IconFor(RegionInfo info)
    {
        return string.IsNullOrEmpty(info.IconPath) ? Path.GetFullPath(DEFAULT_ICON) : info.IconPath;
    }

    private static string IconProblem(string path)
    {
        if (!File.Exists(path))
            return "The icon file doesn't exist: " + path;
        var texture = new Texture2D(2, 2);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path)) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return "The icon needs to be a PNG file.";
            return texture.width == ICON_SIZE && texture.height == ICON_SIZE ? null : $"The icon is {texture.width} by {texture.height}, and Thunderstore needs {ICON_SIZE} by {ICON_SIZE}.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    /// <summary>Exports the open scene and writes the zip. Returns the zip's path and the export's summary.</summary>
    public static string Package(RegionInfo info, string outputFolder, out string summary)
    {
        List<string> problems = Problems(info);
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(" ", problems.ToArray()));

        string stage = Path.Combine(Path.GetTempPath(), "ock-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            summary = RegionExporter.Export(stage);
            File.WriteAllText(Path.Combine(stage, "manifest.json"), Manifest(info), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(stage, "README.md"), Readme(info), new UTF8Encoding(false));
            File.Copy(IconFor(info), Path.Combine(stage, "icon.png"));

            Directory.CreateDirectory(outputFolder);
            string zip = Path.Combine(outputFolder, info.PackageTeam + "-" + info.PackageName + "-" + info.PackageVersion + ".zip");
            if (File.Exists(zip))
                File.Delete(zip);
            using (FileStream file = File.Create(zip))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                foreach (string path in Directory.GetFiles(stage))
                    using (Stream entry = archive.CreateEntry(Path.GetFileName(path)).Open())
                    using (FileStream source = File.OpenRead(path))
                        source.CopyTo(entry);
            return zip;
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, true);
        }
    }

    public static string KitVersion
    {
        get { return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RegionPackager).Assembly).version; }
    }

    private static string Manifest(RegionInfo info)
    {
        return "{\n"
            + "  \"name\": " + Quote(info.PackageName) + ",\n"
            + "  \"version_number\": " + Quote(info.PackageVersion) + ",\n"
            + "  \"website_url\": " + Quote(info.WebsiteUrl ?? "") + ",\n"
            + "  \"description\": " + Quote(info.Description) + ",\n"
            + "  \"dependencies\": [\n    " + Quote(KIT_PACKAGE + KitVersion) + "\n  ]\n"
            + "}\n";
    }

    private static string Readme(RegionInfo info)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(info.DisplayName).Append("\n\n").Append(string.IsNullOrEmpty(info.About) ? info.Description : info.About).Append("\n\n");
        text.Append("## How to get there\n\nLook for its door in ").Append(LocationNames.For(info.DoorScene)).Append(".\n\n");
        text.Append("## Requirements\n\nThis region needs the [Outward Construction Kit](https://thunderstore.io/c/outward/p/Blue_Chamber_Collective/Outward_Construction_Kit/), which r2modman installs for you. Start the game with r2modman's Start modded button.\n");
        return text.ToString();
    }

    // Enough for what JSON can't hold as it is.
    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (char c in text)
        {
            if (c == '"' || c == '\\')
                quoted.Append('\\').Append(c);
            else if (c < ' ')
                quoted.Append("\\u").Append(((int)c).ToString("x4"));
            else
                quoted.Append(c);
        }
        return quoted.Append('"').ToString();
    }
}

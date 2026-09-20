using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Object = UnityEngine.Object;

[FilePath("ProjectSettings/PackageExporterSettings.asset", FilePathAttribute.Location.ProjectFolder)]
public class PackageExporterSettings : ScriptableSingleton<PackageExporterSettings>
{
    public string exportPath = "";
    public string outPath = "";
    public string packageName = "";
    public string dateFormat = "yyyy-MM-dd";
    public List<string> additionalNameStrings = new List<string>();
    public int dateFormatPosition = 0;
    public int subfolderNamePosition = -1;
    public string te64Path = "";
    public bool openWithTE = false;
    public bool overrideExclusionForSubfolders = false;
    public bool excludeDirectoriesWithHyphen = true;
    public bool includeMetaFiles = true;
    public bool includeNestedPrefabs = true;
    public bool includeOtherComponentsInLog = false;
    public List<string> exclusionKeywords = new List<string>
    {
        "packages", "modularavatar", "resources", "textures", "texture", "matcap",
        "material", "materials", "editor", ".cs", ".lilcontainer", "plugin",
        ".png", ".tga", ".jpg", ".exr", ".hdr", ".gif", ".mp4", ".mov",
        ".jpeg", ".bmp", ".webp", ".tif", ".psd", ".icb", ".vda", ".blend", ".blend1", "test"
    };

    /// <summary>
    /// Wrapper for the protected <c>Save</c> method of <see cref="ScriptableSingleton{T}"/>
    /// to persist this settings instance to disk.
    /// </summary>
    /// <param name="saveAsText">Whether to save the asset as text.</param>
    public void SaveSettings(bool saveAsText = false)
    {
        Save(saveAsText);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

/// <summary>
/// Loads placeholder PNG atlases from Assets/Placeholders via Image.LoadFromFile
/// so both headless tests and Godot Editor work identically without import-cache coupling.
/// </summary>
public sealed class TextureLoader
{
    private readonly Dictionary<string, Texture2D> _cache = new(StringComparer.Ordinal);
    private readonly string _placeholdersDir;

    public TextureLoader()
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        _placeholdersDir = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "Assets", "Placeholders"));
    }

    public Texture2D LoadPlaceholder(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var existing))
        {
            return existing;
        }

        string fullPath = Path.Combine(_placeholdersDir, fileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Missing placeholder asset: {fullPath}");
        }

        var image = Image.LoadFromFile(fullPath);
        if (image == null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Failed to decode PNG image: {fullPath}");
        }

        var texture = ImageTexture.CreateFromImage(image);
        _cache[fileName] = texture;
        return texture;
    }
}

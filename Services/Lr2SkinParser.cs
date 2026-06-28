using System.Text;
using System.IO;
using SkinEditorNext.Models;

namespace SkinEditorNext.Services;

public sealed class Lr2SkinParser
{
    private const int MaxIncludeDepth = 12;

    public Lr2SkinDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes);
        var text = encoding.GetString(bytes);
        return ParseMainText(path, text, encoding);
    }

    public Lr2SkinDocument ParseMainText(
        string path,
        string text,
        Encoding encoding,
        IReadOnlyDictionary<string, string>? sourceTextOverrides = null)
    {
        var document = new Lr2SkinDocument
        {
            MainPath = path,
            MainText = NormalizeLineEndings(text),
            Encoding = encoding
        };

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedOverrides = NormalizeSourceTextOverrides(sourceTextOverrides);
        ParseText(document, path, document.MainText, true, 0, visited, normalizedOverrides);
        BuildObjects(document);
        return document;
    }

    public static string ApplyResolution(string text, int width, int height)
    {
        var lines = SplitLines(NormalizeLineEndings(text)).ToList();
        var replacement = $"#RESOLUTION,{width},{height}";

        for (var i = 0; i < lines.Count; i++)
        {
            var fields = CsvUtil.Split(lines[i].Trim());
            if (fields.Count > 0 && IsCommand(fields[0], "#RESOLUTION"))
            {
                lines[i] = replacement;
                return string.Join(Environment.NewLine, lines);
            }
        }

        var insertAt = lines.FindIndex(line =>
        {
            var fields = CsvUtil.Split(line.Trim());
            return fields.Count > 0 && IsCommand(fields[0], "#INFORMATION");
        });

        if (insertAt < 0) insertAt = 0;
        else insertAt++;

        lines.Insert(insertAt, replacement);
        return string.Join(Environment.NewLine, lines);
    }

    public static string UpdateObjectGeometry(string text, SkinObjectView item)
    {
        var lines = SplitLines(NormalizeLineEndings(text)).ToList();

        if (item.SrcLine > 0 && item.SrcLine <= lines.Count)
        {
            var src = CsvUtil.Split(lines[item.SrcLine - 1]);
            if (src.Count > 0 && src[0].StartsWith("#SRC_", StringComparison.OrdinalIgnoreCase))
            {
                if (!src[0].Equals("#SRC_TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    CsvUtil.SetInt(src, 3, item.SourceX);
                    CsvUtil.SetInt(src, 4, item.SourceY);
                    CsvUtil.SetInt(src, 5, item.SourceWidth);
                    CsvUtil.SetInt(src, 6, item.SourceHeight);
                    lines[item.SrcLine - 1] = CsvUtil.Join(src);
                }
            }
        }

        if (item.DstLine > 0 && item.DstLine <= lines.Count)
        {
            var dst = CsvUtil.Split(lines[item.DstLine - 1]);
            if (dst.Count > 0 && dst[0].StartsWith("#DST_", StringComparison.OrdinalIgnoreCase))
            {
                CsvUtil.SetInt(dst, 3, item.DestX);
                CsvUtil.SetInt(dst, 4, item.DestY);
                CsvUtil.SetInt(dst, 5, item.DestWidth);
                CsvUtil.SetInt(dst, 6, item.DestHeight);
                lines[item.DstLine - 1] = CsvUtil.Join(dst);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string ReplaceLine(string text, int lineNumber, string replacement)
    {
        var lines = SplitLines(NormalizeLineEndings(text)).ToList();
        if (lineNumber <= 0 || lineNumber > lines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), $"Line {lineNumber} is outside the current text.");
        }

        lines[lineNumber - 1] = replacement;
        return string.Join(Environment.NewLine, lines);
    }

    private void ParseText(
        Lr2SkinDocument document,
        string sourcePath,
        string text,
        bool isMainFile,
        int depth,
        HashSet<string> visited,
        IReadOnlyDictionary<string, string>? sourceTextOverrides)
    {
        var fullPath = NormalizeSourcePath(sourcePath);
        if (!visited.Add(fullPath))
        {
            document.Diagnostics.Add($"Include cycle skipped: {sourcePath}");
            return;
        }

        var sourceLine = 0;
        foreach (var rawLine in SplitLines(text))
        {
            sourceLine++;
            var trimmed = rawLine.Trim();
            var fields = trimmed.StartsWith('#') ? CsvUtil.Split(trimmed) : [];
            var line = new SkinCommandLine
            {
                SourcePath = sourcePath,
                SourceLine = sourceLine,
                DisplayLine = document.Lines.Count + 1,
                RawText = rawLine,
                Fields = fields,
                IsMainFile = isMainFile
            };
            document.Lines.Add(line);

            if (fields.Count == 0) continue;

            if (IsCommand(fields[0], "#INFORMATION"))
            {
                document.Header = ParseHeader(fields);
                ApplyHeaderResolutionFallback(document);
            }
            else if (IsCommand(fields[0], "#RESOLUTION"))
            {
                document.Resolution = ParseResolution(fields, document.Diagnostics);
                document.HasResolutionCommand = true;
            }
            else if (IsCommand(fields[0], "#CUSTOMFILE") && fields.Count >= 4)
            {
                document.CustomFileRules.Add(new CustomFileRule(fields[2], fields[3]));
            }
            else if (IsCommand(fields[0], "#INCLUDE") && depth < MaxIncludeDepth)
            {
                var includeRawPath = fields.Count > 1 ? fields[1] : string.Empty;
                AddLr2LoadPathCompatibilityDiagnostic(document, sourcePath, sourceLine, "#INCLUDE", includeRawPath);
                var includePaths = ResolveIncludePaths(sourcePath, includeRawPath, document.CustomFileRules);
                if (includePaths.Count == 0)
                {
                    var attemptedPath = ResolvePath(sourcePath, includeRawPath, document.CustomFileRules);
                    document.Diagnostics.Add(attemptedPath is null
                        ? $"{Path.GetFileName(sourcePath)}:{sourceLine} include path is empty."
                        : $"{Path.GetFileName(sourcePath)}:{sourceLine} include not found: {attemptedPath}");
                }
                else
                {
                    foreach (var includePath in includePaths)
                    {
                        if (sourceTextOverrides is not null &&
                            sourceTextOverrides.TryGetValue(NormalizeSourcePath(includePath), out var includeTextOverride))
                        {
                            ParseText(document, includePath, includeTextOverride, false, depth + 1, visited, sourceTextOverrides);
                        }
                        else
                        {
                            var includeBytes = File.ReadAllBytes(includePath);
                            var includeEncoding = DetectEncoding(includeBytes);
                            ParseText(document, includePath, includeEncoding.GetString(includeBytes), false, depth + 1, visited, sourceTextOverrides);
                        }
                    }
                }
            }
        }

        visited.Remove(fullPath);
    }

    private static void ApplyHeaderResolutionFallback(Lr2SkinDocument document)
    {
        if (document.HasResolutionCommand) return;
        if (document.Header.TargetWidth <= 0 || document.Header.TargetHeight <= 0) return;

        // LR2 skins may omit #RESOLUTION and store the preview canvas size in #INFORMATION x,y.
        document.Resolution = new ResolutionInfo(document.Header.TargetWidth, document.Header.TargetHeight);
    }

    private static ResolutionInfo ParseResolution(IReadOnlyList<string> fields, ICollection<string> diagnostics)
    {
        if (fields.Count >= 3)
        {
            var width = CsvUtil.IntAt(fields, 1, 640);
            var height = CsvUtil.IntAt(fields, 2, 480);
            if (width > 0 && height > 0) return new ResolutionInfo(width, height);
        }

        return CsvUtil.IntAt(fields, 1, 0) switch
        {
            0 => new ResolutionInfo(640, 480),
            1 => new ResolutionInfo(1280, 720),
            2 => new ResolutionInfo(1920, 1080),
            3 => new ResolutionInfo(3840, 2160),
            var unknown => WarnAndDefault(unknown, diagnostics)
        };
    }

    private static SkinHeaderInfo ParseHeader(IReadOnlyList<string> fields)
    {
        return new SkinHeaderInfo(
            CsvUtil.IntAt(fields, 1),
            StringAt(fields, 2),
            StringAt(fields, 3),
            StringAt(fields, 4),
            CsvUtil.IntAt(fields, 5),
            CsvUtil.IntAt(fields, 6),
            CsvUtil.IntAt(fields, 7));
    }

    private static ResolutionInfo WarnAndDefault(int preset, ICollection<string> diagnostics)
    {
        diagnostics.Add($"Unknown #RESOLUTION preset '{preset}', using 640x480.");
        return ResolutionInfo.Default;
    }

    private static void BuildObjects(Lr2SkinDocument document)
    {
        // LR2 assigns image/font slots by command order. These lists mirror that order so the creator UI
        // can show the same slot numbers that #SRC_* commands must reference.
        var images = new Dictionary<int, string>();
        var imageIndex = 0;
        var fontIndex = 0;
        var sourcesByKey = new Dictionary<SourceKey, SkinObjectView>();
        var latestSourceBySuffix = new Dictionary<string, SkinObjectView>(StringComparer.OrdinalIgnoreCase);
        var ifStack = new List<IfFrame>();
        var id = 1;

        document.Objects.Clear();
        document.ImageSlots.Clear();
        document.FontSlots.Clear();
        document.CustomOptions.Clear();
        document.CustomFiles.Clear();
        document.ActiveOptions.Clear();
        document.ActiveOptions.Add(0);

        foreach (var line in document.Lines)
        {
            if (line.Fields.Count == 0) continue;
            var command = line.Command;

            if (IsCommand(command, "#CUSTOMOPTION"))
            {
                // Default LR2 behavior selects the first custom option label, so add its start option.
                document.CustomOptions.Add(ParseCustomOption(line));
                var firstOption = CsvUtil.IntAt(line.Fields, 2, -1);
                if (firstOption is >= 0 and <= 999)
                {
                    document.ActiveOptions.Add(firstOption);
                }
                continue;
            }

            if (IsCommand(command, "#CUSTOMFILE") && line.Fields.Count >= 4)
            {
                document.CustomFiles.Add(new CustomFileDefinition(
                    StringAt(line.Fields, 1),
                    StringAt(line.Fields, 2),
                    StringAt(line.Fields, 3),
                    line.SourcePath,
                    line.SourceLine));
                continue;
            }

            if (HandleIfCommand(line, ifStack, document))
            {
                continue;
            }

            if (!IsActive(ifStack))
            {
                // Hidden #IF branches are preserved in Lines/Code mode, but excluded from the active preview.
                continue;
            }

            if (IsCommand(command, "#IMAGE"))
            {
                var imagePath = line.Fields.Count > 1 ? line.Fields[1] : string.Empty;
                var resolvedPath = ResolvePath(line.SourcePath, imagePath, document.CustomFileRules) ?? imagePath;
                AddLr2LoadPathCompatibilityDiagnostic(document, line.SourcePath, line.SourceLine, "#IMAGE", imagePath);
                AddImageLoadDiagnostic(document, line.SourcePath, line.SourceLine, imagePath, resolvedPath);
                images[imageIndex] = resolvedPath;
                document.ImageSlots.Add(new SkinImageSlot(
                    imageIndex,
                    imagePath,
                    resolvedPath,
                    line.SourcePath,
                    line.SourceLine));
                imageIndex++;
                continue;
            }

            if (IsCommand(command, "#FONT") || IsCommand(command, "#LR2FONT"))
            {
                document.FontSlots.Add(new SkinFontSlot(
                    fontIndex++,
                    command.TrimStart('#').ToUpperInvariant(),
                    line.RawText,
                    line.SourcePath,
                    line.SourceLine));
                continue;
            }

            if (command.StartsWith("#SRC_", StringComparison.OrdinalIgnoreCase))
            {
                // A following #DST_* is matched by suffix and n value. If older skins omit a clean match,
                // fall back to the latest source with the same suffix, matching LR2's loose loading style.
                var source = CreateSourceObject(line, images, id++);
                document.Objects.Add(source);
                sourcesByKey[new SourceKey(source.CommandSuffix, source.SourceIndex)] = source;
                latestSourceBySuffix[source.CommandSuffix] = source;
                continue;
            }

            if (!command.StartsWith("#DST_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = CommandSuffix(command, "#DST_");
            var targetIndex = CsvUtil.IntAt(line.Fields, 1, 0);
            if (!sourcesByKey.TryGetValue(new SourceKey(suffix, targetIndex), out var target) &&
                !latestSourceBySuffix.TryGetValue(suffix, out target))
            {
                continue;
            }

            AddDstFrame(target, line);
        }
    }

    private static SkinObjectView CreateSourceObject(
        SkinCommandLine line,
        IReadOnlyDictionary<int, string> images,
        int id)
    {
        var fields = line.Fields;
        var suffix = CommandSuffix(line.Command, "#SRC_");
        var graph = CsvUtil.IntAt(fields, 2, -1);
        var imagePath = !string.Equals(suffix, "TEXT", StringComparison.OrdinalIgnoreCase) &&
                        images.TryGetValue(graph, out var path)
            ? path
            : string.Empty;
        var divX = Math.Max(1, CsvUtil.IntAt(fields, 7, 1));
        var divY = Math.Max(1, CsvUtil.IntAt(fields, 8, 1));

        return new SkinObjectView
        {
            Id = id,
            Kind = line.Command,
            CommandSuffix = suffix,
            ImagePath = imagePath,
            SourceFile = line.SourcePath,
            SrcLine = line.SourceLine,
            IsEditableInMain = line.IsMainFile,
            SourceIndex = CsvUtil.IntAt(fields, 1),
            SourceGraph = graph,
            SourceX = Math.Max(0, CsvUtil.IntAt(fields, 3)),
            SourceY = Math.Max(0, CsvUtil.IntAt(fields, 4)),
            SourceWidth = CsvUtil.IntAt(fields, 5),
            SourceHeight = CsvUtil.IntAt(fields, 6),
            SourceDivX = divX,
            SourceDivY = divY,
            SourceCycle = CsvUtil.IntAt(fields, 9),
            SourceTimer = CsvUtil.IntAt(fields, 10),
            SourceOp1 = CsvUtil.IntAt(fields, 11),
            SourceOp2 = CsvUtil.IntAt(fields, 12),
            SourceOp3 = CsvUtil.IntAt(fields, 13),
            SourceOp4 = CsvUtil.IntAt(fields, 14),
            SourceOp5 = CsvUtil.IntAt(fields, 15)
        };
    }

    private static CustomOptionDefinition ParseCustomOption(SkinCommandLine line)
    {
        var labels = new List<string>();
        for (var i = 3; i < line.Fields.Count; i++)
        {
            var label = line.Fields[i].Trim();
            if (string.IsNullOrWhiteSpace(label)) continue;
            labels.Add(label);
        }

        return new CustomOptionDefinition(
            StringAt(line.Fields, 1),
            CsvUtil.IntAt(line.Fields, 2),
            labels,
            line.SourcePath,
            line.SourceLine);
    }

    private static string StringAt(IReadOnlyList<string> fields, int index)
    {
        return index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;
    }

    private static void AddDstFrame(SkinObjectView target, SkinCommandLine line)
    {
        var frame = new SkinDstFrame
        {
            Line = line.SourceLine,
            Time = CsvUtil.IntAt(line.Fields, 2),
            X = CsvUtil.IntAt(line.Fields, 3),
            Y = CsvUtil.IntAt(line.Fields, 4),
            Width = CsvUtil.IntAt(line.Fields, 5),
            Height = CsvUtil.IntAt(line.Fields, 6),
            Acc = CsvUtil.IntAt(line.Fields, 7),
            Alpha = CsvUtil.IntAt(line.Fields, 8),
            Red = CsvUtil.IntAt(line.Fields, 9),
            Green = CsvUtil.IntAt(line.Fields, 10),
            Blue = CsvUtil.IntAt(line.Fields, 11),
            Blend = CsvUtil.IntAt(line.Fields, 12),
            Filter = CsvUtil.IntAt(line.Fields, 13),
            Angle = CsvUtil.IntAt(line.Fields, 14),
            Center = CsvUtil.IntAt(line.Fields, 15),
            SortId = line.DisplayLine
        };

        if (target.Frames.Count == 0)
        {
            target.DstFile = line.SourcePath;
            target.DstLine = line.SourceLine;
            target.IsEditableInMain = target.IsEditableInMain && line.IsMainFile;
            target.DestLoop = CsvUtil.IntAt(line.Fields, 16);
            target.DestTimer = CsvUtil.IntAt(line.Fields, 17);
            target.DestOp1 = CsvUtil.IntAt(line.Fields, 18);
            target.DestOp2 = CsvUtil.IntAt(line.Fields, 19);
            target.DestOp3 = CsvUtil.IntAt(line.Fields, 20);
            target.DestOp4 = CsvUtil.IntAt(line.Fields, 21);
            target.DestOp5 = CsvUtil.IntAt(line.Fields, 22);
            target.DestX = (int)frame.X;
            target.DestY = (int)frame.Y;
            target.DestWidth = (int)frame.Width;
            target.DestHeight = (int)frame.Height;
        }

        target.Frames.Add(frame);
    }

    private static bool HandleIfCommand(SkinCommandLine line, List<IfFrame> stack, Lr2SkinDocument document)
    {
        if (IsCommand(line.Command, "#IF"))
        {
            var parentActive = IsActive(stack);
            var condition = AreOptionsActive(line.Fields, 1, document.ActiveOptions);
            stack.Add(new IfFrame(parentActive, condition, parentActive && condition));
            return true;
        }

        if (IsCommand(line.Command, "#ELSEIF"))
        {
            if (stack.Count == 0)
            {
                document.Diagnostics.Add($"{Path.GetFileName(line.SourcePath)}:{line.SourceLine} #ELSEIF without #IF.");
                return true;
            }

            var top = stack[^1];
            var condition = !top.BranchMatched && AreOptionsActive(line.Fields, 1, document.ActiveOptions);
            top.CurrentActive = top.ParentActive && condition;
            top.BranchMatched = top.BranchMatched || condition;
            return true;
        }

        if (IsCommand(line.Command, "#ELSE"))
        {
            if (stack.Count == 0)
            {
                document.Diagnostics.Add($"{Path.GetFileName(line.SourcePath)}:{line.SourceLine} #ELSE without #IF.");
                return true;
            }

            var top = stack[^1];
            top.CurrentActive = top.ParentActive && !top.BranchMatched;
            top.BranchMatched = true;
            return true;
        }

        if (IsCommand(line.Command, "#ENDIF"))
        {
            if (stack.Count == 0)
            {
                document.Diagnostics.Add($"{Path.GetFileName(line.SourcePath)}:{line.SourceLine} #ENDIF without #IF.");
                return true;
            }

            stack.RemoveAt(stack.Count - 1);
            return true;
        }

        return false;
    }

    private static bool IsActive(IEnumerable<IfFrame> stack)
    {
        return stack.All(frame => frame.CurrentActive);
    }

    private static bool AreOptionsActive(IReadOnlyList<string> fields, int startIndex, IReadOnlySet<int> activeOptions)
    {
        var end = Math.Min(fields.Count, startIndex + 9);
        for (var i = startIndex; i < end; i++)
        {
            if (string.IsNullOrWhiteSpace(fields[i])) continue;
            var option = CsvUtil.IntAt(fields, i, int.MinValue);
            if (option == 0) continue;
            if (option is < 0 or > 999 || !activeOptions.Contains(option))
            {
                return false;
            }
        }

        return true;
    }

    private static string CommandSuffix(string command, string prefix)
    {
        var suffix = command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? command[prefix.Length..]
            : command.TrimStart('#');
        return suffix.ToUpperInvariant();
    }

    private readonly record struct SourceKey(string Suffix, int Index);

    private sealed class IfFrame(bool parentActive, bool branchMatched, bool currentActive)
    {
        public bool ParentActive { get; } = parentActive;
        public bool BranchMatched { get; set; } = branchMatched;
        public bool CurrentActive { get; set; } = currentActive;
    }

    private static void AddLr2LoadPathCompatibilityDiagnostic(
        Lr2SkinDocument document,
        string sourcePath,
        int sourceLine,
        string command,
        string rawPath)
    {
        var cleaned = CleanLoadPath(rawPath);
        if (IsSpecialLoadPath(cleaned) || Path.IsPathRooted(cleaned) || IsLr2RootRelativePath(cleaned))
        {
            return;
        }

        if (!IsInsideLr2files(sourcePath))
        {
            return;
        }

        document.Diagnostics.Add($"{Path.GetFileName(sourcePath)}:{sourceLine} {command} may not load in LR2: use an LR2files\\... path instead of '{cleaned}'.");
    }

    private static void AddImageLoadDiagnostic(
        Lr2SkinDocument document,
        string sourcePath,
        int sourceLine,
        string rawPath,
        string resolvedPath)
    {
        var cleaned = CleanLoadPath(rawPath);
        if (IsSpecialLoadPath(cleaned) || cleaned.Contains('*') || string.Equals(cleaned, "CONTINUE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(resolvedPath))
        {
            document.Diagnostics.Add($"{Path.GetFileName(sourcePath)}:{sourceLine} image not found: {cleaned} -> {resolvedPath}");
        }
    }

    private static string CleanLoadPath(string rawPath)
    {
        return rawPath.Trim().Trim('"').Trim();
    }

    private static bool IsSpecialLoadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (string.Equals(path, "CONTINUE", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(path, "dummy", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("dummy.", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains('*')) return true;
        return false;
    }

    private static bool IsLr2RootRelativePath(string path)
    {
        var normalized = NormalizeLr2RootRelativePath(path);
        return normalized.Equals("LR2files", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("LR2files" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideLr2files(string path)
    {
        var directory = File.Exists(path)
            ? Path.GetDirectoryName(Path.GetFullPath(path))
            : Path.GetFullPath(path);

        if (string.IsNullOrWhiteSpace(directory)) return false;

        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            if (dir.Name.Equals("LR2files", StringComparison.OrdinalIgnoreCase)) return true;
            dir = dir.Parent;
        }

        return false;
    }
    private static Dictionary<string, string>? NormalizeSourceTextOverrides(IReadOnlyDictionary<string, string>? sourceTextOverrides)
    {
        if (sourceTextOverrides is null || sourceTextOverrides.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in sourceTextOverrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            normalized[NormalizeSourcePath(pair.Key)] = pair.Value;
        }

        return normalized;
    }

    private static string NormalizeSourcePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static IReadOnlyList<string> ResolveIncludePaths(string sourcePath, string rawPath, IReadOnlyList<CustomFileRule>? customFileRules)
    {
        var resolvedPath = ResolvePath(sourcePath, rawPath, customFileRules);
        if (resolvedPath is null)
        {
            return Array.Empty<string>();
        }

        if (!ContainsPathWildcard(resolvedPath))
        {
            if (File.Exists(resolvedPath))
            {
                return [resolvedPath];
            }

            var commonCsvFallback = ResolveCommonCsvIncludeFallback(resolvedPath);
            return commonCsvFallback is not null ? [commonCsvFallback] : Array.Empty<string>();
        }

        var directory = Path.GetDirectoryName(resolvedPath);
        var pattern = Path.GetFileName(resolvedPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(pattern) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(directory, pattern)
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveCommonCsvIncludeFallback(string resolvedPath)
    {
        var directory = Path.GetDirectoryName(resolvedPath);
        var fileName = Path.GetFileName(resolvedPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var modeDirectory = new DirectoryInfo(directory);
        var csvDirectory = modeDirectory.Parent;
        if (csvDirectory is null || !csvDirectory.Name.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fallback = Path.Combine(csvDirectory.FullName, "common", fileName);
        return File.Exists(fallback) ? fallback : null;
    }
    private static bool ContainsPathWildcard(string path)
    {
        return path.Contains('*') || path.Contains('?');
    }
    private static string? ResolvePath(string sourcePath, string rawPath, IReadOnlyList<CustomFileRule>? customFileRules = null)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        var cleaned = ApplyCustomFileRules(rawPath.Trim().Trim('"'), customFileRules);
        if (string.Equals(cleaned, "CONTINUE", StringComparison.OrdinalIgnoreCase)) return cleaned;
        if (Path.IsPathRooted(cleaned)) return cleaned;

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory)) return cleaned;

        var direct = Path.GetFullPath(Path.Combine(sourceDirectory, cleaned));
        var lr2RootPath = ResolveFromLr2Root(sourceDirectory, cleaned);
        return lr2RootPath ?? direct;
    }

    private static string ApplyCustomFileRules(string path, IReadOnlyList<CustomFileRule>? customFileRules)
    {
        if (!path.Contains('*') || customFileRules is null || customFileRules.Count == 0)
        {
            return path;
        }

        foreach (var rule in customFileRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.Selected))
            {
                continue;
            }

            var wildcardIndex = rule.Pattern.IndexOf('*');
            if (wildcardIndex < 0) continue;

            var prefix = rule.Pattern[..wildcardIndex];
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return path.Replace("*", rule.Selected, StringComparison.Ordinal);
        }

        return path;
    }

    private static string? ResolveFromLr2Root(string sourceDirectory, string relativePath)
    {
        var normalized = NormalizeLr2RootRelativePath(relativePath);
        if (!normalized.StartsWith("LR2files", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var dir = new DirectoryInfo(sourceDirectory);
        while (dir is not null)
        {
            var candidateRoot = Path.Combine(dir.FullName, "LR2files");
            if (Directory.Exists(candidateRoot))
            {
                var candidate = Path.GetFullPath(Path.Combine(dir.FullName, normalized));
                if (PathReferenceExists(candidate))
                {
                    return candidate;
                }

                return ResolveFromCurrentThemeAlias(sourceDirectory, dir.FullName, normalized) ?? candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? ResolveFromCurrentThemeAlias(string sourceDirectory, string lr2RootParent, string normalizedPath)
    {
        var parts = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 ||
            !parts[0].Equals("LR2files", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("Theme", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentTheme = FindCurrentThemeName(sourceDirectory);
        if (string.IsNullOrWhiteSpace(currentTheme) || parts[2].Equals(currentTheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        parts[2] = currentTheme;
        var candidate = Path.GetFullPath(Path.Combine(lr2RootParent, Path.Combine(parts)));
        return PathReferenceExists(candidate) ? candidate : null;
    }

    private static string? FindCurrentThemeName(string sourceDirectory)
    {
        var dir = new DirectoryInfo(sourceDirectory);
        while (dir.Parent is not null)
        {
            if (dir.Parent.Name.Equals("Theme", StringComparison.OrdinalIgnoreCase) &&
                dir.Parent.Parent?.Name.Equals("LR2files", StringComparison.OrdinalIgnoreCase) == true)
            {
                return dir.Name;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool PathReferenceExists(string path)
    {
        try
        {
            if (ContainsPathWildcard(path))
            {
                var directory = Path.GetDirectoryName(path);
                var pattern = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(directory) &&
                       !string.IsNullOrWhiteSpace(pattern) &&
                       Directory.Exists(directory) &&
                       Directory.EnumerateFiles(directory, pattern).Any();
            }

            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }
    private static string NormalizeLr2RootRelativePath(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Trim();
        var dotPrefix = "." + Path.DirectorySeparatorChar;
        while (normalized.StartsWith(dotPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[dotPrefix.Length..];
        }

        return normalized;
    }

    public static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        RegisterCodePagesProviderIfPresent();

        foreach (var encoding in CandidateEncodings())
        {
            try
            {
                encoding.GetString(bytes);
                return encoding;
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return Encoding.Latin1;
    }

    private static IEnumerable<Encoding> CandidateEncodings()
    {
        yield return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var codePage in new[] { 932, 949 })
        {
            Encoding? encoding = null;
            try
            {
                encoding = Encoding.GetEncoding(
                    codePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
            }
            catch (ArgumentException)
            {
            }

            if (encoding is not null) yield return encoding;
        }
    }

    private static void RegisterCodePagesProviderIfPresent()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static bool IsCommand(string value, string expected)
    {
        return string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }
}

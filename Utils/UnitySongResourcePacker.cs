using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace AffToSpcConverter.Utils;

public sealed class SongDatabaseSlotInfo
{
    public required int SlotIndex { get; init; }
    public required bool IsEmpty { get; init; }
    public required ushort SongIdValue { get; init; }
    public required string BaseName { get; init; }
    public required int ChartCount { get; init; }
    public required string DisplayNameSectionIndicator { get; init; }
    public required string DisplayArtistSectionIndicator { get; init; }
    public required int GameplayBackground { get; init; }
    public required int RewardStyle { get; init; }
    public string DisplayText => $"[{SlotIndex:00}] {(IsEmpty ? "<空槽>" : BaseName)} (Id={SongIdValue}, Charts={ChartCount})";
    public override string ToString() => DisplayText;
}

public sealed class JacketTemplateCandidate
{
    public required int BundleFileIndex { get; init; }
    public required string AssetsFileName { get; init; }
    public required long TexturePathId { get; init; }
    public required long MaterialPathId { get; init; }
    public required string BaseName { get; init; }
    public required int TextureWidth { get; init; }
    public required int TextureHeight { get; init; }
    public string DisplayText => $"{BaseName} [{TextureWidth}x{TextureHeight}] ({AssetsFileName})";
    public override string ToString() => DisplayText;
}

public sealed class SongBundleScanResult
{
    public required string BundleFilePath { get; init; }
    public required int SongDatabaseBundleFileIndex { get; init; }
    public required long SongDatabasePathId { get; init; }
    public required string SongDatabaseAssetsFileName { get; init; }
    public required IReadOnlyList<SongDatabaseSlotInfo> Slots { get; init; }
    public required IReadOnlyList<JacketTemplateCandidate> JacketTemplates { get; init; }
}

public sealed class NewSongChartPackItem
{
    public required int ChartSlotIndex { get; init; }
    public required string SourceChartFilePath { get; init; }
    public required byte DifficultyFlag { get; init; }
    public required byte Available { get; init; }
    public required int Rating { get; init; }
    public required string LevelSectionIndicator { get; init; }
    public required string DisplayChartDesigner { get; init; }
    public required string DisplayJacketDesigner { get; init; }
}

public sealed class NewSongPackRequest
{
    public required string BundleFilePath { get; init; }
    public required string SharedAssetsFilePath { get; init; }
    public required string OutputDirectory { get; init; }
    public required string JacketImageFilePath { get; init; }
    public required string BgmFilePath { get; init; }
    public required string BaseName { get; init; }
    public bool KeepJacketOriginalSize { get; init; }
    public required SongDatabaseSlotInfo SelectedSlot { get; init; }
    public required JacketTemplateCandidate JacketTemplate { get; init; }
    public required double PreviewStartSeconds { get; init; }
    public required double PreviewEndSeconds { get; init; }
    public required string DisplayNameSectionIndicator { get; init; }
    public required string DisplayArtistSectionIndicator { get; init; }
    public required int GameplayBackground { get; init; }
    public required int RewardStyle { get; init; }
    public required IReadOnlyList<NewSongChartPackItem> Charts { get; init; }
    public bool AutoRenameWhenTargetLocked { get; init; } = true;
}

public sealed class NewSongMappingEntryResult
{
    public required string FullLookupPath { get; init; }
    public required string Guid { get; init; }
    public required int FileLength { get; init; }
}

public sealed class NewSongPackExportResult
{
    public required string OutputBundlePath { get; init; }
    public required string OutputSharedAssetsPath { get; init; }
    public required long NewTexturePathId { get; init; }
    public required long NewMaterialPathId { get; init; }
    public required IReadOnlyList<NewSongMappingEntryResult> AddedMappingEntries { get; init; }
    public required SongDatabaseReadbackValidationResult SongDatabaseReadback { get; init; }
    public required string SongDatabaseArrayStructureDiagnostics { get; init; }
    public required string Summary { get; init; }
}

public sealed class SongDatabaseReadbackValidationResult
{
    public sealed class ChartInfo
    {
        public required int Index { get; init; }
        public required string Id { get; init; }
        public required int Difficulty { get; init; }
        public required int Available { get; init; }
    }

    public sealed class SongIdJacketMaterialEntry
    {
        public required int SongId { get; init; }
        public required int FileId { get; init; }
        public required long PathId { get; init; }
    }

    public sealed class ChartIdJacketMaterialEntry
    {
        public required string ChartId { get; init; }
        public required int FileId { get; init; }
        public required long PathId { get; init; }
    }

    public required int SlotIndex { get; init; }
    public required int SongId { get; init; }
    public required string BaseName { get; init; }
    public required IReadOnlyList<ChartInfo> ChartInfos { get; init; }
    public required IReadOnlyList<SongIdJacketMaterialEntry> SongIdJacketMaterials { get; init; }
    public required IReadOnlyList<ChartIdJacketMaterialEntry> ChartIdJacketMaterials { get; init; }
}

public static class UnitySongResourcePacker
{
    private sealed class GeneratedResourceFile
    {
        public required NewSongMappingEntryResult MappingEntry { get; init; }
        public required byte[] PlainBytes { get; init; }
    }

    private sealed class OutputPlan
    {
        public required string RequestedOutputPath { get; init; }
        public required string FinalOutputPath { get; init; }
        public required string PackOutputPath { get; init; }
        public required bool ReplaceAfterPack { get; init; }
        public required bool AutoRenamedDueToLock { get; init; }
    }

    private sealed class PreparedImage
    {
        public required byte[] PngBytes { get; set; }
        public required byte[] Bgra32Bytes { get; set; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required bool WasResized { get; init; }
        public void ReleaseHeavyBuffers()
        {
            PngBytes = Array.Empty<byte>();
            Bgra32Bytes = Array.Empty<byte>();
        }
    }

    private sealed class EncodedTextureImage
    {
        public required byte[] EncodedBytes { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required TextureFormat FinalFormat { get; init; }
    }

    private sealed class RawTextAssetData
    {
        public required byte[] OriginalBytes { get; init; }
        public required bool BigEndian { get; init; }
        public required string Name { get; init; }
        public required string ContentText { get; init; }
        public required int ScriptLengthOffset { get; init; }
        public required int ScriptDataOffset { get; init; }
        public required int ScriptDataLength { get; init; }
        public required int ScriptAlignedEndOffset { get; init; }
    }

    private sealed class RawStreamingAssetsMappingMonoBehaviourData
    {
        public sealed class Entry
        {
            public required string FullLookupPath { get; init; }
            public required string Guid { get; init; }
            public required long FileLength { get; init; }
        }

        public required byte[] OriginalBytes { get; init; }
        public required bool BigEndian { get; init; }
        public required string ObjectName { get; init; }
        public required int EntriesArrayOffset { get; init; }
        public required int EntriesArrayEndOffset { get; init; }
        public required IReadOnlyList<Entry> Entries { get; init; }
    }

    public static SongBundleScanResult ScanBundle(string bundleFilePath)
    {
        ValidateBundlePath(bundleFilePath);
        var am = new AssetsManager();
        try
        {
            var bunInst = am.LoadBundleFile(bundleFilePath, unpackIfPacked: true);
            var textures = new List<(int bundleIndex, string assetsName, long pathId, string name, int w, int h)>();
            var materials = new List<(int bundleIndex, string assetsName, long pathId, string name)>();
            List<SongDatabaseSlotInfo>? slots = null;
            int songDbBundleIndex = -1;
            long songDbPathId = 0;
            string songDbAssetsName = "";

            int fileCount = bunInst.file.BlockAndDirInfo.DirectoryInfos.Count;
            for (int i = 0; i < fileCount; i++)
            {
                if (!bunInst.file.IsAssetsFile(i)) continue;
                var assetsInst = am.LoadAssetsFileFromBundle(bunInst, i, loadDeps: false);
                string assetsName = SafeGetBundleEntryName(bunInst.file, i);

                CollectTextureAndMaterialNames(am, assetsInst, i, assetsName, textures, materials);

                if (slots == null && TryReadSongDatabaseSlots(am, assetsInst, out var readSlots, out long pathId))
                {
                    slots = readSlots;
                    songDbBundleIndex = i;
                    songDbPathId = pathId;
                    songDbAssetsName = assetsName;
                }
            }

            if (slots == null || songDbBundleIndex < 0)
                throw new Exception("未找到可解析的 SongDatabase（MonoBehaviour）。");

            var matLookup = materials
                .GroupBy(x => (x.bundleIndex, x.name), SongNameKeyComparer.Instance)
                .ToDictionary(g => g.Key, g => g.First(), SongNameKeyComparer.Instance);

            var jacketTemplates = new List<JacketTemplateCandidate>();
            foreach (var t in textures)
            {
                if (!matLookup.TryGetValue((t.bundleIndex, t.name), out var m))
                    continue;

                jacketTemplates.Add(new JacketTemplateCandidate
                {
                    BundleFileIndex = t.bundleIndex,
                    AssetsFileName = t.assetsName,
                    TexturePathId = t.pathId,
                    MaterialPathId = m.pathId,
                    BaseName = t.name,
                    TextureWidth = t.w,
                    TextureHeight = t.h
                });
            }

            return new SongBundleScanResult
            {
                BundleFilePath = Path.GetFullPath(bundleFilePath),
                SongDatabaseBundleFileIndex = songDbBundleIndex,
                SongDatabasePathId = songDbPathId,
                SongDatabaseAssetsFileName = songDbAssetsName,
                Slots = slots,
                JacketTemplates = jacketTemplates.OrderBy(x => x.BaseName, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    public static NewSongPackExportResult ExportNewSongResources(NewSongPackRequest request)
    {
        ValidateRequest(request);
        var generatedFiles = BuildGeneratedResourceFiles(request);

        string bundleSrc = Path.GetFullPath(request.BundleFilePath);
        string sharedSrc = ResolveStreamingAssetsMappingHostPath(request.SharedAssetsFilePath);
        string outDir = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outDir);

        var bundlePlan = PrepareOutputPlan(bundleSrc, Path.Combine(outDir, Path.GetFileName(bundleSrc)), request.AutoRenameWhenTargetLocked);
        var sharedPlan = PrepareOutputPlan(sharedSrc, Path.Combine(outDir, Path.GetFileName(sharedSrc)), request.AutoRenameWhenTargetLocked);

        var jacket = LoadPreparedImage(request.JacketImageFilePath);
        long newTexPathId = 0;
        long newMatPathId = 0;
        string songDbArrayDiagnostics = "";
        SongDatabaseReadbackValidationResult? songDbReadback = null;
        try
        {
            (newTexPathId, newMatPathId, songDbArrayDiagnostics) = ExportModifiedBundle(request, bundlePlan, jacket);
            ExportModifiedSharedAssets(sharedSrc, sharedPlan, generatedFiles.Select(x => x.MappingEntry).ToList());
            FinalizeOutputPlan(bundlePlan);
            FinalizeOutputPlan(sharedPlan);
            try
            {
                songDbReadback = ReadBackExportedSongDatabase(bundlePlan.FinalOutputPath, request.SelectedSlot.SlotIndex);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"{ex.Message}\nSongDatabase 数组结构摘要（写前/写后）：\n{songDbArrayDiagnostics}",
                    ex);
            }

            foreach (var file in generatedFiles)
            {
                string outputPath = Path.Combine(outDir, file.MappingEntry.Guid);
                File.WriteAllBytes(outputPath, GameAssetPacker.EncryptBytesForGame(file.PlainBytes));
            }

            return new NewSongPackExportResult
            {
                OutputBundlePath = bundlePlan.FinalOutputPath,
                OutputSharedAssetsPath = sharedPlan.FinalOutputPath,
                NewTexturePathId = newTexPathId,
                NewMaterialPathId = newMatPathId,
                AddedMappingEntries = generatedFiles.Select(x => x.MappingEntry).ToList(),
                SongDatabaseReadback = songDbReadback ?? throw new Exception("导出后无法读取 SongDatabase 回读校验结果。"),
                SongDatabaseArrayStructureDiagnostics = songDbArrayDiagnostics,
                Summary = $"新增歌曲成功：{request.BaseName}，槽位 {request.SelectedSlot.SlotIndex}。新增映射 {generatedFiles.Count} 项。"
            };
        }
        finally
        {
            jacket.ReleaseHeavyBuffers();
        }
    }

    private static void ValidateRequest(NewSongPackRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        ValidateBundlePath(request.BundleFilePath);
        if (string.IsNullOrWhiteSpace(request.SharedAssetsFilePath) || !File.Exists(request.SharedAssetsFilePath))
            throw new FileNotFoundException($"sharedassets0.assets 不存在：{request.SharedAssetsFilePath}");
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new Exception("请选择导出文件夹。");
        ValidateImagePath(request.JacketImageFilePath);
        if (string.IsNullOrWhiteSpace(request.BgmFilePath) || !File.Exists(request.BgmFilePath))
            throw new FileNotFoundException($"BGM 文件不存在：{request.BgmFilePath}");
        if (request.SelectedSlot == null || !request.SelectedSlot.IsEmpty)
            throw new Exception("请选择空槽。");
        if (request.JacketTemplate == null)
            throw new Exception("请选择曲绘模板。");

        string baseName = request.BaseName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(baseName))
            throw new Exception("BaseName 不能为空。");
        if (!baseName.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'))
            throw new Exception("BaseName 仅允许字母、数字、下划线、连字符。");

        string bgmExt = Path.GetExtension(request.BgmFilePath).ToLowerInvariant();
        if (bgmExt is not ".ogg" and not ".wav")
            throw new Exception($"BGM 仅支持 .ogg/.wav：{request.BgmFilePath}");

        if (request.Charts == null || request.Charts.Count == 0)
            throw new Exception("请至少配置一个谱面。");

        var dupSlot = request.Charts.GroupBy(x => x.ChartSlotIndex).FirstOrDefault(g => g.Count() > 1);
        if (dupSlot != null)
            throw new Exception($"谱面槽位重复：{dupSlot.Key}");
        var dupDiff = request.Charts.GroupBy(x => x.DifficultyFlag).FirstOrDefault(g => g.Count() > 1);
        if (dupDiff != null)
            throw new Exception($"Difficulty 重复：{dupDiff.Key}");

        foreach (var chart in request.Charts)
        {
            if (chart.ChartSlotIndex < 0 || chart.ChartSlotIndex > 3)
                throw new Exception($"谱面槽位必须在 0-3：{chart.ChartSlotIndex}");
            if (chart.DifficultyFlag is not 1 and not 2 and not 4 and not 8)
                throw new Exception($"Difficulty 仅支持 1/2/4/8：{chart.DifficultyFlag}");
            if (string.IsNullOrWhiteSpace(chart.SourceChartFilePath) || !File.Exists(chart.SourceChartFilePath))
                throw new FileNotFoundException($"谱面文件不存在：{chart.SourceChartFilePath}");
            string chartExt = Path.GetExtension(chart.SourceChartFilePath).ToLowerInvariant();
            if (chartExt is not ".txt" and not ".spc")
                throw new Exception($"谱面文件仅支持 .txt/.spc：{chart.SourceChartFilePath}");
        }
    }

    private static List<GeneratedResourceFile> BuildGeneratedResourceFiles(NewSongPackRequest request)
    {
        var list = new List<GeneratedResourceFile>();

        // 游戏运行时按 BaseName.wav 查找 BGM 映射项，因此这里固定使用 .wav 作为 FullLookupPath。
        string bgmLookup = $"{request.BaseName}.wav";
        byte[] bgmBytes = File.ReadAllBytes(request.BgmFilePath);
        list.Add(new GeneratedResourceFile
        {
            PlainBytes = bgmBytes,
            MappingEntry = new NewSongMappingEntryResult
            {
                FullLookupPath = bgmLookup,
                Guid = ComputeGuidFromPathAndBytes(bgmLookup, bgmBytes),
                FileLength = bgmBytes.Length
            }
        });

        foreach (var chart in request.Charts.OrderBy(x => x.ChartSlotIndex))
        {
            string lookup = $"{request.BaseName}{chart.ChartSlotIndex}.spc";
            byte[] bytes = GameAssetPacker.ReadSourceBytesForPacking(chart.SourceChartFilePath);
            list.Add(new GeneratedResourceFile
            {
                PlainBytes = bytes,
                MappingEntry = new NewSongMappingEntryResult
                {
                    FullLookupPath = lookup,
                    Guid = ComputeGuidFromPathAndBytes(lookup, bytes),
                    FileLength = bytes.Length
                }
            });
        }

        var dupLookup = list.GroupBy(x => x.MappingEntry.FullLookupPath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dupLookup != null)
            throw new Exception($"生成的 FullLookupPath 重复：{dupLookup.Key}");
        return list;
    }

    private static string ComputeGuidFromPathAndBytes(string fullLookupPath, byte[] bytes)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(fullLookupPath.Replace('\\', '/'));
        byte[] combined = new byte[pathBytes.Length + bytes.Length];
        Buffer.BlockCopy(pathBytes, 0, combined, 0, pathBytes.Length);
        Buffer.BlockCopy(bytes, 0, combined, pathBytes.Length, bytes.Length);
        byte[] md5 = MD5.HashData(combined);
        return System.Convert.ToHexString(md5).ToLowerInvariant();
    }

    private static void CollectTextureAndMaterialNames(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        int bundleFileIndex,
        string assetsName,
        List<(int bundleIndex, string assetsName, long pathId, string name, int w, int h)> textures,
        List<(int bundleIndex, string assetsName, long pathId, string name)> materials)
    {
        foreach (var texInfo in assetsInst.file.GetAssetsOfType(AssetClassID.Texture2D))
        {
            try
            {
                var field = am.GetBaseField(assetsInst, texInfo, AssetReadFlags.None);
                var tex = TextureFile.ReadTextureFile(field);
                if (string.IsNullOrWhiteSpace(tex.m_Name)) continue;
                textures.Add((bundleFileIndex, assetsName, texInfo.PathId, tex.m_Name, tex.m_Width, tex.m_Height));
            }
            catch { }
        }

        foreach (var matInfo in assetsInst.file.GetAssetsOfType(AssetClassID.Material))
        {
            try
            {
                var field = am.GetBaseField(assetsInst, matInfo, AssetReadFlags.None);
                string name = TryReadStringField(field, "m_Name") ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                materials.Add((bundleFileIndex, assetsName, matInfo.PathId, name));
            }
            catch { }
        }
    }

    private static bool TryReadSongDatabaseSlots(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        out List<SongDatabaseSlotInfo> slots,
        out long songDbPathId)
    {
        slots = new List<SongDatabaseSlotInfo>();
        songDbPathId = 0;

        foreach (var monoInfo in assetsInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            try
            {
                var baseField = am.GetBaseField(assetsInst, monoInfo, AssetReadFlags.None);
                string name = TryReadStringField(baseField, "m_Name") ?? "";
                if (!string.Equals(name, "SongDatabase", StringComparison.OrdinalIgnoreCase))
                    continue;

                slots = ReadSongDatabaseSlots(baseField);
                songDbPathId = monoInfo.PathId;
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static List<SongDatabaseSlotInfo> ReadSongDatabaseSlots(AssetTypeValueField songDbBaseField)
    {
        var allSongInfo = RequireField(songDbBaseField, "allSongInfo");
        var arrayField = RequireArrayField(allSongInfo);
        var elements = GetArrayElements(arrayField);
        var slots = new List<SongDatabaseSlotInfo>(elements.Count);
        for (int i = 0; i < elements.Count; i++)
        {
            var slot = UnwrapDataField(elements[i]);
            ushort id = (ushort)Math.Max(0, TryReadNumberField(slot, "Id", "Value") ?? 0);
            string baseName = TryReadStringField(slot, "BaseName") ?? "";
            int chartCount = 0;
            if (TryGetField(slot, "ChartInfos", out var chartInfos))
            {
                try { chartCount = GetArrayElements(RequireArrayField(chartInfos)).Count; } catch { }
            }
            string displayNameIndicator =
                TryReadStringField(slot, "DisplayNameSectionIndicator")
                ?? (TryReadNumberField(slot, "DisplayNameSectionIndicator")?.ToString() ?? "");
            string displayArtistIndicator =
                TryReadStringField(slot, "DisplayArtistSectionIndicator")
                ?? (TryReadNumberField(slot, "DisplayArtistSectionIndicator")?.ToString() ?? "");
            int gameplayBackground = (int)(TryReadNumberField(slot, "GameplayBackground") ?? 0);
            int rewardStyle = (int)(TryReadNumberField(slot, "RewardStyle") ?? 0);
            bool isEmpty = id == 0 && string.IsNullOrWhiteSpace(baseName) && chartCount == 0;
            slots.Add(new SongDatabaseSlotInfo
            {
                SlotIndex = i,
                IsEmpty = isEmpty,
                SongIdValue = id,
                BaseName = baseName,
                ChartCount = chartCount,
                DisplayNameSectionIndicator = displayNameIndicator,
                DisplayArtistSectionIndicator = displayArtistIndicator,
                GameplayBackground = gameplayBackground,
                RewardStyle = rewardStyle
            });
        }
        return slots;
    }

    private static SongDatabaseReadbackValidationResult ReadBackExportedSongDatabase(string bundleFilePath, int slotIndex)
    {
        var am = new AssetsManager();
        var diagnostics = new List<string>();
        try
        {
            var bunInst = am.LoadBundleFile(bundleFilePath, unpackIfPacked: true);
            int fileCount = bunInst.file.BlockAndDirInfo.DirectoryInfos.Count;
            for (int i = 0; i < fileCount; i++)
            {
                if (!bunInst.file.IsAssetsFile(i)) continue;
                var assetsInst = am.LoadAssetsFileFromBundle(bunInst, i, loadDeps: false);
                if (TryReadSongDatabaseReadback(am, assetsInst, slotIndex, out var readback, out var diag))
                    return readback!;
                if (!string.IsNullOrWhiteSpace(diag))
                    diagnostics.Add($"[{i}] {diag}");
            }

            string detail = diagnostics.Count > 0
                ? "\n内部诊断：\n" + string.Join("\n", diagnostics)
                : "";
            throw new Exception("导出后回读校验失败：未找到可解析的 SongDatabase。" + detail);
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    private static bool TryReadSongDatabaseReadback(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        int slotIndex,
        out SongDatabaseReadbackValidationResult? readback,
        out string? diagnostic)
    {
        readback = null;
        diagnostic = null;
        var errors = new List<string>();
        foreach (var monoInfo in assetsInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            try
            {
                var baseField = am.GetBaseField(assetsInst, monoInfo, AssetReadFlags.None);
                string name = TryReadStringField(baseField, "m_Name") ?? "";
                if (!string.Equals(name, "SongDatabase", StringComparison.OrdinalIgnoreCase))
                    continue;

                readback = ReadSongDatabaseSlotReadback(baseField, slotIndex);
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"PathID={monoInfo.PathId}: {ex.Message}");
            }
        }
        if (errors.Count > 0)
            diagnostic = string.Join(" | ", errors.Take(4));
        return false;
    }

    private static SongDatabaseReadbackValidationResult ReadSongDatabaseSlotReadback(AssetTypeValueField songDbBaseField, int slotIndex)
    {
        var allSongInfo = RequireField(songDbBaseField, "allSongInfo");
        var arrayField = RequireArrayField(allSongInfo);
        var slots = GetArrayElements(arrayField);
        if (slotIndex < 0 || slotIndex >= slots.Count)
            throw new Exception($"导出后回读校验失败：槽位索引越界 {slotIndex}/{slots.Count}");

        var slot = UnwrapDataField(slots[slotIndex]);
        int songId = (int)(TryReadNumberField(slot, "Id", "Value") ?? -1);
        string baseName = TryReadStringField(slot, "BaseName") ?? "";

        var chartInfosField = TryGetField(slot, "ChartInfos", out var chartInfos) ? chartInfos : null;
        if (chartInfosField == null)
            throw new Exception("导出后回读校验失败：未找到 SongDatabase.ChartInfos 字段。");

        var chartElems = GetArrayElements(RequireArrayField(chartInfosField));
        var chartReadbacks = new List<SongDatabaseReadbackValidationResult.ChartInfo>(chartElems.Count);
        for (int i = 0; i < chartElems.Count; i++)
        {
            var elem = UnwrapDataField(chartElems[i]);
            string chartId = TryReadStringField(elem, "Id") ?? "";
            int diff = (int)(TryReadNumberField(elem, "Difficulty") ?? -1);
            int available = (int)(TryReadNumberField(elem, "Available") ?? -1);
            chartReadbacks.Add(new SongDatabaseReadbackValidationResult.ChartInfo
            {
                Index = i,
                Id = chartId,
                Difficulty = diff,
                Available = available
            });
        }

        var songIdJacketEntries = ReadSongIdJacketMaterialReadbacks(songDbBaseField, songId);
        var chartIdJacketEntries = ReadChartIdJacketMaterialReadbacks(songDbBaseField, baseName);

        return new SongDatabaseReadbackValidationResult
        {
            SlotIndex = slotIndex,
            SongId = songId,
            BaseName = baseName,
            ChartInfos = chartReadbacks,
            SongIdJacketMaterials = songIdJacketEntries,
            ChartIdJacketMaterials = chartIdJacketEntries
        };
    }

    private static List<SongDatabaseReadbackValidationResult.SongIdJacketMaterialEntry> ReadSongIdJacketMaterialReadbacks(
        AssetTypeValueField songDbBaseField,
        int targetSongId)
    {
        var result = new List<SongDatabaseReadbackValidationResult.SongIdJacketMaterialEntry>();
        if (!TryGetField(songDbBaseField, "songIdJacketMaterials", out var owner))
            return result;

        foreach (var rawElem in GetArrayElements(RequireArrayField(owner)))
        {
            var elem = UnwrapDataField(rawElem);
            int songId = (int)(TryReadNumberField(elem, "SongId", "Value") ?? -1);
            if (songId != targetSongId)
                continue;

            result.Add(new SongDatabaseReadbackValidationResult.SongIdJacketMaterialEntry
            {
                SongId = songId,
                FileId = (int)ReadPPtrFileId(elem, "JacketMaterial"),
                PathId = ReadPPtrPathId(elem, "JacketMaterial")
            });
        }

        return result;
    }

    private static List<SongDatabaseReadbackValidationResult.ChartIdJacketMaterialEntry> ReadChartIdJacketMaterialReadbacks(
        AssetTypeValueField songDbBaseField,
        string baseName)
    {
        var result = new List<SongDatabaseReadbackValidationResult.ChartIdJacketMaterialEntry>();
        if (string.IsNullOrWhiteSpace(baseName))
            return result;
        if (!TryGetField(songDbBaseField, "chartIdJacketMaterials", out var owner))
            return result;

        var expected = new HashSet<string>(
            Enumerable.Range(0, 4).Select(i => $"{baseName}{i}"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var rawElem in GetArrayElements(RequireArrayField(owner)))
        {
            var elem = UnwrapDataField(rawElem);
            string chartId = TryReadStringField(elem, "ChartId") ?? "";
            if (!expected.Contains(chartId))
                continue;

            result.Add(new SongDatabaseReadbackValidationResult.ChartIdJacketMaterialEntry
            {
                ChartId = chartId,
                FileId = (int)ReadPPtrFileId(elem, "JacketMaterial"),
                PathId = ReadPPtrPathId(elem, "JacketMaterial")
            });
        }

        return result.OrderBy(x => x.ChartId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static long ReadPPtrFileId(AssetTypeValueField parent, string ptrFieldName)
        => TryReadNumberField(parent, ptrFieldName, "m_FileID")
           ?? TryReadNumberField(parent, ptrFieldName, "m_FileId")
           ?? -1;

    private static long ReadPPtrPathId(AssetTypeValueField parent, string ptrFieldName)
        => TryReadNumberField(parent, ptrFieldName, "m_PathID")
           ?? TryReadNumberField(parent, ptrFieldName, "m_PathId")
           ?? 0;

    private sealed class SongNameKeyComparer : IEqualityComparer<(int, string)>
    {
        public static SongNameKeyComparer Instance { get; } = new();
        public bool Equals((int, string) x, (int, string) y)
            => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((int, string) obj)
            => HashCode.Combine(obj.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2 ?? ""));
    }

    private static (long newTexturePathId, long newMaterialPathId, string songDbArrayDiagnostics) ExportModifiedBundle(
        NewSongPackRequest request,
        OutputPlan outputPlan,
        PreparedImage jacketImage)
    {
        var am = new AssetsManager();
        try
        {
            var scan = ScanBundle(request.BundleFilePath);
            var bunInst = am.LoadBundleFile(request.BundleFilePath, unpackIfPacked: true);
            var modifiedIndices = new HashSet<int>();

            // 1) 复制并新增曲绘资源（Texture2D + Material）。
            var jacketAssetsInst = am.LoadAssetsFileFromBundle(bunInst, request.JacketTemplate.BundleFileIndex, loadDeps: false);
            long newTexturePathId = AddNewJacketTexture(
                am,
                jacketAssetsInst,
                request.JacketTemplate,
                request.BaseName,
                jacketImage,
                request.KeepJacketOriginalSize);
            long newMaterialPathId = AddNewJacketMaterial(am, jacketAssetsInst, request.JacketTemplate, request.BaseName, newTexturePathId);
            modifiedIndices.Add(request.JacketTemplate.BundleFileIndex);

            // 2) 写入 SongDatabase 空槽（可能在不同 assets 文件中）。
            var songDbAssetsInst = am.LoadAssetsFileFromBundle(bunInst, scan.SongDatabaseBundleFileIndex, loadDeps: false);
            string songDbArrayDiagnostics = ApplySongDatabaseEdit(am, songDbAssetsInst, scan.SongDatabasePathId, request, newMaterialPathId);
            modifiedIndices.Add(scan.SongDatabaseBundleFileIndex);

            // 3) 将被修改的 assets 文件挂到 bundle 目录 Replacer。
            foreach (int idx in modifiedIndices)
            {
                var inst = am.LoadAssetsFileFromBundle(bunInst, idx, loadDeps: false);
                bunInst.file.BlockAndDirInfo.DirectoryInfos[idx].Replacer = new ContentReplacerFromAssets(inst);
            }

            WriteBundleApplyingReplacers(bunInst.file, bunInst.originalCompression, outputPlan.PackOutputPath);
            return (newTexturePathId, newMaterialPathId, songDbArrayDiagnostics);
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    private static void ExportModifiedSharedAssets(
        string sharedAssetsSourcePath,
        OutputPlan outputPlan,
        IReadOnlyList<NewSongMappingEntryResult> mappingEntries)
    {
        var am = new AssetsManager();
        try
        {
            var assetsInst = am.LoadAssetsFile(sharedAssetsSourcePath, loadDeps: true);
            PrepareAssetsManagerForBaseFieldReading(am, assetsInst);
            var mappingAssetInfo = FindStreamingAssetsMappingMonoBehaviourAsset(assetsInst)
                ?? throw new Exception($"未在 {Path.GetFileName(sharedAssetsSourcePath)} 中找到 StreamingAssetsMapping（MonoBehaviour）。");

            var rawMapping = TryReadRawStreamingAssetsMappingMonoBehaviour(assetsInst, mappingAssetInfo)
                ?? throw new Exception("无法解析 StreamingAssetsMapping（MonoBehaviour）原始内容。");

            byte[] updatedRaw = BuildUpdatedRawStreamingAssetsMappingMonoBehaviour(rawMapping, mappingEntries);
            mappingAssetInfo.SetNewData(updatedRaw);

            WriteAssetsFileApplyingReplacers(assetsInst.file, outputPlan.PackOutputPath);
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    private static string ResolveStreamingAssetsMappingHostPath(string preferredAssetsPath)
    {
        if (string.IsNullOrWhiteSpace(preferredAssetsPath) || !File.Exists(preferredAssetsPath))
            throw new FileNotFoundException($"sharedassets0.assets 不存在：{preferredAssetsPath}");

        string preferred = Path.GetFullPath(preferredAssetsPath);
        string dir = Path.GetDirectoryName(preferred) ?? "";

        var candidates = new List<string> { preferred };
        try
        {
            var siblings = Directory.EnumerateFiles(dir, "*.assets", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Where(p => !string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
            candidates.AddRange(siblings);
        }
        catch
        {
            // 忽略目录扫描失败，至少尝试用户手动选择的文件。
        }

        var am = new AssetsManager();
        try
        {
            foreach (var path in candidates)
            {
                try
                {
                    var assetsInst = am.LoadAssetsFile(path, loadDeps: true);
                    PrepareAssetsManagerForBaseFieldReading(am, assetsInst);
                    if (FindStreamingAssetsMappingMonoBehaviourAsset(assetsInst) != null)
                        return path;
                }
                catch
                {
                    // 个别 assets 文件不是目标格式或依赖缺失时继续扫描下一个。
                }
            }
        }
        finally
        {
            am.UnloadAll(true);
        }

        throw new Exception(
            $"未在目录中找到 StreamingAssetsMapping（MonoBehaviour）。\n已扫描目录：{dir}\n建议确认选择了包含整个游戏资源 assets 文件的目录。");
    }

    private static AssetFileInfo? FindStreamingAssetsMappingTextAsset(AssetsManager am, AssetsFileInstance assetsInst)
    {
        AssetFileInfo? contentFallback = null;
        foreach (var info in assetsInst.file.GetAssetsOfType(AssetClassID.TextAsset))
        {
            try
            {
                var raw = TryReadRawTextAssetData(assetsInst, info);
                if (raw != null)
                {
                    if (string.Equals(raw.Name, "StreamingAssetsMapping.json", StringComparison.OrdinalIgnoreCase))
                        return info;
                    if (contentFallback == null && LooksLikeStreamingAssetsMappingJson(raw.ContentText))
                        contentFallback = info;
                    continue;
                }

                // 原始字节解析失败时再尝试类型树路径（可能触发底层库首发异常，但已被外层捕获）。
                var baseField = TryGetBaseFieldSafe(am, assetsInst, info);
                if (baseField == null) continue;
                string name = TryReadStringField(baseField, "m_Name") ?? "";
                if (string.Equals(name, "StreamingAssetsMapping.json", StringComparison.OrdinalIgnoreCase))
                    return info;

                if (contentFallback == null && LooksLikeStreamingAssetsMappingJson(baseField))
                    contentFallback = info;
            }
            catch { }
        }
        return contentFallback;
    }

    private static AssetFileInfo? FindStreamingAssetsMappingMonoBehaviourAsset(AssetsFileInstance assetsInst)
    {
        foreach (var info in assetsInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            try
            {
                var raw = TryReadRawStreamingAssetsMappingMonoBehaviour(assetsInst, info);
                if (raw != null && string.Equals(raw.ObjectName, "StreamingAssetsMapping", StringComparison.OrdinalIgnoreCase))
                    return info;
            }
            catch
            {
            }
        }
        return null;
    }

    private static AssetTypeValueField? TryGetBaseFieldSafe(AssetsManager am, AssetsFileInstance assetsInst, AssetFileInfo info)
    {
        try
        {
            return am.GetBaseField(assetsInst, info, AssetReadFlags.None);
        }
        catch
        {
            return null;
        }
    }

    private static void PrepareAssetsManagerForBaseFieldReading(AssetsManager am, AssetsFileInstance assetsInst)
    {
        // 当前项目运行环境未提供 classdata.tpk，调用 LoadClassDatabaseFromPackage 会在 AssetsTools.NET 内部抛空引用。
        // 为避免 VS 在“引发异常时中断”，sharedassets 的 Mapping 读取统一走 TextAsset 原始字节回退方案。
        _ = am;
        _ = assetsInst;
    }

    private static bool LooksLikeStreamingAssetsMappingJson(AssetTypeValueField baseField)
    {
        try
        {
            string text = ReadTextAssetContent(baseField);
            return LooksLikeStreamingAssetsMappingJson(text);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeStreamingAssetsMappingJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("FullLookupPath", StringComparison.Ordinal) &&
               text.Contains("Guid", StringComparison.Ordinal) &&
               text.Contains("FileLength", StringComparison.Ordinal);
    }

    private static RawStreamingAssetsMappingMonoBehaviourData? TryReadRawStreamingAssetsMappingMonoBehaviour(AssetsFileInstance assetsInst, AssetFileInfo info)
    {
        try
        {
            if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                return null;

            var fileReader = assetsInst.file.Reader;
            if (fileReader == null) return null;
            bool bigEndian = fileReader.BigEndian;

            long prevPos = fileReader.Position;
            byte[] rawBytes;
            try
            {
                long absOffset = info.GetAbsoluteByteOffset(assetsInst.file);
                fileReader.Position = absOffset;
                rawBytes = ReadExactBytes(fileReader.BaseStream, checked((int)info.ByteSize));
            }
            finally
            {
                try { fileReader.Position = prevPos; } catch { }
            }

            using var ms = new MemoryStream(rawBytes, writable: false);
            using var r = new AssetsFileReader(ms) { BigEndian = bigEndian };

            // MonoBehaviour 基础字段：m_GameObject / m_Enabled / m_Script / m_Name
            _ = r.ReadInt32(); // m_GameObject.m_FileID
            _ = r.ReadInt64(); // m_GameObject.m_PathID
            if (r.BaseStream.ReadByte() < 0) return null; // m_Enabled
            r.Align();
            _ = r.ReadInt32(); // m_Script.m_FileID
            _ = r.ReadInt64(); // m_Script.m_PathID
            string objectName = r.ReadCountStringInt32();
            r.Align();

            int entriesArrayOffset = checked((int)r.Position);
            int count = r.ReadInt32();
            if (count < 0 || count > 2_000_000)
                return null;

            var entries = new List<RawStreamingAssetsMappingMonoBehaviourData.Entry>(Math.Min(count, 1024));
            for (int i = 0; i < count; i++)
            {
                string fullLookupPath = r.ReadCountStringInt32();
                r.Align();
                string guid = r.ReadCountStringInt32();
                r.Align();
                long fileLength = r.ReadInt64();
                entries.Add(new RawStreamingAssetsMappingMonoBehaviourData.Entry
                {
                    FullLookupPath = fullLookupPath,
                    Guid = guid,
                    FileLength = fileLength
                });
            }

            int entriesArrayEndOffset = checked((int)r.Position);
            return new RawStreamingAssetsMappingMonoBehaviourData
            {
                OriginalBytes = rawBytes,
                BigEndian = bigEndian,
                ObjectName = objectName,
                EntriesArrayOffset = entriesArrayOffset,
                EntriesArrayEndOffset = entriesArrayEndOffset,
                Entries = entries
            };
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildUpdatedRawStreamingAssetsMappingMonoBehaviour(
        RawStreamingAssetsMappingMonoBehaviourData raw,
        IReadOnlyList<NewSongMappingEntryResult> newEntries)
    {
        var merged = new List<RawStreamingAssetsMappingMonoBehaviourData.Entry>(raw.Entries.Count + newEntries.Count);
        merged.AddRange(raw.Entries);

        var existing = new HashSet<string>(
            raw.Entries.Select(x => (x.FullLookupPath ?? "").Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var e in newEntries)
        {
            string path = (e.FullLookupPath ?? "").Replace('\\', '/');
            if (!existing.Add(path))
                throw new Exception($"StreamingAssetsMapping 中已存在 FullLookupPath：{path}");

            merged.Add(new RawStreamingAssetsMappingMonoBehaviourData.Entry
            {
                FullLookupPath = path,
                Guid = e.Guid ?? "",
                FileLength = e.FileLength
            });
        }

        using var ms = new MemoryStream(raw.OriginalBytes.Length + Math.Max(1024, newEntries.Count * 96));
        ms.Write(raw.OriginalBytes, 0, raw.EntriesArrayOffset);

        using (var w = new AssetsFileWriter(ms) { BigEndian = raw.BigEndian })
        {
            w.Write(merged.Count);
            foreach (var item in merged)
            {
                w.WriteCountStringInt32(item.FullLookupPath ?? "");
                w.Align();
                w.WriteCountStringInt32(item.Guid ?? "");
                w.Align();
                w.Write(item.FileLength);
            }
        }

        if (raw.EntriesArrayEndOffset < raw.OriginalBytes.Length)
            ms.Write(raw.OriginalBytes, raw.EntriesArrayEndOffset, raw.OriginalBytes.Length - raw.EntriesArrayEndOffset);

        return ms.ToArray();
    }

    private static RawTextAssetData? TryReadRawTextAssetData(AssetsFileInstance assetsInst, AssetFileInfo info)
    {
        try
        {
            var fileReader = assetsInst.file.Reader;
            if (fileReader == null) return null;
            bool bigEndian = fileReader.BigEndian;

            long prevPos = fileReader.Position;
            byte[] rawBytes;
            try
            {
                long absOffset = info.GetAbsoluteByteOffset(assetsInst.file);
                fileReader.Position = absOffset;
                rawBytes = ReadExactBytes(fileReader.BaseStream, checked((int)info.ByteSize));
            }
            finally
            {
                try { fileReader.Position = prevPos; } catch { }
            }

            using var ms = new MemoryStream(rawBytes, writable: false);
            using var r = new AssetsFileReader(ms) { BigEndian = bigEndian };

            string name = r.ReadCountStringInt32();
            r.Align();

            int scriptLengthOffset = checked((int)r.Position);
            int scriptLen = r.ReadInt32();
            if (scriptLen < 0 || scriptLen > ms.Length - r.Position)
                return null;

            int scriptDataOffset = checked((int)r.Position);
            byte[] scriptBytes = ReadExactBytes(r.BaseStream, scriptLen);
            r.Align();
            int scriptAlignedEndOffset = checked((int)r.Position);

            return new RawTextAssetData
            {
                OriginalBytes = rawBytes,
                BigEndian = bigEndian,
                Name = name,
                ContentText = DecodeTextAssetBytes(scriptBytes),
                ScriptLengthOffset = scriptLengthOffset,
                ScriptDataOffset = scriptDataOffset,
                ScriptDataLength = scriptLen,
                ScriptAlignedEndOffset = scriptAlignedEndOffset
            };
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildUpdatedRawTextAssetBytes(RawTextAssetData raw, string updatedText)
    {
        byte[] newScript = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(updatedText);
        int alignedScriptLen = Align4(newScript.Length);
        int padding = alignedScriptLen - newScript.Length;

        using var ms = new MemoryStream(raw.OriginalBytes.Length - raw.ScriptDataLength + alignedScriptLen + 16);
        ms.Write(raw.OriginalBytes, 0, raw.ScriptLengthOffset);

        using (var writer = new AssetsFileWriter(ms) { BigEndian = raw.BigEndian })
        {
            writer.Write(newScript.Length);
        }

        ms.Write(newScript, 0, newScript.Length);
        if (padding > 0)
            ms.Write(new byte[padding], 0, padding);

        if (raw.ScriptAlignedEndOffset < raw.OriginalBytes.Length)
            ms.Write(raw.OriginalBytes, raw.ScriptAlignedEndOffset, raw.OriginalBytes.Length - raw.ScriptAlignedEndOffset);

        return ms.ToArray();
    }

    private static byte[] ReadExactBytes(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int readTotal = 0;
        while (readTotal < count)
        {
            int read = stream.Read(buffer, readTotal, count - readTotal);
            if (read <= 0)
                throw new EndOfStreamException("读取资源字节时提前到达流末尾。");
            readTotal += read;
        }
        return buffer;
    }

    private static string DecodeTextAssetBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static long AddNewJacketTexture(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        JacketTemplateCandidate template,
        string newBaseName,
        PreparedImage jacketImage,
        bool keepOriginalSize)
    {
        var templateInfo = assetsInst.file.GetAssetInfo(template.TexturePathId)
            ?? throw new Exception($"未找到曲绘模板 Texture2D：{template.TexturePathId}");
        var baseField = am.GetBaseField(assetsInst, templateInfo, AssetReadFlags.None);
        var texture = TextureFile.ReadTextureFile(baseField);
        texture.m_Name = newBaseName;

        var encoded = EncodeReplacementImage(
            jacketImage,
            texture.m_TextureFormat,
            texture.m_Width,
            texture.m_Height,
            keepOriginalSize);
        ApplyEncodedTextureData(texture, encoded);
        texture.WriteTo(baseField);
        ForceApplyEncodedTextureDataToBaseField(baseField, encoded);
        RequireSetStringField(baseField, "m_Name", newBaseName);

        return AddClonedAssetFromTemplate(assetsInst.file, templateInfo, baseField);
    }

    private static long AddNewJacketMaterial(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        JacketTemplateCandidate template,
        string newBaseName,
        long newTexturePathId)
    {
        var templateInfo = assetsInst.file.GetAssetInfo(template.MaterialPathId)
            ?? throw new Exception($"未找到曲绘模板 Material：{template.MaterialPathId}");
        var baseField = am.GetBaseField(assetsInst, templateInfo, AssetReadFlags.None);
        RequireSetStringField(baseField, "m_Name", newBaseName);
        int changed = RewriteMaterialTextureReference(baseField, newTexturePathId);
        if (changed <= 0)
            throw new Exception("复制曲绘材质失败：未找到可写入的 m_Texture 引用。");
        return AddClonedAssetFromTemplate(assetsInst.file, templateInfo, baseField);
    }

    private static string ApplySongDatabaseEdit(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        long songDbPathId,
        NewSongPackRequest request,
        long newJacketMaterialPathId)
    {
        var info = assetsInst.file.GetAssetInfo(songDbPathId)
            ?? throw new Exception($"未找到 SongDatabase：PathID={songDbPathId}");
        var baseField = am.GetBaseField(assetsInst, info, AssetReadFlags.None);
        string beforeSummary = SafeBuildSongDatabaseArrayStructureSummary(baseField, request.SelectedSlot.SlotIndex, "写前");

        try
        {
            // 兼容旧测试版本导出的污染数据：若曲绘索引表已存在重复键，先做一次去重修复再继续写入。
            DeduplicateSongDataJacketLookups(baseField);
            ValidateSongDataJacketLookupKeysUnique(baseField, "去重后");

            var allSongInfo = RequireField(baseField, "allSongInfo");
            var allSongInfoArray = RequireArrayField(allSongInfo);
            var slots = GetArrayElements(allSongInfoArray);
            if (request.SelectedSlot.SlotIndex < 0 || request.SelectedSlot.SlotIndex >= slots.Count)
                throw new Exception($"空槽索引超出范围：{request.SelectedSlot.SlotIndex}/{slots.Count}");

            var targetSlot = slots[request.SelectedSlot.SlotIndex];
            // 空槽通常没有 ChartInfos 元素模板。直接在空槽上扩数组可能生成不完整字段树，导致 MonoBehaviour 序列化后损坏。
            // 先用一条已有曲目的 SongInfo 克隆覆盖目标空槽，再在克隆结果上改字段，保持字段结构完整。
            if (GetSongInfoChartCount(targetSlot) <= 0 && request.Charts.Count > 0)
            {
                var songInfoTemplate = FindSongInfoTemplate(slots)
                    ?? throw new Exception("未找到可用的 SongInfo 模板（无法写入 SongDatabase）。");
                targetSlot = ReplaceArrayElementWithTemplateClone(allSongInfoArray, request.SelectedSlot.SlotIndex, songInfoTemplate);
                slots = GetArrayElements(allSongInfoArray);
            }

            WriteSongInfoSlot(targetSlot, request, slots);
            ValidateSongDataJacketLookupKeysUnique(baseField, "写入 SongInfo 后");
            WriteSongDataJacketMaterialLookups(baseField, request, newJacketMaterialPathId);
            ValidateSongDataSongIdsUnique(slots);
            ValidateSongDataJacketLookupKeysUnique(baseField, "写入曲绘索引后");

            string afterSummary = SafeBuildSongDatabaseArrayStructureSummary(baseField, request.SelectedSlot.SlotIndex, "写后");
            info.SetNewData(baseField);
            return beforeSummary + "\n" + afterSummary;
        }
        catch (Exception ex)
        {
            string afterSummary = SafeBuildSongDatabaseArrayStructureSummary(baseField, request.SelectedSlot.SlotIndex, "异常时当前结构");
            throw new Exception($"{ex.Message}\n{beforeSummary}\n{afterSummary}", ex);
        }
    }

    private static void WriteSongInfoSlot(
        AssetTypeValueField slotField,
        NewSongPackRequest request,
        IReadOnlyList<AssetTypeValueField> allSlots)
    {
        slotField = UnwrapDataField(slotField);
        if (TryGetField(slotField, "Id", out var idField))
            RequireSetNumberField(idField, "Value", request.SelectedSlot.SlotIndex);

        RequireSetStringField(slotField, "BaseName", request.BaseName);
        TrySetFloatOrDoubleField(slotField, "PreviewStartSeconds", request.PreviewStartSeconds);
        TrySetFloatOrDoubleField(slotField, "PreviewEndSeconds", request.PreviewEndSeconds);

        if (!TrySetStringField(slotField, "DisplayNameSectionIndicator", request.DisplayNameSectionIndicator))
            TrySetNumberField(slotField, "DisplayNameSectionIndicator", ParseMaybeInt(request.DisplayNameSectionIndicator));
        if (!TrySetStringField(slotField, "DisplayArtistSectionIndicator", request.DisplayArtistSectionIndicator))
            TrySetNumberField(slotField, "DisplayArtistSectionIndicator", ParseMaybeInt(request.DisplayArtistSectionIndicator));

        RequireSetNumberField(slotField, "GameplayBackground", request.GameplayBackground);
        RequireSetNumberField(slotField, "RewardStyle", request.RewardStyle);

        var chartInfos = RequireField(slotField, "ChartInfos");
        var chartInfosArray = RequireArrayField(chartInfos);
        var chartBySlot = request.Charts.ToDictionary(x => x.ChartSlotIndex);
        byte[] defaultDifficultyBySlot = { 1, 2, 4, 8 };
        var existingChartElems = GetArrayElements(chartInfosArray);

        // 优先使用克隆槽位自带的 4 个难度条目模板，原地覆盖字段，避免数组重建导致字段树损坏/串位。
        if (existingChartElems.Count >= 4)
        {
            for (int slotIndex = 0; slotIndex < 4; slotIndex++)
            {
                chartBySlot.TryGetValue(slotIndex, out var chart);
                WriteChartInfoElement(existingChartElems[slotIndex], request.BaseName, slotIndex, chart, defaultDifficultyBySlot[slotIndex]);
            }

            // 若模板条目超过 4，仍强制裁剪到 4 项，避免游戏按固定难度位读取时出现额外脏数据。
            if (existingChartElems.Count != 4)
            {
                ReplaceArrayElements(chartInfosArray, existingChartElems.Take(4).ToList());
            }
            return;
        }

        // 兼容极端情况：模板没有完整 4 难度结构时，退回数组重建。
        var chartTemplate = FindChartInfoTemplate(allSlots)
            ?? throw new Exception("未找到可用的 SongChartInfo 模板。");
        var newCharts = new List<AssetTypeValueField>(4);
        for (int slotIndex = 0; slotIndex < 4; slotIndex++)
        {
            chartBySlot.TryGetValue(slotIndex, out var chart);
            var elem = chartTemplate.Clone();
            WriteChartInfoElement(elem, request.BaseName, slotIndex, chart, defaultDifficultyBySlot[slotIndex]);
            newCharts.Add(elem);
        }
        ReplaceArrayElements(chartInfosArray, newCharts);
    }

    private static void WriteChartInfoElement(
        AssetTypeValueField elem,
        string baseName,
        int chartSlotIndex,
        NewSongChartPackItem? chart,
        byte fallbackDifficultyFlag)
    {
        elem = UnwrapDataField(elem);
        bool exists = chart != null;
        if (!exists)
        {
            // 缺失难度位使用“空条目”风格：不保留模板里的 Id/Difficulty 等值，避免被游戏误判为存在谱面。
            RequireSetStringField(elem, "Id", "");
            RequireSetNumberField(elem, "Available", 0);
            RequireSetNumberField(elem, "Difficulty", 0);
            RequireSetStringField(elem, "DisplayChartDesigner", "");
            RequireSetStringField(elem, "DisplayJacketDesigner", "");
            RequireSetNumberField(elem, "Rating", 0);
            RequireSetStringField(elem, "LevelSectionIndicator", "");
            return;
        }

        RequireSetStringField(elem, "Id", $"{baseName}{chartSlotIndex}");
        RequireSetNumberField(elem, "Available", chart!.Available);
        RequireSetNumberField(elem, "Difficulty", chart.DifficultyFlag);
        RequireSetStringField(elem, "DisplayChartDesigner", chart.DisplayChartDesigner ?? "");
        RequireSetStringField(elem, "DisplayJacketDesigner", chart.DisplayJacketDesigner ?? "");
        RequireSetNumberField(elem, "Rating", chart.Rating);
        RequireSetStringField(elem, "LevelSectionIndicator", chart.LevelSectionIndicator ?? "");
    }

    private static void WriteSongDataJacketMaterialLookups(
        AssetTypeValueField songDbBaseField,
        NewSongPackRequest request,
        long newJacketMaterialPathId)
    {
        UpsertSongIdJacketMaterialEntry(songDbBaseField, request.SelectedSlot.SlotIndex, newJacketMaterialPathId);

        // 仅为实际导入的谱面难度建立 chartId -> jacket 索引。未导入难度会写成空条目（Id=""），不应保留伪造 chartId。
        foreach (int chartSlotIndex in request.Charts.Select(x => x.ChartSlotIndex).Distinct().OrderBy(x => x))
        {
            UpsertChartIdJacketMaterialEntry(songDbBaseField, $"{request.BaseName}{chartSlotIndex}", newJacketMaterialPathId);
        }
    }

    private static void UpsertChartIdJacketMaterialEntry(AssetTypeValueField songDbBaseField, string chartId, long materialPathId)
    {
        var owner = RequireField(songDbBaseField, "chartIdJacketMaterials");
        var array = RequireArrayField(owner);
        var entries = GetArrayElements(array);
        var list = entries.Select(x => x.Clone()).ToList();

        int foundIndex = -1;
        for (int i = 0; i < list.Count; i++)
        {
            string existing = TryReadStringField(list[i], "ChartId")
                              ?? TryReadStringField(list[i], "data", "ChartId")
                              ?? "";
            if (string.Equals(existing, chartId, StringComparison.OrdinalIgnoreCase))
            {
                foundIndex = i;
                break;
            }
        }

        AssetTypeValueField entry = foundIndex >= 0
            ? list[foundIndex]
            : CloneArrayEntryTemplate(array, entries, "chartIdJacketMaterials");

        SetEntryStringField(entry, "ChartId", chartId);
        SetEntryPPtrField(entry, "JacketMaterial", 0, materialPathId);

        if (foundIndex >= 0)
            list[foundIndex] = entry;
        else
            list.Add(entry);

        ReplaceArrayElements(array, list, cloneElements: false);
    }

    private static void UpsertSongIdJacketMaterialEntry(AssetTypeValueField songDbBaseField, int songIdValue, long materialPathId)
    {
        var owner = RequireField(songDbBaseField, "songIdJacketMaterials");
        var array = RequireArrayField(owner);
        var entries = GetArrayElements(array);
        var list = entries.Select(x => x.Clone()).ToList();

        int foundIndex = -1;
        for (int i = 0; i < list.Count; i++)
        {
            long existing = TryReadNumberField(list[i], "SongId", "Value")
                            ?? TryReadNumberField(list[i], "data", "SongId", "Value")
                            ?? -1;
            if (existing == songIdValue)
            {
                foundIndex = i;
                break;
            }
        }

        AssetTypeValueField entry = foundIndex >= 0
            ? list[foundIndex]
            : CloneArrayEntryTemplate(array, entries, "songIdJacketMaterials");

        SetEntrySongIdField(entry, songIdValue);
        SetEntryPPtrField(entry, "JacketMaterial", 0, materialPathId);

        if (foundIndex >= 0)
            list[foundIndex] = entry;
        else
            list.Add(entry);

        ReplaceArrayElements(array, list, cloneElements: false);
    }

    private static AssetTypeValueField CloneArrayEntryTemplate(
        AssetTypeValueField arrayField,
        IReadOnlyList<AssetTypeValueField> currentEntries,
        string arrayName)
    {
        if (currentEntries.Count > 0)
            return currentEntries[0].Clone();

        throw new Exception($"无法为 {arrayName} 创建新条目：未找到数组模板元素。");
    }

    private static void SetEntryStringField(AssetTypeValueField entry, string fieldName, string value)
    {
        entry = UnwrapDataField(entry);
        if (TrySetStringField(entry, fieldName, value))
            return;
        throw new Exception($"无法写入条目字段 {fieldName}（字符串）。");
    }

    private static void SetEntrySongIdField(AssetTypeValueField entry, int songIdValue)
    {
        entry = UnwrapDataField(entry);
        if (TryGetField(entry, "SongId", out var songIdField))
        {
            songIdField = UnwrapDataField(songIdField);
            RequireSetNumberField(songIdField, "Value", songIdValue);
            long actual = TryReadNumberField(songIdField, "Value") ?? -1;
            if (actual != songIdValue)
                throw new Exception($"写入 SongJacketEntry.SongId 后校验失败：期望={songIdValue}，实际={actual}");
            return;
        }

        throw new Exception("无法写入 SongJacketEntry.SongId。");
    }

    private static void SetEntryPPtrField(AssetTypeValueField entry, string fieldName, int fileId, long pathId)
    {
        entry = UnwrapDataField(entry);
        AssetTypeValueField? ptr = null;
        if (TryGetField(entry, fieldName, out var direct))
            ptr = direct;

        if (ptr == null)
            throw new Exception($"无法写入条目字段 {fieldName}（PPtr）。");

        bool okFile = TrySetNumberField(ptr, "m_FileID", fileId) || TrySetNumberField(ptr, "m_FileId", fileId);
        bool okPath = TrySetNumberField(ptr, "m_PathID", pathId) || TrySetNumberField(ptr, "m_PathId", pathId);
        if (!okFile || !okPath)
            throw new Exception($"无法写入条目字段 {fieldName}（PPtr={fileId}:{pathId}）。");
    }

    private static AssetTypeValueField? FindChartInfoTemplate(IReadOnlyList<AssetTypeValueField> allSlots)
    {
        foreach (var slot in allSlots)
        {
            var songInfo = UnwrapDataField(slot);
            if (!TryGetField(songInfo, "ChartInfos", out var chartInfos)) continue;
            try
            {
                var arr = RequireArrayField(chartInfos);
                var elems = GetArrayElements(arr);
                if (elems.Count > 0)
                    return elems[0].Clone();
            }
            catch { }
        }
        return null;
    }

    private static AssetTypeValueField? FindSongInfoTemplate(IReadOnlyList<AssetTypeValueField> allSlots)
    {
        foreach (var slot in allSlots)
        {
            try
            {
                var songInfo = UnwrapDataField(slot);
                string baseName = TryReadStringField(songInfo, "BaseName") ?? "";
                if (string.IsNullOrWhiteSpace(baseName))
                    continue;

                // 优先选择带有 ChartInfos 模板的槽位。
                if (GetSongInfoChartCount(songInfo) > 0)
                    return slot;
            }
            catch
            {
            }
        }

        // 退一步：只要是非空槽也可作为 SongInfo 结构模板。
        foreach (var slot in allSlots)
        {
            try
            {
                string baseName = TryReadStringField(UnwrapDataField(slot), "BaseName") ?? "";
                if (!string.IsNullOrWhiteSpace(baseName))
                    return slot;
            }
            catch
            {
            }
        }
        return null;
    }

    private static int GetSongInfoChartCount(AssetTypeValueField songInfoSlot)
    {
        try
        {
            songInfoSlot = UnwrapDataField(songInfoSlot);
            if (!TryGetField(songInfoSlot, "ChartInfos", out var chartInfos))
                return 0;
            var arr = RequireArrayField(chartInfos);
            return GetArrayElements(arr).Count;
        }
        catch
        {
            return 0;
        }
    }

    private static AssetTypeValueField ReplaceArrayElementWithTemplateClone(
        AssetTypeValueField arrayField,
        int elementIndex,
        AssetTypeValueField templateElement)
    {
        arrayField = RequireArrayField(arrayField);
        arrayField.Children ??= new List<AssetTypeValueField>();
        int offset = (arrayField.Children.Count > 0 &&
                      string.Equals(arrayField.Children[0].FieldName, "size", StringComparison.OrdinalIgnoreCase))
            ? 1 : 0;
        int childIndex = offset + elementIndex;
        if (childIndex < 0 || childIndex >= arrayField.Children.Count)
            throw new Exception($"数组元素索引越界：{elementIndex}");

        var clone = templateElement.Clone();
        arrayField.Children[childIndex] = clone;
        return clone;
    }

    private static AssetTypeValueField UnwrapDataField(AssetTypeValueField field)
    {
        if (field == null) return field;
        return TryGetField(field, "data", out var data) ? data : field;
    }

    private static void ValidateSongDataSongIdsUnique(IReadOnlyList<AssetTypeValueField> slots)
    {
        var seen = new Dictionary<int, int>();
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = UnwrapDataField(slots[i]);
            int songId = (int)(TryReadNumberField(slot, "Id", "Value") ?? -1);
            string baseName = TryReadStringField(slot, "BaseName") ?? "";
            if (songId < 0) continue;
            if (songId == 0 && string.IsNullOrWhiteSpace(baseName))
                continue; // 空槽占位

            if (seen.TryGetValue(songId, out int prevIndex))
            {
                throw new Exception($"SongDatabase 校验失败：重复 SongId={songId}（槽位 {prevIndex} 与 {i}）。");
            }
            seen[songId] = i;
        }
    }

    private static void ValidateSongDataJacketLookupKeysUnique(AssetTypeValueField songDbBaseField, string? phase = null)
    {
        string phasePrefix = string.IsNullOrWhiteSpace(phase) ? "" : $"（{phase}）";
        if (TryGetField(songDbBaseField, "songIdJacketMaterials", out var songIdJackets))
        {
            var seenSongId = new Dictionary<int, int>();
            var entries = GetArrayElements(RequireArrayField(songIdJackets));
            for (int i = 0; i < entries.Count; i++)
            {
                var e = UnwrapDataField(entries[i]);
                int songId = (int)(TryReadNumberField(e, "SongId", "Value") ?? -1);
                if (songId < 0) continue;
                if (seenSongId.TryGetValue(songId, out int prev))
                    throw new Exception(
                        $"SongDatabase 校验失败{phasePrefix}：songIdJacketMaterials 存在重复 SongId={songId}（条目 {prev} 与 {i}）。\n" +
                        $"songIdJacketMaterials 明细：\n{DumpSongIdJacketMaterialsEntries(entries)}");
                seenSongId[songId] = i;
            }
        }

        if (TryGetField(songDbBaseField, "chartIdJacketMaterials", out var chartIdJackets))
        {
            var seenChartId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var entries = GetArrayElements(RequireArrayField(chartIdJackets));
            for (int i = 0; i < entries.Count; i++)
            {
                var e = UnwrapDataField(entries[i]);
                string chartId = TryReadStringField(e, "ChartId") ?? "";
                if (string.IsNullOrWhiteSpace(chartId)) continue;
                if (seenChartId.TryGetValue(chartId, out int prev))
                    throw new Exception(
                        $"SongDatabase 校验失败{phasePrefix}：chartIdJacketMaterials 存在重复 ChartId={chartId}（条目 {prev} 与 {i}）。\n" +
                        $"chartIdJacketMaterials 明细：\n{DumpChartIdJacketMaterialsEntries(entries)}");
                seenChartId[chartId] = i;
            }
        }
    }

    private static string DumpSongIdJacketMaterialsEntries(IReadOnlyList<AssetTypeValueField> entries)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = UnwrapDataField(entries[i]);
            int songId = (int)(TryReadNumberField(e, "SongId", "Value") ?? -1);
            long fileId = ReadPPtrFileId(e, "JacketMaterial");
            long pathId = ReadPPtrPathId(e, "JacketMaterial");
            sb.Append('[').Append(i).Append("] SongId=").Append(songId)
              .Append(", JacketMaterial=").Append(fileId).Append(':').Append(pathId)
              .AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string DumpChartIdJacketMaterialsEntries(IReadOnlyList<AssetTypeValueField> entries)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = UnwrapDataField(entries[i]);
            string chartId = TryReadStringField(e, "ChartId") ?? "";
            long fileId = ReadPPtrFileId(e, "JacketMaterial");
            long pathId = ReadPPtrPathId(e, "JacketMaterial");
            sb.Append('[').Append(i).Append("] ChartId=").Append(chartId)
              .Append(", JacketMaterial=").Append(fileId).Append(':').Append(pathId)
              .AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static void DeduplicateSongDataJacketLookups(AssetTypeValueField songDbBaseField)
    {
        DeduplicateSongIdJacketMaterials(songDbBaseField);
        DeduplicateChartIdJacketMaterials(songDbBaseField);
    }

    private static void DeduplicateSongIdJacketMaterials(AssetTypeValueField songDbBaseField)
    {
        if (!TryGetField(songDbBaseField, "songIdJacketMaterials", out var owner))
            return;

        var array = RequireArrayField(owner);
        var entries = GetArrayElements(array);
        if (entries.Count <= 1) return;

        var deduped = new List<AssetTypeValueField>(entries.Count);
        var seen = new HashSet<int>();
        foreach (var raw in entries)
        {
            var e = UnwrapDataField(raw);
            int songId = (int)(TryReadNumberField(e, "SongId", "Value") ?? -1);
            if (songId >= 0)
            {
                if (!seen.Add(songId))
                    continue;
            }
            deduped.Add(raw.Clone());
        }

        if (deduped.Count != entries.Count)
            ReplaceArrayElements(array, deduped, cloneElements: false);
    }

    private static void DeduplicateChartIdJacketMaterials(AssetTypeValueField songDbBaseField)
    {
        if (!TryGetField(songDbBaseField, "chartIdJacketMaterials", out var owner))
            return;

        var array = RequireArrayField(owner);
        var entries = GetArrayElements(array);
        if (entries.Count <= 1) return;

        var deduped = new List<AssetTypeValueField>(entries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in entries)
        {
            var e = UnwrapDataField(raw);
            string chartId = TryReadStringField(e, "ChartId") ?? "";
            if (!string.IsNullOrWhiteSpace(chartId))
            {
                if (!seen.Add(chartId))
                    continue;
            }
            deduped.Add(raw.Clone());
        }

        if (deduped.Count != entries.Count)
            ReplaceArrayElements(array, deduped, cloneElements: false);
    }

    private static int RewriteMaterialTextureReference(AssetTypeValueField materialBaseField, long newTexturePathId)
    {
        int changed = 0;
        if (TryGetField(materialBaseField, "m_SavedProperties", out var savedProps) &&
            TryGetField(savedProps, "m_TexEnvs", out var texEnvs))
        {
            try
            {
                var arr = RequireArrayField(texEnvs);
                foreach (var env in GetArrayElements(arr))
                {
                    string key = TryReadStringField(env, "first") ?? "";
                    if (!TryGetField(env, "second", out var second)) continue;
                    if (!TryGetField(second, "m_Texture", out var texPtr)) continue;
                    bool ok1 = TrySetNumberField(texPtr, "m_FileID", 0) || TrySetNumberField(texPtr, "m_FileId", 0);
                    bool ok2 = TrySetNumberField(texPtr, "m_PathID", newTexturePathId) || TrySetNumberField(texPtr, "m_PathId", newTexturePathId);
                    if (ok1 && ok2)
                    {
                        changed++;
                        if (string.Equals(key, "_MainTex", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(key, "_BaseMap", StringComparison.OrdinalIgnoreCase))
                            return changed;
                    }
                }
            }
            catch { }
        }

        foreach (var field in EnumerateFields(materialBaseField))
        {
            if (!string.Equals(field.FieldName, "m_Texture", StringComparison.OrdinalIgnoreCase)) continue;
            bool ok1 = TrySetNumberField(field, "m_FileID", 0) || TrySetNumberField(field, "m_FileId", 0);
            bool ok2 = TrySetNumberField(field, "m_PathID", newTexturePathId) || TrySetNumberField(field, "m_PathId", newTexturePathId);
            if (ok1 && ok2) changed++;
        }
        return changed;
    }

    private static IEnumerable<AssetTypeValueField> EnumerateFields(AssetTypeValueField root)
    {
        yield return root;
        if (root.Children == null) yield break;
        foreach (var child in root.Children)
        {
            foreach (var sub in EnumerateFields(child))
                yield return sub;
        }
    }

    private static long AddClonedAssetFromTemplate(AssetsFile assetsFile, AssetFileInfo templateInfo, AssetTypeValueField baseField)
    {
        long pathId = GetNextPathId(assetsFile);
        var newInfo = new AssetFileInfo
        {
            PathId = pathId,
            ByteOffset = 0,
            ByteSize = 0,
            TypeId = templateInfo.TypeId,
            TypeIdOrIndex = templateInfo.TypeIdOrIndex,
            ScriptTypeIndex = templateInfo.ScriptTypeIndex,
            OldTypeId = templateInfo.OldTypeId,
            Stripped = templateInfo.Stripped
        };
        newInfo.SetNewData(baseField);
        assetsFile.AssetInfos.Add(newInfo);
        assetsFile.GenerateQuickLookup();
        return pathId;
    }

    private static long GetNextPathId(AssetsFile assetsFile)
    {
        long max = 0;
        foreach (var info in assetsFile.AssetInfos)
            if (info.PathId > max) max = info.PathId;
        if (max < 1) max = 1;
        return checked(max + 1);
    }

    private static int ParseMaybeInt(string text) => int.TryParse(text, out int v) ? v : 0;

    private static AssetTypeValueField RequireField(AssetTypeValueField parent, string fieldName)
    {
        if (!TryGetField(parent, fieldName, out var field))
            throw new Exception($"字段不存在：{fieldName}");
        return field;
    }

    private static bool TryGetField(AssetTypeValueField parent, string fieldName, out AssetTypeValueField field)
    {
        try
        {
            field = parent.Get(fieldName);
            return field is not null && !field.IsDummy;
        }
        catch
        {
            field = null!;
            return false;
        }
    }

    private static AssetTypeValueField RequireArrayField(AssetTypeValueField ownerField)
    {
        if (TryGetField(ownerField, "Array", out var arr))
            return arr;

        if (string.Equals(ownerField.FieldName, "Array", StringComparison.OrdinalIgnoreCase))
            return ownerField;

        if (ownerField.Children != null)
        {
            var nested = ownerField.Children.FirstOrDefault(x => string.Equals(x.FieldName, "Array", StringComparison.OrdinalIgnoreCase));
            if (nested != null) return nested;
        }

        return ownerField;
    }

    private static List<AssetTypeValueField> GetArrayElements(AssetTypeValueField arrayField)
    {
        if (arrayField.Children == null || arrayField.Children.Count == 0)
            return new List<AssetTypeValueField>();
        int sizeIndex = FindArraySizeFieldIndex(arrayField.Children);
        if (sizeIndex < 0)
            return arrayField.Children.ToList();
        return arrayField.Children.Where((_, idx) => idx != sizeIndex).ToList();
    }

    private static void ReplaceArrayElements(AssetTypeValueField arrayField, IReadOnlyList<AssetTypeValueField> newElements, bool cloneElements = true)
    {
        arrayField = RequireArrayField(arrayField);
        arrayField.Children ??= new List<AssetTypeValueField>();
        int sizeIndex = FindArraySizeFieldIndex(arrayField.Children);
        AssetTypeValueField? sizeField = sizeIndex >= 0 ? arrayField.Children[sizeIndex] : null;

        List<AssetTypeValueField> children;
        if (sizeField != null)
        {
            RequireSetNumberFieldDirect(sizeField, newElements.Count);
            children = new List<AssetTypeValueField>(1 + newElements.Count) { sizeField };
        }
        else
        {
            // 某些数组在 AssetsTools.NET 的字段树中不暴露 size 子字段（长度由 AsArray 维护）。
            // 这类数组若强行插入 synthetic size，会导致序列化时长度错位并在回读时出现“read beyond end of stream”。
            children = new List<AssetTypeValueField>(newElements.Count);
        }

        if (cloneElements)
            children.AddRange(newElements.Select(x => x.Clone()));
        else
            children.AddRange(newElements);
        arrayField.Children = children;
        try { arrayField.AsArray = new AssetTypeArrayInfo(newElements.Count); } catch { }
    }

    private static int FindArraySizeFieldIndex(IReadOnlyList<AssetTypeValueField> children)
    {
        if (children == null || children.Count == 0) return -1;

        for (int i = 0; i < children.Count; i++)
        {
            if (LooksLikeArraySizeField(children[i]))
                return i;
        }
        return -1;
    }

    private static bool LooksLikeArraySizeField(AssetTypeValueField field)
    {
        if (field == null) return false;

        string? fieldName = null;
        string? templateName = null;
        try { fieldName = field.FieldName; } catch { }
        try { templateName = field.TemplateField?.Name; } catch { }

        if (string.Equals(fieldName, "size", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(templateName, "size", StringComparison.OrdinalIgnoreCase))
            return true;

        // 回退：部分 Unity/AssetsTools 组合里 size 字段名称异常，但仍是数组第一个整数字段。
        // 仅将“无子字段的整型标量”视为候选，避免误把结构体元素当作 size。
        bool hasChildren = field.Children != null && field.Children.Count > 0;
        if (hasChildren) return false;

        try { _ = field.AsInt; return true; } catch { }
        try { _ = field.AsLong; return true; } catch { }
        try { _ = field.AsUInt; return true; } catch { }
        return false;
    }

    private static string SafeBuildSongDatabaseArrayStructureSummary(AssetTypeValueField songDbBaseField, int slotIndex, string title)
    {
        try
        {
            return BuildSongDatabaseArrayStructureSummary(songDbBaseField, slotIndex, title);
        }
        catch (Exception ex)
        {
            return $"SongDatabase 数组结构摘要（{title}）构建失败：{ex.Message}";
        }
    }

    private static string BuildSongDatabaseArrayStructureSummary(AssetTypeValueField songDbBaseField, int slotIndex, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SongDatabase 数组结构摘要（{title}）:");
        AppendArrayStructureSummary(sb, "allSongInfo", RequireField(songDbBaseField, "allSongInfo"));

        try
        {
            var allSongInfoArray = RequireArrayField(RequireField(songDbBaseField, "allSongInfo"));
            var slots = GetArrayElements(allSongInfoArray);
            if (slotIndex >= 0 && slotIndex < slots.Count)
            {
                var slot = UnwrapDataField(slots[slotIndex]);
                if (TryGetField(slot, "ChartInfos", out var chartInfos))
                    AppendArrayStructureSummary(sb, $"allSongInfo[{slotIndex}].ChartInfos", chartInfos);
                else
                    sb.AppendLine($"- allSongInfo[{slotIndex}].ChartInfos: <字段不存在>");
            }
            else
            {
                sb.AppendLine($"- allSongInfo[{slotIndex}].ChartInfos: <槽位越界>");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"- allSongInfo[{slotIndex}].ChartInfos: <读取失败> {ex.Message}");
        }

        AppendArrayStructureSummarySafe(sb, "songIdJacketMaterials", songDbBaseField, "songIdJacketMaterials");
        AppendArrayStructureSummarySafe(sb, "chartIdJacketMaterials", songDbBaseField, "chartIdJacketMaterials");
        return sb.ToString().TrimEnd();
    }

    private static void AppendArrayStructureSummarySafe(
        StringBuilder sb,
        string label,
        AssetTypeValueField parent,
        string fieldName)
    {
        try
        {
            AppendArrayStructureSummary(sb, label, RequireField(parent, fieldName));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"- {label}: <读取失败> {ex.Message}");
        }
    }

    private static void AppendArrayStructureSummary(StringBuilder sb, string label, AssetTypeValueField ownerField)
    {
        var arrayField = RequireArrayField(ownerField);
        var children = arrayField.Children?.ToList() ?? new List<AssetTypeValueField>();
        int sizeIndex = FindArraySizeFieldIndex(children);
        int elemCount = GetArrayElements(arrayField).Count;
        sb.AppendLine($"- {label}: explicitSize={(sizeIndex >= 0 ? "Y" : "N")}, children={children.Count}, elements={elemCount}, sizeIndex={sizeIndex}");

        int previewCount = Math.Min(children.Count, 6);
        for (int i = 0; i < previewCount; i++)
        {
            var c = children[i];
            string fieldName = SafeFieldName(c);
            string templateName = SafeTemplateName(c);
            int childCount = c.Children?.Count ?? 0;
            sb.AppendLine($"  [{i}] FieldName={fieldName}, TemplateName={templateName}, Children={childCount}");
        }
    }

    private static string SafeFieldName(AssetTypeValueField field)
    {
        try { return field.FieldName ?? "<null>"; } catch { return "<err>"; }
    }

    private static string SafeTemplateName(AssetTypeValueField field)
    {
        try { return field.TemplateField?.Name ?? "<null>"; } catch { return "<err>"; }
    }

    private static AssetTypeValueField CreateSyntheticArraySizeField(AssetTypeValueField arrayField)
    {
        AssetTypeTemplateField? template = null;
        try
        {
            template = arrayField.TemplateField?.Children?.FirstOrDefault(x => string.Equals(x.Name, "size", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            template = null;
        }
        template ??= new AssetTypeTemplateField
        {
            Name = "size",
            Type = "int",
            ValueType = AssetValueType.Int32,
            HasValue = true,
            IsArray = false,
            IsAligned = false,
            Version = 1,
            Children = new List<AssetTypeTemplateField>()
        };

        var field = new AssetTypeValueField();
        field.Read(new AssetTypeValue(0), template, new List<AssetTypeValueField>());
        return field;
    }

    private static string? TryReadStringField(AssetTypeValueField parent, params string[] path)
    {
        try
        {
            var f = TraversePath(parent, path);
            try { return f.AsString; } catch { }
            try { return Encoding.UTF8.GetString(f.AsByteArray); } catch { }
            return null;
        }
        catch { return null; }
    }

    private static long? TryReadNumberField(AssetTypeValueField parent, params string[] path)
    {
        try
        {
            var f = TraversePath(parent, path);
            try { return f.AsLong; } catch { }
            try { return f.AsInt; } catch { }
            try { return f.AsUInt; } catch { }
            try { return (long)f.AsUShort; } catch { }
            try { return (long)f.AsShort; } catch { }
            try { return (long)f.AsByte; } catch { }
            try { return (long)f.AsSByte; } catch { }
            return null;
        }
        catch { return null; }
    }

    private static AssetTypeValueField TraversePath(AssetTypeValueField parent, params string[] path)
    {
        var cur = parent;
        foreach (var p in path) cur = RequireField(cur, p);
        return cur;
    }

    private static void EnsureFieldValueInitialized(AssetTypeValueField field)
    {
        if (field.Value != null) return;
        var valueType = field.TemplateField?.ValueType ?? AssetValueType.None;
        field.Value = valueType switch
        {
            AssetValueType.Bool => new AssetTypeValue(false),
            AssetValueType.Int8 => new AssetTypeValue((sbyte)0),
            AssetValueType.UInt8 => new AssetTypeValue((byte)0),
            AssetValueType.Int16 => new AssetTypeValue((short)0),
            AssetValueType.UInt16 => new AssetTypeValue((ushort)0),
            AssetValueType.Int32 => new AssetTypeValue(0),
            AssetValueType.UInt32 => new AssetTypeValue((uint)0),
            AssetValueType.Int64 => new AssetTypeValue((long)0),
            AssetValueType.UInt64 => new AssetTypeValue((ulong)0),
            AssetValueType.Float => new AssetTypeValue(0f),
            AssetValueType.Double => new AssetTypeValue(0d),
            AssetValueType.String => new AssetTypeValue(string.Empty),
            AssetValueType.ByteArray => new AssetTypeValue(Array.Empty<byte>(), false),
            AssetValueType.Array => new AssetTypeValue(AssetValueType.Array, new AssetTypeArrayInfo(0)),
            _ => new AssetTypeValue(AssetValueType.None, null!)
        };
        field.Children ??= new List<AssetTypeValueField>();
    }

    private static void RequireSetNumberFieldDirect(AssetTypeValueField field, long value)
    {
        EnsureFieldValueInitialized(field);
        try { field.AsLong = value; return; } catch { }
        try { field.AsInt = checked((int)value); return; } catch { }
        try { field.AsShort = checked((short)value); return; } catch { }
        try { field.AsSByte = checked((sbyte)value); return; } catch { }
        try { field.AsULong = checked((ulong)Math.Max(0, value)); return; } catch { }
        try { field.AsUInt = checked((uint)Math.Max(0, value)); return; } catch { }
        try { field.AsUShort = checked((ushort)Math.Max(0, value)); return; } catch { }
        try { field.AsByte = checked((byte)Math.Max(0, value)); return; } catch { }
        throw new Exception($"无法写入字段 {field.FieldName}");
    }

    private static void RequireSetNumberField(AssetTypeValueField parent, string fieldName, long value)
    {
        if (!TrySetNumberField(parent, fieldName, value))
            throw new Exception($"无法写入字段 {fieldName}（数值：{value}）。");
    }

    private static void RequireSetStringField(AssetTypeValueField parent, string fieldName, string value)
    {
        if (!TrySetStringField(parent, fieldName, value))
            throw new Exception($"无法写入字段 {fieldName}（字符串）。");
    }

    private static void RequireSetByteArrayField(AssetTypeValueField parent, string fieldName, byte[] bytes)
    {
        if (!TrySetByteArrayField(parent, fieldName, bytes))
            throw new Exception($"无法写入字段 {fieldName}（字节数组长度：{bytes.Length}）。");
    }

    private static bool TrySetNumberField(AssetTypeValueField parent, string fieldName, long value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsLong = value; return true; } catch { }
        try { field.AsInt = checked((int)value); return true; } catch { }
        try { field.AsShort = checked((short)value); return true; } catch { }
        try { field.AsSByte = checked((sbyte)value); return true; } catch { }
        try { field.AsULong = checked((ulong)Math.Max(0, value)); return true; } catch { }
        try { field.AsUInt = checked((uint)Math.Max(0, value)); return true; } catch { }
        try { field.AsUShort = checked((ushort)Math.Max(0, value)); return true; } catch { }
        try { field.AsByte = checked((byte)Math.Max(0, value)); return true; } catch { }
        return false;
    }

    private static bool TrySetFloatOrDoubleField(AssetTypeValueField parent, string fieldName, double value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsDouble = value; return true; } catch { }
        try { field.AsFloat = (float)value; return true; } catch { }
        try { field.Value = new AssetTypeValue((float)value); return true; } catch { }
        return false;
    }

    private static bool TrySetStringField(AssetTypeValueField parent, string fieldName, string value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsString = value; return true; } catch { }
        try { field.Value = new AssetTypeValue(value); return true; } catch { return false; }
    }

    private static bool TrySetByteArrayField(AssetTypeValueField parent, string fieldName, byte[] bytes)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        try { EnsureFieldValueInitialized(field); } catch { }
        try { field.AsByteArray = bytes; return true; } catch { }
        try
        {
            field.Value = new AssetTypeValue(bytes, false);
            TrySetNumberField(field, "size", bytes.Length);
            return true;
        }
        catch { return false; }
    }

    private static PreparedImage LoadPreparedImage(string imageFilePath)
    {
        using var fs = File.OpenRead(imageFilePath);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new Exception("无法读取图片。");
        var frame = decoder.Frames[0];
        BitmapSource bgra = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth;
        int h = bgra.PixelHeight;
        int stride = w * 4;
        byte[] bgraBytes = new byte[stride * h];
        bgra.CopyPixels(bgraBytes, stride, 0);
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(frame));
        enc.Save(ms);
        return new PreparedImage { PngBytes = ms.ToArray(), Bgra32Bytes = bgraBytes, Width = w, Height = h, WasResized = false };
    }

    private static PreparedImage ResizePreparedImageIfNeeded(PreparedImage image, int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0) return image;
        if (image.Width == targetWidth && image.Height == targetHeight) return image;
        int srcStride = image.Width * 4;
        var src = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra32Bytes, srcStride);
        var scaled = new TransformedBitmap(src, new ScaleTransform((double)targetWidth / image.Width, (double)targetHeight / image.Height));
        BitmapSource bgra = scaled.Format == PixelFormats.Bgra32 ? scaled : new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int stride = targetWidth * 4;
        byte[] bytes = new byte[stride * targetHeight];
        bgra.CopyPixels(bytes, stride, 0);
        using var ms = new MemoryStream();
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bgra));
        png.Save(ms);
        return new PreparedImage { PngBytes = ms.ToArray(), Bgra32Bytes = bytes, Width = targetWidth, Height = targetHeight, WasResized = true };
    }

    private static EncodedTextureImage EncodeReplacementImage(
        PreparedImage image,
        int originalFormatValue,
        int targetWidth,
        int targetHeight,
        bool keepOriginalSize)
    {
        var working = keepOriginalSize ? image : ResizePreparedImageIfNeeded(image, targetWidth, targetHeight);
        TextureFormat fmt = Enum.IsDefined(typeof(TextureFormat), originalFormatValue) ? (TextureFormat)originalFormatValue : TextureFormat.RGBA32;

        foreach (var candidate in GetPreferredTextureFormats(fmt, working.Width, working.Height))
        {
            if (TryEncodeManaged(working.PngBytes, candidate, out byte[] encoded, out int w, out int h))
                return new EncodedTextureImage { EncodedBytes = encoded, Width = w, Height = h, FinalFormat = candidate };
        }

        return new EncodedTextureImage
        {
            EncodedBytes = BgraToUnityRgbaRaw(working.Bgra32Bytes, working.Width, working.Height),
            Width = working.Width,
            Height = working.Height,
            FinalFormat = TextureFormat.RGBA32
        };
    }

    private static IEnumerable<TextureFormat> GetPreferredTextureFormats(TextureFormat originalFormat, int width, int height)
    {
        var yielded = new HashSet<int>();
        bool canUseBlockCompression = width > 0 && height > 0 && width % 4 == 0 && height % 4 == 0;

        if (yielded.Add((int)originalFormat))
            yield return originalFormat;

        // 当原格式编码失败时，优先再尝试 BC7（若尺寸满足块压缩要求），尽量避免回退到 RGBA32 导致体积暴涨。
        if (canUseBlockCompression && yielded.Add((int)TextureFormat.BC7))
            yield return TextureFormat.BC7;
    }

    private static bool TryEncodeManaged(byte[] pngBytes, TextureFormat format, out byte[] encoded, out int width, out int height)
    {
        encoded = Array.Empty<byte>();
        width = 0;
        height = 0;
        try
        {
            using var ms = new MemoryStream(pngBytes, writable: false);
            encoded = TextureFile.EncodeManagedImage(ms, format, out width, out height);
            return encoded.Length > 0 && width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BgraToUnityRgbaRaw(byte[] bgra, int width, int height)
    {
        int rowStride = width * 4;
        byte[] rgba = new byte[bgra.Length];
        for (int y = 0; y < height; y++)
        {
            int srcRow = y * rowStride;
            int dstRow = (height - 1 - y) * rowStride;
            for (int x = 0; x < rowStride; x += 4)
            {
                int s = srcRow + x;
                int d = dstRow + x;
                rgba[d + 0] = bgra[s + 2];
                rgba[d + 1] = bgra[s + 1];
                rgba[d + 2] = bgra[s + 0];
                rgba[d + 3] = bgra[s + 3];
            }
        }
        return rgba;
    }

    private static void ApplyEncodedTextureData(TextureFile texture, EncodedTextureImage data)
    {
        texture.m_Width = data.Width;
        texture.m_Height = data.Height;
        texture.m_TextureFormat = (int)data.FinalFormat;
        texture.m_CompleteImageSize = data.EncodedBytes.Length;
        texture.m_MipCount = 1;
        texture.m_MipMap = false;
        texture.m_StreamingMipmaps = false;
        texture.pictureData = data.EncodedBytes;
        texture.m_StreamData.offset = 0;
        texture.m_StreamData.size = 0;
        texture.m_StreamData.path = "";
    }

    private static void ForceApplyEncodedTextureDataToBaseField(AssetTypeValueField baseField, EncodedTextureImage data)
    {
        RequireSetNumberField(baseField, "m_Width", data.Width);
        RequireSetNumberField(baseField, "m_Height", data.Height);
        RequireSetNumberField(baseField, "m_TextureFormat", (int)data.FinalFormat);
        RequireSetNumberField(baseField, "m_CompleteImageSize", data.EncodedBytes.Length);
        RequireSetNumberField(baseField, "m_MipCount", 1);
        TrySetBoolField(baseField, "m_MipMap", false);
        TrySetBoolField(baseField, "m_StreamingMipmaps", false);
        RequireSetByteArrayField(baseField, "image data", data.EncodedBytes);
        if ((int)data.FinalFormat == (int)TextureFormat.RGBA32)
            TrySetByteArrayField(baseField, "m_PlatformBlob", Array.Empty<byte>());
        if (TryGetField(baseField, "m_StreamData", out var stream))
        {
            RequireSetNumberField(stream, "offset", 0);
            RequireSetNumberField(stream, "size", 0);
            RequireSetStringField(stream, "path", "");
        }
    }

    private static bool TrySetBoolField(AssetTypeValueField parent, string fieldName, bool value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsBool = value; return true; } catch { }
        try { field.Value = new AssetTypeValue(value); return true; } catch { return false; }
    }

    private static string AppendMappingEntries(string originalJson, IReadOnlyList<NewSongMappingEntryResult> newEntries)
    {
        JsonNode? root = JsonNode.Parse(originalJson);
        if (root == null) throw new Exception("StreamingAssetsMapping.json 格式无效。");

        JsonArray entries;
        if (root is JsonArray directArray)
        {
            entries = directArray;
        }
        else if (root is JsonObject obj)
        {
            if (obj["m_Structure"] is JsonObject st)
                entries = st["Entries"] as JsonArray ?? throw new Exception("缺少 m_Structure.Entries 数组。");
            else
                entries = obj["Entries"] as JsonArray ?? throw new Exception("缺少 Entries 数组。");
        }
        else
        {
            throw new Exception("StreamingAssetsMapping.json 根节点不支持。");
        }

        var existing = new HashSet<string>(
            entries.OfType<JsonObject>()
                .Select(x => x["FullLookupPath"]?.GetValue<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))!
                .Select(x => x!.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var e in newEntries)
        {
            string p = e.FullLookupPath.Replace('\\', '/');
            if (!existing.Add(p))
                throw new Exception($"Mapping 中已存在 FullLookupPath：{p}");
            entries.Add(new JsonObject
            {
                ["FullLookupPath"] = p,
                ["Guid"] = e.Guid,
                ["FileLength"] = e.FileLength
            });
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ReadTextAssetContent(AssetTypeValueField baseField)
    {
        if (TryGetField(baseField, "m_Script", out var f1))
        {
            try { return f1.AsString; } catch { }
            try { return Encoding.UTF8.GetString(f1.AsByteArray); } catch { }
        }
        if (TryGetField(baseField, "m_Text", out var f2))
        {
            try { return f2.AsString; } catch { }
            try { return Encoding.UTF8.GetString(f2.AsByteArray); } catch { }
        }
        throw new Exception("无法读取 TextAsset 文本内容。");
    }

    private static void WriteTextAssetContent(AssetTypeValueField baseField, string content)
    {
        if (TrySetStringField(baseField, "m_Script", content)) return;
        if (TrySetByteArrayField(baseField, "m_Script", Encoding.UTF8.GetBytes(content))) return;
        if (TrySetStringField(baseField, "m_Text", content)) return;
        if (TrySetByteArrayField(baseField, "m_Text", Encoding.UTF8.GetBytes(content))) return;
        throw new Exception("无法写入 TextAsset 文本内容。");
    }

    private static OutputPlan PrepareOutputPlan(string sourcePath, string requestedPath, bool autoRenameWhenTargetLocked)
    {
        string src = Path.GetFullPath(sourcePath);
        string dst = Path.GetFullPath(requestedPath);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
        {
            return new OutputPlan
            {
                RequestedOutputPath = dst,
                FinalOutputPath = dst,
                PackOutputPath = BuildTemporaryExportPath(dst),
                ReplaceAfterPack = true,
                AutoRenamedDueToLock = false
            };
        }

        if (CanOverwritePath(dst, out _))
        {
            return new OutputPlan
            {
                RequestedOutputPath = dst,
                FinalOutputPath = dst,
                PackOutputPath = dst,
                ReplaceAfterPack = false,
                AutoRenamedDueToLock = false
            };
        }

        if (!autoRenameWhenTargetLocked)
            throw new IOException($"目标文件不可写：{dst}");

        string alt = FindAvailableAlternativePath(dst);
        return new OutputPlan
        {
            RequestedOutputPath = dst,
            FinalOutputPath = alt,
            PackOutputPath = alt,
            ReplaceAfterPack = false,
            AutoRenamedDueToLock = true
        };
    }

    private static void FinalizeOutputPlan(OutputPlan plan)
    {
        if (!plan.ReplaceAfterPack) return;
        if (!File.Exists(plan.PackOutputPath))
            throw new FileNotFoundException($"临时导出文件不存在：{plan.PackOutputPath}");
        File.Copy(plan.PackOutputPath, plan.FinalOutputPath, overwrite: true);
        File.Delete(plan.PackOutputPath);
    }

    private static string BuildTemporaryExportPath(string finalPath)
    {
        string dir = Path.GetDirectoryName(finalPath) ?? "";
        string name = Path.GetFileNameWithoutExtension(finalPath);
        string ext = Path.GetExtension(finalPath);
        for (int i = 1; i <= 9999; i++)
        {
            string p = Path.Combine(dir, $"{name}.tmp_{i}{ext}");
            if (!File.Exists(p)) return p;
        }
        return Path.Combine(dir, $"{name}.tmp_{Guid.NewGuid():N}{ext}");
    }

    private static string FindAvailableAlternativePath(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; i <= 9999; i++)
        {
            string alt = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(alt)) return alt;
        }
        return Path.Combine(dir, $"{name} ({Guid.NewGuid():N}){ext}");
    }

    private static bool CanOverwritePath(string path, out string reason)
    {
        reason = "";
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                fs.Dispose();
                File.Delete(path);
                return true;
            }

            using var rw = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static void WriteAssetsFileApplyingReplacers(AssetsFile assetsFile, string outputPath)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new AssetsFileWriter(fs);
        assetsFile.Write(writer, 0);
    }

    private static void WriteBundleApplyingReplacers(AssetBundleFile bundleFile, AssetBundleCompressionType compressionType, string outputPath)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        string tempUnpacked = Path.Combine(dir ?? "", Path.GetFileNameWithoutExtension(outputPath) + ".tmp.unpacked");
        try
        {
            using (var fsWrite = new FileStream(tempUnpacked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new AssetsFileWriter(fsWrite))
            {
                bundleFile.Write(writer);
            }

            var reload = new AssetBundleFile();
            using (var fsRead = new FileStream(tempUnpacked, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new AssetsFileReader(fsRead))
            using (var fsPack = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var packWriter = new AssetsFileWriter(fsPack))
            {
                reload.Read(reader);
                // Pack 过程会继续访问刚刚读取的 bundle 数据块，因此读取流必须保持到 Pack 完成。
                reload.Pack(packWriter, compressionType);
            }
        }
        finally
        {
            try { if (File.Exists(tempUnpacked)) File.Delete(tempUnpacked); } catch { }
        }
    }

    private static string SafeGetBundleEntryName(AssetBundleFile bundleFile, int idx)
    {
        try { return bundleFile.BlockAndDirInfo.DirectoryInfos[idx].Name ?? $"file_{idx}"; }
        catch { return $"file_{idx}"; }
    }

    private static void ValidateBundlePath(string bundleFilePath)
    {
        if (string.IsNullOrWhiteSpace(bundleFilePath))
            throw new Exception("请先导入 .bundle 文件。");
        if (!File.Exists(bundleFilePath))
            throw new FileNotFoundException($"bundle 文件不存在：{bundleFilePath}");
    }

    private static void ValidateImagePath(string imageFilePath)
    {
        if (string.IsNullOrWhiteSpace(imageFilePath))
            throw new Exception("请先导入曲绘。");
        if (!File.Exists(imageFilePath))
            throw new FileNotFoundException($"曲绘图片不存在：{imageFilePath}");
        string ext = Path.GetExtension(imageFilePath).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg")
            throw new Exception("曲绘仅支持 PNG/JPG/JPEG。");
    }
}

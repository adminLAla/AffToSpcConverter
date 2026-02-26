using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
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

// SongDatabase 中单个歌曲槽位的扫描结果信息。
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

// 可用于复制曲绘资源的模板候选（Texture2D + Material 成对资源）。
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

// .bundle 扫描结果，包含 SongDatabase 定位与曲绘模板列表。
public sealed class SongBundleScanResult
{
    public required string BundleFilePath { get; init; }
    public required int SongDatabaseBundleFileIndex { get; init; }
    public required long SongDatabasePathId { get; init; }
    public required string SongDatabaseAssetsFileName { get; init; }
    public required IReadOnlyList<SongDatabaseSlotInfo> Slots { get; init; }
    public required IReadOnlyList<JacketTemplateCandidate> JacketTemplates { get; init; }
}

// 新增歌曲的一条谱面分档输入项。
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

// 新增歌曲打包请求，汇总 UI 收集的全部导出参数。
public sealed class NewSongPackRequest
{
    public required string BundleFilePath { get; init; }
    public required string SharedAssetsFilePath { get; init; }
    public required string ResourcesAssetsFilePath { get; init; }
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
    public required string SongTitleEnglish { get; init; }
    public required string SongArtistEnglish { get; init; }
    public required int GameplayBackground { get; init; }
    public required int RewardStyle { get; init; }
    public required IReadOnlyList<NewSongChartPackItem> Charts { get; init; }
    public bool AutoRenameWhenTargetLocked { get; init; } = true;
}

// 新增写入到 StreamingAssetsMapping 的单条映射结果。
public sealed class NewSongMappingEntryResult
{
    public required string FullLookupPath { get; init; }
    public required string Guid { get; init; }
    public required int FileLength { get; init; }
}

// 新增歌曲导出结果，包含输出文件路径、映射项与部署摘要。
public sealed class NewSongPackExportResult
{
    public required string OutputBundlePath { get; init; }
    public required string OutputSharedAssetsPath { get; init; }
    public required string OutputResourcesAssetsPath { get; init; }
    public required long NewTexturePathId { get; init; }
    public required long NewMaterialPathId { get; init; }
    public required IReadOnlyList<NewSongMappingEntryResult> AddedMappingEntries { get; init; }
    public required SongDatabaseReadbackValidationResult SongDatabaseReadback { get; init; }
    public required string SongDatabaseArrayStructureDiagnostics { get; init; }
    public required string DeploymentSummary { get; init; }
    public required string Summary { get; init; }
}

// SongDatabase 回读校验结果（开发调试用）。
public sealed class SongDatabaseReadbackValidationResult
{
    // SongDatabase 回读时提取的 ChartInfos 条目摘要。
    public sealed class ChartInfo
    {
        public required int Index { get; init; }
        public required string Id { get; init; }
        public required int Difficulty { get; init; }
        public required int Available { get; init; }
    }

    // songIdJacketMaterials 条目摘要。
    public sealed class SongIdJacketMaterialEntry
    {
        public required int SongId { get; init; }
        public required int FileId { get; init; }
        public required long PathId { get; init; }
    }

    // chartIdJacketMaterials 条目摘要。
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

// 新增歌曲资源打包器：修改 SongDatabase、Mapping、DynamicStringMapping 并部署文件。
public static class UnitySongResourcePacker
{
    // 当前 In Falsus Demo（Unity 6000.3.2f1）中 DynamicStringMapping 在 resources.assets 的稳定 PathID。
    // 若后续版本变动，代码会回退到名称扫描逻辑。
    private const long KnownDynamicStringMappingPathId = 1375;

    // 待生成的加密资源文件（含映射项与明文字节）。
    private sealed class GeneratedResourceFile
    {
        public required NewSongMappingEntryResult MappingEntry { get; init; }
        public required byte[] PlainBytes { get; init; }
    }

    // 导出文件路径规划结果（请求路径、最终路径、临时路径）。
    private sealed class OutputPlan
    {
        public required string RequestedOutputPath { get; init; }
        public required string FinalOutputPath { get; init; }
        public required string PackOutputPath { get; init; }
        public required bool ReplaceAfterPack { get; init; }
        public required bool AutoRenamedDueToLock { get; init; }
    }

    // 曲绘图片预处理结果（PNG/BGRA 缓冲与尺寸信息）。
    private sealed class PreparedImage
    {
        public required byte[] PngBytes { get; set; }
        public required byte[] Bgra32Bytes { get; set; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required bool WasResized { get; init; }
        // 释放Heavy 缓冲区。
        public void ReleaseHeavyBuffers()
        {
            PngBytes = Array.Empty<byte>();
            Bgra32Bytes = Array.Empty<byte>();
        }
    }

    // 曲绘纹理编码结果（可直接写入 Texture2D 的数据块）。
    private sealed class EncodedTextureImage
    {
        public required byte[] EncodedBytes { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required TextureFormat FinalFormat { get; init; }
    }

    // TextAsset 原始字节解析结果（供不依赖 typetree 的回退写入使用）。
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

    // StreamingAssetsMapping（MonoBehaviour）原始字节解析结果。
    private sealed class RawStreamingAssetsMappingMonoBehaviourData
    {
        // StreamingAssetsMapping 中单条资源映射项。
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

    private enum RawDynamicStringMappingIdEncoding
    {
        PackedUInt16,
        PackedUInt16ArrayAlign4,
        UInt16Align4PerElement
    }

    // DynamicStringMapping（MonoBehaviour）原始字节解析结果。
    private sealed class RawDynamicStringMappingMonoBehaviourData
    {
        public enum IdsKind
        {
            WrappedUInt16,
            WrappedString,
            PlainInt32
        }

        // DynamicStringMapping 中一条文本的多语言值集合。
        public sealed class LocalizedValue
        {
            public required string English { get; set; }
            public required string Japanese { get; set; }
            public required string Korean { get; set; }
            public required string TraditionalChinese { get; set; }
            public required string SimplifiedChinese { get; set; }
        }

        // DynamicStringMapping 的单个映射字段（Ids/IdStr/IdValues）。
        public sealed class StringTypeMapping
        {
            public required string FieldName { get; init; }
            public required IdsKind IdKind { get; init; }
            public required List<ushort> IdsUInt16 { get; init; }
            public required List<string> IdsString { get; init; }
            public required List<int> IdsInt32 { get; init; }
            public required List<string> IdStr { get; init; }
            public required List<LocalizedValue> IdValues { get; init; }
        }

        public required byte[] OriginalBytes { get; init; }
        public required bool BigEndian { get; init; }
        public required string ObjectName { get; init; }
        public required int StructureOffset { get; init; }
        public required int StructureEndOffset { get; init; }
        public required RawDynamicStringMappingIdEncoding IdEncoding { get; init; }
        public required IReadOnlyList<StringTypeMapping> Mappings { get; init; }
    }

    private static readonly string[] DynamicStringMappingStructureFieldOrder =
    {
        "packIdTypeMapping",
        "encounterIdTypeMapping",
        "rawStoryNameTypeMapping",
        "songIdTitleTypeMapping",
        "songIdArtistTypeMapping",
        "recipeIdTypeMapping",
        "iotaTitleTypeMapping",
        "iotaDescriptionTypeMapping",
        "traitNameTypeMapping",
        "traitDescriptionTypeMapping",
        "cardNameTypeMapping"
    };

    // 扫描并收集Bundle。
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

    // 导出New 歌曲 Resources。
    public static NewSongPackExportResult ExportNewSongResources(NewSongPackRequest request)
    {
        ValidateRequest(request);
        var generatedFiles = BuildGeneratedResourceFiles(request);

        string bundleSrc = Path.GetFullPath(request.BundleFilePath);
        string sharedSrc = ResolveStreamingAssetsMappingHostPath(request.SharedAssetsFilePath);
        string resourcesSrc = Path.GetFullPath(request.ResourcesAssetsFilePath);
        string outDir = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outDir);

        var bundlePlan = PrepareOutputPlan(bundleSrc, Path.Combine(outDir, Path.GetFileName(bundleSrc)), request.AutoRenameWhenTargetLocked);
        var sharedPlan = PrepareOutputPlan(sharedSrc, Path.Combine(outDir, Path.GetFileName(sharedSrc)), request.AutoRenameWhenTargetLocked);
        var resourcesPlan = PrepareOutputPlan(resourcesSrc, Path.Combine(outDir, Path.GetFileName(resourcesSrc)), request.AutoRenameWhenTargetLocked);

        var jacket = LoadPreparedImage(request.JacketImageFilePath);
        long newTexPathId = 0;
        long newMatPathId = 0;
        string songDbArrayDiagnostics = "";
        SongDatabaseReadbackValidationResult? songDbReadback = null;
        string deploymentSummary = "";
        (int written, int reused) encryptedResourceWriteStats = (0, 0);
        try
        {
            (newTexPathId, newMatPathId, songDbArrayDiagnostics) = ExportModifiedBundle(request, bundlePlan, jacket);
            ExportModifiedSharedAssets(sharedSrc, sharedPlan, generatedFiles.Select(x => x.MappingEntry).ToList());
            ExportModifiedResourcesAssets(resourcesSrc, resourcesPlan, request.SelectedSlot.SlotIndex, request.SongTitleEnglish, request.SongArtistEnglish);
            FinalizeOutputPlan(bundlePlan);
            FinalizeOutputPlan(sharedPlan);
            FinalizeOutputPlan(resourcesPlan);

            encryptedResourceWriteStats = WriteGeneratedEncryptedFiles(outDir, generatedFiles);

            deploymentSummary = DeployOutputsToGameDirectories(
                bundleSrc,
                sharedSrc,
                resourcesSrc,
                bundlePlan.FinalOutputPath,
                sharedPlan.FinalOutputPath,
                resourcesPlan.FinalOutputPath,
                outDir,
                generatedFiles);

            return new NewSongPackExportResult
            {
                OutputBundlePath = bundlePlan.FinalOutputPath,
                OutputSharedAssetsPath = sharedPlan.FinalOutputPath,
                OutputResourcesAssetsPath = resourcesPlan.FinalOutputPath,
                NewTexturePathId = newTexPathId,
                NewMaterialPathId = newMatPathId,
                AddedMappingEntries = generatedFiles.Select(x => x.MappingEntry).ToList(),
                SongDatabaseReadback = songDbReadback ?? new SongDatabaseReadbackValidationResult
                {
                    SlotIndex = request.SelectedSlot.SlotIndex,
                    SongId = request.SelectedSlot.SlotIndex,
                    BaseName = request.BaseName,
                    ChartInfos = Array.Empty<SongDatabaseReadbackValidationResult.ChartInfo>(),
                    SongIdJacketMaterials = Array.Empty<SongDatabaseReadbackValidationResult.SongIdJacketMaterialEntry>(),
                    ChartIdJacketMaterials = Array.Empty<SongDatabaseReadbackValidationResult.ChartIdJacketMaterialEntry>()
                },
                SongDatabaseArrayStructureDiagnostics = songDbArrayDiagnostics,
                DeploymentSummary = deploymentSummary,
                Summary =
                    $"新增歌曲成功：{request.BaseName}，槽位 {request.SelectedSlot.SlotIndex}。新增映射 {generatedFiles.Count} 项。\n" +
                    $"加密资源写入：新写入 {encryptedResourceWriteStats.written} 项，复用备份 {encryptedResourceWriteStats.reused} 项。\n" +
                    "已将修改写入游戏目录（原文件备份为 *_original），并在 SongData 目录保留备份。"
            };
        }
        finally
        {
            jacket.ReleaseHeavyBuffers();
            generatedFiles.Clear();
            TryTrimManagedMemoryAfterHeavyOperation();
        }
    }

    // 校验Request是否有效。
    private static void ValidateRequest(NewSongPackRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        ValidateBundlePath(request.BundleFilePath);
        if (string.IsNullOrWhiteSpace(request.SharedAssetsFilePath) || !File.Exists(request.SharedAssetsFilePath))
            throw new FileNotFoundException($"sharedassets0.assets 不存在：{request.SharedAssetsFilePath}");
        if (string.IsNullOrWhiteSpace(request.ResourcesAssetsFilePath) || !File.Exists(request.ResourcesAssetsFilePath))
            throw new FileNotFoundException($"resources.assets 不存在：{request.ResourcesAssetsFilePath}");
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new Exception("请选择导出文件夹。");
        ValidateImagePath(request.JacketImageFilePath);
        if (string.IsNullOrWhiteSpace(request.BgmFilePath) || !File.Exists(request.BgmFilePath))
            throw new FileNotFoundException($"BGM 文件不存在：{request.BgmFilePath}");
        if (request.SelectedSlot == null || !request.SelectedSlot.IsEmpty)
            throw new Exception("请选择空槽。");
        if (request.SelectedSlot.SlotIndex < 2)
            throw new Exception("槽位 00/01 为保留槽位，请选择 02-76 的空槽。");
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

        if (string.IsNullOrWhiteSpace(request.SongTitleEnglish))
            throw new Exception("请填写曲名(English)，用于 resources.assets / DynamicStringMapping 显示。");
        if (string.IsNullOrWhiteSpace(request.SongArtistEnglish))
            throw new Exception("请填写曲师(English)，用于 resources.assets / DynamicStringMapping 显示。");

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

    // 构建Generated Resource 文件。
    private static List<GeneratedResourceFile> BuildGeneratedResourceFiles(NewSongPackRequest request)
    {
        var list = new List<GeneratedResourceFile>(request.Charts.Count + 1);

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

    // 计算Guid From 路径 And 字节。
    private static string ComputeGuidFromPathAndBytes(string fullLookupPath, byte[] bytes)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(fullLookupPath.Replace('\\', '/'));
        using var md5 = MD5.Create();
        md5.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
        md5.TransformFinalBlock(bytes, 0, bytes.Length);
        byte[] md5Bytes = md5.Hash ?? throw new Exception("计算 MD5 失败。");
        return System.Convert.ToHexString(md5Bytes).ToLowerInvariant();
    }

    // 将生成的 BGM/SPC 加密资源写入 SongData 备份目录；若同 GUID 文件已存在则直接复用，减少重复加密与写盘。
    private static (int written, int reused) WriteGeneratedEncryptedFiles(string outputDirectory, IReadOnlyList<GeneratedResourceFile> generatedFiles)
    {
        int written = 0;
        int reused = 0;
        foreach (var file in generatedFiles)
        {
            string outputPath = Path.Combine(outputDirectory, file.MappingEntry.Guid);
            if (TryReuseExistingEncryptedBackup(outputPath, file.PlainBytes.Length))
            {
                reused++;
                continue;
            }

            byte[] encrypted = GameAssetPacker.EncryptBytesForGame(file.PlainBytes);
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024))
            {
                fs.Write(encrypted, 0, encrypted.Length);
            }
            written++;
        }
        return (written, reused);
    }

    // GUID 已包含 FullLookupPath+内容哈希；同名且长度一致时视为可复用备份，避免重复写盘。
    private static bool TryReuseExistingEncryptedBackup(string outputPath, int expectedLength)
    {
        try
        {
            if (!File.Exists(outputPath)) return false;
            var fi = new FileInfo(outputPath);
            return fi.Length == expectedLength;
        }
        catch
        {
            return false;
        }
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

    // 读取歌曲 数据库 Slots。
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

    // 读取Back Exported 歌曲 数据库。
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

    // 读取歌曲 数据库 Slot Readback。
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

    // 读取P Ptr 文件 Id。
    private static long ReadPPtrFileId(AssetTypeValueField parent, string ptrFieldName)
        => TryReadNumberField(parent, ptrFieldName, "m_FileID")
           ?? TryReadNumberField(parent, ptrFieldName, "m_FileId")
           ?? -1;

    // 读取P Ptr 路径 Id。
    private static long ReadPPtrPathId(AssetTypeValueField parent, string ptrFieldName)
        => TryReadNumberField(parent, ptrFieldName, "m_PathID")
           ?? TryReadNumberField(parent, ptrFieldName, "m_PathId")
           ?? 0;

    // (SongId, BaseName) 组合键比较器，忽略 BaseName 大小写。
    private sealed class SongNameKeyComparer : IEqualityComparer<(int, string)>
    {
        public static SongNameKeyComparer Instance { get; } = new();
        // 比较当前对象与目标对象是否相等。
        public bool Equals((int, string) x, (int, string) y)
            => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        // 返回当前对象的哈希码。
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

    private static void ExportModifiedResourcesAssets(
        string resourcesAssetsSourcePath,
        OutputPlan outputPlan,
        int songId,
        string titleEnglish,
        string artistEnglish)
    {
        var am = new AssetsManager();
        try
        {
            var assetsInst = am.LoadAssetsFile(resourcesAssetsSourcePath, loadDeps: true);
            PrepareAssetsManagerForBaseFieldReading(am, assetsInst);

            var dynamicStringMappingInfo = FindDynamicStringMappingMonoBehaviourAsset(am, assetsInst)
                ?? throw new Exception($"未在 {Path.GetFileName(resourcesAssetsSourcePath)} 中找到 DynamicStringMapping（MonoBehaviour）。");

            ApplyDynamicStringMappingEdit(am, assetsInst, dynamicStringMappingInfo, songId, titleEnglish, artistEnglish);
            WriteAssetsFileApplyingReplacers(assetsInst.file, outputPlan.PackOutputPath);
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    // 查找DynamicStringMapping MonoBehaviour 资源。
    private static AssetFileInfo? FindDynamicStringMappingMonoBehaviourAsset(AssetsManager am, AssetsFileInstance assetsInst)
    {
        // 先走已知 PathID 快速路径，避免扫描所有 MonoBehaviour 并触发大量 GetBaseField first-chance 异常。
        var fastInfo = assetsInst.file.GetAssetInfo(KnownDynamicStringMappingPathId);
        if (fastInfo != null && fastInfo.TypeId == (int)AssetClassID.MonoBehaviour)
        {
            string? rawName = TryReadRawMonoBehaviourObjectName(assetsInst, fastInfo);
            if (string.Equals(rawName, "DynamicStringMapping", StringComparison.OrdinalIgnoreCase))
                return fastInfo;

            var fastBaseField = TryGetBaseFieldSafe(am, assetsInst, fastInfo);
            if (fastBaseField != null)
            {
                string fastName = TryReadStringField(fastBaseField, "m_Name") ?? "";
                if (string.Equals(fastName, "DynamicStringMapping", StringComparison.OrdinalIgnoreCase))
                    return fastInfo;
            }
        }

        foreach (var info in assetsInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            if (info.PathId == KnownDynamicStringMappingPathId)
                continue;
            try
            {
                // 优先原始字节读取 m_Name，避免 Unity6 下 GetBaseField 对无关 MonoBehaviour 抛 NRE。
                string name = TryReadRawMonoBehaviourObjectName(assetsInst, info) ?? "";
                if (string.IsNullOrEmpty(name))
                {
                    var baseField = TryGetBaseFieldSafe(am, assetsInst, info);
                    if (baseField == null) continue;
                    name = TryReadStringField(baseField, "m_Name") ?? "";
                }
                if (string.Equals(name, "DynamicStringMapping", StringComparison.OrdinalIgnoreCase))
                    return info;
            }
            catch
            {
            }
        }
        return null;
    }

    // 新增歌曲打包会生成大字节数组（曲绘/加密资源/bundle写出缓存）；导出结束后主动压缩 LOH 并修剪工作集。
    private static void TryTrimManagedMemoryAfterHeavyOperation()
    {
        try
        {
            if (GC.GetTotalMemory(false) < 128L * 1024 * 1024)
                return;

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            TryTrimProcessWorkingSet();
        }
        catch
        {
            // 回收优化失败不影响导出结果。
        }
    }

    // 尝试压缩/回收。
    private static void TryTrimProcessWorkingSet()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            _ = EmptyWorkingSet(process.Handle);
        }
        catch
        {
            // ignore
        }
    }

    // 仅解析 MonoBehaviour 基础字段中的 m_Name，避免构建完整 typetree 带来的异常与开销。
    private static string? TryReadRawMonoBehaviourObjectName(AssetsFileInstance assetsInst, AssetFileInfo info)
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

            _ = r.ReadInt32(); // m_GameObject.m_FileID
            _ = r.ReadInt64(); // m_GameObject.m_PathID
            if (r.BaseStream.ReadByte() < 0) return null; // m_Enabled
            r.Align();
            _ = r.ReadInt32(); // m_Script.m_FileID
            _ = r.ReadInt64(); // m_Script.m_PathID
            string name = r.ReadCountStringInt32();
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyDynamicStringMappingEdit(
        AssetsManager am,
        AssetsFileInstance assetsInst,
        AssetFileInfo mappingInfo,
        int songId,
        string titleEnglish,
        string artistEnglish)
    {
        Exception? typetreeEx = null;
        var baseField = TryGetBaseFieldSafe(am, assetsInst, mappingInfo);
        if (baseField != null)
        {
            try
            {
                string name = TryReadStringField(baseField, "m_Name") ?? "";
                if (!string.Equals(name, "DynamicStringMapping", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"目标 MonoBehaviour 不是 DynamicStringMapping：m_Name={name}");

                var structure = RequireField(baseField, "m_Structure");
                UpsertDynamicStringTypeMappingEntry(structure, "songIdTitleTypeMapping", songId, titleEnglish);
                UpsertDynamicStringTypeMappingEntry(structure, "songIdArtistTypeMapping", songId, artistEnglish);

                mappingInfo.SetNewData(baseField);
                return;
            }
            catch (Exception ex)
            {
                typetreeEx = ex;
            }
        }

        var raw = TryReadRawDynamicStringMappingMonoBehaviour(assetsInst, mappingInfo);
        if (raw != null)
        {
            if (!string.Equals(raw.ObjectName, "DynamicStringMapping", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"目标 MonoBehaviour 不是 DynamicStringMapping：m_Name={raw.ObjectName}");

            UpsertRawDynamicStringTypeMappingEntry(raw, "songIdTitleTypeMapping", checked((ushort)songId), titleEnglish);
            UpsertRawDynamicStringTypeMappingEntry(raw, "songIdArtistTypeMapping", checked((ushort)songId), artistEnglish);
            mappingInfo.SetNewData(BuildUpdatedRawDynamicStringMappingMonoBehaviour(raw));
            return;
        }

        if (typetreeEx != null)
            throw new Exception($"无法写入 DynamicStringMapping：typetree 路径失败且原始字节回退解析失败。typetree错误：{typetreeEx.Message}", typetreeEx);

        throw new Exception("无法写入 DynamicStringMapping：GetBaseField 返回空且原始字节回退解析失败。");
    }

    private static void UpsertDynamicStringTypeMappingEntry(
        AssetTypeValueField structureField,
        string mappingFieldName,
        int songId,
        string englishText)
    {
        var mappingField = RequireField(structureField, mappingFieldName);
        var idsOwner = RequireField(mappingField, "Ids");
        var idsArray = RequireArrayField(idsOwner);
        var idElems = GetArrayElements(idsArray);

        AssetTypeValueField? idStrArray = null;
        List<AssetTypeValueField>? idStrElems = null;
        if (TryGetField(mappingField, "IdStr", out var idStrOwner))
        {
            idStrArray = RequireArrayField(idStrOwner);
            idStrElems = GetArrayElements(idStrArray);
        }

        var idValuesOwner = RequireField(mappingField, "IdValues");
        var idValuesArray = RequireArrayField(idValuesOwner);
        var idValueElems = GetArrayElements(idValuesArray);

        if (idElems.Count != idValueElems.Count)
            throw new Exception($"{mappingFieldName} 结构异常：Ids({idElems.Count}) 与 IdValues({idValueElems.Count}) 数量不一致。");
        if (idStrElems != null && idStrElems.Count != idElems.Count)
            throw new Exception($"{mappingFieldName} 结构异常：Ids({idElems.Count}) 与 IdStr({idStrElems.Count}) 数量不一致。");

        int index = -1;
        for (int i = 0; i < idElems.Count; i++)
        {
            int value = (int)(TryReadNumberField(UnwrapDataField(idElems[i]), "Value") ?? -1);
            if (value == songId)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            WriteSongIdElement(idElems[index], songId);
            if (idStrElems != null) WriteStringArrayElement(idStrElems[index], englishText);
            WriteLocalizedStringValue(idValueElems[index], englishText, clearOtherLanguages: false);
            return;
        }

        if (idElems.Count == 0 || idValueElems.Count == 0)
            throw new Exception($"{mappingFieldName} 结构异常：无法从空数组推断模板，不能新增条目。");

        var newIdElem = idElems[0].Clone();
        WriteSongIdElement(newIdElem, songId);
        var newIds = new List<AssetTypeValueField>(idElems.Count + 1);
        newIds.AddRange(idElems);
        newIds.Add(newIdElem);
        ReplaceArrayElements(idsArray, newIds, cloneElements: false);

        if (idStrArray != null && idStrElems != null)
        {
            if (idStrElems.Count == 0)
                throw new Exception($"{mappingFieldName} 结构异常：IdStr 数组为空，无法克隆模板。");
            var newIdStrElem = idStrElems[0].Clone();
            WriteStringArrayElement(newIdStrElem, englishText);
            var newIdStrs = new List<AssetTypeValueField>(idStrElems.Count + 1);
            newIdStrs.AddRange(idStrElems);
            newIdStrs.Add(newIdStrElem);
            ReplaceArrayElements(idStrArray, newIdStrs, cloneElements: false);
        }

        var newIdValueElem = idValueElems[0].Clone();
        WriteLocalizedStringValue(newIdValueElem, englishText, clearOtherLanguages: true);
        var newIdValues = new List<AssetTypeValueField>(idValueElems.Count + 1);
        newIdValues.AddRange(idValueElems);
        newIdValues.Add(newIdValueElem);
        ReplaceArrayElements(idValuesArray, newIdValues, cloneElements: false);
    }

    // 写入歌曲 Id Element。
    private static void WriteSongIdElement(AssetTypeValueField idElem, int songId)
    {
        idElem = UnwrapDataField(idElem);
        RequireSetNumberField(idElem, "Value", songId);
    }

    // 写入字符串 数组 Element。
    private static void WriteStringArrayElement(AssetTypeValueField elem, string value)
    {
        elem = UnwrapDataField(elem);
        EnsureFieldValueInitialized(elem);
        try { elem.AsString = value; return; } catch { }
        try { elem.Value = new AssetTypeValue(value); return; } catch { }
        throw new Exception($"无法写入字符串数组元素：FieldName={SafeFieldName(elem)}");
    }

    // 写入本地化 字符串 值。
    private static void WriteLocalizedStringValue(AssetTypeValueField idValueElem, string englishText, bool clearOtherLanguages)
    {
        idValueElem = UnwrapDataField(idValueElem);
        RequireSetStringField(idValueElem, "English", englishText ?? "");
        if (!clearOtherLanguages) return;

        TrySetStringField(idValueElem, "Japanese", "");
        TrySetStringField(idValueElem, "Korean", "");
        TrySetStringField(idValueElem, "TraditionalChinese", "");
        TrySetStringField(idValueElem, "SimplifiedChinese", "");
    }

    // 尝试读取原始 DynamicStringMapping MonoBehaviour。
    private static RawDynamicStringMappingMonoBehaviourData? TryReadRawDynamicStringMappingMonoBehaviour(AssetsFileInstance assetsInst, AssetFileInfo info)
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

            string objectName;
            int structureOffset;
            using (var ms = new MemoryStream(rawBytes, writable: false))
            using (var r = new AssetsFileReader(ms) { BigEndian = bigEndian })
            {
                _ = r.ReadInt32(); // m_GameObject.m_FileID
                _ = r.ReadInt64(); // m_GameObject.m_PathID
                if (r.BaseStream.ReadByte() < 0) return null; // m_Enabled
                r.Align();
                _ = r.ReadInt32(); // m_Script.m_FileID
                _ = r.ReadInt64(); // m_Script.m_PathID
                objectName = r.ReadCountStringInt32();
                r.Align();
                structureOffset = checked((int)r.Position);
            }

            foreach (var encoding in Enum.GetValues(typeof(RawDynamicStringMappingIdEncoding)).Cast<RawDynamicStringMappingIdEncoding>())
            {
                try
                {
                    using var ms = new MemoryStream(rawBytes, writable: false);
                    using var r = new AssetsFileReader(ms) { BigEndian = bigEndian };
                    r.Position = structureOffset;

                    var mappings = new List<RawDynamicStringMappingMonoBehaviourData.StringTypeMapping>(DynamicStringMappingStructureFieldOrder.Length);
                    foreach (string fieldName in DynamicStringMappingStructureFieldOrder)
                    {
                        mappings.Add(ReadRawDynamicStringTypeMapping(r, fieldName, encoding));
                    }

                    int structureEndOffset = checked((int)r.Position);
                    if (structureEndOffset <= structureOffset || structureEndOffset > rawBytes.Length)
                        continue;

                    return new RawDynamicStringMappingMonoBehaviourData
                    {
                        OriginalBytes = rawBytes,
                        BigEndian = bigEndian,
                        ObjectName = objectName,
                        StructureOffset = structureOffset,
                        StructureEndOffset = structureEndOffset,
                        IdEncoding = encoding,
                        Mappings = mappings
                    };
                }
                catch
                {
                    // 尝试下一种 Id 编码布局。
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static RawDynamicStringMappingMonoBehaviourData.StringTypeMapping ReadRawDynamicStringTypeMapping(
        AssetsFileReader r,
        string fieldName,
        RawDynamicStringMappingIdEncoding idEncoding)
    {
        var idKind = GetDynamicStringMappingIdsKind(fieldName);
        var idsU16 = new List<ushort>();
        var idsStr = new List<string>();
        var idsI32 = new List<int>();

        switch (idKind)
        {
            case RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedUInt16:
                idsU16 = ReadRawDynamicStringWrappedUInt16IdArray(r, idEncoding);
                break;
            case RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedString:
                idsStr = ReadRawDynamicStringWrappedStringIdArray(r);
                break;
            case RawDynamicStringMappingMonoBehaviourData.IdsKind.PlainInt32:
                idsI32 = ReadRawDynamicStringPlainInt32IdArray(r);
                break;
            default:
                throw new Exception($"未知 DynamicStringMapping Ids 类型：{idKind}");
        }

        var idStr = ReadRawDynamicStringStringArray(r);
        var idValues = ReadRawDynamicStringLocalizedValueArray(r);

        int idCount = idKind switch
        {
            RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedUInt16 => idsU16.Count,
            RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedString => idsStr.Count,
            RawDynamicStringMappingMonoBehaviourData.IdsKind.PlainInt32 => idsI32.Count,
            _ => -1
        };
        if (idCount != idStr.Count || idCount != idValues.Count)
            throw new Exception($"{fieldName} 数组数量不一致：Ids={idCount}, IdStr={idStr.Count}, IdValues={idValues.Count}");

        return new RawDynamicStringMappingMonoBehaviourData.StringTypeMapping
        {
            FieldName = fieldName,
            IdKind = idKind,
            IdsUInt16 = idsU16,
            IdsString = idsStr,
            IdsInt32 = idsI32,
            IdStr = idStr,
            IdValues = idValues
        };
    }

    // 获取DynamicStringMapping Ids 类型。
    private static RawDynamicStringMappingMonoBehaviourData.IdsKind GetDynamicStringMappingIdsKind(string fieldName)
    {
        if (string.Equals(fieldName, "rawStoryNameTypeMapping", StringComparison.Ordinal))
            return RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedString;
        if (string.Equals(fieldName, "cardNameTypeMapping", StringComparison.Ordinal))
            return RawDynamicStringMappingMonoBehaviourData.IdsKind.PlainInt32;
        return RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedUInt16;
    }

    // 读取原始 动态 字符串 Wrapped U Int 16 Id 数组。
    private static List<ushort> ReadRawDynamicStringWrappedUInt16IdArray(AssetsFileReader r, RawDynamicStringMappingIdEncoding encoding)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new Exception($"DynamicStringMapping.Ids 数量异常：{count}");

        var list = new List<ushort>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(r.ReadUInt16());
            if (encoding == RawDynamicStringMappingIdEncoding.UInt16Align4PerElement)
                r.Align();
        }

        if (encoding == RawDynamicStringMappingIdEncoding.PackedUInt16ArrayAlign4)
            r.Align();

        return list;
    }

    // 读取原始 动态 字符串 Wrapped 字符串 Id 数组。
    private static List<string> ReadRawDynamicStringWrappedStringIdArray(AssetsFileReader r)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new Exception($"DynamicStringMapping.Ids(包装字符串) 数量异常：{count}");

        var list = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(r.ReadCountStringInt32());
            r.Align();
        }
        return list;
    }

    // 读取原始 动态 字符串 Plain Int 32 Id 数组。
    private static List<int> ReadRawDynamicStringPlainInt32IdArray(AssetsFileReader r)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new Exception($"DynamicStringMapping.Ids(int) 数量异常：{count}");

        var list = new List<int>(count);
        for (int i = 0; i < count; i++)
            list.Add(r.ReadInt32());
        return list;
    }

    // 读取原始 动态 字符串 字符串 数组。
    private static List<string> ReadRawDynamicStringStringArray(AssetsFileReader r)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new Exception($"DynamicStringMapping.StringArray 数量异常：{count}");

        var list = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(r.ReadCountStringInt32());
            r.Align();
        }
        return list;
    }

    // 读取原始 动态 字符串 本地化值 数组。
    private static List<RawDynamicStringMappingMonoBehaviourData.LocalizedValue> ReadRawDynamicStringLocalizedValueArray(AssetsFileReader r)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new Exception($"DynamicStringMapping.IdValues 数量异常：{count}");

        var list = new List<RawDynamicStringMappingMonoBehaviourData.LocalizedValue>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ReadRawDynamicStringLocalizedValue(r));
        }
        return list;
    }

    // 读取原始 动态 字符串 本地化值。
    private static RawDynamicStringMappingMonoBehaviourData.LocalizedValue ReadRawDynamicStringLocalizedValue(AssetsFileReader r)
    {
        string english = r.ReadCountStringInt32();
        r.Align();
        string japanese = r.ReadCountStringInt32();
        r.Align();
        string korean = r.ReadCountStringInt32();
        r.Align();
        string traditionalChinese = r.ReadCountStringInt32();
        r.Align();
        string simplifiedChinese = r.ReadCountStringInt32();
        r.Align();

        return new RawDynamicStringMappingMonoBehaviourData.LocalizedValue
        {
            English = english,
            Japanese = japanese,
            Korean = korean,
            TraditionalChinese = traditionalChinese,
            SimplifiedChinese = simplifiedChinese
        };
    }

    private static void UpsertRawDynamicStringTypeMappingEntry(
        RawDynamicStringMappingMonoBehaviourData raw,
        string mappingFieldName,
        ushort songId,
        string englishText)
    {
        var mapping = raw.Mappings.FirstOrDefault(m => string.Equals(m.FieldName, mappingFieldName, StringComparison.Ordinal))
            ?? throw new Exception($"DynamicStringMapping 中未找到字段：{mappingFieldName}");

        if (mapping.IdKind != RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedUInt16)
            throw new Exception($"{mappingFieldName} 的 Ids 类型不是 SongId/UInt16，无法按歌曲 ID 写入。");

        if (mapping.IdsUInt16.Count != mapping.IdStr.Count || mapping.IdsUInt16.Count != mapping.IdValues.Count)
            throw new Exception($"{mappingFieldName} 结构异常：Ids/IdStr/IdValues 数量不一致。");

        int index = mapping.IdsUInt16.FindIndex(x => x == songId);
        if (index >= 0)
        {
            mapping.IdStr[index] = englishText ?? "";
            mapping.IdValues[index].English = englishText ?? "";
            return;
        }

        mapping.IdsUInt16.Add(songId);
        mapping.IdStr.Add(englishText ?? "");
        mapping.IdValues.Add(new RawDynamicStringMappingMonoBehaviourData.LocalizedValue
        {
            English = englishText ?? "",
            Japanese = "",
            Korean = "",
            TraditionalChinese = "",
            SimplifiedChinese = ""
        });
    }

    // 构建Updated 原始 DynamicStringMapping MonoBehaviour。
    private static byte[] BuildUpdatedRawDynamicStringMappingMonoBehaviour(RawDynamicStringMappingMonoBehaviourData raw)
    {
        using var ms = new MemoryStream(raw.OriginalBytes.Length + 4096);
        ms.Write(raw.OriginalBytes, 0, raw.StructureOffset);

        using (var w = new AssetsFileWriter(ms) { BigEndian = raw.BigEndian })
        {
            foreach (var mapping in raw.Mappings)
            {
                WriteRawDynamicStringTypeMapping(w, mapping, raw.IdEncoding);
            }
        }

        if (raw.StructureEndOffset < raw.OriginalBytes.Length)
            ms.Write(raw.OriginalBytes, raw.StructureEndOffset, raw.OriginalBytes.Length - raw.StructureEndOffset);

        return ms.ToArray();
    }

    private static void WriteRawDynamicStringTypeMapping(
        AssetsFileWriter w,
        RawDynamicStringMappingMonoBehaviourData.StringTypeMapping mapping,
        RawDynamicStringMappingIdEncoding idEncoding)
    {
        int idCount = mapping.IdKind switch
        {
            RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedUInt16 => mapping.IdsUInt16.Count,
            RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedString => mapping.IdsString.Count,
            RawDynamicStringMappingMonoBehaviourData.IdsKind.PlainInt32 => mapping.IdsInt32.Count,
            _ => -1
        };
        if (idCount != mapping.IdStr.Count || idCount != mapping.IdValues.Count)
            throw new Exception($"{mapping.FieldName} 结构异常：Ids/IdStr/IdValues 数量不一致。");

        switch (mapping.IdKind)
        {
            case RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedUInt16:
                w.Write(mapping.IdsUInt16.Count);
                foreach (ushort id in mapping.IdsUInt16)
                {
                    w.Write(id);
                    if (idEncoding == RawDynamicStringMappingIdEncoding.UInt16Align4PerElement)
                        w.Align();
                }
                if (idEncoding == RawDynamicStringMappingIdEncoding.PackedUInt16ArrayAlign4)
                    w.Align();
                break;
            case RawDynamicStringMappingMonoBehaviourData.IdsKind.WrappedString:
                w.Write(mapping.IdsString.Count);
                foreach (string id in mapping.IdsString)
                {
                    w.WriteCountStringInt32(id ?? "");
                    w.Align();
                }
                break;
            case RawDynamicStringMappingMonoBehaviourData.IdsKind.PlainInt32:
                w.Write(mapping.IdsInt32.Count);
                foreach (int id in mapping.IdsInt32)
                    w.Write(id);
                break;
            default:
                throw new Exception($"未知 DynamicStringMapping Ids 类型：{mapping.IdKind}");
        }

        w.Write(mapping.IdStr.Count);
        foreach (var s in mapping.IdStr)
        {
            w.WriteCountStringInt32(s ?? "");
            w.Align();
        }

        w.Write(mapping.IdValues.Count);
        foreach (var lv in mapping.IdValues)
        {
            WriteRawDynamicStringLocalizedValue(w, lv);
        }
    }

    // 写入原始 动态 字符串 本地化值。
    private static void WriteRawDynamicStringLocalizedValue(AssetsFileWriter w, RawDynamicStringMappingMonoBehaviourData.LocalizedValue value)
    {
        w.WriteCountStringInt32(value.English ?? "");
        w.Align();
        w.WriteCountStringInt32(value.Japanese ?? "");
        w.Align();
        w.WriteCountStringInt32(value.Korean ?? "");
        w.Align();
        w.WriteCountStringInt32(value.TraditionalChinese ?? "");
        w.Align();
        w.WriteCountStringInt32(value.SimplifiedChinese ?? "");
        w.Align();
    }

    // 解析Streaming 资源 映射 宿主 路径。
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

    // 查找Streaming 资源 映射 文本 资源。
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

    // 查找Streaming 资源 映射 MonoBehaviour 资源。
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

    // 尝试获取Base 字段 安全。
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

    // 准备资源 Manager For Base 字段 Reading。
    private static void PrepareAssetsManagerForBaseFieldReading(AssetsManager am, AssetsFileInstance assetsInst)
    {
        // 当前项目运行环境未提供 classdata.tpk，调用 LoadClassDatabaseFromPackage 会在 AssetsTools.NET 内部抛空引用。
        // 为避免 VS 在“引发异常时中断”，sharedassets 的 Mapping 读取统一走 TextAsset 原始字节回退方案。
        _ = am;
        _ = assetsInst;
    }

    // 判断内容是否符合Streaming 资源 映射 Json特征。
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

    // 判断内容是否符合Streaming 资源 映射 Json特征。
    private static bool LooksLikeStreamingAssetsMappingJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("FullLookupPath", StringComparison.Ordinal) &&
               text.Contains("Guid", StringComparison.Ordinal) &&
               text.Contains("FileLength", StringComparison.Ordinal);
    }

    // 尝试读取原始 Streaming 资源 映射 MonoBehaviour。
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

    // 尝试读取原始 文本 资源 数据。
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

    // 构建Updated 原始 文本 资源 字节。
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

    // 读取精确 字节。
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

    // 解码文本 资源 字节。
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
        bool enableStructureSummaryDiagnostics = false;
        string beforeSummary = enableStructureSummaryDiagnostics
            ? SafeBuildSongDatabaseArrayStructureSummary(baseField, request.SelectedSlot.SlotIndex, "写前")
            : "";

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

            info.SetNewData(baseField);
            if (!enableStructureSummaryDiagnostics)
                return "";

            string afterSummary = SafeBuildSongDatabaseArrayStructureSummary(baseField, request.SelectedSlot.SlotIndex, "写后");
            return beforeSummary + "\n" + afterSummary;
        }
        catch (Exception ex)
        {
            if (!enableStructureSummaryDiagnostics)
                throw;

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

    // 处理Upsert 谱面 Id 曲绘 材质 条目。
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

    // 处理Upsert 歌曲 Id 曲绘 材质 条目。
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

    // 设置条目 字符串 字段。
    private static void SetEntryStringField(AssetTypeValueField entry, string fieldName, string value)
    {
        entry = UnwrapDataField(entry);
        if (TrySetStringField(entry, fieldName, value))
            return;
        throw new Exception($"无法写入条目字段 {fieldName}（字符串）。");
    }

    // 设置条目 歌曲 Id 字段。
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

    // 设置条目 P Ptr 字段。
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

    // 查找谱面 Info Template。
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

    // 查找歌曲 Info Template。
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

    // 获取歌曲 Info 谱面 Count。
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

    // 解包数据 字段包装层。
    private static AssetTypeValueField UnwrapDataField(AssetTypeValueField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return TryGetField(field, "data", out var data) ? data : field;
    }

    // 校验歌曲 数据 歌曲 Ids Unique是否有效。
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

    // 校验歌曲 数据 曲绘 Lookup Keys Unique是否有效。
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

    // 处理Dump 歌曲 Id 曲绘 材质 条目。
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

    // 处理Dump 谱面 Id 曲绘 材质 条目。
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

    // 去重歌曲 数据 曲绘 Lookups。
    private static void DeduplicateSongDataJacketLookups(AssetTypeValueField songDbBaseField)
    {
        DeduplicateSongIdJacketMaterials(songDbBaseField);
        DeduplicateChartIdJacketMaterials(songDbBaseField);
    }

    // 去重歌曲 Id 曲绘 材质。
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

    // 去重谱面 Id 曲绘 材质。
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

    // 重写材质 纹理 Reference。
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

    // 枚举字段。
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

    // 处理Add Cloned 资源 From Template。
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

    // 获取Next 路径 Id。
    private static long GetNextPathId(AssetsFile assetsFile)
    {
        long max = 0;
        foreach (var info in assetsFile.AssetInfos)
            if (info.PathId > max) max = info.PathId;
        if (max < 1) max = 1;
        return checked(max + 1);
    }

    private static int ParseMaybeInt(string text) => int.TryParse(text, out int v) ? v : 0;

    // 获取并确保字段存在。
    private static AssetTypeValueField RequireField(AssetTypeValueField parent, string fieldName)
    {
        if (!TryGetField(parent, fieldName, out var field))
            throw new Exception($"字段不存在：{fieldName}");
        return field;
    }

    // 尝试获取字段。
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

    // 获取并确保数组 字段存在。
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

    // 获取数组 Elements。
    private static List<AssetTypeValueField> GetArrayElements(AssetTypeValueField arrayField)
    {
        if (arrayField.Children == null || arrayField.Children.Count == 0)
            return new List<AssetTypeValueField>();
        int sizeIndex = FindArraySizeFieldIndex(arrayField.Children);
        if (sizeIndex < 0)
            return arrayField.Children.ToList();
        return arrayField.Children.Where((_, idx) => idx != sizeIndex).ToList();
    }

    // 处理Replace 数组 Elements。
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

    // 查找数组 Size 字段 Index。
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

    // 判断内容是否符合数组 Size 字段特征。
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

    // 安全处理Build 歌曲 数据库 数组 Structure Summary。
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

    // 构建歌曲 数据库 数组 Structure Summary。
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

    // 追加数组 Structure Summary。
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

    // 安全处理字段 Name。
    private static string SafeFieldName(AssetTypeValueField field)
    {
        try { return field.FieldName ?? "<null>"; } catch { return "<err>"; }
    }

    // 安全处理Template Name。
    private static string SafeTemplateName(AssetTypeValueField field)
    {
        try { return field.TemplateField?.Name ?? "<null>"; } catch { return "<err>"; }
    }

    // 创建Synthetic 数组 Size 字段。
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

    // 尝试读取字符串 字段。
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

    // 尝试读取数值 字段。
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

    // 遍历路径。
    private static AssetTypeValueField TraversePath(AssetTypeValueField parent, params string[] path)
    {
        var cur = parent;
        foreach (var p in path) cur = RequireField(cur, p);
        return cur;
    }

    // 处理Ensure 字段 值 Initialized。
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

    // 获取并确保Set 数值 字段 Direct存在。
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

    // 获取并确保Set 数值 字段存在。
    private static void RequireSetNumberField(AssetTypeValueField parent, string fieldName, long value)
    {
        if (!TrySetNumberField(parent, fieldName, value))
            throw new Exception($"无法写入字段 {fieldName}（数值：{value}）。");
    }

    // 获取并确保Set 字符串 字段存在。
    private static void RequireSetStringField(AssetTypeValueField parent, string fieldName, string value)
    {
        if (!TrySetStringField(parent, fieldName, value))
            throw new Exception($"无法写入字段 {fieldName}（字符串）。");
    }

    // 获取并确保Set 字节 数组 字段存在。
    private static void RequireSetByteArrayField(AssetTypeValueField parent, string fieldName, byte[] bytes)
    {
        if (!TrySetByteArrayField(parent, fieldName, bytes))
            throw new Exception($"无法写入字段 {fieldName}（字节数组长度：{bytes.Length}）。");
    }

    // 尝试写入数值 字段。
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

    // 尝试写入浮点 Or 双精度 字段。
    private static bool TrySetFloatOrDoubleField(AssetTypeValueField parent, string fieldName, double value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsDouble = value; return true; } catch { }
        try { field.AsFloat = (float)value; return true; } catch { }
        try { field.Value = new AssetTypeValue((float)value); return true; } catch { }
        return false;
    }

    // 尝试写入字符串 字段。
    private static bool TrySetStringField(AssetTypeValueField parent, string fieldName, string value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsString = value; return true; } catch { }
        try { field.Value = new AssetTypeValue(value); return true; } catch { return false; }
    }

    // 尝试写入字节 数组 字段。
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

    // 加载预处理 图片。
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

    // 调整预处理 图片 If Needed尺寸。
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

    // 获取Preferred 纹理 Formats。
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

    // 尝试编码Managed。
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

    // 处理Bgra To Unity Rgba 原始。
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

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    // 应用编码后 纹理 数据修改。
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

    // 处理Force Apply 编码后 纹理 数据 To Base 字段。
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

    // 尝试写入布尔 字段。
    private static bool TrySetBoolField(AssetTypeValueField parent, string fieldName, bool value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;
        EnsureFieldValueInitialized(field);
        try { field.AsBool = value; return true; } catch { }
        try { field.Value = new AssetTypeValue(value); return true; } catch { return false; }
    }

    // 追加映射 条目。
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

    // 读取文本 资源 Content。
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

    // 写入文本 资源 Content。
    private static void WriteTextAssetContent(AssetTypeValueField baseField, string content)
    {
        if (TrySetStringField(baseField, "m_Script", content)) return;
        if (TrySetByteArrayField(baseField, "m_Script", Encoding.UTF8.GetBytes(content))) return;
        if (TrySetStringField(baseField, "m_Text", content)) return;
        if (TrySetByteArrayField(baseField, "m_Text", Encoding.UTF8.GetBytes(content))) return;
        throw new Exception("无法写入 TextAsset 文本内容。");
    }

    private static string DeployOutputsToGameDirectories(
        string bundleSourcePath,
        string sharedAssetsSourcePath,
        string resourcesAssetsSourcePath,
        string exportedBundleBackupPath,
        string exportedSharedAssetsBackupPath,
        string exportedResourcesAssetsBackupPath,
        string backupDirectory,
        IReadOnlyList<GeneratedResourceFile> generatedFiles)
    {
        string dataDir = Path.GetDirectoryName(sharedAssetsSourcePath)
            ?? throw new Exception($"无法确定 sharedassets0.assets 所在目录：{sharedAssetsSourcePath}");
        string samDir = Path.Combine(dataDir, "StreamingAssets", "sam");
        Directory.CreateDirectory(samDir);

        var lines = new List<string>();

        DeployOneFileWithOriginalBackup(exportedSharedAssetsBackupPath, sharedAssetsSourcePath, lines, "sharedassets0.assets");
        DeployOneFileWithOriginalBackup(exportedResourcesAssetsBackupPath, resourcesAssetsSourcePath, lines, "resources.assets");
        DeployOneFileWithOriginalBackup(exportedBundleBackupPath, bundleSourcePath, lines, "歌曲数据库 bundle");

        foreach (var file in generatedFiles)
        {
            string backupPath = Path.Combine(backupDirectory, file.MappingEntry.Guid);
            string livePath = Path.Combine(samDir, file.MappingEntry.Guid);
            DeployOneFileWithOriginalBackup(backupPath, livePath, lines, $"加密资源 {file.MappingEntry.FullLookupPath}");
        }

        return string.Join("\n", lines);
    }

    private static void DeployOneFileWithOriginalBackup(
        string exportedBackupPath,
        string liveTargetPath,
        List<string> logLines,
        string label)
    {
        if (!File.Exists(exportedBackupPath))
            throw new FileNotFoundException($"导出备份文件不存在：{exportedBackupPath}");

        string? targetDir = Path.GetDirectoryName(liveTargetPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
            Directory.CreateDirectory(targetDir);

        string backupPath = liveTargetPath + "_original";
        bool createdOriginalBackup = false;
        if (File.Exists(liveTargetPath))
        {
            if (!File.Exists(backupPath))
            {
                File.Move(liveTargetPath, backupPath);
                createdOriginalBackup = true;
            }
            else
            {
                File.Delete(liveTargetPath);
            }
        }

        File.Copy(exportedBackupPath, liveTargetPath, overwrite: true);

        if (createdOriginalBackup)
        {
            logLines.Add($"- {label}：已备份原文件 -> {backupPath}");
        }
        else if (File.Exists(backupPath))
        {
            logLines.Add($"- {label}：使用现有 *_original 备份，已覆盖写入 -> {liveTargetPath}");
        }
        else
        {
            logLines.Add($"- {label}：目标原先不存在，已写入 -> {liveTargetPath}");
        }
    }

    // 恢复Deployed 歌曲 文件。
    public static string RestoreDeployedSongFiles(string gameRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameRootDirectory))
            throw new ArgumentException("游戏根目录不能为空。", nameof(gameRootDirectory));

        string gameRoot = Path.GetFullPath(gameRootDirectory);
        if (!Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException($"游戏根目录不存在：{gameRoot}");

        string dataDir = Path.Combine(gameRoot, "if-app_Data");
        if (!Directory.Exists(dataDir))
            throw new DirectoryNotFoundException($"未找到 if-app_Data：{dataDir}");

        string songDataDir = Path.Combine(gameRoot, "SongData");
        string samDir = Path.Combine(dataDir, "StreamingAssets", "sam");
        string bundleDir = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");

        var lines = new List<string>();
        int restoredCount = 0;
        int deletedCount = 0;
        int skippedCount = 0;

        // 固定文件：sharedassets0.assets / resources.assets
        TryRestoreOneLiveFile(Path.Combine(dataDir, "sharedassets0.assets"), "sharedassets0.assets", lines, ref restoredCount, ref deletedCount, ref skippedCount);
        TryRestoreOneLiveFile(Path.Combine(dataDir, "resources.assets"), "resources.assets", lines, ref restoredCount, ref deletedCount, ref skippedCount);

        // 歌曲数据库 bundle：恢复该目录下 *_original 的 bundle（工具通常只会改一个固定 bundle）。
        if (Directory.Exists(bundleDir))
        {
            foreach (var originalPath in Directory.EnumerateFiles(bundleDir, "*.bundle_original", SearchOption.TopDirectoryOnly)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string livePath = originalPath[..^"_original".Length];
                TryRestoreOneLiveFile(livePath, $"歌曲数据库 bundle ({Path.GetFileName(livePath)})", lines, ref restoredCount, ref deletedCount, ref skippedCount);
            }
        }
        else
        {
            lines.Add($"- 歌曲数据库 bundle：未找到目录，跳过 -> {bundleDir}");
            skippedCount++;
        }

        // sam：以 SongData 备份目录中的 32 位哈希文件名为依据回滚/删除部署到 sam 的文件。
        if (!Directory.Exists(songDataDir))
        {
            lines.Add($"- SongData 备份目录不存在，无法推断需清理的 sam 资源 -> {songDataDir}");
            skippedCount++;
        }
        else if (!Directory.Exists(samDir))
        {
            lines.Add($"- sam 目录不存在，跳过清理 -> {samDir}");
            skippedCount++;
        }
        else
        {
            var backupNames = Directory.EnumerateFiles(songDataDir, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(IsHexFileName32)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (backupNames.Count == 0)
            {
                lines.Add("- sam 清理：SongData 中未找到 32 位哈希备份文件，未执行删除。");
                skippedCount++;
            }
            else
            {
                foreach (var name in backupNames)
                {
                    string livePath = Path.Combine(samDir, name!);
                    string originalPath = livePath + "_original";

                    if (File.Exists(originalPath))
                    {
                        if (File.Exists(livePath))
                        {
                            File.Delete(livePath);
                            deletedCount++;
                        }
                        File.Move(originalPath, livePath, overwrite: true);
                        restoredCount++;
                        lines.Add($"- sam 资源 {name}：已恢复 *_original 备份。");
                        continue;
                    }

                    if (File.Exists(livePath))
                    {
                        File.Delete(livePath);
                        deletedCount++;
                        lines.Add($"- sam 资源 {name}：已删除新增文件。");
                    }
                    else
                    {
                        skippedCount++;
                        lines.Add($"- sam 资源 {name}：未找到，跳过。");
                    }
                }
            }
        }

        lines.Insert(0, $"恢复完成：恢复 {restoredCount} 项，删除 {deletedCount} 项，跳过 {skippedCount} 项。");
        return string.Join("\n", lines);
    }

    private static void TryRestoreOneLiveFile(
        string livePath,
        string label,
        List<string> lines,
        ref int restoredCount,
        ref int deletedCount,
        ref int skippedCount)
    {
        string originalPath = livePath + "_original";
        bool hasOriginal = File.Exists(originalPath);
        bool hasLive = File.Exists(livePath);

        if (!hasOriginal)
        {
            lines.Add($"- {label}：未找到 *_original 备份，跳过。");
            skippedCount++;
            return;
        }

        if (hasLive)
        {
            File.Delete(livePath);
            deletedCount++;
        }

        File.Move(originalPath, livePath, overwrite: true);
        restoredCount++;
        lines.Add($"- {label}：已恢复原文件 -> {livePath}");
    }

    // 判断是否满足Hex 文件 Name 32条件。
    private static bool IsHexFileName32(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName!.Length != 32)
            return false;

        for (int i = 0; i < fileName.Length; i++)
        {
            char c = fileName[i];
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    // 准备输出 计划。
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

    // 完成输出 计划收尾处理。
    private static void FinalizeOutputPlan(OutputPlan plan)
    {
        if (!plan.ReplaceAfterPack) return;
        if (!File.Exists(plan.PackOutputPath))
            throw new FileNotFoundException($"临时导出文件不存在：{plan.PackOutputPath}");
        try
        {
            // 同盘临时文件收尾优先使用 Move，避免再次全量复制大型 bundle/assets。
            File.Move(plan.PackOutputPath, plan.FinalOutputPath, overwrite: true);
        }
        catch
        {
            File.Copy(plan.PackOutputPath, plan.FinalOutputPath, overwrite: true);
            File.Delete(plan.PackOutputPath);
        }
    }

    // 构建Temporary Export 路径。
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

    // 查找Available Alternative 路径。
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

    // 判断是否可Overwrite 路径。
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

    // 写入资源 文件 Applying Replacers。
    private static void WriteAssetsFileApplyingReplacers(AssetsFile assetsFile, string outputPath)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new AssetsFileWriter(fs);
        assetsFile.Write(writer, 0);
    }

    // 写入Bundle Applying Replacers。
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

    // 安全处理Get Bundle 条目 Name。
    private static string SafeGetBundleEntryName(AssetBundleFile bundleFile, int idx)
    {
        try { return bundleFile.BlockAndDirInfo.DirectoryInfos[idx].Name ?? $"file_{idx}"; }
        catch { return $"file_{idx}"; }
    }

    // 校验Bundle 路径是否有效。
    private static void ValidateBundlePath(string bundleFilePath)
    {
        if (string.IsNullOrWhiteSpace(bundleFilePath))
            throw new Exception("请先导入 .bundle 文件。");
        if (!File.Exists(bundleFilePath))
            throw new FileNotFoundException($"bundle 文件不存在：{bundleFilePath}");
    }

    // 校验图片 路径是否有效。
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

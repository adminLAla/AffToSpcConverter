using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace AffToSpcConverter.Utils;

public sealed class BundleTextureEntry
{
    // bundle 内部 assets 文件索引（用于写回定位）。
    public required int BundleFileIndex { get; init; }

    // bundle 内部 assets 文件名（仅用于显示）。
    public required string AssetsFileName { get; init; }

    // Texture2D 对象的 pathID（用于精确定位）。
    public required long PathId { get; init; }

    // 纹理名（m_Name）。
    public required string TextureName { get; init; }

    // 当前纹理宽度。
    public required int Width { get; init; }

    // 当前纹理高度。
    public required int Height { get; init; }

    // 当前纹理格式原始整数值。
    public required int TextureFormatValue { get; init; }

    // 当前纹理格式名称（若能识别）。
    public required string TextureFormatName { get; init; }

    // 是否使用 streamData（常见于 .resS 外置数据）。
    public required bool UsesStreamData { get; init; }

    // 下拉框显示文本。
    public string DisplayText =>
        $"{TextureName}  [{Width}x{Height}, {TextureFormatName}{(UsesStreamData ? ", UsesStreamData" : "")}]  ({AssetsFileName}, PathID={PathId})";

    public override string ToString() => DisplayText;
}

public sealed class BundleTextureExportResult
{
    // 导出后的 bundle 完整路径。
    public required string OutputBundlePath { get; init; }

    // 请求导出的原始目标路径（未自动另存为前）。
    public required string RequestedOutputBundlePath { get; init; }

    // 导入图片是否在处理中从 JPG/JPEG 转成了 PNG。
    public required bool InputImageConvertedToPng { get; init; }

    // 最终写入使用的纹理格式名称。
    public required string FinalTextureFormatName { get; init; }

    // 是否因原格式编码失败而回退到 RGBA32。
    public required bool FallbackToRgba32 { get; init; }

    // 是否因为目标文件被占用而自动另存为新文件名。
    public required bool AutoRenamedDueToFileLock { get; init; }

    // 导出路径与源 bundle 相同，是否使用临时文件写入后覆盖。
    public required bool UsedTempFileForInPlaceOverwrite { get; init; }

    // 输入图片原始宽度。
    public required int InputImageWidth { get; init; }

    // 输入图片原始高度。
    public required int InputImageHeight { get; init; }

    // 实际写入纹理数据宽度。
    public required int OutputImageWidth { get; init; }

    // 实际写入纹理数据高度。
    public required int OutputImageHeight { get; init; }

    // 是否为匹配目标纹理尺寸而做了缩放。
    public required bool ImageResizedToMatchTexture { get; init; }

    // 导出后自动回读校验结果。
    public required bool ReadbackVerified { get; init; }

    // 导出后自动回读校验摘要。
    public required string ReadbackSummary { get; init; }
}

public static class UnityBundleTexturePacker
{
    // 读取未加密 Unity bundle 中所有 Texture2D，供用户选择替换目标。
    public static IReadOnlyList<BundleTextureEntry> ListTextures(string bundleFilePath)
    {
        ValidateBundlePath(bundleFilePath);

        var am = new AssetsManager();
        try
        {
            var bunInst = am.LoadBundleFile(bundleFilePath, unpackIfPacked: true);
            var result = new List<BundleTextureEntry>();

            int fileCount = bunInst.file.BlockAndDirInfo.DirectoryInfos.Count;
            for (int bundleFileIndex = 0; bundleFileIndex < fileCount; bundleFileIndex++)
            {
                if (!bunInst.file.IsAssetsFile(bundleFileIndex))
                    continue;

                var assetsInst = am.LoadAssetsFileFromBundle(bunInst, bundleFileIndex, loadDeps: false);
                string assetsFileName = SafeGetBundleEntryName(bunInst.file, bundleFileIndex);

                foreach (var assetInfo in assetsInst.file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    try
                    {
                        var baseField = am.GetBaseField(assetsInst, assetInfo, AssetReadFlags.None);
                        var texture = TextureFile.ReadTextureFile(baseField);

                        int formatValue = texture.m_TextureFormat;
                        string formatName = Enum.IsDefined(typeof(TextureFormat), formatValue)
                            ? ((TextureFormat)formatValue).ToString()
                            : $"Unknown({formatValue})";

                        result.Add(new BundleTextureEntry
                        {
                            BundleFileIndex = bundleFileIndex,
                            AssetsFileName = assetsFileName,
                            PathId = assetInfo.PathId,
                            TextureName = string.IsNullOrWhiteSpace(texture.m_Name) ? "<Unnamed>" : texture.m_Name,
                            Width = texture.m_Width,
                            Height = texture.m_Height,
                            TextureFormatValue = formatValue,
                            TextureFormatName = formatName,
                            UsesStreamData = texture.m_StreamData.size > 0 ||
                                             !string.IsNullOrWhiteSpace(texture.m_StreamData.path)
                        });
                    }
                    catch
                    {
                        // 某些 Texture2D（缺少类型树或版本特殊）可能读取失败，跳过后继续列出其余纹理。
                    }
                }
            }

            return result
                .OrderBy(x => x.TextureName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.AssetsFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PathId)
                .ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"读取 bundle 纹理失败：{ex.Message}", ex);
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    // 使用 PNG/JPG 图片替换 bundle 内指定 Texture2D，并导出新的 .bundle 文件。
    public static BundleTextureExportResult ExportBundleWithReplacedTexture(
        string imageFilePath,
        string bundleFilePath,
        BundleTextureEntry targetTexture,
        string outputDirectory,
        bool autoRenameWhenTargetLocked,
        bool verifyReadbackSha256 = true)
    {
        ValidateImagePath(imageFilePath);
        ValidateBundlePath(bundleFilePath);
        if (targetTexture == null) throw new ArgumentNullException(nameof(targetTexture));
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new Exception("请选择导出文件夹。");

        Directory.CreateDirectory(outputDirectory);

        string sourceBundleFullPath = Path.GetFullPath(bundleFilePath);
        string requestedOutputBundlePath = Path.GetFullPath(Path.Combine(outputDirectory, Path.GetFileName(bundleFilePath)));
        var outputPlan = PrepareOutputPlan(sourceBundleFullPath, requestedOutputBundlePath, autoRenameWhenTargetLocked);
        var preparedImage = LoadPreparedImage(imageFilePath);
        int inputImageWidth = preparedImage.Width;
        int inputImageHeight = preparedImage.Height;
        bool inputImageConvertedToPng = preparedImage.WasConvertedFromJpeg;

        string? tempPackFileToDelete = null;
        EncodedTextureImage? encodeResult = null;

        var am = new AssetsManager();
        try
        {
            var bunInst = am.LoadBundleFile(sourceBundleFullPath, unpackIfPacked: true);

            if (targetTexture.BundleFileIndex < 0 ||
                targetTexture.BundleFileIndex >= bunInst.file.BlockAndDirInfo.DirectoryInfos.Count)
            {
                throw new Exception("所选纹理所属的 bundle 文件索引无效。");
            }

            if (!bunInst.file.IsAssetsFile(targetTexture.BundleFileIndex))
                throw new Exception("所选目标不是有效的 assets 文件。");

            var assetsInst = am.LoadAssetsFileFromBundle(bunInst, targetTexture.BundleFileIndex, loadDeps: false);
            var assetInfo = assetsInst.file.GetAssetInfo(targetTexture.PathId);
            if (assetInfo == null)
                throw new Exception("在 bundle 中未找到选中的 Texture2D（可能 bundle 已变化，请重新导入）。");

            var baseField = am.GetBaseField(assetsInst, assetInfo, AssetReadFlags.None);
            var texture = TextureFile.ReadTextureFile(baseField);

            encodeResult = EncodeReplacementImage(preparedImage, texture.m_TextureFormat, texture.m_Width, texture.m_Height);
            // 编码结果已经生成，后续不再需要源图的 PNG/BGRA 缓冲，尽早释放以降低峰值内存。
            preparedImage.ReleaseHeavyBuffers();
            ApplyEncodedTextureData(texture, encodeResult);

            texture.WriteTo(baseField);
            // 某些 UsesStreamData 纹理在 TextureFile.WriteTo 后仍会保留旧格式/旧图像数据。
            // 这里强制回写关键字段，确保 m_TextureFormat / image data / m_StreamData 与预期一致。
            ForceApplyEncodedTextureDataToBaseField(baseField, encodeResult);
            assetInfo.SetNewData(baseField);

            bunInst.file.BlockAndDirInfo.DirectoryInfos[targetTexture.BundleFileIndex].Replacer =
                new ContentReplacerFromAssets(assetsInst);

            tempPackFileToDelete = outputPlan.PackOutputPath;
            WriteBundleApplyingReplacers(
                bunInst.file,
                bunInst.originalCompression,
                outputPlan.PackOutputPath);
        }
        catch (Exception ex)
        {
            throw new Exception($"导出 bundle 失败：{ex.Message}", ex);
        }
        finally
        {
            am.UnloadAll(true);
        }

        try
        {
            FinalizeOutputPlan(outputPlan);
            tempPackFileToDelete = null;
        }
        catch (Exception ex)
        {
            throw new Exception($"导出 bundle 失败：{ex.Message}", ex);
        }
        finally
        {
            // 清理未成功提交的临时导出文件，避免遗留垃圾文件。
            if (!string.IsNullOrWhiteSpace(tempPackFileToDelete) && File.Exists(tempPackFileToDelete))
            {
                try { File.Delete(tempPackFileToDelete); } catch { /* ignore cleanup failure */ }
            }
        }

        if (encodeResult == null)
            throw new Exception("导出 bundle 失败：未生成纹理编码结果。");

        var readback = VerifyExportedTextureReadback(
            outputPlan.FinalOutputPath,
            targetTexture,
            encodeResult,
            verifyReadbackSha256);

        if (!readback.Verified)
        {
            encodeResult.ReleaseEncodedBytes();
            TryTrimManagedMemoryAfterHeavyOperation();
            throw new Exception(
                $"导出文件已生成，但回读校验未通过：{readback.Summary}\n" +
                $"输出文件：{outputPlan.FinalOutputPath}");
        }

        string finalTextureFormatName = encodeResult.FormatName;
        bool fallbackToRgba32 = encodeResult.FallbackToRgba32;
        int outputImageWidth = encodeResult.Width;
        int outputImageHeight = encodeResult.Height;
        bool imageResizedToMatchTexture = encodeResult.ResizedToTarget;
        encodeResult.ReleaseEncodedBytes();
        TryTrimManagedMemoryAfterHeavyOperation();

        return new BundleTextureExportResult
        {
            RequestedOutputBundlePath = outputPlan.RequestedOutputPath,
            OutputBundlePath = outputPlan.FinalOutputPath,
            InputImageConvertedToPng = inputImageConvertedToPng,
            FinalTextureFormatName = finalTextureFormatName,
            FallbackToRgba32 = fallbackToRgba32,
            AutoRenamedDueToFileLock = outputPlan.AutoRenamedDueToLock,
            UsedTempFileForInPlaceOverwrite = outputPlan.ReplaceAfterPack,
            InputImageWidth = inputImageWidth,
            InputImageHeight = inputImageHeight,
            OutputImageWidth = outputImageWidth,
            OutputImageHeight = outputImageHeight,
            ImageResizedToMatchTexture = imageResizedToMatchTexture,
            ReadbackVerified = true,
            ReadbackSummary = readback.Summary
        };
    }

    // 为导出目标路径做预检查：处理文件占用提示、自动另存为、以及“源路径=导出路径”的自占用场景。
    private static OutputPlan PrepareOutputPlan(string sourceBundlePath, string requestedOutputPath, bool autoRenameWhenTargetLocked)
    {
        string sourceFull = Path.GetFullPath(sourceBundlePath);
        string requestedFull = Path.GetFullPath(requestedOutputPath);

        // 导出路径与源路径相同：当前进程读取 bundle 时会持有源文件句柄，必须先写临时文件再覆盖。
        if (string.Equals(sourceFull, requestedFull, StringComparison.OrdinalIgnoreCase))
        {
            // 在打开源 bundle 前先检查一次目标文件是否已被其他进程占用，给出更明确提示。
            if (!CanOverwritePath(requestedFull, out var lockReason))
            {
                if (autoRenameWhenTargetLocked)
                {
                    string altPath = FindAvailableAlternativePath(requestedFull);
                    return new OutputPlan
                    {
                        RequestedOutputPath = requestedFull,
                        FinalOutputPath = altPath,
                        PackOutputPath = altPath,
                        ReplaceAfterPack = false,
                        AutoRenamedDueToLock = true
                    };
                }

                throw new IOException(
                    $"目标文件已被其他进程占用，无法覆盖：{requestedFull}\n{lockReason}\n" +
                    "如需自动避开冲突，请勾选“文件被占用时自动另存为 xxx (1).bundle”。");
            }

            string tempPath = BuildTemporaryExportPath(requestedFull);
            return new OutputPlan
            {
                RequestedOutputPath = requestedFull,
                FinalOutputPath = requestedFull,
                PackOutputPath = tempPath,
                ReplaceAfterPack = true,
                AutoRenamedDueToLock = false
            };
        }

        if (CanOverwritePath(requestedFull, out var reason))
        {
            return new OutputPlan
            {
                RequestedOutputPath = requestedFull,
                FinalOutputPath = requestedFull,
                PackOutputPath = requestedFull,
                ReplaceAfterPack = false,
                AutoRenamedDueToLock = false
            };
        }

        if (autoRenameWhenTargetLocked)
        {
            string altPath = FindAvailableAlternativePath(requestedFull);
            return new OutputPlan
            {
                RequestedOutputPath = requestedFull,
                FinalOutputPath = altPath,
                PackOutputPath = altPath,
                ReplaceAfterPack = false,
                AutoRenamedDueToLock = true
            };
        }

        throw new IOException(
            $"目标文件不可写，无法导出：{requestedFull}\n{reason}\n" +
            "请关闭占用该文件的程序，或启用“文件被占用时自动另存为 xxx (1).bundle”。");
    }

    // 将临时导出文件提交到最终路径（仅在“源路径=导出路径”时使用）。
    private static void FinalizeOutputPlan(OutputPlan plan)
    {
        if (!plan.ReplaceAfterPack) return;

        if (!File.Exists(plan.PackOutputPath))
            throw new FileNotFoundException($"临时导出文件不存在：{plan.PackOutputPath}");

        string targetDir = Path.GetDirectoryName(plan.FinalOutputPath) ?? "";
        if (!string.IsNullOrWhiteSpace(targetDir))
            Directory.CreateDirectory(targetDir);

        // 优先使用覆盖复制，兼容跨卷；失败时给出更明确提示。
        try
        {
            File.Copy(plan.PackOutputPath, plan.FinalOutputPath, overwrite: true);
            File.Delete(plan.PackOutputPath);
        }
        catch (IOException ioEx)
        {
            throw new IOException(
                $"无法覆盖目标文件（可能在导出完成前被其他进程占用）：{plan.FinalOutputPath}\n{ioEx.Message}", ioEx);
        }
    }

    // 先 Write() 应用目录 Replacer，再按原压缩格式 Pack()，避免替换内容在压缩阶段丢失。
    private static void WriteBundleApplyingReplacers(
        AssetBundleFile bundleFile,
        AssetBundleCompressionType compressionType,
        string outputPath)
    {
        if (bundleFile == null) throw new ArgumentNullException(nameof(bundleFile));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("输出路径不能为空。", nameof(outputPath));

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        // 直接写出会正确应用 DirectoryInfos[x].Replacer。
        if (compressionType == AssetBundleCompressionType.None)
        {
            using var directWriter = new AssetsFileWriter(outputPath);
            bundleFile.Write(directWriter, 0);
            return;
        }

        // 对压缩 bundle，先写出替换后的未压缩 bundle，再重新加载进行压缩打包。
        string tempUncompressedPath = BuildTemporaryExportPath(outputPath);
        try
        {
            using (var tempWriter = new AssetsFileWriter(tempUncompressedPath))
            {
                bundleFile.Write(tempWriter, 0);
            }

            var am = new AssetsManager();
            try
            {
                var tempBundleInst = am.LoadBundleFile(tempUncompressedPath, unpackIfPacked: false);
                using var finalWriter = new AssetsFileWriter(outputPath);
                tempBundleInst.file.Pack(finalWriter, compressionType, false, null);
            }
            finally
            {
                am.UnloadAll(true);
            }
        }
        finally
        {
            if (File.Exists(tempUncompressedPath))
            {
                try { File.Delete(tempUncompressedPath); } catch { /* ignore cleanup failure */ }
            }
        }
    }

    // 检测目标文件是否可覆盖；若文件不存在则视为可写。
    private static bool CanOverwritePath(string path, out string reason)
    {
        reason = "";
        if (!File.Exists(path))
            return true;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            reason = "";
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            reason = $"没有写入权限或文件为只读：{ex.Message}";
            return false;
        }
        catch (IOException ex)
        {
            // Windows 共享冲突常见错误码：32/33。
            if (IsSharingViolation(ex))
                reason = "文件当前被其他进程占用。";
            else
                reason = ex.Message;
            return false;
        }
    }

    // 判断 IOException 是否为 Windows 文件共享冲突。
    private static bool IsSharingViolation(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    // 为被占用的目标生成 `name (1).ext` 形式的新文件名。
    private static string FindAvailableAlternativePath(string originalPath)
    {
        string directory = Path.GetDirectoryName(originalPath) ?? "";
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
        string ext = Path.GetExtension(originalPath);

        for (int i = 1; i <= 999; i++)
        {
            string candidate = Path.Combine(directory, $"{fileNameWithoutExt} ({i}){ext}");
            if (CanOverwritePath(candidate, out _))
                return candidate;
        }

        throw new IOException($"无法找到可用的自动另存为文件名：{originalPath}");
    }

    // 为“原地覆盖”生成临时导出路径，避免当前进程读取源 bundle 时的自占用冲突。
    private static string BuildTemporaryExportPath(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath) ?? "";
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(finalPath);
        string ext = Path.GetExtension(finalPath);

        for (int i = 0; i < 1000; i++)
        {
            string suffix = i == 0 ? ".tmp-export" : $".tmp-export-{i}";
            string candidate = Path.Combine(directory, $"{fileNameWithoutExt}{suffix}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException($"无法创建临时导出文件：{finalPath}");
    }

    // 导出后重新打开 bundle，并对目标纹理做回读校验，避免“显示成功但实际未替换”。
    private static ReadbackVerificationResult VerifyExportedTextureReadback(
        string exportedBundlePath,
        BundleTextureEntry targetTexture,
        EncodedTextureImage expected,
        bool verifySha256)
    {
        var am = new AssetsManager();
        try
        {
            var bunInst = am.LoadBundleFile(exportedBundlePath, unpackIfPacked: true);

            if (targetTexture.BundleFileIndex < 0 ||
                targetTexture.BundleFileIndex >= bunInst.file.BlockAndDirInfo.DirectoryInfos.Count)
            {
                return ReadbackVerificationResult.Fail("导出后 bundle 内文件索引越界。");
            }

            if (!bunInst.file.IsAssetsFile(targetTexture.BundleFileIndex))
                return ReadbackVerificationResult.Fail("导出后目标文件不再是 assets 文件。");

            var assetsInst = am.LoadAssetsFileFromBundle(bunInst, targetTexture.BundleFileIndex, loadDeps: false);
            var assetInfo = assetsInst.file.GetAssetInfo(targetTexture.PathId);
            if (assetInfo == null)
                return ReadbackVerificationResult.Fail("导出后找不到目标 Texture2D（PathID 不存在）。");

            var baseField = am.GetBaseField(assetsInst, assetInfo, AssetReadFlags.None);
            var texture = TextureFile.ReadTextureFile(baseField);

            // 先记录导出文件里的原始 streamData 字段；FillPictureData 会在内存里清空它，不能直接拿来判断是否真正写回。
            long rawStreamDataSize = texture.m_StreamData.size;
            string rawStreamDataPath = texture.m_StreamData.path ?? string.Empty;
            bool streamDataCleared = rawStreamDataSize == 0 && string.IsNullOrWhiteSpace(rawStreamDataPath);

            // 仅在需要校验内容哈希时才拉取完整 pictureData，减少不必要的大数组分配。
            if (verifySha256 && (!streamDataCleared || texture.pictureData == null || texture.pictureData.Length == 0))
            {
                try { texture.FillPictureData(assetsInst); } catch { /* ignore */ }
            }

            bool widthOk = texture.m_Width == expected.Width;
            bool heightOk = texture.m_Height == expected.Height;
            bool formatOk = texture.m_TextureFormat == (int)expected.FinalFormat;
            bool dataLenOk = true;

            string actualFormatName = Enum.IsDefined(typeof(TextureFormat), texture.m_TextureFormat)
                ? ((TextureFormat)texture.m_TextureFormat).ToString()
                : $"Unknown({texture.m_TextureFormat})";

            bool dataHashOk = true;
            if (verifySha256)
            {
                dataLenOk = texture.pictureData != null && texture.pictureData.Length == expected.EncodedBytes.Length;
                string expectedHash = ComputeSha256Hex(expected.EncodedBytes);
                string actualHash = ComputeSha256Hex(texture.pictureData ?? Array.Empty<byte>());
                dataHashOk = string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
            }

            if (widthOk && heightOk && formatOk && streamDataCleared && dataLenOk && dataHashOk)
            {
                string okSummary =
                    $"回读校验通过：{texture.m_Width}x{texture.m_Height}, {actualFormatName}, " +
                    (verifySha256
                        ? $"PictureData={texture.pictureData?.Length ?? 0} bytes, StreamData=cleared, SHA256=已校验"
                        : "StreamData=cleared, SHA256=已跳过");
                return ReadbackVerificationResult.Ok(okSummary);
            }

            string failSummary =
                $"尺寸({texture.m_Width}x{texture.m_Height} / 期望 {expected.Width}x{expected.Height})，" +
                $"格式({actualFormatName} / 期望 {expected.FinalFormat})，" +
                $"StreamData={(streamDataCleared ? "cleared" : $"path='{rawStreamDataPath}', size={rawStreamDataSize}")}，" +
                (verifySha256
                    ? $"PictureDataLen={texture.pictureData?.Length ?? 0}/{expected.EncodedBytes.Length}，SHA256={(dataHashOk ? "一致" : "不一致")}"
                    : "SHA256=已跳过");
            return ReadbackVerificationResult.Fail(failSummary);
        }
        catch (Exception ex)
        {
            return ReadbackVerificationResult.Fail($"回读校验异常：{ex.Message}");
        }
        finally
        {
            am.UnloadAll(true);
        }
    }

    // 计算字节数组的 SHA-256，用于导出后回读校验。
    private static string ComputeSha256Hex(byte[] bytes)
    {
        if (bytes.Length == 0) return "EMPTY";
        byte[] hash = SHA256.HashData(bytes);
        return System.Convert.ToHexString(hash);
    }

    // 打包流程会创建大量大对象数组（图片像素、纹理数据、bundle 缓冲）；结束后主动压缩一次 LOH，降低常驻内存。
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
            // 忽略回收优化失败，不影响导出结果。
        }
    }

    // 任务管理器中的“内存”主要反映工作集；导出完成后主动修剪工作集，缓解占用长期停在高位的情况。
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

    // 按当前 bundle 条目索引获取内部文件名，兼容异常场景下的回退显示。
    private static string SafeGetBundleEntryName(AssetBundleFile bundleFile, int bundleFileIndex)
    {
        try
        {
            return bundleFile.GetFileName(bundleFileIndex);
        }
        catch
        {
            return $"<file#{bundleFileIndex}>";
        }
    }

    // 校验导入图片路径与格式（仅允许 PNG/JPG/JPEG）。
    private static void ValidateImagePath(string imageFilePath)
    {
        if (string.IsNullOrWhiteSpace(imageFilePath))
            throw new Exception("请先导入曲绘图片。");
        if (!File.Exists(imageFilePath))
            throw new FileNotFoundException($"图片不存在：{imageFilePath}");

        string ext = Path.GetExtension(imageFilePath).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg")
            throw new Exception("仅支持 PNG 或 JPG/JPEG 图片。");
    }

    // 校验 bundle 路径存在性。
    private static void ValidateBundlePath(string bundleFilePath)
    {
        if (string.IsNullOrWhiteSpace(bundleFilePath))
            throw new Exception("请先导入 .bundle 文件。");
        if (!File.Exists(bundleFilePath))
            throw new FileNotFoundException($"bundle 文件不存在：{bundleFilePath}");
    }

    // 读取输入图片，并准备 PNG 字节流（供原格式编码尝试）与 BGRA32 原始像素（供缩放/回退 RGBA32）。
    private static PreparedImage LoadPreparedImage(string imageFilePath)
    {
        string ext = Path.GetExtension(imageFilePath).ToLowerInvariant();
        bool wasJpeg = ext is ".jpg" or ".jpeg";

        using var fs = File.OpenRead(imageFilePath);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new Exception("无法读取图片内容。");

        var frame = decoder.Frames[0];
        int width = frame.PixelWidth;
        int height = frame.PixelHeight;
        if (width <= 0 || height <= 0)
            throw new Exception("图片尺寸无效。");

        BitmapSource bgraFrame = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int stride = width * 4;
        byte[] bgraBytes = new byte[stride * height];
        bgraFrame.CopyPixels(bgraBytes, stride, 0);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return new PreparedImage
        {
            PngBytes = ms.ToArray(),
            Bgra32Bytes = bgraBytes,
            Width = width,
            Height = height,
            WasConvertedFromJpeg = wasJpeg,
            WasResized = false
        };
    }

    // 先尝试按原纹理格式编码，失败时回退为 RGBA32 提高兼容性。
    private static EncodedTextureImage EncodeReplacementImage(
        PreparedImage preparedImage,
        int originalFormatValue,
        int targetWidth,
        int targetHeight)
    {
        if (preparedImage.PngBytes.Length == 0 || preparedImage.Bgra32Bytes.Length == 0)
            throw new Exception("导入图片为空。");

        PreparedImage workingImage = ResizePreparedImageIfNeeded(preparedImage, targetWidth, targetHeight);

        TextureFormat originalFormat = Enum.IsDefined(typeof(TextureFormat), originalFormatValue)
            ? (TextureFormat)originalFormatValue
            : TextureFormat.RGBA32;

        if (TryEncode(workingImage.PngBytes, originalFormat, out var encoded, out int width, out int height))
        {
            return new EncodedTextureImage
            {
                EncodedBytes = encoded,
                Width = width,
                Height = height,
                FinalFormat = originalFormat,
                FallbackToRgba32 = originalFormat != (TextureFormat)originalFormatValue && originalFormatValue != (int)TextureFormat.RGBA32,
                ResizedToTarget = workingImage.WasResized
            };
        }

        return new EncodedTextureImage
        {
            EncodedBytes = BgraToUnityRgbaRaw(workingImage.Bgra32Bytes, workingImage.Width, workingImage.Height),
            Width = workingImage.Width,
            Height = workingImage.Height,
            FinalFormat = TextureFormat.RGBA32,
            FallbackToRgba32 = true,
            ResizedToTarget = workingImage.WasResized
        };
    }

    // 使用 TextureFile 的托管编码器将 PNG 图像编码为 Unity Texture2D 原始数据。
    private static bool TryEncode(
        byte[] pngBytes,
        TextureFormat format,
        out byte[] encodedBytes,
        out int width,
        out int height)
    {
        encodedBytes = Array.Empty<byte>();
        width = 0;
        height = 0;

        try
        {
            using var ms = new MemoryStream(pngBytes, writable: false);
            encodedBytes = TextureFile.EncodeManagedImage(ms, format, out width, out height);
            return encodedBytes != null && encodedBytes.Length > 0 && width > 0 && height > 0;
        }
        catch
        {
            encodedBytes = Array.Empty<byte>();
            width = 0;
            height = 0;
            return false;
        }
    }

    // 将编码后的纹理数据写回 TextureFile，并清理 streamData 以改为内嵌存储。
    private static void ApplyEncodedTextureData(TextureFile texture, EncodedTextureImage encodeResult)
    {
        texture.m_Width = encodeResult.Width;
        texture.m_Height = encodeResult.Height;
        texture.m_TextureFormat = (int)encodeResult.FinalFormat;
        texture.m_CompleteImageSize = encodeResult.EncodedBytes.Length;
        texture.m_MipCount = 1;
        texture.m_MipMap = false;
        texture.m_StreamingMipmaps = false;
        texture.pictureData = encodeResult.EncodedBytes;

        texture.m_StreamData.offset = 0;
        texture.m_StreamData.size = 0;
        texture.m_StreamData.path = "";
    }

    // 对 Texture2D 的关键字段做直接回写，绕过个别版本/格式下 TextureFile.WriteTo 未完全覆盖的问题。
    private static void ForceApplyEncodedTextureDataToBaseField(AssetTypeValueField baseField, EncodedTextureImage encodeResult)
    {
        RequireSetNumberField(baseField, "m_Width", encodeResult.Width);
        RequireSetNumberField(baseField, "m_Height", encodeResult.Height);
        RequireSetNumberField(baseField, "m_TextureFormat", (int)encodeResult.FinalFormat);
        RequireSetNumberField(baseField, "m_CompleteImageSize", encodeResult.EncodedBytes.Length);
        RequireSetNumberField(baseField, "m_MipCount", 1);
        TrySetBoolField(baseField, "m_MipMap", false);
        TrySetBoolField(baseField, "m_StreamingMipmaps", false);
        RequireSetByteArrayField(baseField, "image data", encodeResult.EncodedBytes);

        if (encodeResult.FallbackToRgba32)
        {
            // 压缩格式回退 RGBA32 时清空平台 blob，避免查看器/运行时继续按旧 GPU 压缩信息解释。
            TrySetByteArrayField(baseField, "m_PlatformBlob", Array.Empty<byte>());
        }

        if (TryGetField(baseField, "m_StreamData", out var streamDataField))
        {
            RequireSetNumberField(streamDataField, "offset", 0L);
            RequireSetNumberField(streamDataField, "size", 0);
            RequireSetStringField(streamDataField, "path", "");
        }
    }

    // 强制写入整数字段；失败时直接抛错，避免静默失败导致回读才发现问题。
    private static void RequireSetNumberField(AssetTypeValueField parent, string fieldName, long value)
    {
        if (!TrySetNumberField(parent, fieldName, value))
            throw new Exception($"无法写入字段 {fieldName}（数值：{value}）。");
    }

    // 强制写入字符串字段；失败时直接抛错。
    private static void RequireSetStringField(AssetTypeValueField parent, string fieldName, string value)
    {
        if (!TrySetStringField(parent, fieldName, value))
            throw new Exception($"无法写入字段 {fieldName}（字符串）。");
    }

    // 强制写入字节数组字段；失败时直接抛错。
    private static void RequireSetByteArrayField(AssetTypeValueField parent, string fieldName, byte[] bytes)
    {
        if (!TrySetByteArrayField(parent, fieldName, bytes))
            throw new Exception($"无法写入字段 {fieldName}（字节数组长度：{bytes.Length}）。");
    }

    // 安全获取子字段，不存在时返回 false。
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

    // 尝试按字段实际数值类型写入整数（支持 Int/UInt/Long/ULong 等常见情况）。
    private static bool TrySetNumberField(AssetTypeValueField parent, string fieldName, long value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;

        EnsureFieldValueInitialized(field);

        try { field.AsLong = value; return true; } catch { /* try next */ }
        try { field.AsInt = checked((int)value); return true; } catch { /* try next */ }
        try { field.AsShort = checked((short)value); return true; } catch { /* try next */ }
        try { field.AsSByte = checked((sbyte)value); return true; } catch { /* try next */ }
        try { field.AsULong = checked((ulong)Math.Max(0, value)); return true; } catch { /* try next */ }
        try { field.AsUInt = checked((uint)Math.Max(0, value)); return true; } catch { /* try next */ }
        try { field.AsUShort = checked((ushort)Math.Max(0, value)); return true; } catch { /* try next */ }
        try { field.AsByte = checked((byte)Math.Max(0, value)); return true; } catch { /* try next */ }
        return false;
    }

    // 尝试写入布尔字段。
    private static bool TrySetBoolField(AssetTypeValueField parent, string fieldName, bool value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;

        EnsureFieldValueInitialized(field);

        try { field.AsBool = value; return true; } catch { /* fallback below */ }
        try { field.Value = new AssetTypeValue(value); return true; } catch { return false; }
    }

    // 尝试写入字符串字段。
    private static bool TrySetStringField(AssetTypeValueField parent, string fieldName, string value)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;

        EnsureFieldValueInitialized(field);

        try { field.AsString = value; return true; } catch { /* fallback below */ }
        try { field.Value = new AssetTypeValue(value); return true; } catch { return false; }
    }

    // 尝试写入字节数组字段（例如 Texture2D 的 "image data" / m_PlatformBlob）。
    private static bool TrySetByteArrayField(AssetTypeValueField parent, string fieldName, byte[] bytes)
    {
        if (!TryGetField(parent, fieldName, out var field)) return false;

        // 某些字段（尤其 UsesStreamData 纹理上的 image data / m_PlatformBlob）在这里会出现 Value 为空，
        // 直接写 AsByteArray 会触发内部空引用。先补一个空字节数组值，再走标准属性写入。
        try
        {
            EnsureFieldValueInitialized(field);
        }
        catch
        {
            // 忽略，后续走其他写入路径。
        }

        try
        {
            field.AsByteArray = bytes;
            return true;
        }
        catch
        {
            // 回退路径：直接替换底层 Value，并尝试同步数组 size 子字段。
            try
            {
                field.Value = new AssetTypeValue(bytes, false);
                TrySetNumberField(field, "size", bytes.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // 为尚未初始化 Value 的字段补一个与模板类型匹配的默认值，避免属性写入触发空引用。
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
            AssetValueType.Array => new AssetTypeValue(AssetValueType.Array, new AssetTypeArrayInfo { size = 0 }),
            _ => new AssetTypeValue(AssetValueType.None, null!)
        };

        field.Children ??= new List<AssetTypeValueField>();
    }

    // 若导入图片尺寸与目标纹理不一致，则先缩放到目标尺寸，避免替换后仍显示原图或显示异常。
    private static PreparedImage ResizePreparedImageIfNeeded(PreparedImage image, int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
            return image;
        if (image.Width == targetWidth && image.Height == targetHeight)
            return image;

        int srcStride = image.Width * 4;
        var src = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            image.Bgra32Bytes,
            srcStride);

        double scaleX = (double)targetWidth / image.Width;
        double scaleY = (double)targetHeight / image.Height;
        var scaled = new TransformedBitmap(src, new ScaleTransform(scaleX, scaleY));

        BitmapSource bgraScaled = scaled.Format == PixelFormats.Bgra32
            ? scaled
            : new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

        int targetStride = targetWidth * 4;
        byte[] bgraBytes = new byte[targetStride * targetHeight];
        bgraScaled.CopyPixels(bgraBytes, targetStride, 0);

        using var pngMs = new MemoryStream();
        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(bgraScaled));
        pngEncoder.Save(pngMs);

        return new PreparedImage
        {
            PngBytes = pngMs.ToArray(),
            Bgra32Bytes = bgraBytes,
            Width = targetWidth,
            Height = targetHeight,
            WasConvertedFromJpeg = image.WasConvertedFromJpeg,
            WasResized = true
        };
    }

    // 将 WPF 的 BGRA32（左上角为原点）转换为 Unity Texture2D RGBA32 原始字节（按行翻转为左下角原点）。
    private static byte[] BgraToUnityRgbaRaw(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new Exception("图片尺寸无效。");

        int rowStride = width * 4;
        if (bgra.Length != rowStride * height)
            throw new Exception("图片像素数据长度异常。");

        byte[] rgba = new byte[bgra.Length];
        for (int y = 0; y < height; y++)
        {
            // WPF CopyPixels 输出是自上而下；Unity 原始 RGBA32 纹理数据按底到顶解释。
            int srcRowStart = y * rowStride;
            int dstRowStart = (height - 1 - y) * rowStride;

            for (int x = 0; x < rowStride; x += 4)
            {
                int src = srcRowStart + x;
                int dst = dstRowStart + x;
                rgba[dst + 0] = bgra[src + 2]; // R
                rgba[dst + 1] = bgra[src + 1]; // G
                rgba[dst + 2] = bgra[src + 0]; // B
                rgba[dst + 3] = bgra[src + 3]; // A
            }
        }
        return rgba;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private sealed class PreparedImage
    {
        public required byte[] PngBytes { get; set; }
        public required byte[] Bgra32Bytes { get; set; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required bool WasConvertedFromJpeg { get; init; }
        public required bool WasResized { get; init; }

        public void ReleaseHeavyBuffers()
        {
            PngBytes = Array.Empty<byte>();
            Bgra32Bytes = Array.Empty<byte>();
        }
    }

    private sealed class EncodedTextureImage
    {
        public required byte[] EncodedBytes { get; set; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required TextureFormat FinalFormat { get; init; }
        public required bool FallbackToRgba32 { get; init; }
        public required bool ResizedToTarget { get; init; }
        public string FormatName => FinalFormat.ToString();

        public void ReleaseEncodedBytes() => EncodedBytes = Array.Empty<byte>();
    }

    private sealed class OutputPlan
    {
        public required string RequestedOutputPath { get; init; }
        public required string FinalOutputPath { get; init; }
        public required string PackOutputPath { get; init; }
        public required bool ReplaceAfterPack { get; init; }
        public required bool AutoRenamedDueToLock { get; init; }
    }

    private sealed class ReadbackVerificationResult
    {
        public required bool Verified { get; init; }
        public required string Summary { get; init; }

        public static ReadbackVerificationResult Ok(string summary) =>
            new() { Verified = true, Summary = summary };

        public static ReadbackVerificationResult Fail(string summary) =>
            new() { Verified = false, Summary = summary };
    }
}


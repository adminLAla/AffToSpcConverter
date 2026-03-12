using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Generic;

namespace AffToSpcConverter.Utils;

// 游戏资源加密打包器，用于按映射表替换并输出加密资源文件。
public class GameAssetPacker
{
    // 游戏资源加密使用的固定数据密钥与 tweak 密钥。
    private static readonly byte[] DataKey = HexToBytes("D98633AC10EB3D600FBECBA023FADF58");
    private static readonly byte[] TweakKey = HexToBytes("B3BC4F5C8FBFC6B2126A50EFAE032210");

    // 读取资源文件并按映射表加密打包输出。
    public static void Pack(string sourceFilePath, string originalGamePath, string mappingJsonPath, string outputDirectory)
    {
        // 1. 校验输入文件与映射文件路径。
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");
        if (!File.Exists(mappingJsonPath))
            throw new FileNotFoundException($"Mapping JSON not found: {mappingJsonPath}");
        ValidateSupportedSourceFileType(sourceFilePath);
        ValidateReplacementType(sourceFilePath, originalGamePath);

        // 2. 解析映射 JSON。
        string jsonContent = File.ReadAllText(mappingJsonPath);
        var mapping = ParseMapping(jsonContent);

        // 查找目标原始路径对应的映射条目。
        var entry = FindEntry(mapping, originalGamePath);
        string targetGuid = entry.Guid ?? throw new Exception("GUID not found for entry.");

        // 3. 准备输出目录与目标文件路径。
        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        string outputFilePath = Path.Combine(outputDirectory, targetGuid);

        // 4. 读取源文件内容。
        byte[] sourceBytes = File.ReadAllBytes(sourceFilePath);

        // 5. 预处理：对 .txt 文件去除 UTF-8 BOM。
        string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext == ".txt" && sourceBytes.Length >= 3 && sourceBytes[0] == 0xEF && sourceBytes[1] == 0xBB && sourceBytes[2] == 0xBF)
        {
            var trimmed = new byte[sourceBytes.Length - 3];
            Buffer.BlockCopy(sourceBytes, 3, trimmed, 0, trimmed.Length);
            sourceBytes = trimmed;
        }

        // 6. 使用 XTS 模式加密数据。
        byte[] encryptedBytes = XtsEncrypt(sourceBytes);

        // 7. 写出加密结果文件。
        File.WriteAllBytes(outputFilePath, encryptedBytes);
    }

    // 读取源文件并执行与打包一致的预处理（例如 .txt 去除 UTF-8 BOM），供新歌曲资源打包复用。
    public static byte[] ReadSourceBytesForPacking(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");

        byte[] sourceBytes = File.ReadAllBytes(sourceFilePath);
        string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext == ".txt" && sourceBytes.Length >= 3 && sourceBytes[0] == 0xEF && sourceBytes[1] == 0xBB && sourceBytes[2] == 0xBF)
        {
            var trimmed = new byte[sourceBytes.Length - 3];
            Buffer.BlockCopy(sourceBytes, 3, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        return sourceBytes;
    }

    // 使用与游戏一致的 XTS 流程加密原始字节数组。
    public static byte[] EncryptBytesForGame(byte[] plainBytes)
    {
        if (plainBytes == null) throw new ArgumentNullException(nameof(plainBytes));
        return XtsEncrypt(plainBytes);
    }

    // 读取映射表中的 FullLookupPath 列表，并按源文件类型过滤可替换目标。
    public static IReadOnlyList<string> GetReplacementCandidates(string mappingJsonPath, string? sourceFilePath)
    {
        if (!File.Exists(mappingJsonPath))
            throw new FileNotFoundException($"Mapping JSON not found: {mappingJsonPath}");

        string jsonContent = File.ReadAllText(mappingJsonPath);
        var mapping = ParseMapping(jsonContent);
        var allowedTargetExts = GetAllowedTargetExtensionsForSource(sourceFilePath);

        IEnumerable<string> paths = mapping.Entries!
            .Select(x => x.FullLookupPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Replace('\\', '/'));

        if (allowedTargetExts != null)
        {
            paths = paths.Where(p => allowedTargetExts.Contains(Path.GetExtension(p)));
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Contains('/'))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // 校验源文件类型是否允许替换目标路径类型（按扩展名判定）。
    public static void ValidateReplacementType(string sourceFilePath, string targetLookupPath)
    {
        var allowedTargetExts = GetAllowedTargetExtensionsForSource(sourceFilePath);
        if (allowedTargetExts == null) return;

        string targetExt = Path.GetExtension(targetLookupPath).ToLowerInvariant();
        if (allowedTargetExts.Contains(targetExt)) return;

        string sourceExt = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        string allowedDesc = string.Join(" / ", allowedTargetExts.OrderBy(x => x));
        throw new Exception($"源文件类型 {sourceExt} 不能替换目标类型 {targetExt}。允许替换的目标类型：{allowedDesc}");
    }

    // 使用 XTS 逻辑加密字节数组。
    private static byte[] XtsEncrypt(byte[] data)
    {
        if (data == null || data.Length == 0) return Array.Empty<byte>();

        // --- 修改点 1: 数据填充 (补齐至 16 字节倍数) ---
        int paddingLen = (16 - data.Length % 16) % 16;
        byte[] paddedData = data;
        if (paddingLen > 0)
        {
            paddedData = new byte[data.Length + paddingLen];
            Buffer.BlockCopy(data, 0, paddedData, 0, data.Length);
            // 填充换行符 '\n' (0x0A)，与 Python 代码一致
            for (int i = 0; i < paddingLen; i++) paddedData[data.Length + i] = 0x0A;
        }

        using (Aes aesData = Aes.Create())
        using (Aes aesTweak = Aes.Create())
        {
            aesData.Mode = CipherMode.ECB; aesData.Padding = PaddingMode.None; aesData.Key = DataKey;
            aesTweak.Mode = CipherMode.ECB; aesTweak.Padding = PaddingMode.None; aesTweak.Key = TweakKey;

            using (ICryptoTransform encData = aesData.CreateEncryptor())
            using (ICryptoTransform encTweak = aesTweak.CreateEncryptor())
            {
                int blockSize = 16;
                int sectorSize = 512;
                byte[] result = new byte[paddedData.Length];

                for (int offset = 0; offset < paddedData.Length; offset += sectorSize)
                {
                    int currentSectorLen = Math.Min(sectorSize, paddedData.Length - offset);
                    int sectorIndex = offset / sectorSize;

                    // --- 修改点 2: 构造 Tweak 输入 (Little Endian 16 bytes) ---
                    byte[] sectorIdxBytes = new byte[16];
                    byte[] indexBytes = BitConverter.GetBytes(sectorIndex);
                    if (!BitConverter.IsLittleEndian) Array.Reverse(indexBytes);
                    Array.Copy(indexBytes, 0, sectorIdxBytes, 0, 4);

                    byte[] tweak = new byte[16];
                    encTweak.TransformBlock(sectorIdxBytes, 0, 16, tweak, 0);

                    // 逐块加密 (16字节一组)
                    for (int i = 0; i < currentSectorLen; i += blockSize)
                    {
                        int blkOff = offset + i;
                        ProcessBlockEnc(paddedData, blkOff, tweak, encData, result, blkOff);
                        tweak = TweakMul2(tweak); // 更新 Tweak
                    }
                }
                return result;
            }
        }
    }

    // 按 XTS 单块流程加密一个 16 字节分组。
    private static void ProcessBlockEnc(byte[] input, int inOff, byte[] tweak, ICryptoTransform enc, byte[] output, int outOff)
    {
        byte[] tmp = new byte[16];
        for (int i = 0; i < 16; i++) tmp[i] = (byte)(input[inOff + i] ^ tweak[i]);

        byte[] encBlk = new byte[16];
        enc.TransformBlock(tmp, 0, 16, encBlk, 0);

        for (int i = 0; i < 16; i++) output[outOff + i] = (byte)(encBlk[i] ^ tweak[i]);
    }

    // 计算 XTS 中 tweak 的 GF(2^128) 乘 2。
    private static byte[] TweakMul2(byte[] t)
    {
        byte[] r = new byte[16];
        byte carry = 0;

        // 模拟 Python 的 little endian 128-bit 移位
        for (int i = 0; i < 16; i++)
        {
            byte nextCarry = (byte)((t[i] & 0x80) >> 7);
            r[i] = (byte)((t[i] << 1) | carry);
            carry = nextCarry;
        }

        // 如果最高位（最后一个字节的最高位）有进位，则异或多项式
        if (carry != 0)
        {
            r[0] ^= 0x87;
        }
        return r;
    }

    // 将十六进制字符串转换为字节数组。
    private static byte[] HexToBytes(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
            bytes[i / 2] = System.Convert.ToByte(hex.Substring(i, 2), 16);
        return bytes;
    }

    // --- JSON 辅助方法 ---

    // 根据源文件扩展名返回允许替换的目标扩展名集合；返回 null 表示不额外限制。
    private static HashSet<string>? GetAllowedTargetExtensionsForSource(string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            return null;

        string sourceExt = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        return sourceExt switch
        {
            ".ogg" => new HashSet<string>(new[] { ".ogg", ".wav" }, StringComparer.OrdinalIgnoreCase),
            ".txt" => new HashSet<string>(new[] { ".spc" }, StringComparer.OrdinalIgnoreCase),
            ".spc" => new HashSet<string>(new[] { ".spc" }, StringComparer.OrdinalIgnoreCase),
            _ => null
        };
    }

    // 校验源文件类型仅允许谱面或音乐文件（.txt/.spc/.ogg）。
    private static void ValidateSupportedSourceFileType(string sourceFilePath)
    {
        string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is ".txt" or ".spc" or ".ogg")
            return;

        throw new Exception($"仅支持谱面/音乐文件（.txt / .spc / .ogg），当前文件类型为 {ext}。");
    }

    // 解析相关数据并返回结果。
    private static MappingData ParseMapping(string jsonContent)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var mapping = new MappingData();
        try
        {
            using (JsonDocument doc = JsonDocument.Parse(jsonContent))
            {
                JsonElement root = doc.RootElement;
                JsonElement entriesElement = default;
                bool foundEntries = false;

                if (root.ValueKind == JsonValueKind.Array) { entriesElement = root; foundEntries = true; }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetPropertyCaseInsensitive(root, "Entries", out entriesElement)) foundEntries = true;
                    else if (TryGetPropertyCaseInsensitive(root, "m_Structure", out JsonElement st))
                        if (TryGetPropertyCaseInsensitive(st, "Entries", out entriesElement)) foundEntries = true;
                }

                if (foundEntries)
                    mapping.Entries = JsonSerializer.Deserialize<List<MappingEntry>>(entriesElement.GetRawText(), options);
            }
        }
        catch { /* ignored, handle below */ }

        if (mapping.Entries == null || mapping.Entries.Count == 0)
             throw new Exception("Failed to find 'Entries' array in mapping JSON.");

        return mapping;
    }

    // 按路径在映射表中查找目标条目。
    private static MappingEntry FindEntry(MappingData mapping, string originalPath)
    {
        // 1) 优先按完整路径精确匹配。
        var e = mapping.Entries!.FirstOrDefault(x => string.Equals(x.FullLookupPath, originalPath, StringComparison.OrdinalIgnoreCase));
        if (e != null) return e;

        // 2) 将路径分隔符统一后再次匹配。
        string norm = originalPath.Replace('\\', '/');
        e = mapping.Entries!.FirstOrDefault(x => string.Equals(x.FullLookupPath?.Replace('\\', '/'), norm, StringComparison.OrdinalIgnoreCase));
        if (e != null) return e;

        // 3) 最后尝试以后缀匹配（仅唯一命中时接受）。
        var matches = mapping.Entries!.Where(x => x.FullLookupPath != null && x.FullLookupPath.EndsWith(originalPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1) throw new Exception($"Multiple matches for '{originalPath}'. Use full path.");
        
        throw new Exception($"Entry not found: {originalPath}");
    }

    // 不区分大小写读取 JSON 属性。
    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    // StreamingAssetsMapping.json 的根对象（仅保留本工具需要的字段）。
    private class MappingData { public List<MappingEntry>? Entries { get; set; } }
    // 单条映射项（仅使用 Guid 与 FullLookupPath）。
    private class MappingEntry { public string? Guid { get; set; } public string? FullLookupPath { get; set; } }
}

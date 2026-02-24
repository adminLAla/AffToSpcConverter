using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Generic;

namespace AffToSpcConverter.Utils;

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
            sourceBytes = sourceBytes.Skip(3).ToArray();
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
            return sourceBytes.Skip(3).ToArray();

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
        if (data.Length == 0) return data;

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
                int sectorIndex = 0;
                
                byte[] result = new byte[data.Length];

                for (int offset = 0; offset < data.Length; offset += sectorSize)
                {
                    int len = Math.Min(sectorSize, data.Length - offset);
                    byte[] sectorBytes = new byte[len];
                    Array.Copy(data, offset, sectorBytes, 0, len);

                    // 构造当前扇区编号对应的 16 字节 tweak 输入块。
                    byte[] sectorIdxBytes = new byte[16];
                    Array.Copy(BitConverter.GetBytes(sectorIndex), 0, sectorIdxBytes, 0, 4); // Little endian 4 bytes
                    
                    byte[] tweak = new byte[16];
                    encTweak.TransformBlock(sectorIdxBytes, 0, 16, tweak, 0);

                    // 计算当前扇区的完整块数量与尾部剩余字节数。
                    int fullBlocks = len / blockSize;
                    int remainder = len % blockSize;
                    
                    // 若存在尾块残余，需要预留最后一个完整块给 CTS 处理。
                    // standardBlocks 表示可按普通 XTS 直接处理的完整块数量。
                    int standardBlocks = (remainder > 0) ? fullBlocks - 1 : fullBlocks;

                    byte[] encryptedSector = new byte[len];
                    byte[] curTweak = new byte[16];
                    Array.Copy(tweak, curTweak, 16);

                    // 1. 先加密可按标准 XTS 处理的完整块。
                    for (int i = 0; i < standardBlocks; i++)
                    {
                        int blkOff = i * blockSize;
                        byte[] blk = new byte[blockSize];
                        Array.Copy(sectorBytes, blkOff, blk, 0, blockSize);

                        ProcessBlockEnc(blk, curTweak, encData, encryptedSector, blkOff);
                        
                        curTweak = TweakMul2(curTweak);
                    }

                    // 2. 若存在尾部残余，则执行 XTS-CTS（密文窃取）处理。
                    if (remainder > 0)
                    {
                        // 当前处理第 m-1 个完整块与第 m 个部分块。
                        // curTweak 对应 T_{m-1}。
                        // tweakM 对应 T_m。
                        byte[] tweakMm1 = new byte[16]; Array.Copy(curTweak, tweakMm1, 16);
                        byte[] tweakM = TweakMul2(tweakMm1);

                        int offMm1 = standardBlocks * blockSize;
                        int offM = offMm1 + blockSize;

                        // 读取明文块 P_{m-1}。
                        byte[] P_Mm1 = new byte[blockSize];
                        Array.Copy(sectorBytes, offMm1, P_Mm1, 0, blockSize);

                        // 读取明文部分块 P_m。
                        byte[] P_M = new byte[remainder];
                        Array.Copy(sectorBytes, offM, P_M, 0, remainder);

                        // 计算临时密文块 CC（由 P_{m-1} 经 T_{m-1} 加密得到）。
                        byte[] CC = new byte[blockSize];
                        // 第一步：与 T_{m-1} 异或。
                        for(int k=0; k<16; k++) CC[k] = (byte)(P_Mm1[k] ^ tweakMm1[k]);
                        // 第二步：执行分组加密。
                        byte[] temp = new byte[blockSize];
                        encData.TransformBlock(CC, 0, 16, temp, 0);
                        // 第三步：再次与 T_{m-1} 异或。
                        for (int k = 0; k < 16; k++) CC[k] ^= tweakMm1[k];

                        // 取 CC 的前 remainder 字节作为末尾部分密文 C_m。
                        // 相当于从前一块“借用”密文字节完成窃取。
                        Array.Copy(CC, 0, encryptedSector, offM, remainder);

                        // 构造 PP：P_m 与 CC 剩余字节拼接。
                        byte[] PP = new byte[blockSize];
                        Array.Copy(P_M, 0, PP, 0, remainder);
                        Array.Copy(CC, remainder, PP, remainder, blockSize - remainder);

                        // 使用 T_m 加密 PP，得到位置 m-1 的完整密文块 C_{m-1}。
                        // ProcessBlockEnc 内部会先与 tweak 异或。
                        // 然后执行 AES-ECB 加密。
                        // 最后再次异或 tweak 并写回输出缓冲区。
                        ProcessBlockEnc(PP, tweakM, encData, encryptedSector, offMm1);
                    }

                    Array.Copy(encryptedSector, 0, result, offset, len);
                    sectorIndex++;
                }

                return result;
            }
        }
    }

    // 按 XTS 单块流程加密一个 16 字节分组。
    private static void ProcessBlockEnc(byte[] input16, byte[] tweak, ICryptoTransform enc, byte[] outBuf, int outOffset)
    {
        byte[] tmp = new byte[16];
        // 先将输入块与 tweak 异或。
        for(int i=0; i<16; i++) tmp[i] = (byte)(input16[i] ^ tweak[i]);
        // 执行 AES 分组加密。
        byte[] encBlk = new byte[16];
        enc.TransformBlock(tmp, 0, 16, encBlk, 0);
        // 将加密结果再次与 tweak 异或。
        for (int i = 0; i < 16; i++) encBlk[i] ^= tweak[i];
        
        Array.Copy(encBlk, 0, outBuf, outOffset, 16);
    }

    // 计算 XTS 中 tweak 的 GF(2^128) 乘 2。
    private static byte[] TweakMul2(byte[] t)
    {
        bool c = (t[15] & 0x80) != 0;
        byte[] r = new byte[16];
        byte s = 0;
        for (int i = 0; i < 16; i++) { byte n = (byte)((t[i] & 0x80) >> 7); r[i] = (byte)((t[i] << 1) | s); s = n; }
        if (c) r[0] ^= 0x87;
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

    private class MappingData { public List<MappingEntry>? Entries { get; set; } }
    private class MappingEntry { public string? Guid { get; set; } public string? FullLookupPath { get; set; } }
}

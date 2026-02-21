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
    // 硬编码密钥 (从用户提供的 XtsCore 提取)
    private static readonly byte[] DataKey = HexToBytes("D98633AC10EB3D600FBECBA023FADF58");
    private static readonly byte[] TweakKey = HexToBytes("B3BC4F5C8FBFC6B2126A50EFAE032210");

    public static void Pack(string sourceFilePath, string originalGamePath, string mappingJsonPath, string outputDirectory)
    {
        // 1. Validate paths
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");
        if (!File.Exists(mappingJsonPath))
            throw new FileNotFoundException($"Mapping JSON not found: {mappingJsonPath}");

        // 2. Parse Mapping JSON
        string jsonContent = File.ReadAllText(mappingJsonPath);
        var mapping = ParseMapping(jsonContent);

        // Find entry
        var entry = FindEntry(mapping, originalGamePath);
        string targetGuid = entry.Guid ?? throw new Exception("GUID not found for entry.");

        // 3. Prepare output
        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        string outputFilePath = Path.Combine(outputDirectory, targetGuid);

        // 4. Read Source
        byte[] sourceBytes = File.ReadAllBytes(sourceFilePath);

        // 5. Preprocess (Remove BOM for .txt)
        string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext == ".txt" && sourceBytes.Length >= 3 && sourceBytes[0] == 0xEF && sourceBytes[1] == 0xBB && sourceBytes[2] == 0xBF)
        {
            sourceBytes = sourceBytes.Skip(3).ToArray();
        }

        // 6. Add padding logic
        // User requested incrementing bytes starting from 00.
        // The game likely expects the file to be aligned to 16 bytes, AND possibly requires at least some padding bytes if the parser overreads.
        // Update: Always add at least one block of padding if it lands exactly on boundary? 
        // Or strictly align to next 16-byte boundary.
        // If the file size is already multiple of 16, user reports crash/issue. 
        // Let's force adding a full 16 bytes of padding if remainder is 0, or just standard alignment.
        // However, looking at the previous specific request "at the end of 0A add incrementing bytes starting from 00",
        // and knowing standard PKCS7 padding behaviors (always pad), let's try ensuring we always append padding to align to the *next* 16 byte boundary.
        
        int remainder = sourceBytes.Length % 16;
        int paddingNeeded = 16 - remainder; 
        
        // If remainder is 0, paddingNeeded is 16. This behaves like PKCS7 where we always pad.
        // This ensures there are always extra bytes at the end for the parser to consume safely if it expects valid termination.
        
        byte[] newSource = new byte[sourceBytes.Length + paddingNeeded];
        Array.Copy(sourceBytes, newSource, sourceBytes.Length);
        
        for (int i = 0; i < paddingNeeded; i++)
        {
            newSource[sourceBytes.Length + i] = (byte)i;
        }
        sourceBytes = newSource;

        // 7. Encrypt using correct XTS logic
        byte[] encryptedBytes = XtsEncrypt(sourceBytes);

        // 8. Write
        File.WriteAllBytes(outputFilePath, encryptedBytes);
    }

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

                    // Tweak for this sector
                    byte[] sectorIdxBytes = new byte[16];
                    Array.Copy(BitConverter.GetBytes(sectorIndex), 0, sectorIdxBytes, 0, 4); // Little endian 4 bytes
                    
                    byte[] tweak = new byte[16];
                    encTweak.TransformBlock(sectorIdxBytes, 0, 16, tweak, 0);

                    // Process blocks
                    int fullBlocks = len / blockSize;
                    int remainder = len % blockSize;
                    
                    // If remainder > 0, we have specific XTS stealing logic
                    // Standard blocks count
                    int standardBlocks = (remainder > 0) ? fullBlocks - 1 : fullBlocks;

                    byte[] encryptedSector = new byte[len];
                    byte[] curTweak = new byte[16];
                    Array.Copy(tweak, curTweak, 16);

                    // 1. Encrypt standard blocks
                    for (int i = 0; i < standardBlocks; i++)
                    {
                        int blkOff = i * blockSize;
                        byte[] blk = new byte[blockSize];
                        Array.Copy(sectorBytes, blkOff, blk, 0, blockSize);

                        ProcessBlockEnc(blk, curTweak, encData, encryptedSector, blkOff);
                        
                        curTweak = TweakMul2(curTweak);
                    }

                    // 2. Handle CTS if needed
                    if (remainder > 0)
                    {
                        // We are at block m-1 (full) and m (partial).
                        // curTweak is T_{m-1}.
                        // Next tweak is T_m.
                        byte[] tweakMm1 = new byte[16]; Array.Copy(curTweak, tweakMm1, 16);
                        byte[] tweakM = TweakMul2(tweakMm1);

                        int offMm1 = standardBlocks * blockSize;
                        int offM = offMm1 + blockSize;

                        // Plaintext P_{m-1}
                        byte[] P_Mm1 = new byte[blockSize];
                        Array.Copy(sectorBytes, offMm1, P_Mm1, 0, blockSize);

                        // Plaintext P_m (partial)
                        byte[] P_M = new byte[remainder];
                        Array.Copy(sectorBytes, offM, P_M, 0, remainder);

                        // Encrypt P_{m-1} with T_{m-1} -> produces CC (draft ciphertext)
                        byte[] CC = new byte[blockSize];
                        // Xor T
                        for(int k=0; k<16; k++) CC[k] = (byte)(P_Mm1[k] ^ tweakMm1[k]);
                        // Encrypt
                        byte[] temp = new byte[blockSize];
                        encData.TransformBlock(CC, 0, 16, temp, 0);
                        // Xor T
                        for (int k = 0; k < 16; k++) CC[k] ^= tweakMm1[k];

                        // The first 'remainder' bytes of CC become C_m (the partial ciphertext at end)
                        // This effectively "steals" ciphertext from block m-1 to fill block m
                        Array.Copy(CC, 0, encryptedSector, offM, remainder);

                        // Construct PP: P_m concatenated with the *rest* of CC
                        byte[] PP = new byte[blockSize];
                        Array.Copy(P_M, 0, PP, 0, remainder);
                        Array.Copy(CC, remainder, PP, remainder, blockSize - remainder);

                        // Encrypt PP with T_m -> produces C_{m-1} (the full ciphertext block at m-1 position)
                        // Xor T_m
                        // Encrypt
                        // Xor T_m
                        ProcessBlockEnc(PP, tweakM, encData, encryptedSector, offMm1);
                    }

                    Array.Copy(encryptedSector, 0, result, offset, len);
                    sectorIndex++;
                }

                return result;
            }
        }
    }

    private static void ProcessBlockEnc(byte[] input16, byte[] tweak, ICryptoTransform enc, byte[] outBuf, int outOffset)
    {
        byte[] tmp = new byte[16];
        // P xor T
        for(int i=0; i<16; i++) tmp[i] = (byte)(input16[i] ^ tweak[i]);
        // E(...)
        byte[] encBlk = new byte[16];
        enc.TransformBlock(tmp, 0, 16, encBlk, 0);
        // C xor T
        for (int i = 0; i < 16; i++) encBlk[i] ^= tweak[i];
        
        Array.Copy(encBlk, 0, outBuf, outOffset, 16);
    }

    private static byte[] TweakMul2(byte[] t)
    {
        bool c = (t[15] & 0x80) != 0;
        byte[] r = new byte[16];
        byte s = 0;
        for (int i = 0; i < 16; i++) { byte n = (byte)((t[i] & 0x80) >> 7); r[i] = (byte)((t[i] << 1) | s); s = n; }
        if (c) r[0] ^= 0x87;
        return r;
    }

    private static byte[] HexToBytes(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
            bytes[i / 2] = System.Convert.ToByte(hex.Substring(i, 2), 16);
        return bytes;
    }

    // --- JSON Helpers ---

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

    private static MappingEntry FindEntry(MappingData mapping, string originalPath)
    {
        // Exact match
        var e = mapping.Entries!.FirstOrDefault(x => string.Equals(x.FullLookupPath, originalPath, StringComparison.OrdinalIgnoreCase));
        if (e != null) return e;

        // Normalized slash
        string norm = originalPath.Replace('\\', '/');
        e = mapping.Entries!.FirstOrDefault(x => string.Equals(x.FullLookupPath?.Replace('\\', '/'), norm, StringComparison.OrdinalIgnoreCase));
        if (e != null) return e;

        // Endswith
        var matches = mapping.Entries!.Where(x => x.FullLookupPath != null && x.FullLookupPath.EndsWith(originalPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1) throw new Exception($"Multiple matches for '{originalPath}'. Use full path.");
        
        throw new Exception($"Entry not found: {originalPath}");
    }

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

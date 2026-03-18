using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Microsoft.Win32;
using InFalsusSongPackStudio.Utils;
using Microsoft.VisualBasic.ApplicationServices;
using static System.Windows.Forms.AxHost;
using System.Collections.Specialized;
using System.Threading;

namespace InFalsusSongPackStudio.Views
{
    /// <summary>
    /// BatchBundleWindow.xaml 的交互逻辑
    /// </summary>
    /// 

    // 批量打包窗口视图模型，维护路径、曲包列表和状态日志。
    public class BatchBundleViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<SongPackInfo> SongPacks { get; } = new();
        public ObservableCollection<string> Status { get; } = new();

        private string _gameDirectory = "";
        public string GameDirectory
        {
            get => _gameDirectory;
            set { _gameDirectory = value; OnPropertyChanged(nameof(GameDirectory)); }
        }

        private string _sharedAssetsFilePath = "";
        public string SharedAssetsFilePath
        {
            get => _sharedAssetsFilePath;
            set { _sharedAssetsFilePath = value; OnPropertyChanged(nameof(SharedAssetsFilePath)); }
        }

        private string _resourcesAssetsFilePath = "";
        public string ResourcesAssetsFilePath
        {
            get => _resourcesAssetsFilePath;
            set { _resourcesAssetsFilePath = value; OnPropertyChanged(nameof(ResourcesAssetsFilePath)); }
        }

        private string _bundleFilePath = "";
        public string BundleFilePath
        {
            get => _bundleFilePath;
            set { _bundleFilePath = value; OnPropertyChanged(nameof(BundleFilePath)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    // 单个曲包的解析结果，用于列表展示与导出参数输入。
    public class SongPackInfo
    {
        public string ZipPath { get; set; } = "";
        public string BaseName { get; set; } = "";
        public int SlotIndex { get; set; } // 新增 slot_id 属性
        public string SongTitleEnglish { get; set; } = "";
        public string SongArtistEnglish { get; set; } = "";
        public int ChartCount { get; set; }
        public JsonElement RawJson { get; set; }
    }

    // 批量导入导出曲包窗口。
    public partial class BatchBundleWindow : Window
    {

        public string GameDirectory { get; set; } = "";
        public string DataDir { get; set; } = "";
        public string SharedAssetsFilePath { get; set; } = "";
        public string ResourcesAssetsFilePath { get; set; } = "";
        public string BundleDir { get; set; } = "";
        public string BundleFilePath { get; set; } = "";

    // 初始化窗口并绑定视图模型。
    public BatchBundleWindow()
        {
            InitializeComponent();
            DataContext = new BatchBundleViewModel();
        }

        // 扫描目录中的 ZIP，识别可导出曲包并填充列表。
        private void BtnBatchImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择包含曲包ZIP的文件夹"
            };
            if (dialog.ShowDialog() != true) return;

            var vm = DataContext as BatchBundleViewModel;
            if (vm == null)
            {
                MessageBox.Show("DataContext 未正确初始化。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            vm.SongPacks.Clear();
            vm.Status.Clear();

            var packList = new List<SongPackInfo>();
            int skippedNoSongJson = 0;
            int parseFailed = 0;

            foreach (var zipPath in Directory.EnumerateFiles(dialog.FolderName, "*.zip", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    var entry = FindSongJsonEntry(archive);
                    if (entry == null)
                    {
                        skippedNoSongJson++;
                        vm.Status.Add($"{Path.GetFileName(zipPath)}: 已跳过（非曲包，未找到 song.json）");
                        continue;
                    }

                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    var json = reader.ReadToEnd();
                    var songInfo = JsonSerializer.Deserialize<JsonElement>(json);

                    var info = ParseSongPackInfo(zipPath, songInfo);
                    packList.Add(info);
                    vm.Status.Add($"{Path.GetFileName(zipPath)}: 已识别曲包（{entry.FullName}）");
                }
                catch (Exception ex)
                {
                    parseFailed++;
                    vm.Status.Add($"{Path.GetFileName(zipPath)}: 解析失败 - {BuildErrorMessage(ex)}");
                }
            }

            foreach (var info in packList.OrderBy(p => p.SlotIndex))
            {
                vm.SongPacks.Add(info);
            }

            vm.Status.Add($"导入完成：成功 {packList.Count} 个，跳过 {skippedNoSongJson} 个，失败 {parseFailed} 个。");
            if (packList.Count == 0)
                vm.Status.Add("提示：当前目录下未识别到可导出的曲包。请确认 ZIP 内含 song.json。");
        }

        // 执行批量导出：扫描空槽、自动分配槽位并输出详细状态。
        private async void BtnBatchExport_Click(object sender, RoutedEventArgs e)
        {
            var vm = (BatchBundleViewModel)DataContext;
            if (vm.SongPacks.Count == 0)
            {
                MessageBox.Show("请先批量导入曲包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string gameDirectory = vm.GameDirectory;
            string sharedAssetsFilePath = vm.SharedAssetsFilePath;
            string resourcesAssetsFilePath = vm.ResourcesAssetsFilePath;
            string bundleFilePath = vm.BundleFilePath;

            if (string.IsNullOrWhiteSpace(gameDirectory) ||
                string.IsNullOrWhiteSpace(sharedAssetsFilePath) ||
                string.IsNullOrWhiteSpace(resourcesAssetsFilePath) ||
                string.IsNullOrWhiteSpace(bundleFilePath))
            {
                MessageBox.Show("请先选择有效的游戏目录并完成资源路径定位。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(sharedAssetsFilePath) || !File.Exists(resourcesAssetsFilePath) || !File.Exists(bundleFilePath))
            {
                MessageBox.Show("资源路径无效：sharedassets0.assets / resources.assets / .bundle 中至少一个不存在，请重新定位。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            vm.Status.Clear();


            await Task.Run(() =>
            {
                SongBundleScanResult scan;
                try
                {
                    scan = UnitySongResourcePacker.ScanBundle(bundleFilePath);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => vm.Status.Add($"导出前扫描失败：{BuildErrorMessage(ex)}"));
                    return;
                }

                var slots = scan.Slots.OrderBy(s => s.SlotIndex).ToList();
                var usedSlots = new HashSet<int>(slots.Where(s => !s.IsEmpty).Select(s => s.SlotIndex));
                int successCount = 0;
                int failCount = 0;

                foreach (var pack in vm.SongPacks)
                {
                    string extractDir = "";
                    try
                    {
                        extractDir = ExtractZipToTempDirectory(pack.ZipPath);

                        var songInfo = pack.RawJson;
                        int preferredSlot = TryReadPreferredSlotIndex(songInfo);
                        SongDatabaseSlotInfo targetSlot = ResolveTargetSlot(slots, preferredSlot, usedSlots);
                        string jacketPath = ResolveRequiredFilePath(songInfo, "JacketImageFileName", extractDir, "曲绘");
                        string bgmPath = ResolveRequiredFilePath(songInfo, "BgmFileName", extractDir, "BGM");
                        var jacketTemplate = ParseJacketTemplate(songInfo);
                        var charts = ParseCharts(songInfo, extractDir);

                        var request = BuildRequest(
                            songInfo,
                            bundleFilePath,
                            sharedAssetsFilePath,
                            resourcesAssetsFilePath,
                            gameDirectory,
                            jacketPath,
                            bgmPath,
                            targetSlot,
                            jacketTemplate,
                            charts);

                        var result = UnitySongResourcePacker.ExportNewSongResources(request);
                        usedSlots.Add(targetSlot.SlotIndex);
                        successCount++;
                        Dispatcher.Invoke(() =>
                        {
                            string zipName = Path.GetFileName(pack.ZipPath);
                            if (preferredSlot != targetSlot.SlotIndex)
                            {
                                vm.Status.Add($"{zipName}: 请求槽位 {preferredSlot} 不可用，已自动写入空槽 {targetSlot.SlotIndex}。");
                            }

                            vm.Status.Add($"{zipName}: 导出成功 -> 槽位 {targetSlot.SlotIndex}，SongId={result.SongDatabaseReadback.SongId}，BaseName={result.SongDatabaseReadback.BaseName}");
                            vm.Status.Add($"  输出目录: {request.OutputDirectory}");
                            vm.Status.Add($"  输出文件: {result.OutputBundlePath}");
                            vm.Status.Add($"            {result.OutputSharedAssetsPath}");
                            vm.Status.Add($"            {result.OutputResourcesAssetsPath}");
                        });

                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Dispatcher.Invoke(() =>
                            vm.Status.Add($"{Path.GetFileName(pack.ZipPath)}: 导出失败 - {BuildErrorMessage(ex)}"));
                    }
                    finally
                    {
                        DeleteDirectorySafe(extractDir);
                    }
                }

                Dispatcher.Invoke(() => vm.Status.Add($"批量导出完成：成功 {successCount} 首，失败 {failCount} 首。"));
            });

            MessageBox.Show("批量导出完成。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 在 ZIP 中查找 song.json（支持大小写和子目录）。
        private static ZipArchiveEntry? FindSongJsonEntry(ZipArchive archive)
            => archive.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Where(e => string.Equals(e.Name, "song.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => CountZipEntryDepth(e.FullName))
                .ThenBy(e => e.FullName.Length)
                .FirstOrDefault();

        // 统计条目路径深度，用于优先选择浅层 song.json。
        private static int CountZipEntryDepth(string fullName)
            => fullName.Count(ch => ch == '/' || ch == '\\');

        // 将 song.json 解析为列表展示模型。
        private static SongPackInfo ParseSongPackInfo(string zipPath, JsonElement songInfo)
        {
            int slotIndex = TryReadPreferredSlotIndex(songInfo);
            return new SongPackInfo
            {
                ZipPath = zipPath,
                BaseName = songInfo.GetProperty("BaseName").GetString() ?? "",
                SlotIndex = slotIndex,
                SongTitleEnglish = songInfo.TryGetProperty("SongTitleEnglish", out var ste) ? ste.GetString() ?? "" : "",
                SongArtistEnglish = songInfo.TryGetProperty("SongArtistEnglish", out var sae) ? sae.GetString() ?? "" : "",
                ChartCount = songInfo.TryGetProperty("Charts", out var charts) && charts.ValueKind == JsonValueKind.Array ? charts.GetArrayLength() : 0,
                RawJson = songInfo
            };
        }

        // 读取曲包请求槽位；无请求时返回 -1。
        private static int TryReadPreferredSlotIndex(JsonElement songInfo)
        {
            if (songInfo.TryGetProperty("SelectedSlot", out var slotJson) && slotJson.ValueKind == JsonValueKind.Object)
            {
                if (slotJson.TryGetProperty("SlotIndex", out var slotIndexJson))
                    return slotIndexJson.GetInt32();
            }
            return -1;
        }

        // 解析最终写入槽位：优先请求槽位，不可用则自动选空槽。
        private static SongDatabaseSlotInfo ResolveTargetSlot(
            IReadOnlyList<SongDatabaseSlotInfo> slots,
            int preferredSlot,
            HashSet<int> usedSlots)
        {
            bool PreferredUsable(SongDatabaseSlotInfo s)
                => s.SlotIndex == preferredSlot && s.SlotIndex >= 2 && s.IsEmpty && !usedSlots.Contains(s.SlotIndex);

            var preferred = slots.FirstOrDefault(PreferredUsable);
            if (preferred != null)
                return preferred;

            var fallback = slots
                .Where(s => s.SlotIndex >= 2 && s.IsEmpty && !usedSlots.Contains(s.SlotIndex))
                .OrderBy(s => s.SlotIndex)
                .FirstOrDefault();

            if (fallback == null)
                throw new Exception("没有可用空槽可写入。请先在游戏中清理空槽或减少批量导出数量。");

            return fallback;
        }

        // 解压 ZIP 到系统临时目录，避免覆盖原目录同名文件夹。
        private static string ExtractZipToTempDirectory(string zipPath)
        {
            string zipNameWithoutExt = Path.GetFileNameWithoutExtension(zipPath);
            string rootTempDir = Path.Combine(Path.GetTempPath(), "InFalsusSongPackStudio", "BatchExport");
            Directory.CreateDirectory(rootTempDir);

            string safeBaseName = string.Concat(zipNameWithoutExt.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            string extractDir = Path.Combine(rootTempDir, $"{safeBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);

            ZipFile.ExtractToDirectory(zipPath, extractDir);
            return extractDir;
        }

        // 解析并校验曲绘/BGM等必需资源路径。
        private static string ResolveRequiredFilePath(JsonElement songInfo, string propertyName, string extractDir, string displayName)
        {
            string relative = songInfo.TryGetProperty(propertyName, out var p) ? p.GetString() ?? "" : "";
            string fullPath = Path.Combine(extractDir, relative);
            if (string.IsNullOrWhiteSpace(relative) || !File.Exists(fullPath))
                throw new FileNotFoundException($"未找到{displayName}文件：{fullPath}");
            return fullPath;
        }

        // 解析曲绘模板信息。
        private static JacketTemplateCandidate ParseJacketTemplate(JsonElement songInfo)
        {
            if (!songInfo.TryGetProperty("JacketTemplate", out var jacketJson) || jacketJson.ValueKind != JsonValueKind.Object)
                throw new Exception("曲包缺少 JacketTemplate 信息");

            return new JacketTemplateCandidate
            {
                BundleFileIndex = jacketJson.GetProperty("BundleFileIndex").GetInt32(),
                AssetsFileName = jacketJson.GetProperty("AssetsFileName").GetString() ?? "",
                TexturePathId = jacketJson.GetProperty("TexturePathId").GetInt64(),
                MaterialPathId = jacketJson.GetProperty("MaterialPathId").GetInt64(),
                BaseName = jacketJson.GetProperty("BaseName").GetString() ?? "",
                TextureWidth = jacketJson.GetProperty("TextureWidth").GetInt32(),
                TextureHeight = jacketJson.GetProperty("TextureHeight").GetInt32(),
            };
        }

        // 解析谱面列表并校验谱面文件存在性。
        private static List<NewSongChartPackItem> ParseCharts(JsonElement songInfo, string extractDir)
        {
            var charts = new List<NewSongChartPackItem>();
            if (songInfo.TryGetProperty("Charts", out var chartsJson) && chartsJson.ValueKind == JsonValueKind.Array)
            {
                foreach (var chart in chartsJson.EnumerateArray())
                {
                    string chartFileName = chart.TryGetProperty("SourceChartFileName", out var chartFile) ? chartFile.GetString() ?? "" : "";
                    charts.Add(new NewSongChartPackItem
                    {
                        ChartSlotIndex = chart.GetProperty("ChartSlotIndex").GetInt32(),
                        SourceChartFilePath = Path.Combine(extractDir, chartFileName),
                        DifficultyFlag = chart.GetProperty("DifficultyFlag").GetByte(),
                        Available = chart.GetProperty("Available").GetByte(),
                        Rating = chart.GetProperty("Rating").GetInt32(),
                        LevelSectionIndicator = chart.GetProperty("LevelSectionIndicator").GetString() ?? "1",
                        DisplayChartDesigner = chart.GetProperty("DisplayChartDesigner").GetString() ?? "",
                        DisplayJacketDesigner = chart.GetProperty("DisplayJacketDesigner").GetString() ?? ""
                    });
                }
            }

            if (charts.Count == 0)
                throw new Exception("曲包中没有可导出的谱面（Charts 为空）。");

            var missingChart = charts
                .Select(c => c.SourceChartFilePath)
                .FirstOrDefault(p => string.IsNullOrWhiteSpace(p) || !File.Exists(p));
            if (!string.IsNullOrWhiteSpace(missingChart))
                throw new FileNotFoundException($"未找到谱面文件：{missingChart}");

            return charts;
        }

        // 组装导出请求参数。
        private static NewSongPackRequest BuildRequest(
            JsonElement songInfo,
            string bundleFilePath,
            string sharedAssetsFilePath,
            string resourcesAssetsFilePath,
            string gameDirectory,
            string jacketPath,
            string bgmPath,
            SongDatabaseSlotInfo targetSlot,
            JacketTemplateCandidate jacketTemplate,
            IReadOnlyList<NewSongChartPackItem> charts)
        {
            return new NewSongPackRequest
            {
                BundleFilePath = bundleFilePath,
                SharedAssetsFilePath = sharedAssetsFilePath,
                ResourcesAssetsFilePath = resourcesAssetsFilePath,
                OutputDirectory = Path.Combine(gameDirectory, "SongData"),
                JacketImageFilePath = jacketPath,
                BgmFilePath = bgmPath,
                BaseName = songInfo.GetProperty("BaseName").GetString() ?? "",
                KeepJacketOriginalSize = songInfo.TryGetProperty("KeepJacketOriginalSize", out var keepJacket) && keepJacket.GetBoolean(),
                SelectedSlot = targetSlot,
                JacketTemplate = jacketTemplate,
                PreviewStartSeconds = songInfo.TryGetProperty("PreviewStartSeconds", out var previewStart) ? previewStart.GetDouble() : 0,
                PreviewEndSeconds = songInfo.TryGetProperty("PreviewEndSeconds", out var previewEnd) ? previewEnd.GetDouble() : 15,
                DisplayNameSectionIndicator = songInfo.TryGetProperty("DisplayNameSectionIndicator", out var dnsi) ? dnsi.GetString() ?? "" : "",
                DisplayArtistSectionIndicator = songInfo.TryGetProperty("DisplayArtistSectionIndicator", out var dasi) ? dasi.GetString() ?? "" : "",
                SongTitleEnglish = songInfo.TryGetProperty("SongTitleEnglish", out var ste) ? ste.GetString() ?? "" : "",
                SongArtistEnglish = songInfo.TryGetProperty("SongArtistEnglish", out var sae) ? sae.GetString() ?? "" : "",
                GameplayBackground = songInfo.TryGetProperty("GameplayBackground", out var gb) ? gb.GetInt32() : 3,
                RewardStyle = songInfo.TryGetProperty("RewardStyle", out var rs) ? rs.GetInt32() : 0,
                Charts = charts,
                AutoRenameWhenTargetLocked = songInfo.TryGetProperty("AutoRenameWhenTargetLocked", out var autoRename) && autoRename.GetBoolean()
            };
        }

        // 安全删除目录（清理失败时忽略，避免阻断主流程）。
        private static void DeleteDirectorySafe(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        // 统一格式化错误信息（包含内层异常摘要）。
        private static string BuildErrorMessage(Exception ex)
        {
            if (ex.InnerException == null)
                return $"{ex.GetType().Name}: {ex.Message}";
            return $"{ex.GetType().Name}: {ex.Message} | Inner={ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        }

        // 选择游戏目录并刷新关键资源路径。
        private void BtnBrowseGameDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() != true) return;
            ApplyGameDirectory(dialog.FolderName);
        }
        // 根据游戏根目录推导 bundle/assets 路径。
        private void ApplyGameDirectory(string gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
                return;
            var vm = DataContext as BatchBundleViewModel;
            if (vm == null) return;
            GameDirectory = Path.GetFullPath(gameDirectory);
            DataDir = Path.Combine(GameDirectory, "if-app_Data");
            if (!Directory.Exists(DataDir))
            {
                MessageBox.Show("未在该目录下找到 if-app_Data。\n请选择游戏根目录（例如 In Falsus Demo）。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SharedAssetsFilePath = Path.Combine(DataDir, "sharedassets0.assets");
            ResourcesAssetsFilePath = Path.Combine(DataDir, "resources.assets");
            BundleDir = Path.Combine(DataDir, "StreamingAssets", "aa", "StandaloneWindows64");
            string defaultBundle = Path.Combine(BundleDir, "3d6c628d95a26a13f4e5a73be91cb4f7.bundle");
            BundleFilePath = ResolveTargetSongBundlePath(BundleDir, defaultBundle) ?? "";
            var notes = new List<string>();
            if (!File.Exists(SharedAssetsFilePath))
                notes.Add("未找到 if-app_Data\\sharedassets0.assets");
            if (!File.Exists(ResourcesAssetsFilePath))
                notes.Add("未找到 if-app_Data\\resources.assets");
            if (string.IsNullOrWhiteSpace(BundleFilePath))
                notes.Add("未找到目标歌曲数据库 bundle（默认 3d6c...bundle 及目录内 .bundle 已尝试）。");
            vm.GameDirectory = GameDirectory;
            vm.SharedAssetsFilePath = SharedAssetsFilePath;
            vm.ResourcesAssetsFilePath = ResourcesAssetsFilePath;
            vm.BundleFilePath = BundleFilePath;
        }
        // 解析目标歌曲数据库 bundle：优先默认文件名，失败时回退到目录扫描。
        private static string? ResolveTargetSongBundlePath(string bundleDir, string defaultBundlePath)
        {
            if (File.Exists(defaultBundlePath))
                return defaultBundlePath;
            if (!Directory.Exists(bundleDir))
                return null;
            return Directory.EnumerateFiles(bundleDir, "*.bundle", SearchOption.TopDirectoryOnly)
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
    }
}

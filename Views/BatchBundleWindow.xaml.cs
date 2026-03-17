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
using AffToSpcConverter.Utils;
using Microsoft.VisualBasic.ApplicationServices;
using static System.Windows.Forms.AxHost;
using System.Collections.Specialized;
using System.Threading;

namespace AffToSpcConverter.Views
{
    /// <summary>
    /// BatchBundleWindow.xaml 的交互逻辑
    /// </summary>
    /// 

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

    public partial class BatchBundleWindow : Window
    {

        public string GameDirectory { get; set; } = "";
        public string DataDir { get; set; } = "";
        public string SharedAssetsFilePath { get; set; } = "";
        public string ResourcesAssetsFilePath { get; set; } = "";
        public string BundleDir { get; set; } = "";
        public string BundleFilePath { get; set; } = "";

    public BatchBundleWindow()
        {
            InitializeComponent();
            DataContext = new BatchBundleViewModel();
        }
        private void BtnBatchImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择包含曲包ZIP的文件夹"
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var vm = DataContext as BatchBundleViewModel;
            if (vm == null)
            {
                MessageBox.Show("DataContext 未正确初始化。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            vm.SongPacks.Clear();
            vm.Status.Clear();

            var packList = new List<SongPackInfo>();

            foreach (var zipPath in Directory.EnumerateFiles(dialog.SelectedPath, "*.zip"))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    var entry = archive.GetEntry("song.json");
                    if (entry == null)
                    {
                        vm.Status.Add($"{Path.GetFileName(zipPath)}: 未找到 song.json");
                        continue;
                    }
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    var json = reader.ReadToEnd();
                    var songInfo = JsonSerializer.Deserialize<JsonElement>(json);

                    int slotIndex = 0;
                    if (songInfo.TryGetProperty("SelectedSlot", out var slotJson) && slotJson.ValueKind == JsonValueKind.Object)
                    {
                        slotIndex = slotJson.TryGetProperty("SlotIndex", out var idJson) ? idJson.GetInt32() : 0;
                    }

                    var info = new SongPackInfo
                    {
                        ZipPath = zipPath,
                        BaseName = songInfo.GetProperty("BaseName").GetString() ?? "",
                        SlotIndex = slotIndex,
                        SongTitleEnglish = songInfo.TryGetProperty("SongTitleEnglish", out var ste) ? ste.GetString() ?? "" : "",
                        SongArtistEnglish = songInfo.TryGetProperty("SongArtistEnglish", out var sae) ? sae.GetString() ?? "" : "",
                        ChartCount = songInfo.TryGetProperty("Charts", out var charts) && charts.ValueKind == JsonValueKind.Array ? charts.GetArrayLength() : 0,
                        RawJson = songInfo
                    };
                    packList.Add(info);
                }
                catch (Exception ex)
                {
                    vm.Status.Add($"{Path.GetFileName(zipPath)}: {ex.Message}");
                }
            }

            // 按 slot_id 排序
            foreach (var info in packList.OrderBy(p => p.SlotIndex))
            {
                vm.SongPacks.Add(info);
            }

            //StatusTextBox.Text = string.Join(Environment.NewLine, vm.Status);
        }

        private async void BtnBatchExport_Click(object sender, RoutedEventArgs e)
        {
            var vm = (BatchBundleViewModel)DataContext;
            if (vm.SongPacks.Count == 0)
            {
                MessageBox.Show("请先批量导入曲包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            vm.Status.Clear();


            await Task.Run(() =>
            {
                foreach (var pack in vm.SongPacks)
                {
                    try
                    {
                        // 1. 解压 zip 到临时目录
                        string zipDir = Path.GetDirectoryName(pack.ZipPath) ?? "";
                        string zipNameWithoutExt = Path.GetFileNameWithoutExtension(pack.ZipPath);
                        string extractDir = Path.Combine(zipDir, zipNameWithoutExt);

                        if (Directory.Exists(extractDir))
                        {
                            Directory.Delete(extractDir, true);
                        }
                        ZipFile.ExtractToDirectory(pack.ZipPath, extractDir);

                        var songInfo = pack.RawJson;

                        // 2. 构造资源绝对路径
                        string jacketPath = "";
                        string bgmPath = "";
                        if (songInfo.TryGetProperty("JacketImageFileName", out var jacket))
                            jacketPath = Path.Combine(extractDir, jacket.GetString() ?? "");
                        if (songInfo.TryGetProperty("BgmFileName", out var bgm))
                            bgmPath = Path.Combine(extractDir, bgm.GetString() ?? "");

                        // 3. 解析 SelectedSlot
                        SongDatabaseSlotInfo? selectedSlot = null;
                        if (songInfo.TryGetProperty("SelectedSlot", out var slotJson) && slotJson.ValueKind == JsonValueKind.Object)
                        {
                            selectedSlot = new SongDatabaseSlotInfo
                            {
                                SlotIndex = slotJson.GetProperty("SlotIndex").GetInt32(),
                                IsEmpty = slotJson.GetProperty("IsEmpty").GetBoolean(),
                                SongIdValue = (ushort)slotJson.GetProperty("SongIdValue").GetInt32(),
                                BaseName = slotJson.GetProperty("BaseName").GetString() ?? "",
                                ChartCount = slotJson.GetProperty("ChartCount").GetInt32(),
                                DisplayNameSectionIndicator = slotJson.GetProperty("DisplayNameSectionIndicator").GetString() ?? "",
                                DisplayArtistSectionIndicator = slotJson.GetProperty("DisplayArtistSectionIndicator").GetString() ?? "",
                                GameplayBackground = slotJson.GetProperty("GameplayBackground").GetInt32(),
                                RewardStyle = slotJson.GetProperty("RewardStyle").GetInt32(),
                            };
                        }

                        // 4. 解析 JacketTemplate
                        JacketTemplateCandidate? jacketTemplate = null;
                        if (songInfo.TryGetProperty("JacketTemplate", out var jacketJson) && jacketJson.ValueKind == JsonValueKind.Object)
                        {
                            jacketTemplate = new JacketTemplateCandidate
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

                        // 5. 解析 Charts，路径为解压目录下的绝对路径
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

                        // 6. 构造导出请求
                        var request = new NewSongPackRequest
                        {
                            BundleFilePath = BundleFilePath,
                            SharedAssetsFilePath = SharedAssetsFilePath,
                            ResourcesAssetsFilePath = ResourcesAssetsFilePath,
                            OutputDirectory = !string.IsNullOrWhiteSpace(GameDirectory)
                                ? Path.Combine(GameDirectory, "SongData")
                                : GameDirectory,

                            JacketImageFilePath = jacketPath,
                            BgmFilePath = bgmPath,
                            BaseName = songInfo.GetProperty("BaseName").GetString() ?? "",
                            KeepJacketOriginalSize = songInfo.TryGetProperty("KeepJacketOriginalSize", out var keepJacket) && keepJacket.GetBoolean(),

                            SelectedSlot = selectedSlot ?? throw new Exception("曲包缺少 SelectedSlot 信息"),
                            JacketTemplate = jacketTemplate ?? throw new Exception("曲包缺少 JacketTemplate 信息"),

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

                        // 7. 调用导出方法
                        var result = UnitySongResourcePacker.ExportNewSongResources(request);
                        //vm.Status.Add($"{Path.GetFileName(pack.ZipPath)}: 导出成功");
                        Dispatcher.Invoke(() => vm.Status.Add($"{Path.GetFileName(pack.ZipPath)}: 导出成功")
            );

                    }
                    catch (Exception ex)
                    {
                        //vm.Status.Add($"{Path.GetFileName(pack.ZipPath)}: 导出失败 - {ex.Message}");\
                        Dispatcher.Invoke(() =>
                            vm.Status.Add($"{Path.GetFileName(pack.ZipPath)}: 导出失败 - {ex.Message}")
            );
                    }
                }
            });

            //StatusTextBox.Text = string.Join(Environment.NewLine, vm.Status);
            MessageBox.Show("批量导出完成。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void BtnBrowseGameDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() != true) return;
            ApplyGameDirectory(dialog.FolderName);
        }
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
            vm.BundleFilePath = BundleDir;
        }
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

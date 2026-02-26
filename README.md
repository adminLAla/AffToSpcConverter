# AffToSpcConverter

Arcaea → In Falsus 谱面转换与资源打包工具（WPF / .NET 8）。

当前版本：`v0.9.0`

## 项目简介

本项目用于将 Arcaea 的 `.aff` 谱面转换为 In Falsus 使用的 `.spc` 格式，并提供可视化预览、文本编辑、合法性校验、统计信息，以及面向 In Falsus 的新增歌曲资源打包功能（含曲绘 / BGM / 谱面写入与游戏资源索引更新）。

## 主要功能

### 1. 谱面转换（AFF -> SPC）
- 打开 `.aff` 文件并转换为 `.spc` 文本
- 支持转换规则设置、自定义映射规则编辑
- 支持导出 `.spc`

### 2. SPC 文本编辑与校验
- 输出区支持文本编辑（副本编辑，不会直接覆盖原文件）
- 行号显示（输入 / 输出文本区）
- 非法性检验（Error / Warning 分级）
- 错误信息支持定位到对应行
- 文本区与可视化预览可双向同步（切换时同步副本内容）

### 3. 可视化预览与编辑（Skia + BGM）
- SPC 可视化预览（地面 / 天空 / 合并视图 / 分离视图）
- 支持选中、编辑、添加、删除音符
- 支持撤销 / 恢复（预览编辑历史）
- 播放 BGM 并同步预览时间线
- 支持 VSync 渲染与 FPS/ft 显示
- 支持流速（px/s）与播放速度控制

### 4. 统计信息（工具 -> 统计信息）
- 统计 SPC 事件数量（tap / hold / skyarea / flick 等）
- 显示当前规则设置关键参数（中文标注，便于核对）

### ~~5. 打包谱面（新增歌曲资源）~~
~~用于向 In Falsus 游戏资源中新增歌曲。~~

~~功能包括：~~
- ~~自动定位游戏目录内关键文件：~~
  - ~~`sharedassets0.assets`~~
  - ~~`resources.assets`~~
  - ~~`StreamingAssets/aa/.../*.bundle`~~
- ~~写入 / 更新 `SongDatabase`（MonoBehaviour）~~
- ~~写入 / 更新 `StreamingAssetsMapping`（MonoBehaviour）~~
- ~~写入 / 更新 `DynamicStringMapping` （`resources.assets`，曲名/曲师英文）~~
- ~~导入曲绘（Jacket）、BGM、谱面文件（最多 4 个谱面分档）~~
- ~~自动生成并部署加密资源（GUID 32位哈希文件名）~~
- ~~自动备份原始文件（重命名为 `*_original`）~~
- ~~同时在游戏根目录 `SongData` 保留导出备份~~

### 6. 恢复文件（打包 -> 恢复文件...）
- 选择游戏根目录后，一键回滚 `*_original` 备份
- 清理 `sam` 文件夹内由“打包谱面”新增的加密资源
- 弹窗报告执行情况

### 7. 运行时错误日志
- 记录软件运行中的异常与错误信息
- 日志文件：`runtime-errors.log`
- 路径：程序 `exe` 所在目录（`AppContext.BaseDirectory`）

## 依赖项

- .NET 8 (`net8.0-windows`)
- WPF
- SkiaSharp (`SkiaSharp.Views.WPF`)
- NAudio + NAudio.Vorbis（音频播放）
- AssetsTools.NET / AssetsTools.NET.Texture（Unity assets / bundle 读写）

## 运行环境

- Windows（推荐 `win-x64`）
- 若使用“依赖框架（Framework-dependent）”版本：
  - 需安装 `.NET 8 Desktop Runtime`

## 快速开始

### A. AFF 转 SPC
1. 启动程序
2. `文件 -> 打开 .aff...`
3. 点击 `转换`
4. 在输出区检查 / 编辑 `.spc`
5. `文件 -> 保存 .spc...`

### B. 可视化预览与编辑
1. 确保已生成或打开 `.spc`
2. 点击主界面 `可视化预览` 或 `视图 -> 可视化预览`
3. 可在预览界面进行播放、选中、编辑、增删音符
4. 退出预览时会同步回文本副本

### C. 打包谱面（新增歌曲）
1. `打包 -> 打包谱面...`
2. 选择游戏根目录（例如 `...\In Falsus Demo`）
3. 填写：
   - `BaseName`
   - 曲名 / 曲师（English）
   - 歌曲显示与行为字段
   - ChartInfos（最多 4 项）
4. 导入曲绘、BGM、谱面文件
5. 点击 `导出`
6. 成功后会：
   - 写入游戏目录对应位置
   - 备份原文件为 `*_original`
   - 在 `SongData` 目录保留一份导出备份

### D. 恢复修改
1. `打包 -> 恢复文件...`
2. 选择游戏根目录
3. 确认回滚

## 项目结构（简要）

- `Convert/`：转换与预览渲染模型构建
- `Parsing/`：AFF/SPC 解析
- `Models/`：事件与数据模型
- `Views/`：WPF 界面与预览控件
- `ViewModels/`：视图模型
- `Utils/`：校验、统计、打包、Unity 资源处理等工具类
- `IO/`：读写相关逻辑

## 注意事项

- 禁止随意传播此工具。
- 若出现运行异常，请查看程序目录下的 `runtime-errors.log`。

## 许可证

本项目使用 MIT License（见 `LICENSE.txt`）。
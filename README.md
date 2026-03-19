# InFalsus SongPack Studio

InFalsus 曲包导入与资源打包工具（WPF / .NET 8）。

当前版本：`v0.9.0-rc3`

## 项目简介

本项目以 In Falsus 的曲包导入与资源打包为核心，提供歌曲资源写入、导入导出、恢复与校验能力；同时包含 Arcaea `.aff` 到 `.spc` 的转谱、可视化预览与文本编辑等附属功能。

## 主要功能

### 1. 曲包导入与资源打包
- 自动定位游戏目录内关键文件（`sharedassets0.assets`、`resources.assets`、`StreamingAssets/aa/.../*.bundle`）
- 写入 / 更新 `SongDatabase`、`StreamingAssetsMapping`、`DynamicStringMapping`
- 导入曲绘、BGM、谱面文件（最多 4 个谱面分档）
- 生成并部署加密资源（GUID 32 位哈希文件名）
- 自动备份原始文件（重命名为 `*_original`）并在 `SongData` 保留导出备份

### 2. 批量处理并导出曲包
- 支持批量导出曲包到游戏资源目录

### 3. 恢复写入文件
- 选择游戏根目录后，一键回滚 `*_original` 备份
- 清理 `sam` 文件夹内由“打包谱面”新增的加密资源
- 弹窗报告执行情况

### 4. 谱面转换
- 目前仅支持`aff`->`spc`
   - 打开 `.aff` 文件并转换为 `.spc` 文本
   - 支持转换规则设置、自定义映射规则编辑
   - 支持导出 `.spc`

### 5. SPC 文本编辑与校验
- 输出区支持文本编辑（副本编辑，不会直接覆盖原文件）
- 行号显示（输入 / 输出文本区）
- 非法性检验（Error / Warning 分级）
- 支持定位错误信息到对应行
- 文本区与可视化预览可双向同步（切换时同步副本内容）

### 6. 可视化预览与编辑（Skia + BGM）
- SPC 可视化预览（地面 / 天空 / 合并视图 / 分离视图）
- 支持选中、编辑、添加、删除音符
- 支持撤销 / 恢复（预览编辑历史）
- 播放 BGM 并同步预览时间线
- 支持 VSync 渲染与 FPS/ft 显示
- 支持流速（px/s）与播放速度控制
- 目前暂不支持速度倍率变化等高级谱面特效事件预览

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
- Release 提供两种 `x64` 版本：
   - `fd`（framework-dependent）：需要安装 `.NET 8 Desktop Runtime`
   - `sc`（self-contained）：无需额外安装 .NET 运行时

## 快速开始

### 1. 准备环境
- 从 Release 下载对应版本：
   - `x64-fd`：已安装 `.NET 8 Desktop Runtime`
   - `x64-sc`：无需安装运行时，直接使用

### 2. 运行
1. 解压下载的发布包。
2. 双击可执行文件启动程序。

### 3. 首次启动建议流程
1. 点击左侧边栏 `设置`，选择游戏根目录，或者直接在`主页`导航栏点击`前往设置`跳转。
2. 回到 `打包谱面`，确认资源文件已自动定位（`.bundle / sharedassets0.assets / resources.assets`）。
3. 按右侧步骤依次导入曲绘、BGM、谱面并填写曲目信息。
4. 选择 `导出为曲包`（单曲 ZIP，用于批量导出）或 `直接导出`（直接写入游戏目录）。

### 4. 批量导入导出流程
1. 进入 `批量打包`，点击 `导入文件夹` 或 `导入ZIP文件`。
2. 确认列表识别到曲目与槽位信息。
3. 点击 `批量导出` 写入游戏目录。

### 5. 其他页面
- `谱面转换`：目前仅支持aff 转 spc，并可编辑输出spc文本。
- `谱面预览`：导入 spc 后进行可视化查看与编辑。
- `恢复写入文件`：回滚备份并清理新增资源。



## 项目结构（简要）

- `Convert/`：转换与预览渲染模型构建
- `Parsing/`：aff/spc 解析
- `Models/`：事件与数据模型
- `Views/`：WPF 界面与预览控件
- `ViewModels/`：视图模型
- `Utils/`：校验、统计与打包、Unity 资源处理等工具类
- `IO/`：读写相关逻辑

## 致谢

- 感谢 @havebeenseen 提出并实现批量打包相关功能（批量导入 / 批量导出曲包）。

## 注意事项

- 禁止随意传播此工具。
- 若出现运行异常，请查看程序目录下的 `runtime-errors.log`。
- 若导入资源后游戏出现崩溃卡死等相关问题，请查看游戏日志文件 
路径：**%USERPROFILE%\AppData\LocalLow\lowiro\if-app\Player.log**
- 欢迎提交Issues/Pull Requests反馈问题

## 许可证

本项目使用 MIT License（见 `LICENSE.txt`）。
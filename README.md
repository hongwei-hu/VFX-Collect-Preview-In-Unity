# VFX-Collect-Preview-In-Unity

## 简介

本工具用于在 Unity 编辑器中批量收集、预览和管理粒子特效（Particle Prefab）。通过自定义编辑器窗口，开发者可以快速扫描项目中的粒子预制体，并在专用预览场景中进行可视化预览和管理，极大提升特效资源的整理与查找效率。

## 主要功能

- 扫描指定目录下的所有粒子特效预制体（Prefab）。
- 以列表形式展示所有扫描到的粒子特效。
- 支持在自定义预览场景中快速预览粒子效果。
- 支持一键定位、管理和批量操作粒子特效资源。

## 使用方法

1. 将 `Editor/ParticlePrefabCollector` 文件夹下的所有脚本和资源导入到你的 Unity 项目中。
2. 在 Unity 菜单栏选择 `窗口(Window)` > `粒子特效收集器(Particle Prefab Collector)` 打开工具窗口。
3. 点击“扫描”按钮，选择需要扫描的目录，工具会自动收集所有粒子预制体。
4. 在工具窗口中浏览、预览和管理粒子特效资源。

## 依赖环境

- Unity 2020.3 或更高版本
- 支持的操作系统：Windows、macOS

## 文件说明

- `ParticlePrefabCollectorWindow.cs`：主编辑器窗口脚本，实现 UI 和交互逻辑。
- `ParticlePrefabPreviewSceneHelper.cs`：预览场景辅助脚本，用于在专用场景中展示粒子效果。
- `ParticlePrefabScanResult.asset`：扫描结果的 ScriptableObject 资源文件。

## 贡献与反馈

如有建议、Bug 反馈或功能需求，欢迎提交 Issue 或 Pull Request。

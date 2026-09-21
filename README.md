# Project Setting Profiles

## Installation / 安装

- Unity Package Manager (Git): **Window > Package Manager > + > Add package from git URL** and enter `https://your-host/your-plugin-repo.git`. Replace the URL with the published repository address. The repository root contains `package.json`, so no `?path=` suffix is needed. Unity requires Git to be installed locally.
- `.unitypackage`: import the separately exported `ProjectSettingProfiles-1.2.0.unitypackage` using **Assets > Import Package > Custom Package**. In the development Unity project it is located at `Dist/ProjectSettingProfiles-1.2.0.unitypackage` and installs under `Assets/ProjectSettingProfiles/`.
- Choose **one** installation method per project. Installing both creates duplicate editor classes and menus. Neither distribution includes your profiles in `<project>/ProjectSettingsProfiles/`.
- The Git repository contains only the UPM package. The development Unity project keeps its `.unitypackage` export and `Tools/Export-ProjectSettingProfiles.ps1` outside this package repository.

Git 安装请在包管理器中直接使用插件仓库 URL，无须 `?path=`；传统导入请使用单独导出的 `.unitypackage`。两种方式不能同时安装。档案数据不会包含在分发包中。

## 简体中文

在 Unity 2022.3 或更新版本中，通过 **Tools > Project Setting Profiles** 打开窗口。窗口工具栏的 **Language** 下拉菜单可切换为 **简体中文**；默认英文，语言选择会为当前用户保存，不影响 Unity 编辑器语言。

- 创建档案会保存当前 `ProjectSettings` 目录、构建目标，以及 Build Settings 的场景顺序/启用状态、压缩方式、常用开关和目标平台选项。
- 修改项目设置后，点击对应档案的 **保存** 更新快照；修改档案名称、构建目标或 **输出文件夹** 后也需点击 **保存**。输出文件夹留空时沿用原来的自动命名；自定义名称与已有文件夹重名时依次添加 `_2`、`_3` 等后缀。
- **切换** 会应用快照、移除快照中不存在的设置文件并切换构建目标。切换前请保存尚未保存的设置。
- **打包** 会选择输出目录，应用档案并等待 Unity 刷新和平台切换，然后打包 Build Settings 中启用的场景。勾选多个档案可按列表顺序 **批量打包**。每次打包使用独立输出子目录；失败时队列停止，工程保留最后应用的档案。

档案保存在工程根目录的 `ProjectSettingsProfiles/`，可纳入版本控制。`Library/ProjectSettingProfiles/` 仅保存临时队列和回滚数据。`ProjectVersion.txt` 表示 Unity 编辑器版本，不参与切换。

快照递归覆盖 `ProjectSettings` 下的全部文件，包括后续插件新增到该目录的文件。其他插件若把设置保存在 `Assets`、`UserSettings`、`Library`、工程外部或内存中，则无法通用地捕获，需要该插件单独提供导出/导入接口。打包前还需安装目标平台模块并启用至少一个构建场景。

Build Settings 的场景列表通过 Unity API 显式恢复；常用构建开关和 Standalone、Android、WebGL、iOS、WSA、Switch 的可用平台选项也会恢复。压缩方式按目标平台组保存，并用于实际打包。Unity 的非公开压缩设置 API 若在未来版本改变，保存/切换时会明确报错。旧档案仍可使用，但需要重新 **保存** 才能加入新增的 Build Settings 参数；未列入 Unity 公开设置 API 的专有选项不能保证跨版本还原。

### 切换完成回调

通过 `ProfileSwitchEvents.AfterSwitch` 订阅统一回调。手动切换、单档案打包和批量打包均在设置与构建目标应用完成后触发；批量打包的每个档案各触发一次，且回调完成、编辑器刷新/编译稳定后才开始该档案的打包。`IsBuild` 标识是否准备打包，`ProfileIndex` 从 0 开始，`ProfileCount` 是队列总数；非打包切换的 `OutputDirectory` 为 `null`。切换失败不会触发，回调抛出异常会停止任务。其他 Editor 程序集需要引用 `ProjectSettingProfiles.Editor`，并在每次域重载后重新订阅。

```csharp
using ProjectSettingProfiles;
using UnityEditor;

[InitializeOnLoad]
internal static class MyProfileHook
{
    static MyProfileHook() { ProfileSwitchEvents.AfterSwitch += OnSwitched; }

    private static void OnSwitched(object sender, ProfileSwitchedEventArgs args)
    {
        UnityEngine.Debug.Log($"Applied {args.ProfileName} ({args.ProfileIndex + 1}/{args.ProfileCount})");
    }
}
```

## English

Open **Tools > Project Setting Profiles** in Unity 2022.3 or later.

Use the **Language** dropdown in the window toolbar to switch between English and Simplified Chinese. English is the default; the choice is saved per user and does not change the Unity editor language.

- Create a profile to capture the current `ProjectSettings` tree, build target, Build Settings scenes (order and enabled state), compression method, common build switches, and available target-specific options.
- Change settings in Unity, then click **Save** on the desired row to replace its snapshot. Editing its name, target, or **Output Folder** also requires **Save**. An empty output folder uses the previous generated name; a custom name already in use gets a `_2`, `_3`, etc. suffix.
- **Switch** applies the snapshot (including removal of settings files absent from it) and switches build targets. Save any unsaved settings before switching.
- **Build** chooses an output directory, applies the profile, waits for Unity's refresh and platform switch, then builds all enabled scenes. Select multiple rows and use **Batch Build** to run them in list order. Each build gets a unique output subdirectory. A failed build stops the queue; the last applied profile remains active.

Profiles live in `<project>/ProjectSettingsProfiles/` next to `Assets` and `ProjectSettings`. Include that directory in version control to share profiles. `Library/ProjectSettingProfiles/` only holds transient queue and rollback data. `ProjectVersion.txt` is intentionally never switched because it determines the Unity editor version.

The snapshot covers every file recursively under `ProjectSettings`, including files added there by future packages. A plugin that stores its settings in `Assets`, `UserSettings`, `Library`, an external location, or only in memory cannot be captured generically; its own export/import integration would be required. Platform build modules and enabled build scenes must be present for builds.

Build Settings scenes are explicitly restored through Unity's API. Common build switches and available options for Standalone, Android, WebGL, iOS, WSA, and Switch are also restored; compression is saved per target group and applied to builds. Unity's compression-setting API is nonpublic, so an incompatible future editor version reports an error instead of silently ignoring it. Existing profiles remain readable; **Save** them again to capture the new fields. Proprietary options not exposed by Unity's public settings API may not round-trip across versions.

### After-switch callback

Subscribe to `ProfileSwitchEvents.AfterSwitch` as shown above. It fires after a successful settings/target switch for manual switching, single builds, and **each profile** in a batch build, before that profile is built. The queue waits for the callback and any subsequent editor refresh/compilation before building. `IsBuild` identifies build operations; `ProfileIndex` is zero-based, `ProfileCount` is the queue length, and `OutputDirectory` is `null` for a switch without a build. Failed switches do not notify; an exception in a handler stops the operation. Other Editor assemblies must reference `ProjectSettingProfiles.Editor` and subscribe again after domain reload (for example via `[InitializeOnLoad]`).

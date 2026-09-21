# Project Setting Profiles

## Installation / 安装

- Unity Package Manager (Git): **Window > Package Manager > + > Add package from git URL** and enter `https://your-host/your-repo.git?path=/Packages/com.projectsettingprofiles.editor`. Replace the repository URL with the URL of the Git repository containing this project. Unity requires Git to be installed locally. The repository root is a Unity project; the `?path=` suffix is required.
- `.unitypackage`: import `Dist/ProjectSettingProfiles-1.0.0.unitypackage` using **Assets > Import Package > Custom Package**. It installs the editor scripts under `Assets/ProjectSettingProfiles/`.
- Choose **one** installation method per project. Installing both creates duplicate editor classes and menus. Neither distribution includes your profiles in `<project>/ProjectSettingsProfiles/`.
- To rebuild the `.unitypackage` from the UPM source, run `powershell -ExecutionPolicy Bypass -File Tools/Export-ProjectSettingProfiles.ps1` from the repository root.

Git 安装请在包管理器中使用上述带 `?path=` 的仓库 URL；传统导入请使用 `Dist` 下的 `.unitypackage`。两种方式不能同时安装。档案数据不会包含在分发包中。

## 简体中文

在 Unity 2022.3 或更新版本中，通过 **Tools > Project Setting Profiles** 打开窗口。窗口工具栏的 **Language** 下拉菜单可切换为 **简体中文**；默认英文，语言选择会为当前用户保存，不影响 Unity 编辑器语言。

- 创建档案会保存当前 `ProjectSettings` 目录、构建目标，以及开发构建、脚本调试、Profiler 连接和 Android App Bundle 选项。
- 修改项目设置后，点击对应档案的 **保存** 更新快照；修改档案名称或构建目标后也需点击 **保存**。
- **切换** 会应用快照、移除快照中不存在的设置文件并切换构建目标。切换前请保存尚未保存的设置。
- **打包** 会选择输出目录，应用档案并等待 Unity 刷新和平台切换，然后打包 Build Settings 中启用的场景。勾选多个档案可按列表顺序 **批量打包**。每次打包使用独立输出子目录；失败时队列停止，工程保留最后应用的档案。

档案保存在工程根目录的 `ProjectSettingsProfiles/`，可纳入版本控制。`Library/ProjectSettingProfiles/` 仅保存临时队列和回滚数据。`ProjectVersion.txt` 表示 Unity 编辑器版本，不参与切换。

快照递归覆盖 `ProjectSettings` 下的全部文件，包括后续插件新增到该目录的文件。其他插件若把设置保存在 `Assets`、`UserSettings`、`Library`、工程外部或内存中，则无法通用地捕获，需要该插件单独提供导出/导入接口。打包前还需安装目标平台模块并启用至少一个构建场景。

## English

Open **Tools > Project Setting Profiles** in Unity 2022.3 or later.

Use the **Language** dropdown in the window toolbar to switch between English and Simplified Chinese. English is the default; the choice is saved per user and does not change the Unity editor language.

- Create a profile to capture the current `ProjectSettings` tree and selected build target. The snapshot also remembers Development Build, Script Debugging, Profiler connection and Android App Bundle options.
- Change settings in Unity, then click **Save** on the desired row to replace its snapshot. Editing a row's name or target also requires **Save**.
- **Switch** applies the snapshot (including removal of settings files absent from it) and switches build targets. Save any unsaved settings before switching.
- **Build** chooses an output directory, applies the profile, waits for Unity's refresh and platform switch, then builds all enabled scenes. Select multiple rows and use **Batch Build** to run them in list order. Each build gets a unique output subdirectory. A failed build stops the queue; the last applied profile remains active.

Profiles live in `<project>/ProjectSettingsProfiles/` next to `Assets` and `ProjectSettings`. Include that directory in version control to share profiles. `Library/ProjectSettingProfiles/` only holds transient queue and rollback data. `ProjectVersion.txt` is intentionally never switched because it determines the Unity editor version.

The snapshot covers every file recursively under `ProjectSettings`, including files added there by future packages. A plugin that stores its settings in `Assets`, `UserSettings`, `Library`, an external location, or only in memory cannot be captured generically; its own export/import integration would be required. Platform build modules and enabled build scenes must be present for builds.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectSettingProfiles
{
    [Serializable]
    internal sealed class ProfileJob
    {
        public string[] ids;
        public int index;
        public bool build;
        public string outputDirectory;
        public string phase;
    }

    [InitializeOnLoad]
    internal static class ProfileBuildQueue
    {
        private static readonly string JobPath = Path.Combine(ProfileStore.ProjectRoot, "Library", "ProjectSettingProfiles", "pending.json");
        private static readonly string ActiveKey = "ProjectSettingProfiles.Active." + ProfileStore.ProjectRoot;
        private static double readyAfter;
        private static bool running;

        internal static bool IsBusy { get { return File.Exists(JobPath); } }
        internal static string ActiveId { get { return EditorPrefs.GetString(ActiveKey, ""); } }

        static ProfileBuildQueue()
        {
            readyAfter = EditorApplication.timeSinceStartup + 0.5;
            EditorApplication.update += Update;
        }

        internal static void Start(string[] ids, bool build, string outputDirectory = null)
        {
            if (IsBusy) throw new InvalidOperationException(ProfileText.T("已有档案任务正在运行。", "A profile operation is already running."));
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException(ProfileText.T("请退出播放模式并等待编译完成后再切换档案。", "Leave Play Mode and wait for compilation before switching profiles."));
            if (ids == null || ids.Length == 0) throw new ArgumentException(ProfileText.T("请至少选择一个档案。", "Select at least one profile."));
            foreach (var id in ids)
            {
                var profile = ProfileStore.Load(id);
                ProfileStore.ValidateSnapshot(profile);
                if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(profile.target), profile.target))
                    throw new InvalidOperationException(ProfileText.T("未安装此平台的构建支持：", "Build support is not installed for ") + profile.target);
            }
            if (build && (string.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory)))
                throw new DirectoryNotFoundException(ProfileText.T("请选择已存在的打包输出目录。", "Choose an existing build output directory."));
            if (build)
            {
                var full = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var protectedRoot in new[] { Application.dataPath, ProfileStore.SettingsRoot, ProfileStore.ProfilesRoot })
                    if (full.StartsWith(Path.GetFullPath(protectedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(ProfileText.T("请选择 Assets、ProjectSettings 和 ProjectSettingsProfiles 之外的打包目录。", "Choose a build directory outside Assets, ProjectSettings and ProjectSettingsProfiles."));
            }
            AssetDatabase.SaveAssets();
            Save(new ProfileJob { ids = ids, build = build, outputDirectory = outputDirectory, phase = "apply" });
            readyAfter = EditorApplication.timeSinceStartup;
        }

        private static void Update()
        {
            if (running || !IsBusy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < readyAfter) return;
            running = true;
            try
            {
                var job = JsonUtility.FromJson<ProfileJob>(File.ReadAllText(JobPath));
                if (job == null || job.ids == null || job.index < 0 || job.index >= job.ids.Length)
                    throw new InvalidDataException(ProfileText.T("档案打包队列无效。", "Invalid profile build queue."));
                var profile = ProfileStore.Load(job.ids[job.index]);
                switch (job.phase)
                {
                    case "apply":
                        job.phase = "switch";
                        Save(job);
                        ProfileStore.Apply(profile);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        WaitForEditor();
                        break;
                    case "switch":
                        if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(profile.target), profile.target))
                            throw new InvalidOperationException(ProfileText.T("未安装此平台的构建支持：", "Build support is not installed for ") + profile.target);
                        job.phase = "ready";
                        Save(job);
                        if (EditorUserBuildSettings.activeBuildTarget != profile.target &&
                            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(profile.target), profile.target))
                            throw new InvalidOperationException(ProfileText.T("无法切换构建目标至 ", "Could not switch build target to ") + profile.target);
                        WaitForEditor();
                        break;
                    case "ready":
                        if (EditorUserBuildSettings.activeBuildTarget != profile.target)
                            throw new InvalidOperationException(ProfileText.T("构建目标切换未完成：", "Build target switch did not finish: ") + profile.target);
                        if (profile.buildSettings != null) profile.buildSettings.Apply(profile.target);
                        else
                        {
                            EditorUserBuildSettings.development = profile.development;
                            EditorUserBuildSettings.allowDebugging = profile.allowDebugging;
                            EditorUserBuildSettings.connectProfiler = profile.connectProfiler;
                            EditorUserBuildSettings.buildAppBundle = profile.buildAppBundle;
                        }
                        EditorPrefs.SetString(ActiveKey, profile.id);
                        job.phase = "after-switch";
                        Save(job);
                        ProfileSwitchEvents.Raise(new ProfileSwitchedEventArgs(profile.id, profile.name, profile.target,
                            job.build, job.index, job.ids.Length, job.outputDirectory));
                        WaitForEditor();
                        break;
                    case "after-switch":
                        if (EditorUserBuildSettings.activeBuildTarget != profile.target)
                            throw new InvalidOperationException(ProfileText.T("回调后构建目标发生变化：", "Build target changed after callback: ") + profile.target);
                        if (job.build)
                        {
                            job.phase = "building";
                            Save(job);
                            Build(profile, job.outputDirectory);
                        }
                        Next(job);
                        break;
                    case "building":
                        throw new InvalidOperationException(ProfileText.T("编辑器在打包时重新加载。为避免重复打包，队列已停止。", "The editor reloaded during a build. The queue was stopped to avoid building twice."));
                    default:
                        throw new InvalidDataException(ProfileText.T("未知的队列阶段：", "Unknown queue phase: ") + job.phase);
                }
            }
            catch (Exception e)
            {
                if (File.Exists(JobPath)) File.Delete(JobPath);
                Debug.LogException(e);
                EditorUtility.DisplayDialog(ProfileText.Title, ProfileText.T("操作已停止：", "Operation stopped: ") + e.Message, ProfileText.Ok);
            }
            finally { running = false; }
        }

        private static void Next(ProfileJob job)
        {
            job.index++;
            if (job.index == job.ids.Length)
            {
                File.Delete(JobPath);
                Debug.Log(job.build ? ProfileText.T("所有档案打包完成。", "Profile builds completed.") : ProfileText.T("档案切换完成。", "Profile switched."));
                if (job.build) EditorUtility.DisplayDialog(ProfileText.Title, ProfileText.T("所选档案均已成功打包。", "All selected profiles built successfully."), ProfileText.Ok);
            }
            else
            {
                job.phase = "apply";
                Save(job);
                WaitForEditor();
            }
        }

        private static void Build(Profile profile, string root)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException(ProfileText.T("构建设置中没有启用的场景，档案：", "No enabled scenes in Build Settings for ") + profile.name);
            var folder = OutputFolder(profile, root);
            Directory.CreateDirectory(folder);
            var bundle = profile.buildSettings?.GetBool("buildAppBundle", profile.buildAppBundle) ?? profile.buildAppBundle;
            var extension = Extension(profile.target, bundle);
            var output = extension == null ? Path.Combine(folder, "Build") : Path.Combine(folder, SafeName(PlayerSettings.productName) + extension);
            var settings = profile.buildSettings;
            var development = settings?.GetBool("development", profile.development) ?? profile.development;
            var options = development ? BuildOptions.Development : BuildOptions.None;
            if (development && (settings?.GetBool("allowDebugging", profile.allowDebugging) ?? profile.allowDebugging)) options |= BuildOptions.AllowDebugging;
            if (development && (settings?.GetBool("connectProfiler", profile.connectProfiler) ?? profile.connectProfiler)) options |= BuildOptions.ConnectWithProfiler;
            if (development && settings != null && settings.GetBool("buildWithDeepProfilingSupport")) options |= BuildOptions.EnableDeepProfilingSupport;
            if (settings != null)
            {
                if (settings.GetBool("waitForPlayerConnection")) options |= BuildOptions.WaitForPlayerConnection;
                if (settings.GetBool("buildScriptsOnly")) options |= BuildOptions.BuildScriptsOnly;
                if (settings.GetBool("symlinkSources")) options |= BuildOptions.SymlinkSources;
                if (settings.GetBool("symlinkLibraries")) options |= BuildOptions.SymlinkLibraries;
                if (settings.GetBool("installInBuildFolder")) options |= BuildOptions.InstallInBuildFolder;
                if (settings.GetBool("enableHeadlessMode")) options |= BuildOptions.EnableHeadlessMode;
                if (settings.compressionType == 2) options |= BuildOptions.CompressWithLz4;
                if (settings.compressionType == 3) options |= BuildOptions.CompressWithLz4HC;
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = profile.target,
                subtarget = settings != null && BuildPipeline.GetBuildTargetGroup(profile.target) == BuildTargetGroup.Standalone
                    ? settings.GetInt("standaloneBuildSubtarget") : 0,
                options = options
            });
            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(ProfileText.T("档案打包失败：", "Build failed for ") + profile.name + ProfileText.T("。详情请查看控制台。", ". See Console for details."));
            Debug.Log(ProfileText.T("档案“" + profile.name + "”已打包至 ", "Built profile '" + profile.name + "' to ") + output);
        }

        private static string Extension(BuildTarget target, bool buildAppBundle)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return ".exe";
                case BuildTarget.StandaloneLinux64: return ".x86_64";
                case BuildTarget.StandaloneOSX: return ".app";
                case BuildTarget.Android: return buildAppBundle ? ".aab" : ".apk";
                default: return null;
            }
        }

        private static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var value = new string((name ?? "Build").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(value) ? "Build" : value;
        }

        private static string OutputFolder(Profile profile, string root)
        {
            var name = string.IsNullOrEmpty(profile.outputFolderName)
                ? SafeName(profile.name) + "_" + profile.target + "_" + profile.id.Substring(0, 8) +
                    "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff")
                : profile.outputFolderName;
            var path = Path.Combine(root, name);
            for (var suffix = 2; Directory.Exists(path) || File.Exists(path); suffix++)
                path = Path.Combine(root, name + "_" + suffix);
            return path;
        }

        private static void WaitForEditor() { readyAfter = EditorApplication.timeSinceStartup + 0.5; }

        private static void Save(ProfileJob job)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JobPath));
            File.WriteAllText(JobPath, JsonUtility.ToJson(job));
        }
    }
}

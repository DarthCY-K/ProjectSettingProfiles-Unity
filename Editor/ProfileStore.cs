using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace ProjectSettingProfiles
{
    [Serializable]
    internal sealed class ProfileFile
    {
        public string path;
        public string sha256;
    }

    [Serializable]
    internal sealed class Profile
    {
        public string id;
        public string name;
        public BuildTarget target;
        public bool development;
        public bool allowDebugging;
        public bool connectProfiler;
        public bool buildAppBundle;
        public List<ProfileFile> files = new List<ProfileFile>();
    }

    internal static class ProfileStore
    {
        internal static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        internal static readonly string SettingsRoot = Path.Combine(ProjectRoot, "ProjectSettings");
        internal static readonly string ProfilesRoot = Path.Combine(ProjectRoot, "ProjectSettingsProfiles");
        private const string Manifest = "profile.json";

        internal static List<Profile> LoadAll()
        {
            var profiles = new List<Profile>();
            if (!Directory.Exists(ProfilesRoot)) return profiles;
            foreach (var directory in Directory.GetDirectories(ProfilesRoot))
            {
                var id = Path.GetFileName(directory);
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                try { profiles.Add(Load(id)); }
                catch (Exception e) { Debug.LogWarning(ProfileText.T("跳过无效的项目设置档案 ", "Skipping invalid project setting profile ") + id + ": " + e.Message); }
            }
            return profiles.OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static Profile Load(string id)
        {
            ValidateId(id);
            var profile = JsonUtility.FromJson<Profile>(File.ReadAllText(Path.Combine(ProfileDirectory(id), Manifest)));
            if (profile == null || profile.id != id || profile.files == null || string.IsNullOrWhiteSpace(profile.name))
                throw new InvalidDataException(ProfileText.T("档案清单无效：", "Invalid profile manifest: ") + id);
            return profile;
        }

        internal static Profile Save(string id, string name, BuildTarget target)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(ProfileText.T("档案名称不能为空。", "Profile name cannot be empty."));
            if (target == BuildTarget.NoTarget) throw new ArgumentException(ProfileText.T("请选择构建目标。", "Select a build target."));
            if (id == null) id = Guid.NewGuid().ToString("N");
            ValidateId(id);
            Directory.CreateDirectory(ProfilesRoot);
            var stage = Path.Combine(ProfilesRoot, Guid.NewGuid().ToString("N") + ".tmp");
            var destination = ProfileDirectory(id);
            var backup = Path.Combine(ProfilesRoot, id + ".bak");
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(destination)) throw new IOException(ProfileText.T("上次的档案备份需要人工检查：", "Previous profile backup requires manual review: ") + backup);
                Directory.Move(backup, destination);
            }
            var profile = new Profile
            {
                id = id,
                name = name.Trim(),
                target = target,
                development = EditorUserBuildSettings.development,
                allowDebugging = EditorUserBuildSettings.allowDebugging,
                connectProfiler = EditorUserBuildSettings.connectProfiler,
                buildAppBundle = EditorUserBuildSettings.buildAppBundle
            };
            Directory.CreateDirectory(Path.Combine(stage, "settings"));
            try
            {
                foreach (var path in EnumerateSettings())
                {
                    var relative = RelativePath(path);
                    var copy = SnapshotPath(stage, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(path, copy);
                    profile.files.Add(new ProfileFile { path = relative, sha256 = Hash(copy) });
                }
                File.WriteAllText(Path.Combine(stage, Manifest), JsonUtility.ToJson(profile, true));
                if (Directory.Exists(destination)) Directory.Move(destination, backup);
                try { Directory.Move(stage, destination); }
                catch
                {
                    if (Directory.Exists(backup)) Directory.Move(backup, destination);
                    throw;
                }
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                return profile;
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }

        internal static void Delete(string id)
        {
            ValidateId(id);
            var path = ProfileDirectory(id);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        internal static void ValidateSnapshot(Profile profile)
        {
            ValidateId(profile.id);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in profile.files)
            {
                ValidateRelative(file.path);
                if (!paths.Add(file.path)) throw new InvalidDataException(ProfileText.T("档案中有重复路径：", "Duplicate path: ") + file.path);
                var source = SnapshotPath(ProfileDirectory(profile.id), file.path);
                if (!File.Exists(source) || !string.Equals(Hash(source), file.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(ProfileText.T("档案文件缺失或损坏：", "Missing or damaged profile file: ") + file.path);
            }
        }

        internal static void Apply(Profile profile)
        {
            ValidateSnapshot(profile);
            var desired = new HashSet<string>(profile.files.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
            var previous = EnumerateSettings().ToArray();
            var rollback = Path.Combine(ProjectRoot, "Library", "ProjectSettingProfiles", "rollback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rollback);
            bool completed = false;
            try
            {
                foreach (var path in previous)
                {
                    var copy = Path.Combine(rollback, RelativePath(path).Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(path, copy);
                }
                foreach (var path in previous)
                    if (!desired.Contains(RelativePath(path))) File.Delete(path);
                foreach (var file in profile.files)
                {
                    var destination = SettingsPath(file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(SnapshotPath(ProfileDirectory(profile.id), file.path), destination, true);
                }
                completed = true;
            }
            catch
            {
                foreach (var path in EnumerateSettings()) File.Delete(path);
                foreach (var path in Directory.GetFiles(rollback, "*", SearchOption.AllDirectories))
                {
                    var relative = path.Substring(rollback.Length).TrimStart(Path.DirectorySeparatorChar);
                    var destination = Path.Combine(SettingsRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(path, destination, true);
                }
                completed = true;
                throw;
            }
            finally
            {
                // Keep the rollback copy if restoration itself fails.
                if (completed && Directory.Exists(rollback)) Directory.Delete(rollback, true);
            }
            // Avoid deleting ProjectVersion.txt: it identifies the editor version, not a setting.
            foreach (var directory in Directory.GetDirectories(SettingsRoot, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }

        private static IEnumerable<string> EnumerateSettings()
        {
            if (!Directory.Exists(SettingsRoot)) yield break;
            var pending = new Stack<string>();
            pending.Push(SettingsRoot);
            while (pending.Count != 0)
            {
                var directory = pending.Pop();
                foreach (var child in Directory.GetDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException(ProfileText.T("不支持链接形式的设置目录：", "Linked settings directories are not supported: ") + child);
                    pending.Push(child);
                }
                foreach (var path in Directory.GetFiles(directory))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException(ProfileText.T("不支持链接形式的设置文件：", "Linked settings files are not supported: ") + path);
                    if (!string.Equals(RelativePath(path), "ProjectVersion.txt", StringComparison.OrdinalIgnoreCase)) yield return path;
                }
            }
        }

        private static string RelativePath(string path)
        {
            return path.Substring(SettingsRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
        }

        private static string SettingsPath(string relative)
        {
            ValidateRelative(relative);
            return Path.Combine(SettingsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SnapshotPath(string root, string relative)
        {
            ValidateRelative(relative);
            return Path.Combine(root, "settings", relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void ValidateRelative(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("\\") || path.Contains(":") ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.Split('/').Any(part => part == "" || part == "." || part == "..") ||
                string.Equals(path, "ProjectVersion.txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(ProfileText.T("项目设置路径无效：", "Invalid project setting path: ") + path);
        }

        private static void ValidateId(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException(ProfileText.T("档案 ID 无效：", "Invalid profile id: ") + id);
        }

        private static string ProfileDirectory(string id) { return Path.Combine(ProfilesRoot, id); }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}

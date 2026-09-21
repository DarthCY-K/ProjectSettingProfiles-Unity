using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ProjectSettingProfiles
{
    [Serializable]
    internal sealed class ProfileBuildScene
    {
        public string path;
        public bool enabled;
    }

    [Serializable]
    internal sealed class ProfileBuildValue
    {
        public string name;
        public string value;
    }

    [Serializable]
    internal sealed class ProfileBuildSettings
    {
        private static readonly string[] CommonOptions =
        {
            "development", "allowDebugging", "connectProfiler", "buildWithDeepProfilingSupport",
            "waitForPlayerConnection", "waitForManagedDebugger", "buildScriptsOnly",
            "overrideMaxTextureSize", "overrideTextureCompression"
        };

        private static readonly Dictionary<BuildTargetGroup, string[]> PlatformOptions = new Dictionary<BuildTargetGroup, string[]>
        {
            { BuildTargetGroup.Standalone, new[] { "standaloneBuildSubtarget", "symlinkSources", "symlinkLibraries", "installInBuildFolder", "enableHeadlessMode", "macOSXcodeBuildConfig" } },
            { BuildTargetGroup.Android, new[] { "androidBuildSubtarget", "androidETC2Fallback", "androidBuildSystem", "androidBuildType", "androidCreateSymbols", "androidCreateSymbolsZip", "androidUseLegacySdkTools", "androidDebugMinification", "androidReleaseMinification", "exportAsGoogleAndroidProject", "buildAppBundle", "symlinkSources" } },
            { BuildTargetGroup.WebGL, new[] { "webGLBuildSubtarget", "webGLUsePreBuiltUnityEngine" } },
            { BuildTargetGroup.iOS, new[] { "iOSXcodeBuildConfig", "iOSBuildConfigType", "symlinkSources", "symlinkLibraries" } },
            { BuildTargetGroup.WSA, new[] { "wsaSubtarget", "wsaSDK", "wsaUWPBuildType", "wsaUWPSDK", "wsaMinUWPSDK", "wsaArchitecture", "wsaUWPVisualStudioVersion" } },
            { BuildTargetGroup.Switch, new[] { "switchEnableRomCompression", "switchRomCompressionType", "switchRomCompressionLevel", "switchRomCompressionConfig", "switchSaveADF" } }
        };

        public List<ProfileBuildScene> scenes = new List<ProfileBuildScene>();
        public List<ProfileBuildValue> options = new List<ProfileBuildValue>();
        public int compressionType = -1;

        internal static ProfileBuildSettings Capture(BuildTarget target)
        {
            var settings = new ProfileBuildSettings
            {
                scenes = EditorBuildSettings.scenes.Select(scene => new ProfileBuildScene
                {
                    path = scene.path,
                    enabled = scene.enabled
                }).ToList(),
                compressionType = GetCompressionType(target)
            };
            foreach (var name in OptionNames(target))
            {
                var property = typeof(EditorUserBuildSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (property == null || !property.CanRead || !property.CanWrite) continue;
                try
                {
                    settings.options.Add(new ProfileBuildValue
                    {
                        name = name,
                        value = Convert.ToString(property.GetValue(null, null), CultureInfo.InvariantCulture)
                    });
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogWarning("Could not capture Build Settings option " + name + ": " + e.InnerException?.Message);
                }
            }
            return settings;
        }

        internal void Apply(BuildTarget target)
        {
            if (options != null)
            {
                var values = options.Where(option => option != null).GroupBy(option => option.name)
                    .ToDictionary(group => group.Key, group => group.Last().value);
                foreach (var name in OptionNames(target))
                {
                    if (!values.TryGetValue(name, out var value)) continue;
                    var property = typeof(EditorUserBuildSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                    if (property == null || !property.CanWrite)
                    {
                        Debug.LogWarning("Build Settings option is unavailable in this Unity version: " + name);
                        continue;
                    }
                    var parsed = property.PropertyType.IsEnum
                        ? Enum.Parse(property.PropertyType, value)
                        : Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture);
                    property.SetValue(null, parsed, null);
                }
            }
            SetCompressionType(target, compressionType);
            if (scenes != null)
                EditorBuildSettings.scenes = scenes.Select(scene => new EditorBuildSettingsScene(scene.path, scene.enabled)).ToArray();
        }

        internal bool GetBool(string name, bool fallback = false)
        {
            var value = options?.FirstOrDefault(option => option != null && option.name == name)?.value;
            return value == null ? fallback : bool.Parse(value);
        }

        internal int GetInt(string name, int fallback = 0)
        {
            var value = options?.FirstOrDefault(option => option != null && option.name == name)?.value;
            return value == null ? fallback : int.Parse(value, CultureInfo.InvariantCulture);
        }

        private static IEnumerable<string> OptionNames(BuildTarget target)
        {
            foreach (var name in CommonOptions) yield return name;
            if (PlatformOptions.TryGetValue(BuildPipeline.GetBuildTargetGroup(target), out var platform))
                foreach (var name in platform) yield return name;
        }

        private static int GetCompressionType(BuildTarget target)
        {
            var method = typeof(EditorUserBuildSettings).GetMethod("GetCompressionType", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(BuildTargetGroup) }, null);
            if (method == null) throw new NotSupportedException("This Unity version does not expose the Build Settings compression method.");
            return Convert.ToInt32(method.Invoke(null, new object[] { BuildPipeline.GetBuildTargetGroup(target) }), CultureInfo.InvariantCulture);
        }

        private static void SetCompressionType(BuildTarget target, int compressionType)
        {
            if (compressionType != -1 && compressionType != 0 && compressionType != 2 && compressionType != 3)
                throw new ArgumentOutOfRangeException(nameof(compressionType));
            var method = typeof(EditorUserBuildSettings).GetMethod("SetCompressionType", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new NotSupportedException("This Unity version does not expose the Build Settings compression method.");
            var compression = Enum.ToObject(method.GetParameters()[1].ParameterType, compressionType);
            method.Invoke(null, new[] { (object)BuildPipeline.GetBuildTargetGroup(target), compression });
        }
    }
}

using System;
using UnityEditor;

namespace ProjectSettingProfiles
{
    public sealed class ProfileSwitchedEventArgs : EventArgs
    {
        public string ProfileId { get; private set; }
        public string ProfileName { get; private set; }
        public BuildTarget BuildTarget { get; private set; }
        public bool IsBuild { get; private set; }
        public int ProfileIndex { get; private set; }
        public int ProfileCount { get; private set; }
        public string OutputDirectory { get; private set; }

        internal ProfileSwitchedEventArgs(string id, string name, BuildTarget target, bool isBuild,
            int index, int count, string outputDirectory)
        {
            ProfileId = id;
            ProfileName = name;
            BuildTarget = target;
            IsBuild = isBuild;
            ProfileIndex = index;
            ProfileCount = count;
            OutputDirectory = outputDirectory;
        }
    }

    public static class ProfileSwitchEvents
    {
        public static event EventHandler<ProfileSwitchedEventArgs> AfterSwitch;

        internal static void Raise(ProfileSwitchedEventArgs args)
        {
            var handler = AfterSwitch;
            if (handler != null) handler(null, args);
        }
    }
}

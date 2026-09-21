using UnityEditor;

namespace ProjectSettingProfiles
{
    internal static class ProfileText
    {
        private const string LanguageKey = "ProjectSettingProfiles.Language";
        private static int languageIndex = EditorPrefs.GetInt(LanguageKey, 0) == 1 ? 1 : 0;

        internal static int LanguageIndex
        {
            get { return languageIndex; }
            set
            {
                languageIndex = value == 1 ? 1 : 0;
                EditorPrefs.SetInt(LanguageKey, languageIndex);
            }
        }
        internal static bool IsChinese { get { return languageIndex == 1; } }
        internal static string T(string chinese, string english) { return IsChinese ? chinese : english; }
        internal static string Title { get { return T("项目设置档案", "Project Setting Profiles"); } }
        internal static string Ok { get { return T("确定", "OK"); } }
    }
}

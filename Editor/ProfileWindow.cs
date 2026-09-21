using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectSettingProfiles
{
    internal sealed class ProfileWindow : EditorWindow
    {
        private static readonly string[] Languages = { "English", "简体中文" };
        private readonly HashSet<string> selected = new HashSet<string>();
        private readonly Dictionary<string, string> names = new Dictionary<string, string>();
        private readonly Dictionary<string, string> outputFolderNames = new Dictionary<string, string>();
        private readonly Dictionary<string, BuildTarget> targets = new Dictionary<string, BuildTarget>();
        private List<Profile> profiles = new List<Profile>();
        private string newName;
        private bool? nameLanguage;
        private BuildTarget newTarget;
        private Vector2 scroll;

        [MenuItem("Tools/Project Setting Profiles")]
        private static void Open() { GetWindow<ProfileWindow>(ProfileText.Title); }

        private void OnEnable()
        {
            minSize = new Vector2(950, 240);
            newTarget = EditorUserBuildSettings.activeBuildTarget;
            if (string.IsNullOrEmpty(newName)) newName = ProfileText.T("新档案", "New Profile");
            nameLanguage = ProfileText.IsChinese;
            Reload();
            EditorApplication.update += Repaint;
        }

        private void OnDisable() { EditorApplication.update -= Repaint; }

        private void Reload()
        {
            profiles = ProfileStore.LoadAll();
            foreach (var profile in profiles)
            {
                names[profile.id] = profile.name;
                outputFolderNames[profile.id] = profile.outputFolderName ?? "";
                targets[profile.id] = profile.target;
            }
            selected.RemoveWhere(id => profiles.All(p => p.id != id));
            Repaint();
        }

        private void OnGUI()
        {
            if (nameLanguage != ProfileText.IsChinese)
            {
                if (newName == (nameLanguage == true ? "新档案" : "New Profile"))
                    newName = ProfileText.T("新档案", "New Profile");
                nameLanguage = ProfileText.IsChinese;
            }
            titleContent = new GUIContent(ProfileText.Title);
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            newName = EditorGUILayout.TextField(ProfileText.T("新档案", "New profile"), newName);
            newTarget = (BuildTarget)EditorGUILayout.EnumPopup(newTarget, GUILayout.Width(180));
            using (new EditorGUI.DisabledScope(ProfileBuildQueue.IsBusy))
                if (GUILayout.Button(ProfileText.T("创建快照", "Create Snapshot"), GUILayout.Width(110))) Execute(() =>
                {
                    ProfileStore.Save(null, newName, newTarget);
                    Reload();
                });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(ProfileText.T("档案", "Profiles"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(ProfileText.T("语言", "Language"), EditorStyles.miniLabel, GUILayout.Width(55));
            var language = EditorGUILayout.Popup(ProfileText.LanguageIndex, Languages, EditorStyles.toolbarPopup, GUILayout.Width(100));
            if (language != ProfileText.LanguageIndex)
            {
                ProfileText.LanguageIndex = language;
                Repaint();
            }
            using (new EditorGUI.DisabledScope(ProfileBuildQueue.IsBusy || selected.Count == 0))
                if (GUILayout.Button(ProfileText.T("批量打包", "Batch Build") + " (" + selected.Count + ")", EditorStyles.toolbarButton, GUILayout.Width(125)))
                    Build(profiles.Where(p => selected.Contains(p.id)).Select(p => p.id).ToArray());
            if (GUILayout.Button(ProfileText.T("刷新", "Refresh"), EditorStyles.toolbarButton, GUILayout.Width(60))) Reload();
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(35);
            GUILayout.Label(ProfileText.T("档案名称", "Profile Name"), GUILayout.Width(180));
            GUILayout.Label(ProfileText.T("构建目标", "Build Target"), GUILayout.Width(170));
            GUILayout.Label(new GUIContent(ProfileText.T("输出文件夹", "Output Folder"),
                ProfileText.T("留空时使用自动名称。", "Leave empty for the automatic name.")), GUILayout.Width(180));
            EditorGUILayout.EndHorizontal();
            foreach (var profile in profiles)
            {
                EditorGUILayout.BeginHorizontal();
                bool checkedNow = selected.Contains(profile.id);
                if (GUILayout.Toggle(checkedNow, GUIContent.none, GUILayout.Width(20)) != checkedNow)
                {
                    if (checkedNow) selected.Remove(profile.id);
                    else selected.Add(profile.id);
                }
                GUILayout.Label(ProfileBuildQueue.ActiveId == profile.id ? "●" : " ", GUILayout.Width(15));
                names[profile.id] = EditorGUILayout.TextField(names[profile.id], GUILayout.Width(180));
                targets[profile.id] = (BuildTarget)EditorGUILayout.EnumPopup(targets[profile.id], GUILayout.Width(170));
                outputFolderNames[profile.id] = EditorGUILayout.TextField(outputFolderNames[profile.id], GUILayout.Width(180));
                using (new EditorGUI.DisabledScope(ProfileBuildQueue.IsBusy))
                {
                    if (GUILayout.Button(ProfileText.T("保存", "Save"), GUILayout.Width(60))) Execute(() =>
                    {
                        ProfileStore.Save(profile.id, names[profile.id], targets[profile.id], outputFolderNames[profile.id]);
                        Reload();
                    });
                    if (GUILayout.Button(ProfileText.T("切换", "Switch"), GUILayout.Width(60)) &&
                        EditorUtility.DisplayDialog(ProfileText.T("切换档案", "Switch Profile"),
                            ProfileText.T("当前 ProjectSettings 文件将被该档案覆盖。继续？", "This profile will overwrite the current ProjectSettings files. Continue?"),
                            ProfileText.T("切换", "Switch"), ProfileText.T("取消", "Cancel")))
                        Execute(() => ProfileBuildQueue.Start(new[] { profile.id }, false));
                    if (GUILayout.Button(ProfileText.T("打包", "Build"), GUILayout.Width(60))) Build(new[] { profile.id });
                    if (GUILayout.Button(ProfileText.T("删除", "Delete"), GUILayout.Width(60)) &&
                        EditorUtility.DisplayDialog(ProfileText.T("删除档案", "Delete Profile"),
                            ProfileText.T("删除档案“" + profile.name + "”及其快照？", "Delete profile '" + profile.name + "' and its snapshot?"),
                            ProfileText.T("删除", "Delete"), ProfileText.T("取消", "Cancel")))
                        Execute(() =>
                        {
                            ProfileStore.Delete(profile.id);
                            Reload();
                        });
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (ProfileBuildQueue.IsBusy) EditorGUILayout.HelpBox(ProfileText.T("正在切换设置或打包，请等待任务完成。", "Switching settings or building. Wait for the operation to finish."), MessageType.Info);
        }

        private static void Build(string[] ids)
        {
            var directory = EditorUtility.OpenFolderPanel(ProfileText.T("选择打包输出目录", "Choose Build Output Directory"), "", "");
            if (string.IsNullOrEmpty(directory)) return;
            Execute(() => ProfileBuildQueue.Start(ids, true, directory));
        }

        private static void Execute(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog(ProfileText.Title, e.Message, ProfileText.Ok);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Klei;
using Newtonsoft.Json;
using UnityEngine;

namespace ModManager_XJ
{
    // 数据层：备注库 + 预设的读写，数据都放在用户目录 OniModManager 下
    public static class ModManagerStore
    {
        private static readonly string DataDir = Path.Combine(Util.RootFolder(), "OniModManager");
        private static readonly string NotesPath = Path.Combine(DataDir, "notes.json");
        private static readonly string PresetsDir = Path.Combine(DataDir, "presets");
        private static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");

        // 管理器设置
        public class ManagerSettings
        {
            public bool mergeEnabled;          // 合并显示开关
            public bool mergeSortEnabled;      // 移动时是否合并排序
            public bool hasPromptedSort;       // 是否已弹过排序提示（只弹一次）
            public Dictionary<string, string> preferredPlatform;  // 每个配对优先用哪个平台（staticID → "Local"/"Steam"）
        }

        private static ManagerSettings s_settings;
        private static bool s_settingsLoaded;

        public static ManagerSettings LoadSettings()
        {
            if (s_settingsLoaded) return s_settings;
            s_settingsLoaded = true;
            try
            {
                EnsureDataDir();
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        s_settings = JsonConvert.DeserializeObject<ManagerSettings>(json);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 加载设置失败: " + e);
            }
            if (s_settings == null)
            {
                s_settings = new ManagerSettings();
                s_settings.mergeEnabled = false;
                s_settings.mergeSortEnabled = true;
                s_settings.preferredPlatform = new Dictionary<string, string>();
            }
            if (s_settings.preferredPlatform == null)
            {
                s_settings.preferredPlatform = new Dictionary<string, string>();
            }
            return s_settings;
        }

        public static void SaveSettings()
        {
            try
            {
                EnsureDataDir();
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(s_settings, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 保存设置失败: " + e);
            }
        }

        private static void EnsureDataDir()
        {
            if (!Directory.Exists(DataDir))
            {
                Directory.CreateDirectory(DataDir);
            }
        }

        // 备注库内存缓存：第一次读文件，之后直接用缓存。
        // 列表重建时会对每个 mod 调一次 GetNote，不缓存的话会重复读文件 100+ 次。
        private static Dictionary<string, string> s_notesCache;
        private static bool s_notesLoaded;

        // 读备注库，文件不存在返回空字典
        public static Dictionary<string, string> LoadNotes()
        {
            try
            {
                EnsureDataDir();
                if (!s_notesLoaded)
                {
                    s_notesCache = new Dictionary<string, string>();
                    if (File.Exists(NotesPath))
                    {
                        string json = File.ReadAllText(NotesPath);
                        if (!string.IsNullOrEmpty(json))
                        {
                            Dictionary<string, string> loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                            if (loaded != null)
                            {
                                s_notesCache = loaded;
                            }
                        }
                    }
                    s_notesLoaded = true;
                }
                return s_notesCache;
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
                return new Dictionary<string, string>();
            }
        }

        // 保存备注，备注为空时删除这个 key
        public static void SaveNote(string key, string note)
        {
            try
            {
                Dictionary<string, string> notes = LoadNotes();
                if (string.IsNullOrEmpty(note))
                {
                    notes.Remove(key);
                }
                else
                {
                    notes[key] = note;
                }
                EnsureDataDir();
                File.WriteAllText(NotesPath, JsonConvert.SerializeObject(notes, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
            }
        }

        // 返回预设名列表（按文件名，不带 .json 后缀）
        public static List<string> ListPresets()
        {
            List<string> names = new List<string>();
            try
            {
                EnsureDataDir();
                if (Directory.Exists(PresetsDir))
                {
                    foreach (string file in Directory.GetFiles(PresetsDir, "*.json"))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                    names.Sort();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
            }
            return names;
        }

        // 读预设 JSON，不存在返回 null
        public static string LoadPresetJson(string name)
        {
            try
            {
                EnsureDataDir();
                string path = Path.Combine(PresetsDir, SanitizeName(name) + ".json");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
            }
            return null;
        }

        // 写预设 JSON
        public static void SavePresetJson(string name, string json)
        {
            try
            {
                string safe = SanitizeName(name);
                if (string.IsNullOrEmpty(safe))
                {
                    safe = "preset";
                }
                EnsureDataDir();
                if (!Directory.Exists(PresetsDir))
                {
                    Directory.CreateDirectory(PresetsDir);
                }
                File.WriteAllText(Path.Combine(PresetsDir, safe + ".json"), json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
            }
        }

        // 删除预设
        public static void DeletePreset(string name)
        {
            try
            {
                EnsureDataDir();
                string path = Path.Combine(PresetsDir, SanitizeName(name) + ".json");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
            }
        }

        // 清洗预设名的非法字符：\ / : * ? " < > | 和首尾空格
        private static string SanitizeName(string name)
        {
            if (name == null)
            {
                return "";
            }
            return Regex.Replace(name, @"[\\/:*?""<>|]", "_").Trim();
        }
    }
}

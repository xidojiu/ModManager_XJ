using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using KMod;
using Newtonsoft.Json;
using STRINGS;
using UnityEngine;

namespace ModManager_XJ
{
    // 操作结果：ok 是否成功，message 中文提示
    public struct ActionResult
    {
        public bool ok;
        public string message;
    }

    // 操作层：一键转本地、备注、开关、预设。只调游戏 API 和 ModManagerStore，不碰 UI
    public static class ModManagerActions
    {
        // 预设里的单条 mod 记录
        private class PresetEntry
        {
            public string platform;
            public string id;
            public string title;
            public bool enabled;
        }

        // 一键转本地：把 Steam mod 复制到 mods/Local/<新id>/，改 staticID 加 .Local，
        // 加入 manager.mods，禁用原 Steam mod，然后保存
        public static ActionResult LocalizeMod(KMod.Mod mod)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                Debug.Log("[MM] 转本地 步骤1: 开始, mod=" + (mod == null ? "null" : mod.title));
                if (mod == null)
                {
                    result.message = "mod 无效";
                    return result;
                }
                if (mod.label.distribution_platform != Label.DistributionPlatform.Steam)
                {
                    result.message = "只能转 Steam mod";
                    return result;
                }

                string sourceDir = mod.label.install_path;
                Debug.Log("[MM] 转本地 步骤2: 源目录=" + sourceDir + " 存在=" + System.IO.Directory.Exists(sourceDir));
                if (!System.IO.Directory.Exists(sourceDir))
                {
                    result.message = "找不到原 mod 文件夹";
                    return result;
                }

                // 生成新 id
                string newId = SanitizeName(mod.title);
                if (string.IsNullOrEmpty(newId))
                {
                    newId = mod.label.id + "_Local";
                }
                string localRoot = Path.Combine(Manager.GetDirectory(), "Local");
                string targetDir = Path.Combine(localRoot, newId);
                int x = 2;
                while (System.IO.Directory.Exists(targetDir))
                {
                    targetDir = Path.Combine(localRoot, newId + "_" + x);
                    x++;
                }
                newId = Path.GetFileName(targetDir);
                Debug.Log("[MM] 转本地 步骤3: 目标目录=" + targetDir);

                // 递归复制
                CopyDirectory(sourceDir, targetDir);
                Debug.Log("[MM] 转本地 步骤4: 复制完成");

                // 改副本 mod.yaml 的 staticID 加 .Local
                string newStaticId = string.IsNullOrEmpty(mod.staticID) ? (mod.label.id + ".Local") : (mod.staticID + ".Local");
                string yamlPath = Path.Combine(targetDir, "mod.yaml");
                if (File.Exists(yamlPath))
                {
                    string yamlText = File.ReadAllText(yamlPath);
                    if (Regex.IsMatch(yamlText, @"^\s*staticID\s*:", RegexOptions.Multiline))
                    {
                        yamlText = Regex.Replace(yamlText, @"^(\s*staticID\s*:\s*)[^\r\n]*$",
                            delegate(Match match) { return "staticID: " + newStaticId; }, RegexOptions.Multiline);
                    }
                    else
                    {
                        yamlText += Environment.NewLine + "staticID: " + newStaticId;
                    }
                    File.WriteAllText(yamlPath, yamlText, new UTF8Encoding(false));
                    Debug.Log("[MM] 转本地 步骤5: staticID 已改为 " + newStaticId);
                }
                else
                {
                    Debug.Log("[MM] 转本地 步骤5: 跳过改 staticID（mod.yaml 不存在或 staticID 为空）");
                }

                // 新建本地 mod 对象
                Debug.Log("[MM] 转本地 步骤6: 开始创建 Mod 对象");
                Label newLabel = new Label();
                newLabel.distribution_platform = Label.DistributionPlatform.Local;
                newLabel.id = newId;
                newLabel.title = mod.title;
                newLabel.version = 0;
                KMod.Mod newMod = new KMod.Mod(newLabel, newStaticId, mod.description, null, default(LocString), null);
                Debug.Log("[MM] 转本地 步骤7: Mod 对象创建完成, available=" + newMod.available_content
                    + " IsEmpty=" + newMod.IsEmpty());

                // 插进列表
                Debug.Log("[MM] 转本地 步骤8: 开始插入列表");
                int index = Global.Instance.modManager.mods.IndexOf(mod);
                Debug.Log("[MM] 转本地 步骤8.1: 原mod在列表位置=" + index);
                if (index < 0)
                {
                    result.message = "找不到原 mod 在列表里的位置";
                    return result;
                }
                Global.Instance.modManager.mods.Insert(index + 1, newMod);
                Debug.Log("[MM] 转本地 步骤9: 已插入列表, 列表总数=" + Global.Instance.modManager.mods.Count);

                // 诊断
                bool found = Global.Instance.modManager.FindMod(newMod.label) != null;
                string dlcList = (newMod.enabledForDlc == null) ? "null" : string.Join(",", newMod.enabledForDlc.ToArray());
                Debug.Log("[MM] 转本地 诊断: FindMod非空=" + found
                    + " 列表含新mod=" + Global.Instance.modManager.mods.Contains(newMod)
                    + " available=" + newMod.available_content
                    + " IsEmpty=" + newMod.IsEmpty()
                    + " 当前启用=" + newMod.IsEnabledForActiveDlc()
                    + " enabledForDlc=" + dlcList
                    + " install_path=" + newMod.label.install_path);

                if (newMod.available_content == (Content)0)
                {
                    result.message = "转本地完成，但内容扫描为空，可能无法加载（请发日志）";
                    return result;
                }

                // 设置启用状态：用 EnableMod 而不是 SetEnabledForActiveDlc，
                // 因为 EnableMod 会调 mod.Load()/Unload() 真正加载内容并触发 on_update，
                // 只改标记不 Load，游戏后续访问新 mod 内容会空引用崩溃
                Debug.Log("[MM] 转本地 步骤10: 设置启用状态");
                Global.Instance.modManager.EnableMod(mod.label, false, null);
                Debug.Log("[MM] 转本地 步骤10.1: 原mod已禁用并卸载");
                Global.Instance.modManager.EnableMod(newMod.label, true, null);
                Debug.Log("[MM] 转本地 步骤10.2: 新mod已启用并加载");
                Global.Instance.modManager.Save();
                Debug.Log("[MM] 转本地 步骤11: 已保存");

                result.ok = true;
                result.message = "已转为本地: " + mod.title + "（staticID 加 .Local）";
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 转本地 崩溃: " + e);
                result.message = "转本地失败";
            }
            return result;
        }

        // 删除本地 mod（转本地产生的副本，或自己放进 Local 目录的 mod）：
        // 1. 从 manager.mods 移除；2. 删掉它的文件夹；3. 清掉备注；
        // 4. 如果是转本地产生的（staticID 带 .Local），把被禁用的 Steam 原版重新启用
        public static ActionResult DeleteLocalMod(KMod.Mod mod)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                Debug.Log("[MM] 删除本地 步骤1: 开始, mod=" + (mod == null ? "null" : mod.title));
                if (mod == null)
                {
                    result.message = "mod 无效";
                    return result;
                }
                if (mod.label.distribution_platform != Label.DistributionPlatform.Local)
                {
                    result.message = "只能删除本地 mod";
                    return result;
                }
                Debug.Log("[MM] 删除本地 步骤2: staticID=" + mod.staticID + " id=" + mod.label.id);

                // 先找对应的 Steam 原版
                KMod.Mod original = null;
                if (!string.IsNullOrEmpty(mod.staticID) && mod.staticID.EndsWith(".Local", StringComparison.Ordinal))
                {
                    string originalStaticId = mod.staticID.Substring(0, mod.staticID.Length - ".Local".Length);
                    Debug.Log("[MM] 删除本地 步骤3: 找 Steam 原版, originalStaticId=" + originalStaticId);
                    foreach (KMod.Mod m in Global.Instance.modManager.mods)
                    {
                        if (m != null && m != mod
                            && m.label.distribution_platform == Label.DistributionPlatform.Steam
                            && m.staticID == originalStaticId)
                        {
                            original = m;
                            Debug.Log("[MM] 删除本地 步骤3.1: 找到原版: " + m.title);
                            break;
                        }
                    }
                    if (original == null)
                    {
                        Debug.Log("[MM] 删除本地 步骤3.2: 没找到对应的 Steam 原版");
                    }
                }
                else
                {
                    Debug.Log("[MM] 删除本地 步骤3: staticID 不带 .Local，不找原版");
                }

                // 从列表移除
                Debug.Log("[MM] 删除本地 步骤4: 从列表移除");
                if (Global.Instance.modManager.mods.Remove(mod))
                {
                    Debug.Log("[MM] 删除本地 步骤4.1: 移除成功");
                }
                else
                {
                    Debug.Log("[MM] 删除本地 步骤4.2: mod 不在列表里");
                }

                // 删文件夹
                Debug.Log("[MM] 删除本地 步骤5: 删文件夹");
                string installPath = mod.label.install_path;
                string localRoot = Path.GetFullPath(Path.Combine(Manager.GetDirectory(), "Local"));
                Debug.Log("[MM] 删除本地 步骤5.1: installPath=" + installPath + " localRoot=" + localRoot);
                if (!string.IsNullOrEmpty(installPath))
                {
                    string fullPath = Path.GetFullPath(installPath);
                    if (fullPath.StartsWith(localRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && System.IO.Directory.Exists(fullPath))
                    {
                        System.IO.Directory.Delete(fullPath, true);
                        Debug.Log("[MM] 删除本地 步骤5.2: 已删除文件夹");
                    }
                    else
                    {
                        Debug.Log("[MM] 删除本地 步骤5.3: 跳过删文件夹, 路径不在 Local 目录或不存在");
                    }
                }

                // 清备注
                Debug.Log("[MM] 删除本地 步骤6: 清备注");
                string noteKey = mod.label.distribution_platform.ToString() + "|" + mod.label.id;
                Dictionary<string, string> notes = ModManagerStore.LoadNotes();
                if (notes != null && notes.ContainsKey(noteKey))
                {
                    ModManagerStore.SaveNote(noteKey, "");
                    Debug.Log("[MM] 删除本地 步骤6.1: 已清除备注");
                }
                else
                {
                    Debug.Log("[MM] 删除本地 步骤6.2: 没有备注需要清除");
                }

                // 恢复 Steam 原版启用
                if (original != null)
                {
                    Debug.Log("[MM] 删除本地 步骤7: 恢复原版启用");
                    Global.Instance.modManager.EnableMod(original.label, true, null);
                    Debug.Log("[MM] 删除本地 步骤7.1: 已恢复并加载, 启用=" + original.IsEnabledForActiveDlc());
                }
                else
                {
                    Debug.Log("[MM] 删除本地 步骤7: 没有原版需要恢复");
                }

                Global.Instance.modManager.Save();
                Debug.Log("[MM] 删除本地 步骤8: 已保存");
                result.ok = true;
                result.message = (original != null)
                    ? "已删除本地版，原版已恢复: " + original.title
                    : "已删除: " + mod.title;
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 删除本地 崩溃: " + e);
                result.message = "删除本地 mod 失败";
            }
            return result;
        }

        // 写备注：writeToTitle 且是本地 mod 时写 mod.yaml 的 title 并同步存 notes.json，
        // 否则只存 notes.json。备注统一以 notes.json 为准，标题只是展示载体。
        public static ActionResult SetNote(KMod.Mod mod, string note, bool writeToTitle)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                Debug.Log("[MM] 备注 步骤1: mod=" + (mod == null ? "null" : mod.title) + " note=" + (note ?? "") + " writeToTitle=" + writeToTitle);
                if (mod == null)
                {
                    result.message = "mod 无效";
                    return result;
                }
                string key = mod.label.distribution_platform.ToString() + "|" + mod.label.id;
                Debug.Log("[MM] 备注 步骤2: key=" + key + " IsLocal=" + mod.IsLocal);

                if (writeToTitle && mod.IsLocal)
                {
                    Debug.Log("[MM] 备注 步骤3: 写回标题模式");
                    string yamlPath = Path.Combine(mod.label.install_path, "mod.yaml");
                    Debug.Log("[MM] 备注 步骤3.1: yamlPath=" + yamlPath + " 存在=" + File.Exists(yamlPath));
                    if (!File.Exists(yamlPath))
                    {
                        result.message = "没找到 mod.yaml";
                        return result;
                    }
                    string yamlText = File.ReadAllText(yamlPath);
                    Match m = Regex.Match(yamlText, @"^title\s*:\s*(.*)$", RegexOptions.Multiline);
                    if (!m.Success)
                    {
                        result.message = "mod.yaml 里没有 title";
                        return result;
                    }
                    string oldTitle = m.Groups[1].Value.Trim();
                    string oldTitleValue = oldTitle.Trim('"');
                    bool quoted = oldTitle.StartsWith("\"");
                    Debug.Log("[MM] 备注 步骤3.2: oldTitle=" + oldTitleValue + " quoted=" + quoted);

                    string baseTitle = oldTitleValue;
                    string oldNote = "";
                    Dictionary<string, string> notes = ModManagerStore.LoadNotes();
                    if (notes != null && notes.ContainsKey(key))
                    {
                        oldNote = notes[key];
                    }
                    if (!string.IsNullOrEmpty(oldNote))
                    {
                        string oldTag = "[" + oldNote + "]";
                        int tagIdx = baseTitle.IndexOf(oldTag, StringComparison.Ordinal);
                        if (tagIdx >= 0)
                        {
                            baseTitle = baseTitle.Remove(tagIdx, oldTag.Length).Trim();
                            Debug.Log("[MM] 备注 步骤3.3: 剥掉旧备注, baseTitle=" + baseTitle);
                        }
                    }

                    if (string.IsNullOrEmpty(note))
                    {
                        Debug.Log("[MM] 备注 步骤4: 清空备注");
                        WriteYamlTitle(yamlPath, yamlText, baseTitle, quoted);
                        ModManagerStore.SaveNote(key, "");
                        mod.title = baseTitle;
                        result.ok = true;
                        result.message = "备注已清空";
                    }
                    else
                    {
                        string newTitleValue = baseTitle + "[" + note + "]";
                        Debug.Log("[MM] 备注 步骤4: 写入备注, newTitle=" + newTitleValue);
                        WriteYamlTitle(yamlPath, yamlText, newTitleValue, quoted);
                        ModManagerStore.SaveNote(key, note);
                        mod.title = newTitleValue;
                        result.ok = true;
                        result.message = "备注已保存（已写进标题）";
                    }
                }
                else
                {
                    Debug.Log("[MM] 备注 步骤3: 普通备注模式");
                    ModManagerStore.SaveNote(key, note);
                    Debug.Log("[MM] 备注 步骤4: 已保存到 notes.json");
                    result.ok = true;
                    result.message = "备注已保存";
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 备注 崩溃: " + e);
                result.message = "保存备注失败";
            }
            return result;
        }

        // 只改 mod.yaml 里的 title 行，其余原样写回（保留注释等）
        private static void WriteYamlTitle(string yamlPath, string yamlText, string newTitleValue, bool quoted)
        {
            string replacement = quoted ? ("title: \"" + newTitleValue + "\"") : ("title: " + newTitleValue);
            string updated = Regex.Replace(yamlText, @"^title\s*:\s*.*$",
                delegate(Match match) { return replacement; }, RegexOptions.Multiline);
            File.WriteAllText(yamlPath, updated, new UTF8Encoding(false));
        }

        // 启用 / 禁用某个 mod
        public static ActionResult ToggleMod(KMod.Mod mod, bool enable)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                Debug.Log("[MM] 开关 步骤1: mod=" + (mod == null ? "null" : mod.title) + " 目标=" + (enable ? "启用" : "禁用"));
                if (mod == null)
                {
                    result.message = "mod 无效";
                    return result;
                }
                Global.Instance.modManager.EnableMod(mod.label, enable, null);
                Debug.Log("[MM] 开关 步骤2: EnableMod 调用完成");
                Global.Instance.modManager.Save();
                Debug.Log("[MM] 开关 步骤3: 已保存");
                result.ok = true;
                result.message = enable ? ("已启用: " + mod.title) : ("已禁用: " + mod.title);
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 开关 崩溃: " + e);
                result.message = "切换失败";
            }
            return result;
        }

        // 保存预设：记下当前所有 mod 的启用状态 + 顺序
        public static ActionResult SavePreset(string name)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                Debug.Log("[MM] 保存预设 步骤1: name=" + (name ?? ""));
                if (string.IsNullOrEmpty(name))
                {
                    result.message = "预设名不能为空";
                    return result;
                }
                List<PresetEntry> entries = new List<PresetEntry>();
                foreach (KMod.Mod mod in Global.Instance.modManager.mods)
                {
                    PresetEntry entry = new PresetEntry();
                    entry.platform = mod.label.distribution_platform.ToString();
                    entry.id = mod.label.id;
                    entry.title = mod.title;
                    entry.enabled = mod.IsEnabledForActiveDlc();
                    entries.Add(entry);
                }
                Debug.Log("[MM] 保存预设 步骤2: 已记录 " + entries.Count + " 个 mod");
                string json = JsonConvert.SerializeObject(entries, Formatting.Indented);
                ModManagerStore.SavePresetJson(name, json);
                Debug.Log("[MM] 保存预设 步骤3: 已保存到文件");
                result.ok = true;
                result.message = "预设已保存: " + name + "（" + entries.Count + " 个 mod）";
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 保存预设 崩溃: " + e);
                result.message = "保存预设失败";
            }
            return result;
        }

        // 应用预设：恢复每个 mod 的启用状态，再按预设顺序重排
        public static ActionResult ApplyPreset(string name)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                Debug.Log("[MM] 应用预设 步骤1: name=" + (name ?? ""));
                string json = ModManagerStore.LoadPresetJson(name);
                if (json == null)
                {
                    result.message = "没找到预设: " + name;
                    return result;
                }
                List<PresetEntry> entries = JsonConvert.DeserializeObject<List<PresetEntry>>(json);
                if (entries == null)
                {
                    result.message = "预设数据坏了";
                    return result;
                }
                Debug.Log("[MM] 应用预设 步骤2: 预设条目数=" + entries.Count);

                // 按 platform+id 匹配列表里的 mod，恢复启用状态
                List<KMod.Mod> matched = new List<KMod.Mod>();
                foreach (PresetEntry entry in entries)
                {
                    foreach (KMod.Mod mod in Global.Instance.modManager.mods)
                    {
                        if (mod.label.id == entry.id && mod.label.distribution_platform.ToString() == entry.platform)
                        {
                            Global.Instance.modManager.EnableMod(mod.label, entry.enabled, null);
                            matched.Add(mod);
                            break;
                        }
                    }
                }
                Debug.Log("[MM] 应用预设 步骤3: 已匹配 " + matched.Count + " 个 mod");

                // 按预设顺序重排
                List<KMod.Mod> mods = Global.Instance.modManager.mods;
                int pos = 0;
                for (int i = 0; i < matched.Count; i++)
                {
                    int src = mods.IndexOf(matched[i]);
                    if (src < 0)
                    {
                        continue;
                    }
                    if (src == pos)
                    {
                        pos++;
                        continue;
                    }
                    mods.RemoveAt(src);
                    mods.Insert(pos, matched[i]);
                    pos++;
                }
                Debug.Log("[MM] 应用预设 步骤4: 重排完成");

                Global.Instance.modManager.Save();
                Debug.Log("[MM] 应用预设 步骤5: 已保存");
                result.ok = true;
                result.message = "预设已应用: " + name;
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 应用预设 崩溃: " + e);
                result.message = "应用预设失败";
            }
            return result;
        }

        // 删除预设
        public static ActionResult DeletePreset(string name)
        {
            ActionResult result = new ActionResult();
            result.ok = false;
            result.message = "";
            try
            {
                ModManagerStore.DeletePreset(name);
                result.ok = true;
                result.message = "预设已删除: " + name;
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 错误: " + e);
                result.message = "删除预设失败";
            }
            return result;
        }

        // 列出所有预设名
        public static List<string> ListPresets()
        {
            return ModManagerStore.ListPresets();
        }

        // 递归复制目录：所有层级都跳过 obj/、bin/ 目录和 .pdb、.cs 文件
        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            System.IO.Directory.CreateDirectory(targetDir);
            foreach (string file in System.IO.Directory.GetFiles(sourceDir))
            {
                if (ShouldSkipFile(file))
                {
                    continue;
                }
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }
            foreach (string dir in System.IO.Directory.GetDirectories(sourceDir))
            {
                if (ShouldSkipDirectory(dir))
                {
                    continue;
                }
                // 递归时走同一套过滤，保证子目录里的 .pdb/.cs 也会被跳过
                CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
            }
        }

        // 是否跳过该文件：.pdb、.cs（忽略大小写）
        private static bool ShouldSkipFile(string file)
        {
            string ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }
            ext = ext.ToLowerInvariant();
            return ext == ".pdb" || ext == ".cs";
        }

        // 是否跳过该目录：obj、bin（忽略大小写）
        private static bool ShouldSkipDirectory(string dir)
        {
            string name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            name = name.ToLowerInvariant();
            return name == "obj" || name == "bin";
        }

        // 清洗标题里的 Windows 非法字符：\ / : * ? " < > | 和首尾空格
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

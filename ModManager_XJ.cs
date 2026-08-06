using System.Reflection;
using HarmonyLib;
using KMod;
using STRINGS;
using UnityEngine;

namespace ModManager_XJ
{
    public class ModManager_XJMod : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            Debug.Log("[MM] ModManager_XJ loading...");
            RegisterStrings();
            // 注意：不能用 PatchAll 依赖类级 [HarmonyPatch] 属性的自动发现（踩过坑），
            // 这里显式注册 Postfix，目标方法不存在时只打日志不崩溃
            MethodInfo target = AccessTools.Method(typeof(ModsScreen), "OnActivate");
            if (target == null)
            {
                Debug.LogError("[MM] 找不到 ModsScreen.OnActivate，无法注入高级管理按钮");
            }
            else
            {
                MethodInfo postfix = AccessTools.Method(typeof(ModManagerEntry), "Postfix");
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                Debug.Log("[MM] 已注册 ModsScreen.OnActivate Postfix");
            }
            Debug.Log("[MM] ModManager_XJ loaded.");
        }

        private static void RegisterStrings()
        {
            try
            {
                Strings.Add("STRINGS.MODMANAGER.TITLE", new LocString("Mod Manager"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_DISPLAY_ON", new LocString("Merge: On"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_DISPLAY_OFF", new LocString("Merge: Off"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_DISPLAY_TOOLTIP", new LocString("Hide Steam mods that have a local copy, show only the preferred version"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_SORT_ON", new LocString("Merge Sort: On"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_SORT_OFF", new LocString("Merge Sort: Off"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_SORT_TOOLTIP", new LocString("When on, paired mods move together; when off, only the clicked mod moves"));
                Strings.Add("STRINGS.MODMANAGER.CLOSE", new LocString("Close"));
                Strings.Add("STRINGS.MODMANAGER.PRESET_LABEL", new LocString("Preset:"));
                Strings.Add("STRINGS.MODMANAGER.PRESET_PLACEHOLDER", new LocString("Preset name"));
                Strings.Add("STRINGS.MODMANAGER.SAVE_PRESET", new LocString("Save Preset"));
                Strings.Add("STRINGS.MODMANAGER.SELECT_PRESET", new LocString("Select Preset"));
                Strings.Add("STRINGS.MODMANAGER.APPLY_PRESET", new LocString("Apply Preset"));
                Strings.Add("STRINGS.MODMANAGER.DELETE_PRESET", new LocString("Delete Preset"));
                Strings.Add("STRINGS.MODMANAGER.PRESET_HINT", new LocString("Use [Up][Down] in the enabled list to reorder"));
                Strings.Add("STRINGS.MODMANAGER.NO_PRESETS", new LocString("No presets yet"));
                Strings.Add("STRINGS.MODMANAGER.SEARCH_LABEL", new LocString("Search:"));
                Strings.Add("STRINGS.MODMANAGER.SEARCH_PLACEHOLDER", new LocString("Filter by title or note"));
                Strings.Add("STRINGS.MODMANAGER.SEARCH_CLEAR", new LocString("Clear"));
                Strings.Add("STRINGS.MODMANAGER.SEARCH_HINT", new LocString("Filters the list in real-time"));
                Strings.Add("STRINGS.MODMANAGER.ENABLED_HEADER", new LocString("Enabled ({0})"));
                Strings.Add("STRINGS.MODMANAGER.DISABLED_HEADER", new LocString("Disabled ({0})"));
                Strings.Add("STRINGS.MODMANAGER.MOVE_UP", new LocString("Up"));
                Strings.Add("STRINGS.MODMANAGER.MOVE_DOWN", new LocString("Down"));
                Strings.Add("STRINGS.MODMANAGER.LOCALIZE", new LocString("Localize"));
                Strings.Add("STRINGS.MODMANAGER.DELETE", new LocString("Delete"));
                Strings.Add("STRINGS.MODMANAGER.CONFIG", new LocString("Config"));
                Strings.Add("STRINGS.MODMANAGER.NOTE", new LocString("Note"));
                Strings.Add("STRINGS.MODMANAGER.USE_STEAM", new LocString("Use Steam"));
                Strings.Add("STRINGS.MODMANAGER.USE_LOCAL", new LocString("Use Local"));
                Strings.Add("STRINGS.MODMANAGER.SORT_DIALOG_TITLE", new LocString("Sort"));
                Strings.Add("STRINGS.MODMANAGER.SORT_DIALOG_TEXT", new LocString("Pair local mods with their Steam versions?"));
                Strings.Add("STRINGS.MODMANAGER.SORT_DIALOG_YES", new LocString("Sort"));
                Strings.Add("STRINGS.MODMANAGER.SORT_DIALOG_NO", new LocString("Don't Sort"));
                Strings.Add("STRINGS.MODMANAGER.DELETE_TITLE", new LocString("Delete Local Mod"));
                Strings.Add("STRINGS.MODMANAGER.DELETE_CONFIRM", new LocString("Delete [{0}]?\nIts folder will be deleted.\nIf it was localized, the original Steam version will be restored."));
                Strings.Add("STRINGS.MODMANAGER.DELETE_OK", new LocString("Delete"));
                Strings.Add("STRINGS.MODMANAGER.CANCEL", new LocString("Cancel"));
                Strings.Add("STRINGS.MODMANAGER.APPLY_PRESET_TITLE", new LocString("Apply Preset"));
                Strings.Add("STRINGS.MODMANAGER.APPLY_PRESET_CONFIRM", new LocString("Apply preset [{0}]?\nAll current mod states and order will be overwritten."));
                Strings.Add("STRINGS.MODMANAGER.APPLY_PRESET_OK", new LocString("Apply"));
                Strings.Add("STRINGS.MODMANAGER.NOTE_POPUP_TITLE", new LocString("Edit Note"));
                Strings.Add("STRINGS.MODMANAGER.NOTE_PLACEHOLDER", new LocString("Note content"));
                Strings.Add("STRINGS.MODMANAGER.NOTE_WRITE_BACK", new LocString("Write back to title (local mods only)"));
                Strings.Add("STRINGS.MODMANAGER.NOTE_HINT", new LocString("Notes are saved locally, works for workshop mods too"));
                Strings.Add("STRINGS.MODMANAGER.NOTE_SAVE", new LocString("Save"));
                Strings.Add("STRINGS.MODMANAGER.ADVANCED_MANAGE", new LocString("Advanced Manage"));
                Strings.Add("STRINGS.MODMANAGER.ADVANCED_MANAGE_TOOLTIP", new LocString("Open Mod Manager: localize, notes, sort, presets"));
                Strings.Add("STRINGS.MODMANAGER.TOAST_DONE", new LocString("Done"));
                Strings.Add("STRINGS.MODMANAGER.TOAST_NO_TEMPLATE", new LocString("Failed to get entry template, list unavailable"));
                Strings.Add("STRINGS.MODMANAGER.TOAST_MERGE_ADJUSTED", new LocString("Adjusted {0} mod(s) based on preferred platform"));
                Strings.Add("STRINGS.MODMANAGER.TOAST_SELECT_PRESET_FIRST", new LocString("Select a preset first"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_ENABLED", new LocString("Enabled"));
                Strings.Add("STRINGS.MODMANAGER.MERGE_DISABLED", new LocString("Disabled"));
                Strings.Add("STRINGS.MODMANAGER.LOCAL_TAG", new LocString("[Local]"));
                Debug.Log("[MM] 已注册 " + 52 + " 个本地化字符串");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[MM] 注册本地化字符串失败: " + e);
            }
        }
    }
}

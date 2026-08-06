using System;
using KMod;
using UnityEngine;

namespace ModManager_XJ
{
    // 本地化字符串：根据游戏语言自动切换中/英
    // 检测方式：Localization.GetLocale().Lang == Language.Chinese
    public static class ModStrings
    {
        private static bool? s_isChinese;

        private static bool IsChinese
        {
            get
            {
                if (!s_isChinese.HasValue)
                {
                    try
                    {
                        Localization.Locale locale = Localization.GetLocale();
                        s_isChinese = (locale != null && locale.Lang == Localization.Language.Chinese);
                    }
                    catch
                    {
                        s_isChinese = true; // 默认中文
                    }
                }
                return s_isChinese.Value;
            }
        }

        // ==================== 界面标题 ====================
        public static string WindowTitle { get { return IsChinese ? "Mod 管理器" : "Mod Manager"; } }

        // ==================== 合并显示 ====================
        public static string MergeDisplayOn { get { return IsChinese ? "合并显示: 开" : "Merge: On"; } }
        public static string MergeDisplayOff { get { return IsChinese ? "合并显示: 关" : "Merge: Off"; } }
        public static string MergeDisplayTooltip { get { return IsChinese ? "开启后，有本地版的 Steam mod 会被隐藏，只显示优先版本" : "Hide Steam mods that have a local copy, show only the preferred version"; } }

        // ==================== 合并排序 ====================
        public static string MergeSortOn { get { return IsChinese ? "合并排序: 开" : "Merge Sort: On"; } }
        public static string MergeSortOff { get { return IsChinese ? "合并排序: 关" : "Merge Sort: Off"; } }
        public static string MergeSortTooltip { get { return IsChinese ? "开启后，排序时本地版和 Steam 版整组移动；关闭则只移动单个 mod" : "When on, paired mods move together; when off, only the clicked mod moves"; } }

        // ==================== 关闭按钮 ====================
        public static string Close { get { return IsChinese ? "关闭" : "Close"; } }

        // ==================== 预设 ====================
        public static string PresetLabel { get { return IsChinese ? "预设:" : "Preset:"; } }
        public static string PresetPlaceholder { get { return IsChinese ? "预设名" : "Preset name"; } }
        public static string SavePreset { get { return IsChinese ? "保存预设" : "Save Preset"; } }
        public static string SelectPreset { get { return IsChinese ? "选择预设" : "Select Preset"; } }
        public static string ApplyPreset { get { return IsChinese ? "应用预设" : "Apply Preset"; } }
        public static string DeletePreset { get { return IsChinese ? "删除预设" : "Delete Preset"; } }
        public static string PresetHint { get { return IsChinese ? "已启用栏可用 [上移][下移] 调整加载顺序" : "Use [Up][Down] in the enabled list to reorder"; } }
        public static string NoPresets { get { return IsChinese ? "还没有预设" : "No presets yet"; } }

        // ==================== 搜索 ====================
        public static string SearchLabel { get { return IsChinese ? "搜索:" : "Search:"; } }
        public static string SearchPlaceholder { get { return IsChinese ? "按标题或备注过滤" : "Filter by title or note"; } }
        public static string SearchClear { get { return IsChinese ? "清除" : "Clear"; } }
        public static string SearchHint { get { return IsChinese ? "输入后列表实时过滤" : "Filters the list in real-time"; } }

        // ==================== 栏标题 ====================
        public static string EnabledHeader(int count) { return IsChinese ? "已启用 (" + count + ")" : "Enabled (" + count + ")"; }
        public static string DisabledHeader(int count) { return IsChinese ? "未启用 (" + count + ")" : "Disabled (" + count + ")"; }

        // ==================== 行按钮 ====================
        public static string MoveUp { get { return IsChinese ? "上移" : "Up"; } }
        public static string MoveDown { get { return IsChinese ? "下移" : "Down"; } }
        public static string Localize { get { return IsChinese ? "转本地" : "Localize"; } }
        public static string Delete { get { return IsChinese ? "删除" : "Delete"; } }
        public static string Config { get { return IsChinese ? "配置" : "Config"; } }
        public static string Note { get { return IsChinese ? "备注" : "Note"; } }
        public static string UseSteam { get { return IsChinese ? "用Steam" : "Use Steam"; } }
        public static string UseLocal { get { return IsChinese ? "用本地" : "Use Local"; } }

        // ==================== 排序弹窗 ====================
        public static string SortDialogTitle { get { return IsChinese ? "排序" : "Sort"; } }
        public static string SortDialogText { get { return IsChinese ? "是否要把本地 mod 和对应的 Steam 版排在一起？" : "Pair local mods with their Steam versions?"; } }
        public static string SortDialogYes { get { return IsChinese ? "排序" : "Sort"; } }
        public static string SortDialogNo { get { return IsChinese ? "不排序" : "Don't Sort"; } }

        // ==================== 删除确认弹窗 ====================
        public static string DeleteTitle { get { return IsChinese ? "删除本地 mod" : "Delete Local Mod"; } }
        public static string DeleteConfirm(string modName)
        {
            return IsChinese
                ? "确定要删除 [" + modName + "] 吗？\n会删掉它的文件夹。\n如果是转本地产生的，会顺便恢复原来的 Steam 版。"
                : "Delete [" + modName + "]?\nIts folder will be deleted.\nIf it was localized, the original Steam version will be restored.";
        }
        public static string DeleteOk { get { return IsChinese ? "删除" : "Delete"; } }
        public static string Cancel { get { return IsChinese ? "取消" : "Cancel"; } }

        // ==================== 应用预设确认弹窗 ====================
        public static string ApplyPresetTitle { get { return IsChinese ? "应用预设" : "Apply Preset"; } }
        public static string ApplyPresetConfirm(string name)
        {
            return IsChinese
                ? "确定要应用预设 [" + name + "] 吗？\n当前所有 mod 的开关状态和顺序会被覆盖。"
                : "Apply preset [" + name + "]?\nAll current mod states and order will be overwritten.";
        }
        public static string ApplyPresetOk { get { return IsChinese ? "确定" : "Apply"; } }

        // ==================== 备注弹窗 ====================
        public static string NotePopupTitle { get { return IsChinese ? "写备注" : "Edit Note"; } }
        public static string NotePlaceholder { get { return IsChinese ? "备注内容" : "Note content"; } }
        public static string NoteWriteBack { get { return IsChinese ? "写回标题（仅本地 mod）" : "Write back to title (local mods only)"; } }
        public static string NoteHint { get { return IsChinese ? "备注保存在本地，创意工坊 mod 也能用，保存后立即生效" : "Notes are saved locally, works for workshop mods too"; } }
        public static string NoteSave { get { return IsChinese ? "保存" : "Save"; } }

        // ==================== 高级管理按钮 ====================
        public static string AdvancedManage { get { return IsChinese ? "高级管理" : "Advanced Manage"; } }
        public static string AdvancedManageTooltip { get { return IsChinese ? "打开 Mod 管理器：转本地、备注、排序、预设" : "Open Mod Manager: localize, notes, sort, presets"; } }

        // ==================== 提示消息 ====================
        public static string ToastDone { get { return IsChinese ? "完成" : "Done"; } }
        public static string ToastNoTemplate { get { return IsChinese ? "拿不到原版条目模板，列表无法显示" : "Failed to get entry template, list unavailable"; } }
        public static string ToastMergeAdjusted(int count)
        {
            return IsChinese
                ? "已根据优先平台调整 " + count + " 个 mod 的启用状态"
                : "Adjusted " + count + " mod(s) based on preferred platform";
        }
        public static string ToastSelectPresetFirst { get { return IsChinese ? "请先点击选择预设" : "Select a preset first"; } }

        // ==================== 合并启用/禁用日志 ====================
        public static string MergeEnabled { get { return IsChinese ? "启用" : "Enabled"; } }
        public static string MergeDisabled { get { return IsChinese ? "停用" : "Disabled"; } }

        // ==================== 本地标记 ====================
        public static string LocalTag { get { return IsChinese ? "[本地]" : "[Local]"; } }
    }
}
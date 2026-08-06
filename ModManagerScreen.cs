using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KMod;
using STRINGS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PeterHan.PLib.Options;

namespace ModManager_XJ
{
    // UI 层：游戏内 mod 管理器界面（两栏列表 + 预设 + 备注 + 排序）
    //
    // 重做思路（上一版全部手算 RectTransform 坐标，导致条目不显示/错位）：
    // 1. mod 条目不再手画——直接克隆原版 ModsScreen 的条目 prefab（反射拿 entryPrefab），
    //    标题/版本/启用开关/按钮全是原版组件，外观和原版模组列表一致；
    // 2. 界面里所有按钮（关闭/备注/转本地/上移/下移/预设按钮）都克隆条目里的 ManageButton；
    // 3. 框架排版全部交给 VerticalLayoutGroup / HorizontalLayoutGroup / ContentSizeFitter，
    //    不手算任何坐标；
    // 4. 备注弹窗的勾选框继续用 Unity 标准 Toggle（Klei 的 MultiToggle 纯代码创建会崩）。
    public class ModManagerScreen : KModalScreen
    {
        // ==================== 原版模板（静态缓存） ====================

        private static GameObject s_entryPrefab;      // 原版 mod 条目 prefab
        private static GameObject s_buttonTemplate;   // 原版"管理"按钮模板（隐藏）
        private static GameObject s_templateRoot;     // 隐藏模板容器
        private static float s_entryHeight = 36f;     // 原版条目高度

        // ==================== Steam/本地合并（本地优先） ====================

        // 搬迁/转本地时 staticID 被改过名、靠后缀匹配不上的 Steam 版 → 本地版 id 手工配对
        private static readonly Dictionary<string, string> SteamLocalPair = new Dictionary<string, string>
        {
            { "3732422991", "存储网络[搬迁20260714]" },   // StorageNetwork
            { "3235904679", "拼音搜索[搬迁20260714]" },   // PinYinSearchDev
            { "3130257075", "揭示地图[搬迁20260714]" },   // Reveal Map
            { "3547651475", "无限擦拭[搬迁20260714]" },   // 无限擦拭
            { "2713769536", "Temperature Controller" },   // Temperature Controller
        };

        private HashSet<Mod> s_mergedLocals;              // 有 Steam 配对、界面里已合并的本地 mod（标题加 [本地]）

        // ==================== 静态入口 ====================

        // 打开 mod 管理器（重复调用会先关掉已打开的）
        public static void Open()
        {
            try
            {
                ModManagerScreen existing = UnityEngine.Object.FindObjectOfType<ModManagerScreen>();
                if (existing != null)
                {
                    existing.Deactivate();
                    return;
                }
                // 先拿到原版条目 prefab 和按钮模板，再建界面
                PrepareTemplates();
                GameObject go = new GameObject("ModManagerScreen");
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                go.AddComponent<ModManagerScreen>();
                KScreenManager.Instance.ActivateScreen(go, Global.Instance.globalCanvas);
                Debug.Log("[MM] ModManagerScreen 已打开");
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 打开界面失败: " + e);
            }
        }

        // 从正在打开的 ModsScreen 身上反射拿 entryPrefab，
        // 并克隆一份隐藏条目，把里面的 ManageButton 拆出来当全局按钮模板
        private static void PrepareTemplates()
        {
            try
            {
                if (s_templateRoot != null)
                {
                    UnityEngine.Object.Destroy(s_templateRoot);
                    s_templateRoot = null;
                }
                s_entryPrefab = null;
                s_buttonTemplate = null;
                s_entryHeight = 36f;

                ModsScreen modsScreen = UnityEngine.Object.FindObjectOfType(typeof(ModsScreen)) as ModsScreen;
                if (modsScreen == null)
                {
                    Debug.LogError("[MM] 找不到 ModsScreen 实例，拿不到原版条目 prefab");
                    return;
                }
                FieldInfo field = typeof(ModsScreen).GetField("entryPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    Debug.LogError("[MM] 反射不到 ModsScreen.entryPrefab 字段");
                    return;
                }
                s_entryPrefab = field.GetValue(modsScreen) as GameObject;
                if (s_entryPrefab == null)
                {
                    Debug.LogError("[MM] ModsScreen.entryPrefab 是空的");
                    return;
                }

                // 量一下原版条目高度，行高跟它走
                RectTransform prt = s_entryPrefab.transform as RectTransform;
                if (prt != null)
                {
                    float h = prt.sizeDelta.y;
                    if (h < 20f)
                    {
                        h = prt.rect.height;
                    }
                    if (h >= 20f)
                    {
                        s_entryHeight = h;
                    }
                }

                // 克隆一份隐藏模板条目，拆出 ManageButton 当按钮模板
                s_templateRoot = new GameObject("MM_Templates");
                GameObject tmpEntry = UnityEngine.Object.Instantiate(s_entryPrefab, s_templateRoot.transform);
                HierarchyReferences hr = tmpEntry.GetComponent<HierarchyReferences>();
                if (hr != null)
                {
                    KButton manage = hr.GetReference<KButton>("ManageButton");
                    if (manage != null)
                    {
                        manage.transform.SetParent(s_templateRoot.transform, false);
                        manage.ClearOnClick();
                        manage.gameObject.SetActive(false);
                        s_buttonTemplate = manage.gameObject;
                    }
                }
                UnityEngine.Object.Destroy(tmpEntry);
                s_templateRoot.SetActive(false);
                Debug.Log("[MM] 已拿到原版条目 prefab，条目高度=" + s_entryHeight + "，按钮模板=" + (s_buttonTemplate != null));
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 准备原版模板失败: " + e);
            }
        }

        // ==================== 界面引用 ====================

        private bool uiBuilt;

        private GameObject mainPanel;         // 主面板

        private GameObject leftContent;       // 已启用列表内容容器
        private GameObject rightContent;      // 未启用列表内容容器
        private TextMeshProUGUI leftHeader;   // 已启用栏标题
        private TextMeshProUGUI rightHeader;  // 未启用栏标题

        private TMP_InputField presetInput;   // 预设名输入框
        private GameObject presetDropBtn;     // 预设下拉按钮
        private GameObject presetDropdown;    // 预设下拉列表（叠加层）
        private string currentPreset = "";    // 当前选中的预设名

        private TMP_InputField searchInput;   // 搜索输入框
        private string searchText = "";       // 当前搜索词（空=不过滤）

        // ==================== 排序 + 合并开关 ====================

        private static bool s_hasShownSortPrompt;  // 是否已显示排序提示（每次启动游戏后仅一次）
        private static bool s_sortPromptChecked;    // 是否已从设置读取过弹窗状态
        private bool s_mergeEnabled;               // 当前是否启用合并显示
        private bool s_mergeSortEnabled;           // 移动时是否合并排序
        private GameObject s_mergeButton;          // 合并按钮 UI
        private GameObject s_mergeSortButton;      // 合并排序按钮 UI

        // PLib 选项映射缓存：staticID → 选项 Type
        private static Dictionary<string, Type> s_plibOptionsCache;
        private static bool s_plibCacheLoaded;

        // 配对缓存：Rebuild 时预计算，避免每行都 O(n) 扫描
        private HashSet<Mod> s_pairedMods;
        private bool s_suppressRebuild;  // 批量操作期间抑制 on_update 触发的 Rebuild

        private GameObject notePopup;         // 备注弹窗
        private TMP_InputField noteInput;     // 备注输入框
        private Toggle noteToggle;            // 写回标题勾选（Unity 标准 Toggle）
        private Mod noteMod;                  // 当前写备注的 mod

        private TextMeshProUGUI toastText;    // 底部提示文字
        private Coroutine toastRoutine;       // 提示消失协程

        // ==================== 生命周期 ====================

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            try
            {
                BuildUI();
                BuildPresetDropdown();
                BuildNotePopup();
                BuildToast();
                uiBuilt = true;
                Debug.Log("[MM] ModManagerScreen UI 构建完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 构建界面失败: " + e);
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            try
            {
                if (Global.Instance != null && Global.Instance.modManager != null)
                {
                    Global.Instance.modManager.on_update += OnModsChanged;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 订阅 mod 更新失败: " + e);
            }
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            EnsureToolTipOnTop();
            // 从设置加载状态
            ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
            s_mergeEnabled = settings.mergeEnabled;
            s_mergeSortEnabled = settings.mergeSortEnabled;
            UpdateMergeButtons();
            // 排序提示只弹一次：从设置读取，弹过后存盘
            if (!s_sortPromptChecked)
            {
                s_sortPromptChecked = true;
                s_hasShownSortPrompt = settings.hasPromptedSort;
            }
            if (!s_hasShownSortPrompt)
            {
                s_hasShownSortPrompt = true;
                settings.hasPromptedSort = true;
                ModManagerStore.SaveSettings();
                Manager.Dialog(
                    Global.Instance.globalCanvas,
                    ModStrings.SortDialogTitle,
                    ModStrings.SortDialogText,
                    ModStrings.SortDialogYes,
                    delegate() { SortPairedMods(); Rebuild(); },
                    ModStrings.SortDialogNo,
                    delegate() { Rebuild(); });
                return;
            }
            if (s_mergeEnabled)
            {
                EnsureMergeLocal();
            }
            Rebuild();
        }

        // 确保 ToolTip 提示框在最上层（抄 Ony：找 ToolTipScreen 下的 "ToolTip" 子物体的 Canvas）
        private static void EnsureToolTipOnTop()
        {
            ToolTipScreen tts = UnityEngine.Object.FindObjectOfType<ToolTipScreen>();
            if (tts == null) return;
            Transform toolTipTr = tts.transform.Find("ToolTip");
            if (toolTipTr == null)
            {
                // 递归找
                foreach (Canvas c in tts.GetComponentsInChildren<Canvas>(true))
                {
                    c.overrideSorting = true;
                    c.sortingOrder = 32767;
                }
                return;
            }
            Canvas tipCanvas = toolTipTr.GetComponent<Canvas>();
            if (tipCanvas == null) tipCanvas = toolTipTr.gameObject.AddComponent<Canvas>();
            tipCanvas.overrideSorting = true;
            tipCanvas.sortingOrder = 32767;
        }

        // 更新合并按钮文字
        private void UpdateMergeButtons()
        {
            if (s_mergeButton != null)
            {
                SetButtonText(s_mergeButton, s_mergeEnabled ? ModStrings.MergeDisplayOn : ModStrings.MergeDisplayOff);
            }
            if (s_mergeSortButton != null)
            {
                SetButtonText(s_mergeSortButton, s_mergeSortEnabled ? ModStrings.MergeSortOn : ModStrings.MergeSortOff);
            }
        }

        // 合并按钮点击：切换合并显示
        private void OnMergeButtonClicked()
        {
            try
            {
                s_mergeEnabled = !s_mergeEnabled;
                Debug.Log("[MM] 合并显示: " + (s_mergeEnabled ? "启用" : "禁用"));
                ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
                settings.mergeEnabled = s_mergeEnabled;
                ModManagerStore.SaveSettings();
                UpdateMergeButtons();
                s_suppressRebuild = true;
                if (s_mergeEnabled)
                {
                    EnsureMergeLocal();
                }
                else
                {
                    // 关闭合并时，恢复所有被停用的配对 mod
                    RestoreAllPairedMods();
                }
                s_suppressRebuild = false;
                Rebuild();
            }
            catch (Exception e)
            {
                s_suppressRebuild = false;
                Debug.LogError("[MM] 合并显示切换失败: " + e);
            }
        }

        // 关闭合并时，恢复所有有配对的 mod（两个都启用，让用户自己决定用哪个）
        private void RestoreAllPairedMods()
        {
            try
            {
                if (Global.Instance == null || Global.Instance.modManager == null) return;
                List<Mod> mods = Global.Instance.modManager.mods;
                int changed = 0;
                // 用预计算的配对集合，O(n) 遍历即可
                if (s_pairedMods == null)
                {
                    // 集合还没算过（理论上不会，因为 Rebuild 先算），兜底算一次
                    s_pairedMods = new HashSet<Mod>();
                    for (int i = 0; i < mods.Count; i++)
                    {
                        if (mods[i] == null) continue;
                        for (int j = i + 1; j < mods.Count; j++)
                        {
                            if (mods[j] != null && IsSameMod(mods[i], mods[j]))
                            {
                                s_pairedMods.Add(mods[i]);
                                s_pairedMods.Add(mods[j]);
                                break;
                            }
                        }
                    }
                }
                for (int i = 0; i < mods.Count; i++)
                {
                    Mod m = mods[i];
                    if (m == null || !s_pairedMods.Contains(m)) continue;
                    if (!m.IsEnabledForActiveDlc())
                    {
                        Global.Instance.modManager.EnableMod(m.label, true, null);
                        changed++;
                    }
                }
                if (changed > 0)
                {
                    Global.Instance.modManager.Save();
                    Debug.Log("[MM] 关闭合并: 恢复了 " + changed + " 个 mod");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 恢复配对 mod 失败: " + e);
            }
        }

        // 合并排序按钮点击：切换移动时是否整组移动
        private void OnMergeSortButtonClicked()
        {
            try
            {
                s_mergeSortEnabled = !s_mergeSortEnabled;
                Debug.Log("[MM] 合并排序: " + (s_mergeSortEnabled ? "启用" : "禁用"));
                ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
                settings.mergeSortEnabled = s_mergeSortEnabled;
                ModManagerStore.SaveSettings();
                UpdateMergeButtons();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 合并排序切换失败: " + e);
            }
        }

        // 反射获取 PLib 已注册的选项映射（staticID → Type）
        private static Dictionary<string, Type> GetPLibOptions()
        {
            // 如果缓存有数据就直接用；如果是空的，每次都重试（避免 PLib 还没初始化时缓存了空结果）
            if (s_plibCacheLoaded && s_plibOptionsCache != null && s_plibOptionsCache.Count > 0)
            {
                return s_plibOptionsCache;
            }
            if (s_plibOptionsCache == null)
            {
                s_plibOptionsCache = new Dictionary<string, Type>();
            }
            try
            {
                // POptions.Instance 是 internal static 属性
                Type poptType = typeof(POptions);
                PropertyInfo instanceProp = poptType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (instanceProp == null)
                {
                    Debug.Log("[MM] PLib: 找不到 POptions.Instance 属性");
                    return s_plibOptionsCache;
                }
                object instance = instanceProp.GetValue(null, null);
                if (instance == null)
                {
                    Debug.Log("[MM] PLib: POptions.Instance 为 null（PLib 可能未初始化）");
                    return s_plibOptionsCache;
                }

                // 先尝试 registered 字典（Initialize 后合并的所有选项）
                FieldInfo registeredField = poptType.GetField("registered", BindingFlags.NonPublic | BindingFlags.Instance);
                if (registeredField != null)
                {
                    System.Collections.IDictionary dict = registeredField.GetValue(instance) as System.Collections.IDictionary;
                    if (dict != null && dict.Count > 0)
                    {
                        foreach (DictionaryEntry entry in dict)
                        {
                            string sid = entry.Key as string;
                            object handler = entry.Value;
                            if (sid == null || handler == null) continue;
                            FieldInfo optionsTypeField = handler.GetType().GetField("optionsType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (optionsTypeField != null)
                            {
                                Type optType = optionsTypeField.GetValue(handler) as Type;
                                if (optType != null && !s_plibOptionsCache.ContainsKey(sid))
                                {
                                    s_plibOptionsCache[sid] = optType;
                                }
                            }
                        }
                    }
                }

                // 如果 registered 为空，尝试 modOptions 字典（RegisterOptions 时直接填充）
                if (s_plibOptionsCache.Count == 0)
                {
                    FieldInfo modOptionsField = poptType.GetField("modOptions", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (modOptionsField != null)
                    {
                        System.Collections.IDictionary modOpts = modOptionsField.GetValue(instance) as System.Collections.IDictionary;
                        if (modOpts != null)
                        {
                            foreach (DictionaryEntry entry in modOpts)
                            {
                                string sid = entry.Key as string;
                                Type optType = entry.Value as Type;
                                if (sid != null && optType != null && !s_plibOptionsCache.ContainsKey(sid))
                                {
                                    s_plibOptionsCache[sid] = optType;
                                }
                            }
                        }
                    }
                }

                if (s_plibOptionsCache.Count > 0)
                {
                    s_plibCacheLoaded = true;
                    Debug.Log("[MM] PLib: 已加载 " + s_plibOptionsCache.Count + " 个选项映射");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] PLib 反射失败: " + e);
            }
            return s_plibOptionsCache;
        }

        // 检查某个 mod 是否有 Steam/本地配对（用 Rebuild 预计算的集合，O(1) 查找）
        private bool HasPair(Mod mod)
        {
            return mod != null && s_pairedMods != null && s_pairedMods.Contains(mod);
        }

        // 检查某个 mod 是否有 PLib 选项
        private static bool HasPLibOptions(Mod mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.staticID)) return false;
            Dictionary<string, Type> map = GetPLibOptions();
            return map.ContainsKey(mod.staticID);
        }

        // 弹出某个 mod 的 PLib 选项面板
        private static void ShowPLibOptions(Mod mod)
        {
            try
            {
                Dictionary<string, Type> map = GetPLibOptions();
                Type optType;
                if (map.TryGetValue(mod.staticID, out optType))
                {
                    POptions.ShowDialog(optType);
                    Debug.Log("[MM] PLib: 已弹出 " + mod.title + " 的选项面板");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] PLib 弹出选项失败: " + e);
            }
        }

        // 配对排序：把本地 mod 和对应的 Steam 版排在一起
        private void SortPairedMods()
        {
            try
            {
                Debug.Log("[MM] 排序 开始配对排序");
                List<Mod> mods = Global.Instance.modManager.mods;
                for (int i = 0; i < mods.Count; i++)
                {
                    Mod m = mods[i];
                    if (m == null) continue;
                    for (int j = i + 1; j < mods.Count; j++)
                    {
                        if (mods[j] != null && IsSameMod(m, mods[j]))
                        {
                            Mod pair = mods[j];
                            mods.RemoveAt(j);
                            mods.Insert(i + 1, pair);
                            i++;
                            Debug.Log("[MM] 排序 已配对: " + m.title + " <-> " + pair.title);
                            break;
                        }
                    }
                }
                Global.Instance.modManager.Save();
                Debug.Log("[MM] 排序 完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 排序 崩溃: " + e);
            }
        }

        // 根据合并设置和优先平台，启用/停用对应版本
        private void EnsureMergeLocal()
        {
            try
            {
                if (Global.Instance == null || Global.Instance.modManager == null)
                {
                    return;
                }
                List<Mod> mods = Global.Instance.modManager.mods;
                ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
                int changedCount = 0;
                // 先收集本地 mod 列表，避免 O(n²) 全表扫描
                List<Mod> localMods = new List<Mod>();
                for (int i = 0; i < mods.Count; i++)
                {
                    if (mods[i] != null && mods[i].label.distribution_platform == Label.DistributionPlatform.Local)
                    {
                        localMods.Add(mods[i]);
                    }
                }
                // 遍历所有 Steam mod，找它的本地配对
                for (int i = 0; i < mods.Count; i++)
                {
                    Mod steamMod = mods[i];
                    if (steamMod == null || steamMod.label.distribution_platform != Label.DistributionPlatform.Steam)
                    {
                        continue;
                    }
                    for (int j = 0; j < localMods.Count; j++)
                    {
                        Mod localMod = localMods[j];
                        if (!IsSameMod(steamMod, localMod))
                        {
                            continue;
                        }
                        // 找到配对，根据优先平台决定谁启用
                        string normId = NormalizeStaticId(steamMod.staticID);
                        string preferred = "Local";
                        if (settings.preferredPlatform != null && settings.preferredPlatform.ContainsKey(normId))
                        {
                            preferred = settings.preferredPlatform[normId];
                        }
                        bool shouldSteamEnabled = (preferred == "Steam");
                        bool shouldLocalEnabled = (preferred == "Local");
                        if (steamMod.IsEnabledForActiveDlc() != shouldSteamEnabled)
                        {
                            Global.Instance.modManager.EnableMod(steamMod.label, shouldSteamEnabled, null);
                            changedCount++;
                            Debug.Log("[MM] 合并: " + (shouldSteamEnabled ? ModStrings.MergeEnabled : ModStrings.MergeDisabled) + " Steam 版: " + steamMod.title);
                        }
                        if (localMod.IsEnabledForActiveDlc() != shouldLocalEnabled)
                        {
                            Global.Instance.modManager.EnableMod(localMod.label, shouldLocalEnabled, null);
                            changedCount++;
                            Debug.Log("[MM] 合并: " + (shouldLocalEnabled ? ModStrings.MergeEnabled : ModStrings.MergeDisabled) + " 本地版: " + localMod.title);
                        }
                        break;
                    }
                }
                if (changedCount > 0)
                {
                    Global.Instance.modManager.Save();
                    ShowToast(ModStrings.ToastMergeAdjusted(changedCount), true);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 合并本地检查失败: " + e);
            }
        }

        // 归一化 staticID：剥掉结尾的 .Local / .Steam 后缀，用于 Steam 版和本地版配对
        private static string NormalizeStaticId(string sid)
        {
            if (string.IsNullOrEmpty(sid))
            {
                return "";
            }
            string s = sid;
            bool changed;
            do
            {
                changed = false;
                if (s.EndsWith(".Local", StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - ".Local".Length);
                    changed = true;
                }
                if (s.EndsWith(".Steam", StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - ".Steam".Length);
                    changed = true;
                }
            }
            while (changed);
            return s;
        }

        // 判定两个 mod 是不是同一个（staticID 归一化相等，或走了手工配对表）
        private static bool IsSameMod(Mod a, Mod b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            string na = NormalizeStaticId(a.staticID);
            string nb = NormalizeStaticId(b.staticID);
            if (!string.IsNullOrEmpty(na) && na == nb)
            {
                return true;
            }
            // 双向查手工配对表：不管 a 是 Steam 版还是本地版都能配对
            if (a.label.distribution_platform == Label.DistributionPlatform.Steam)
            {
                string localId;
                if (SteamLocalPair.TryGetValue(a.label.id, out localId) && b.label.id == localId)
                {
                    return true;
                }
            }
            if (b.label.distribution_platform == Label.DistributionPlatform.Steam)
            {
                string localId;
                if (SteamLocalPair.TryGetValue(b.label.id, out localId) && a.label.id == localId)
                {
                    return true;
                }
            }
            return false;
        }

        // 收集合并信息：steamToHide = 有本地版、列表里要藏掉的 Steam mod；mergedLocals = 对应的本地 mod
        private static void CollectMergeInfo(List<Mod> mods, out HashSet<Mod> steamToHide, out HashSet<Mod> mergedLocals)
        {
            steamToHide = new HashSet<Mod>();
            mergedLocals = new HashSet<Mod>();
            if (mods == null)
            {
                return;
            }
            ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
            List<Mod> localMods = new List<Mod>();
            for (int i = 0; i < mods.Count; i++)
            {
                Mod m = mods[i];
                if (m != null && m.label.distribution_platform == Label.DistributionPlatform.Local)
                {
                    localMods.Add(m);
                }
            }
            for (int i = 0; i < mods.Count; i++)
            {
                Mod m = mods[i];
                if (m == null || m.label.distribution_platform != Label.DistributionPlatform.Steam)
                {
                    continue;
                }
                for (int j = 0; j < localMods.Count; j++)
                {
                    if (IsSameMod(m, localMods[j]))
                    {
                        // 根据优先平台决定藏哪个
                        string normId = NormalizeStaticId(m.staticID);
                        string preferred = "Local";
                        if (settings.preferredPlatform != null && settings.preferredPlatform.ContainsKey(normId))
                        {
                            preferred = settings.preferredPlatform[normId];
                        }
                        if (preferred == "Steam")
                        {
                            // 优先 Steam：藏本地版
                            steamToHide.Add(localMods[j]);
                        }
                        else
                        {
                            // 优先 Local（默认）：藏 Steam 版
                            steamToHide.Add(m);
                            mergedLocals.Add(localMods[j]);
                        }
                        break;
                    }
                }
            }
        }

        // 切换某配对的优先平台
        private void TogglePreferredPlatform(Mod mod)
        {
            try
            {
                string normId = NormalizeStaticId(mod.staticID);
                ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
                string current = "Local";
                if (settings.preferredPlatform != null && settings.preferredPlatform.ContainsKey(normId))
                {
                    current = settings.preferredPlatform[normId];
                }
                string next = (current == "Local") ? "Steam" : "Local";
                settings.preferredPlatform[normId] = next;
                ModManagerStore.SaveSettings();
                Debug.Log("[MM] 切换优先平台: " + normId + " → " + next);
                s_suppressRebuild = true;
                if (s_mergeEnabled)
                {
                    EnsureMergeLocal();
                }
                s_suppressRebuild = false;
                Rebuild();
            }
            catch (Exception e)
            {
                s_suppressRebuild = false;
                Debug.LogError("[MM] 切换优先平台失败: " + e);
            }
        }

        // 获取某配对当前优先平台
        private static string GetPreferredPlatform(Mod mod)
        {
            string normId = NormalizeStaticId(mod.staticID);
            ModManagerStore.ManagerSettings settings = ModManagerStore.LoadSettings();
            if (settings.preferredPlatform != null && settings.preferredPlatform.ContainsKey(normId))
            {
                return settings.preferredPlatform[normId];
            }
            return "Local";
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();
            CloseNotePopup();
            if (presetDropdown != null)
            {
                presetDropdown.SetActive(false);
            }
            // 关掉管理器后刷新原版 ModsScreen，让排序变化实时显示
            ModManagerEntry.RefreshModsScreen();
        }

        protected override void OnCleanUp()
        {
            try
            {
                if (Global.Instance != null && Global.Instance.modManager != null)
                {
                    Global.Instance.modManager.on_update -= OnModsChanged;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 取消订阅失败: " + e);
            }
            base.OnCleanUp();
        }

        // mod 列表变化（启用/禁用/排序）时自动刷新
        private void OnModsChanged(object change_source)
        {
            if (s_suppressRebuild) return;
            try
            {
                Rebuild();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 刷新列表失败: " + e);
            }
        }

        // ==================== 主界面构建（全部交给布局组件，不手算坐标） ====================

        private void BuildUI()
        {
            // 主面板：放大到接近全屏，居中，深色背景
            mainPanel = new GameObject("MainPanel");
            mainPanel.transform.SetParent(transform, false);
            RectTransform mainPanelRT = mainPanel.AddComponent<RectTransform>();
            mainPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
            mainPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
            mainPanelRT.pivot = new Vector2(0.5f, 0.5f);
            mainPanelRT.sizeDelta = new Vector2(1600f, 900f);
            Image panelImg = mainPanel.AddComponent<Image>();
            panelImg.color = new Color(0.11f, 0.12f, 0.15f, 0.98f);
            panelImg.raycastTarget = true;

            VerticalLayoutGroup vlg = mainPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            BuildTitleRow();
            BuildPresetRow();
            BuildSearchRow();
            BuildHeaderRow();

            // 两栏容器
            GameObject columns = new GameObject("Columns");
            columns.transform.SetParent(mainPanel.transform, false);
            LayoutElement colLE = columns.AddComponent<LayoutElement>();
            colLE.preferredHeight = 740f;
            colLE.minHeight = 200f;
            colLE.flexibleHeight = 1f;
            HorizontalLayoutGroup colHLG = columns.AddComponent<HorizontalLayoutGroup>();
            colHLG.spacing = 8f;
            colHLG.padding = new RectOffset(0, 0, 0, 0);
            colHLG.childControlWidth = true;
            colHLG.childControlHeight = true;
            colHLG.childForceExpandWidth = false;
            colHLG.childForceExpandHeight = false;
            colHLG.childAlignment = TextAnchor.MiddleLeft;

            leftContent = BuildColumn(columns);
            rightContent = BuildColumn(columns);
        }

        // 标题行：左边标题，右边关闭按钮
        private void BuildTitleRow()
        {
            GameObject row = new GameObject("TitleRow");
            row.transform.SetParent(mainPanel.transform, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 38f;
            le.minHeight = 38f;
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(6, 6, 0, 0);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI title = MakeText(row, ModStrings.WindowTitle, 20f, Color.white, TextAlignmentOptions.Left);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;

            // 合并显示开关
            GameObject mergeBtn = AddButton(row, ModStrings.MergeDisplayOff, 120f, delegate() { OnMergeButtonClicked(); });
            s_mergeButton = mergeBtn;
            ToolTip mergeTip = mergeBtn.GetComponentInChildren<ToolTip>(true);
            if (mergeTip == null) mergeTip = mergeBtn.AddComponent<ToolTip>();
            mergeTip.toolTip = ModStrings.MergeDisplayTooltip;

            // 合并排序开关
            GameObject mergeSortBtn = AddButton(row, ModStrings.MergeSortOn, 120f, delegate() { OnMergeSortButtonClicked(); });
            s_mergeSortButton = mergeSortBtn;
            ToolTip mergeSortTip = mergeSortBtn.GetComponentInChildren<ToolTip>(true);
            if (mergeSortTip == null) mergeSortTip = mergeSortBtn.AddComponent<ToolTip>();
            mergeSortTip.toolTip = ModStrings.MergeSortTooltip;

            AddButton(row, ModStrings.Close, 84f, delegate() { Deactivate(); });
        }

        // 预设行：输入框 + 保存 + 下拉选择 + 应用 + 删除
        private void BuildPresetRow()
        {
            GameObject row = new GameObject("PresetRow");
            row.transform.SetParent(mainPanel.transform, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minHeight = 40f;
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(6, 6, 0, 0);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI label = MakeText(row, ModStrings.PresetLabel, 15f, new Color(0.8f, 0.82f, 0.85f), TextAlignmentOptions.Left);
            LayoutElement labelLE = label.GetComponent<LayoutElement>();
            labelLE.preferredWidth = 48f;
            labelLE.minWidth = 48f;

            presetInput = MakeInput(row, 160f, ModStrings.PresetPlaceholder);

            AddButton(row, ModStrings.SavePreset, 92f, delegate() { OnSavePresetClicked(); });

            presetDropBtn = AddButton(row, ModStrings.SelectPreset, 170f, delegate() { OnPresetDropBtnClicked(); });

            AddButton(row, ModStrings.ApplyPreset, 92f, delegate() { OnApplyPresetClicked(); });
            AddButton(row, ModStrings.DeletePreset, 92f, delegate() { OnDeletePresetClicked(); });

            TextMeshProUGUI hint = MakeText(row, ModStrings.PresetHint, 12f, new Color(0.55f, 0.57f, 0.62f), TextAlignmentOptions.Left);
            hint.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        // 搜索行：输入关键词后两个列表都按标题/备注过滤
        private void BuildSearchRow()
        {
            GameObject row = new GameObject("SearchRow");
            row.transform.SetParent(mainPanel.transform, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.minHeight = 36f;
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(6, 6, 0, 0);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI label = MakeText(row, ModStrings.SearchLabel, 15f, new Color(0.8f, 0.82f, 0.85f), TextAlignmentOptions.Left);
            LayoutElement labelLE = label.GetComponent<LayoutElement>();
            labelLE.preferredWidth = 48f;
            labelLE.minWidth = 48f;

            searchInput = MakeInput(row, 220f, ModStrings.SearchPlaceholder);
            LayoutElement inputLE = searchInput.GetComponent<LayoutElement>();
            inputLE.flexibleWidth = 1f;
            inputLE.preferredWidth = 220f;
            inputLE.minWidth = 120f;
            searchInput.onValueChanged.AddListener(delegate(string v) { OnSearchTextChanged(v); });

            AddButton(row, ModStrings.SearchClear, 64f, delegate() { OnSearchCleared(); });

            TextMeshProUGUI hint = MakeText(row, ModStrings.SearchHint, 12f, new Color(0.55f, 0.57f, 0.62f), TextAlignmentOptions.Left);
            hint.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        // 搜索框内容变化：记录关键词并刷新列表
        private void OnSearchTextChanged(string v)
        {
            searchText = (v == null ? "" : v.Trim());
            Rebuild();
        }

        // 清除搜索：清空输入框和关键词，恢复完整列表
        // 注意：清空输入框本身会触发 onValueChanged → 自动重建一次，
        // 这里不要再手动 Rebuild，避免同一帧重建两次浪费性能
        private void OnSearchCleared()
        {
            if (searchInput != null && !string.IsNullOrEmpty(searchInput.text))
            {
                searchInput.text = "";
                return;
            }
            // 输入框本来就是空的（用户手动删光了文本），但关键词还在的话兜底清掉
            if (!string.IsNullOrEmpty(searchText))
            {
                searchText = "";
                Rebuild();
            }
        }

        // 栏标题行：显示已启用/未启用数量
        private void BuildHeaderRow()
        {
            GameObject row = new GameObject("HeaderRow");
            row.transform.SetParent(mainPanel.transform, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.minHeight = 24f;
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(4, 4, 0, 0);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            leftHeader = MakeText(row, ModStrings.EnabledHeader(0), 15f, new Color(0.55f, 0.9f, 0.62f), TextAlignmentOptions.Left);
            leftHeader.GetComponent<LayoutElement>().flexibleWidth = 1f;
            rightHeader = MakeText(row, ModStrings.DisabledHeader(0), 15f, new Color(0.9f, 0.6f, 0.55f), TextAlignmentOptions.Left);
            rightHeader.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        // 构建一栏滚动列表，返回内容容器（条目挂这里）
        private GameObject BuildColumn(GameObject columnsRoot)
        {
            GameObject col = new GameObject("Column");
            col.transform.SetParent(columnsRoot.transform, false);
            LayoutElement colLE = col.AddComponent<LayoutElement>();
            colLE.flexibleWidth = 1f;
            colLE.preferredHeight = 740f;
            colLE.minHeight = 200f;
            Image colImg = col.AddComponent<Image>();
            colImg.color = new Color(0.16f, 0.17f, 0.21f, 1f);
            colImg.raycastTarget = true;

            KScrollRect scrollRect = col.AddComponent<KScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.allowVerticalScrollWheel = true;

            // Viewport：裁剪窗口
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(col.transform, false);
            RectTransform vpRT = viewport.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(2f, 2f);
            vpRT.offsetMax = new Vector2(-2f, -2f);
            Image vpImg = viewport.AddComponent<Image>();
            // Viewport：滚动视口。Mask 用它裁剪子物体，遮罩 Image 必须不透明！
            // 坑1：alpha=0 的 Image 作为 Mask 会把子物体全裁掉（"点击有反应但看不见"）
            // 坑2：raycastTarget=true 会挡在条目上面吃掉所有点击，必须关掉让点击穿透到条目
            vpImg.color = new Color(1f, 1f, 1f, 1f);
            vpImg.raycastTarget = false;
            viewport.AddComponent<Mask>();

            // Content：内容容器，高度由我们手动算（布局系统在纯代码+KScreen 下不可靠，
            // ContentSizeFitter 会把高度算回 0 覆盖手动值，必须删掉不用）
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform cRT = content.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0f, 1f);
            cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(0.5f, 1f);
            cRT.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(3, 3, 3, 3);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            scrollRect.viewport = vpRT;
            scrollRect.content = cRT;
            return content;
        }

        // ==================== 列表刷新 ====================

        private void Rebuild()
        {
            try
            {
                if (!uiBuilt)
                {
                    return;
                }
                ClearList(leftContent);
                ClearList(rightContent);

                if (s_entryPrefab == null)
                {
                    Debug.LogError("[MM] 没有原版条目 prefab，无法构建列表");
                    ShowToast(ModStrings.ToastNoTemplate, false);
                    UpdateHeaders(0, 0);
                    return;
                }

                List<Mod> mods = Global.Instance.modManager.mods;
                // 合并 Steam/本地（仅合并开关启用时）
                HashSet<Mod> steamToHide = new HashSet<Mod>();
                s_mergedLocals = new HashSet<Mod>();
                if (s_mergeEnabled)
                {
                    CollectMergeInfo(mods, out steamToHide, out s_mergedLocals);
                }
                // 预计算配对集合（O(n²) 只做一次，BuildRow 里 O(1) 查找）
                s_pairedMods = new HashSet<Mod>();
                for (int i = 0; i < mods.Count; i++)
                {
                    if (mods[i] == null) continue;
                    for (int j = i + 1; j < mods.Count; j++)
                    {
                        if (mods[j] != null && IsSameMod(mods[i], mods[j]))
                        {
                            s_pairedMods.Add(mods[i]);
                            s_pairedMods.Add(mods[j]);
                            break;
                        }
                    }
                }
                int leftCount = 0;
                int rightCount = 0;
                // 先统计已启用总数：排序按钮的置灰要按"已启用序列"算，
                // 第一个已启用 mod 上移按钮置灰、最后一个下移置灰
                int enabledTotal = 0;
                for (int i = 0; i < mods.Count; i++)
                {
                    Mod m = mods[i];
                    if (m == null || steamToHide.Contains(m))
                    {
                        continue;
                    }
                    if (m.IsEnabledForActiveDlc())
                    {
                        enabledTotal++;
                    }
                }
                int enabledSeq = 0;
                for (int i = 0; i < mods.Count; i++)
                {
                    Mod mod = mods[i];
                    if (mod == null || steamToHide.Contains(mod))
                    {
                        continue;
                    }
                    // 搜索过滤：标题或备注包含关键词才显示（不区分大小写）
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        string title = (mod.title == null ? "" : mod.title);
                        string note = GetNote(mod);
                        if (title.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0
                            && note.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }
                    bool enabled = mod.IsEnabledForActiveDlc();
                    if (enabled)
                    {
                        enabledSeq++;
                    }
                    BuildRow(mod, enabled, enabledSeq, enabledTotal, enabled ? leftContent : rightContent);
                    if (enabled)
                    {
                        leftCount++;
                    }
                    else
                    {
                        rightCount++;
                    }
                }
                Debug.Log("[MM] Rebuild: 共 " + mods.Count + " 个 mod，已启用 " + leftCount
                    + " 未启用 " + rightCount);

                UpdateHeaders(leftCount, rightCount);
                RefreshPresetSelection();

                // 保底：布局系统没撑开时手动算高度（条目数*行高+边距），保证内容可见。
                RectTransform lcRT = leftContent.transform as RectTransform;
                if (lcRT != null && leftCount > 0)
                {
                    float needH = (float)leftCount * (Mathf.Max(s_entryHeight, 36f) + 2f) + 6f;
                    lcRT.sizeDelta = new Vector2(lcRT.sizeDelta.x, needH);
                }
                RectTransform rcRT = rightContent.transform as RectTransform;
                if (rcRT != null && rightCount > 0)
                {
                    float needH2 = (float)rightCount * (Mathf.Max(s_entryHeight, 36f) + 2f) + 6f;
                    rcRT.sizeDelta = new Vector2(rcRT.sizeDelta.x, needH2);
                }
                EnsureToolTipOnTop();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 刷新列表失败: " + e);
            }
        }

        private void ClearList(GameObject content)
        {
            if (content == null)
            {
                return;
            }
            // 先隐藏，再延迟到帧末销毁。这是"不叠加"和"不崩"的唯一两全写法：
            // 1. 直接 DestroyImmediate：点原版开关时，事件还没处理完条目就被销毁，
            //    MultiToggle.RefreshHoverColor 访问已销毁组件 → 空引用崩溃（踩过坑）；
            // 2. 只用 Destroy：同帧内多次 Rebuild（开关/移动操作都会触发），旧条目
            //    到帧末才删、新条目又建，全部叠在一起（"按键挤在一起"就是这么来的）。
            // 先 SetActive(false) 让旧条目立即不可见、也不参与布局，帧末再真正删掉。
            for (int i = content.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = content.transform.GetChild(i);
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private void UpdateHeaders(int leftCount, int rightCount)
        {
            if (leftHeader != null)
            {
                leftHeader.text = ModStrings.EnabledHeader(leftCount);
            }
            if (rightHeader != null)
            {
                rightHeader.text = ModStrings.DisabledHeader(rightCount);
            }
        }

        // 构建一行：条目铺满整行（原版外观，用户喜欢的样式），操作按钮绝对定位贴右侧。
        // 关键：按钮右缘避开条目的启用开关（不然按钮盖住开关点不到，之前踩过坑），
        // 标题右缘避开按钮区并加省略号（像原版列表）。
        // enabledSeq / enabledTotal：该 mod 在"已启用序列"里的序号和总数，
        // 排序按钮置灰按它算（第一个上移置灰、最后一个下移置灰）
        private GameObject BuildRow(Mod mod, bool enabled, int enabledSeq, int enabledTotal, GameObject parent)
        {
            float rowH = Mathf.Max(s_entryHeight, 36f);
            GameObject row = new GameObject("Row_" + mod.label.id);
            row.transform.SetParent(parent.transform, false);
            LayoutElement rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = rowH;
            rowLE.minHeight = rowH;
            // 注意：new GameObject + SetParent 后 Row 已经自动带 RectTransform，
            // 再 AddComponent<RectTransform>() 会报 "already added" 刷屏，绝对不能加

            // 条目铺满整行（和原版 ModsScreen.BuildDisplay 同样的方式实例化）
            HierarchyReferences hr = Util.KInstantiateUI<HierarchyReferences>(s_entryPrefab, row);
            RectTransform hrRT = hr.transform as RectTransform;
            if (hrRT != null)
            {
                hrRT.anchorMin = Vector2.zero;
                hrRT.anchorMax = Vector2.one;
                hrRT.offsetMin = Vector2.zero;
                hrRT.offsetMax = Vector2.zero;
            }

            // 量一下启用开关在条目右侧占多宽：按钮区右缘要停在开关左边。
            // 开关锚点右对齐时，它离条目右边缘的距离和条目宽度无关，布局前算也算得准
            float rightReserve = 130f;  // 兜底值：开关 + 原版管理按钮区域
            MultiToggle toggle = hr.GetReference<MultiToggle>("EnabledToggle");
            if (toggle != null)
            {
                RectTransform tRT = toggle.transform as RectTransform;
                if (tRT != null)
                {
                    float d = -tRT.anchoredPosition.x + tRT.rect.width * (1f - tRT.pivot.x);
                    if (d >= 8f && d <= 400f)
                    {
                        rightReserve = d;
                    }
                }
            }

            ConfigureEntry(hr, mod, enabled);
            hr.gameObject.SetActive(true);

            // 操作按钮：右对齐到"开关左边沿"，从右往左排
            float rightPad = rightReserve + 4f;
            float btnY = (rowH - 28f) * 0.5f;

            // 上移 / 下移（仅已启用栏，调加载顺序）
            if (enabled)
            {
                Mod capturedUp = mod;
                GameObject upBtn = AddButtonAbsolute(row, ModStrings.MoveUp, 52f, 28f, delegate() { OnMoveClicked(capturedUp, -1); });
                SetButtonInteractable(upBtn, enabledSeq > 1);
                RectTransform upRT = upBtn.transform as RectTransform;
                upRT.anchorMin = new Vector2(1f, 0f);
                upRT.anchorMax = new Vector2(1f, 0f);
                upRT.pivot = new Vector2(1f, 0f);
                upRT.anchoredPosition = new Vector2(-rightPad - 52f - 4f, btnY);

                Mod capturedDown = mod;
                GameObject downBtn = AddButtonAbsolute(row, ModStrings.MoveDown, 52f, 28f, delegate() { OnMoveClicked(capturedDown, 1); });
                SetButtonInteractable(downBtn, enabledSeq < enabledTotal);
                RectTransform downRT = downBtn.transform as RectTransform;
                downRT.anchorMin = new Vector2(1f, 0f);
                downRT.anchorMax = new Vector2(1f, 0f);
                downRT.pivot = new Vector2(1f, 0f);
                downRT.anchoredPosition = new Vector2(-rightPad, btnY);

                rightPad += 52f + 4f + 52f + 8f;
            }

            // 转本地（仅 Steam mod）
            if (mod.label.distribution_platform == Label.DistributionPlatform.Steam)
            {
                Mod captured = mod;
                GameObject locBtn = AddButtonAbsolute(row, ModStrings.Localize, 64f, 28f, delegate() { OnLocalizeClicked(captured); });
                RectTransform locRT = locBtn.transform as RectTransform;
                locRT.anchorMin = new Vector2(1f, 0f);
                locRT.anchorMax = new Vector2(1f, 0f);
                locRT.pivot = new Vector2(1f, 0f);
                locRT.anchoredPosition = new Vector2(-rightPad, btnY);
                rightPad += 64f + 4f;
            }

            // 删除（仅本地 mod：转本地产生的副本或自己放进 Local 目录的 mod）
            if (mod.label.distribution_platform == Label.DistributionPlatform.Local)
            {
                Mod capturedDel = mod;
                GameObject delBtn = AddButtonAbsolute(row, ModStrings.Delete, 52f, 28f, delegate() { OnDeleteLocalClicked(capturedDel); });
                RectTransform delRT = delBtn.transform as RectTransform;
                delRT.anchorMin = new Vector2(1f, 0f);
                delRT.anchorMax = new Vector2(1f, 0f);
                delRT.pivot = new Vector2(1f, 0f);
                delRT.anchoredPosition = new Vector2(-rightPad, btnY);
                rightPad += 52f + 4f;
            }

            // 配置（有 PLib 选项的 mod 才显示）
            if (HasPLibOptions(mod))
            {
                Mod capturedCfg = mod;
                GameObject cfgBtn = AddButtonAbsolute(row, ModStrings.Config, 52f, 28f, delegate() { ShowPLibOptions(capturedCfg); });
                RectTransform cfgRT = cfgBtn.transform as RectTransform;
                cfgRT.anchorMin = new Vector2(1f, 0f);
                cfgRT.anchorMax = new Vector2(1f, 0f);
                cfgRT.pivot = new Vector2(1f, 0f);
                cfgRT.anchoredPosition = new Vector2(-rightPad, btnY);
                rightPad += 52f + 4f;
            }

            // 切换本地/Steam（合并开启且该 mod 有配对时显示）
            if (s_mergeEnabled && HasPair(mod))
            {
                Mod capturedSwap = mod;
                string pref = GetPreferredPlatform(mod);
                string swapLabel = (pref == "Local") ? ModStrings.UseSteam : ModStrings.UseLocal;
                GameObject swapBtn = AddButtonAbsolute(row, swapLabel, 64f, 28f, delegate() { TogglePreferredPlatform(capturedSwap); });
                RectTransform swapRT = swapBtn.transform as RectTransform;
                swapRT.anchorMin = new Vector2(1f, 0f);
                swapRT.anchorMax = new Vector2(1f, 0f);
                swapRT.pivot = new Vector2(1f, 0f);
                swapRT.anchoredPosition = new Vector2(-rightPad, btnY);
                rightPad += 64f + 4f;
            }

            // 备注（最靠左，标题右缘就停在它左边沿）
            Mod capturedNote = mod;
            GameObject noteBtn = AddButtonAbsolute(row, ModStrings.Note, 52f, 28f, delegate() { OpenNotePopup(capturedNote); });
            RectTransform noteRT = noteBtn.transform as RectTransform;
            noteRT.anchorMin = new Vector2(1f, 0f);
            noteRT.anchorMax = new Vector2(1f, 0f);
            noteRT.pivot = new Vector2(1f, 0f);
            noteRT.anchoredPosition = new Vector2(-rightPad, btnY);

            // 标题限宽 + 省略号：右缘停到最左按钮（备注）的左边沿
            float btnTotal = rightPad + 52f + 4f;
            ApplyTitleOverflow(hr, btnTotal);

            return row;
        }

        // 标题右边缘限宽（避开按钮区）+ 超出加省略号，像原版 mod 列表
        private void ApplyTitleOverflow(HierarchyReferences hr, float rightSpace)
        {
            LocText title = hr.GetReference<LocText>("Title");
            if (title == null)
            {
                return;
            }
            RectTransform tRT = title.transform as RectTransform;
            if (tRT == null)
            {
                return;
            }
            // 标题锚点转成"水平铺满"，右边缘让出按钮区
            tRT.anchorMin = new Vector2(0f, tRT.anchorMin.y);
            tRT.anchorMax = new Vector2(1f, tRT.anchorMax.y);
            tRT.offsetMax = new Vector2(-rightSpace, tRT.offsetMax.y);
            // 单行 + 超出省略号（游戏内 TMP 用旧属性名 overflowMode）
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.enableWordWrapping = false;
            title.enableAutoSizing = false;
        }

        // 配置克隆出来的原版条目：标题/版本/开关/备注按钮/提示
        private void ConfigureEntry(HierarchyReferences hr, Mod mod, bool enabled)
        {
            // 删掉拖拽排序组件：它的 listener 是空的，玩家一拖就会空引用崩
            DragMe drag = hr.GetComponent<DragMe>();
            if (drag != null)
            {
                UnityEngine.Object.Destroy(drag);
            }

            // 标题（和原版一样优先查本地化），有备注就拼成 [备注] 标题
            LocText title = hr.GetReference<LocText>("Title");
            if (title != null)
            {
                title.key = "";
                string text = mod.title;
                StringEntry localized;
                if (Strings.TryGet(mod.title, out localized))
                {
                    text = localized;
                }
                string note = GetNote(mod);
                if (!string.IsNullOrEmpty(note))
                {
                    // 本地 mod 的备注可能已经写进标题里（比如 jecharacters[通用拼音搜索]），先去掉避免重复
                    string tag = "[" + note + "]";
                    int tagIdx = text.IndexOf(tag, StringComparison.Ordinal);
                    if (tagIdx >= 0)
                    {
                        text = text.Remove(tagIdx, tag.Length).Trim();
                    }
                    // 备注放前面、标题放后面，同色直接拼：[通用拼音搜索] jecharacters-1.21.1-neoforge-4.5.25
                    text = tag + " " + text;
                }
                // 合并显示：有 Steam 配对、界面里用的是本地版，标出来一眼分清
                if (s_mergedLocals != null && s_mergedLocals.Contains(mod))
                {
                    string pref = GetPreferredPlatform(mod);
                    if (pref == "Local")
                    {
                        text = ModStrings.LocalTag + " " + text;
                    }
                }
                title.text = text;
            }
            hr.name = mod.title;

            // 版本号
            LocText version = hr.GetReference<LocText>("Version");
            if (version != null)
            {
                string v = GetVersion(mod);
                if (v.Length > 0)
                {
                    version.key = "";
                    version.text = v;
                    version.gameObject.SetActive(true);
                }
                else
                {
                    version.gameObject.SetActive(false);
                }
            }

            // 描述提示
            ToolTip tip = hr.GetReference<ToolTip>("Description");
            if (tip != null && (int)mod.available_content > 0)
            {
                StringEntry desc;
                if (Strings.TryGet(mod.description, out desc))
                {
                    tip.toolTip = desc;
                }
                else
                {
                    tip.toolTip = mod.description;
                }
            }

            // 背景状态（和原版一样：有内容 Inactive，没内容 Disabled）
            KImage bg = hr.GetReference<KImage>("BG");
            if (bg != null)
            {
                if ((int)mod.available_content > 0)
                {
                    bg.defaultState = KImage.ColorSelector.Inactive;
                    bg.ColorState = KImage.ColorSelector.Inactive;
                }
                else
                {
                    bg.defaultState = KImage.ColorSelector.Disabled;
                    bg.ColorState = KImage.ColorSelector.Disabled;
                }
            }

            // 启用/禁用开关（原版 MultiToggle，克隆来的可以正常 ChangeState）
            MultiToggle toggle = hr.GetReference<MultiToggle>("EnabledToggle");
            if (toggle != null)
            {
                toggle.ChangeState(enabled ? 1 : 0);
                Mod capturedMod = mod;
                MultiToggle capturedToggle = toggle;
                toggle.onClick = delegate() { OnEntryToggleClicked(capturedToggle, capturedMod); };
                ToolTip toggleTip = toggle.GetComponent<ToolTip>();
                if (toggleTip != null)
                {
                    toggleTip.OnToolTip = delegate()
                    {
                        if (capturedMod.IsEnabledForActiveDlc())
                        {
                            return UI.FRONTEND.MODS.TOOLTIPS.ENABLED;
                        }
                        return UI.FRONTEND.MODS.TOOLTIPS.DISABLED;
                    };
                }
            }

            // 原版的"管理"按钮：藏掉，不用它当备注按钮了。
            // 之前把它改文字当备注用，会和我们行上横排的按钮叠在一起盖住开关（踩过坑）
            KButton manage = hr.GetReference<KButton>("ManageButton");
            if (manage != null)
            {
                manage.ClearOnClick();
                manage.isInteractable = false;
                manage.gameObject.SetActive(false);
            }
        }

        // 条目开关点击：切换启用状态
        private void OnEntryToggleClicked(MultiToggle toggle, Mod mod)
        {
            try
            {
                bool enable = !mod.IsEnabledForActiveDlc();
                if (toggle != null)
                {
                    toggle.ChangeState(enable ? 1 : 0);
                }
                s_suppressRebuild = true;
                ActionResult r = ModManagerActions.ToggleMod(mod, enable);
                // 合并开着时，手动开关可能破坏"只启用优先版本"的状态，重新对齐
                if (s_mergeEnabled && HasPair(mod))
                {
                    EnsureMergeLocal();
                }
                s_suppressRebuild = false;
                ShowToast(r.message, r.ok);
                Rebuild();
            }
            catch (Exception e)
            {
                s_suppressRebuild = false;
                Debug.LogError("[MM] 切换 mod 失败: " + e);
            }
        }

        // ==================== 行操作 ====================

        private void OnLocalizeClicked(Mod mod)
        {
            try
            {
                s_suppressRebuild = true;
                ActionResult r = ModManagerActions.LocalizeMod(mod);
                // 转本地后如果合并开着，需要重新计算启用/停用状态
                if (s_mergeEnabled)
                {
                    EnsureMergeLocal();
                }
                s_suppressRebuild = false;
                ShowToast(r.message, r.ok);
                if (r.ok)
                {
                    Rebuild();
                }
            }
            catch (Exception e)
            {
                s_suppressRebuild = false;
                Debug.LogError("[MM] 转本地失败: " + e);
            }
        }

        // 删除本地 mod：先弹原版确认框，确认后真删
        private void OnDeleteLocalClicked(Mod mod)
        {
            try
            {
                if (mod == null)
                {
                    return;
                }
                Mod captured = mod;
                Manager.Dialog(
                    Global.Instance.globalCanvas,
                    ModStrings.DeleteTitle,
                    ModStrings.DeleteConfirm(mod.title),
                    ModStrings.DeleteOk,
                    delegate() { DoDeleteLocal(captured); },
                    ModStrings.Cancel,
                    null);
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 打开删除确认框失败: " + e);
            }
        }

        private void DoDeleteLocal(Mod mod)
        {
            try
            {
                s_suppressRebuild = true;
                ActionResult r = ModManagerActions.DeleteLocalMod(mod);
                // 删除后如果合并开着，需要重新计算（被删的本地版对应的 Steam 版可能需要重新启用）
                if (s_mergeEnabled)
                {
                    EnsureMergeLocal();
                }
                s_suppressRebuild = false;
                ShowToast(r.message, r.ok);
                if (r.ok)
                {
                    Rebuild();
                }
            }
            catch (Exception e)
            {
                s_suppressRebuild = false;
                Debug.LogError("[MM] 删除本地 mod 失败: " + e);
            }
        }

        // 上移 / 下移（调整加载顺序）
        private void OnMoveClicked(Mod mod, int delta)
        {
            try
            {
                List<Mod> mods = Global.Instance.modManager.mods;
                // 组 = mod + 它的 Steam/本地配对（仅合并排序开启时）
                HashSet<Mod> group = new HashSet<Mod>();
                group.Add(mod);
                if (s_mergeSortEnabled)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        if (mods[i] != null && mods[i] != mod && IsSameMod(mods[i], mod))
                        {
                            group.Add(mods[i]);
                        }
                    }
                }

                // 组的数组区间 [gStart, gEnd]
                int gStart = int.MaxValue;
                int gEnd = -1;
                for (int i = 0; i < mods.Count; i++)
                {
                    if (group.Contains(mods[i]))
                    {
                        if (i < gStart)
                        {
                            gStart = i;
                        }
                        if (i > gEnd)
                        {
                            gEnd = i;
                        }
                    }
                }
                if (gStart == int.MaxValue)
                {
                    return;
                }

                // 找组外最近的已启用 mod（按数组顺序），它就是移动目标
                int target = -1;
                if (delta < 0)
                {
                    for (int i = gStart - 1; i >= 0; i--)
                    {
                        if (mods[i] != null && !group.Contains(mods[i]) && mods[i].IsEnabledForActiveDlc())
                        {
                            target = i;
                            break;
                        }
                    }
                }
                else
                {
                    for (int i = gEnd + 1; i < mods.Count; i++)
                    {
                        if (mods[i] != null && !group.Contains(mods[i]) && mods[i].IsEnabledForActiveDlc())
                        {
                            target = i;
                            break;
                        }
                    }
                }
                if (target < 0)
                {
                    return; // 已经到头了
                }

                // 记录组内成员原本的数组顺序（比如 Steam 在前、本地在后）
                List<Mod> orderedGroup = new List<Mod>();
                for (int i = 0; i < mods.Count; i++)
                {
                    if (group.Contains(mods[i]))
                    {
                        orderedGroup.Add(mods[i]);
                    }
                }
                int groupCount = gEnd - gStart + 1;

                // 移除整个组
                mods.RemoveAll(delegate(Mod m) { return group.Contains(m); });

                // 上移：插到 target 前面（target 在组前，移除组后位置不变）
                // 下移：插到 target 后面（target 在组后，移除组后前移 groupCount 个位置）
                int insertAt = target;
                if (delta > 0)
                {
                    insertAt = target - groupCount + 1;
                }
                if (insertAt < 0)
                {
                    insertAt = 0;
                }
                if (insertAt > mods.Count)
                {
                    insertAt = mods.Count;
                }
                mods.InsertRange(insertAt, orderedGroup);

                Global.Instance.modManager.Save();
                Rebuild();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 调整顺序失败: " + e);
            }
        }

        // ==================== 预设 ====================

        private void OnSavePresetClicked()
        {
            try
            {
                string name = presetInput.text.Trim();
                ActionResult r = ModManagerActions.SavePreset(name);
                ShowToast(r.message, r.ok);
                if (r.ok)
                {
                    presetInput.text = "";
                    currentPreset = name;
                    RefreshPresetSelection();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 保存预设失败: " + e);
            }
        }

        private void OnApplyPresetClicked()
        {
            try
            {
                if (string.IsNullOrEmpty(currentPreset))
                {
                    ShowToast(ModStrings.ToastSelectPresetFirst, false);
                    return;
                }
                // 用原版确认框确认
                Manager.Dialog(
                    Global.Instance.globalCanvas,
                    "应用预设",
                    "确定要应用预设 [" + currentPreset + "] 吗？\n当前所有 mod 的开关状态和顺序会被覆盖。",
                    "确定",
                    delegate() { DoApplyPreset(); },
                    "取消",
                    null);
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 打开确认框失败: " + e);
            }
        }

        private void DoApplyPreset()
        {
            bool reSubscribed = false;
            try
            {
                // 临时退订：ApplyPreset 里会对每个状态变化的 mod 调 EnableMod，
                // 每个 EnableMod 都会同步触发 on_update → Rebuild（全量重建 138 行）。
                // 几十个 mod 连着重建会明显卡顿，所以先退订，最后手动刷新一次。
                if (Global.Instance != null && Global.Instance.modManager != null)
                {
                    Global.Instance.modManager.on_update -= OnModsChanged;
                    reSubscribed = true;
                }
                ActionResult r = ModManagerActions.ApplyPreset(currentPreset);
                // 恢复订阅（无论成功失败都要恢复，否则后面开关切换不再自动刷新）
                if (reSubscribed)
                {
                    Global.Instance.modManager.on_update += OnModsChanged;
                    reSubscribed = false;
                }
                ShowToast(r.message, r.ok);
                if (r.ok)
                {
                    Rebuild();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 应用预设失败: " + e);
                if (reSubscribed)
                {
                    Global.Instance.modManager.on_update += OnModsChanged;
                    reSubscribed = false;
                }
            }
            finally
            {
                // 兜底：任何路径都不允许漏订阅
                if (reSubscribed)
                {
                    try
                    {
                        Global.Instance.modManager.on_update += OnModsChanged;
                    }
                    catch (Exception e2)
                    {
                        Debug.LogError("[MM] 恢复订阅失败: " + e2);
                    }
                }
            }
        }

        private void OnDeletePresetClicked()
        {
            try
            {
                if (string.IsNullOrEmpty(currentPreset))
                {
                    ShowToast("请先点击选择预设", false);
                    return;
                }
                ActionResult r = ModManagerActions.DeletePreset(currentPreset);
                ShowToast(r.message, r.ok);
                if (r.ok)
                {
                    currentPreset = "";
                    RefreshPresetSelection();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 删除预设失败: " + e);
            }
        }

        // ==================== 预设下拉列表（叠加层） ====================

        private void BuildPresetDropdown()
        {
            presetDropdown = new GameObject("PresetDropdown");
            presetDropdown.transform.SetParent(transform, false);
            RectTransform rt = presetDropdown.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(210f, 60f);
            // 脱离布局：不参与任何 LayoutGroup，自己定位
            LayoutElement le = presetDropdown.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            Image img = presetDropdown.AddComponent<Image>();
            img.color = new Color(0.13f, 0.14f, 0.18f, 0.98f);
            img.raycastTarget = true;
            VerticalLayoutGroup vlg = presetDropdown.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter fitter = presetDropdown.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            presetDropdown.SetActive(false);
        }

        private void OnPresetDropBtnClicked()
        {
            try
            {
                PlayClick();
                if (presetDropdown.activeSelf)
                {
                    presetDropdown.SetActive(false);
                    return;
                }
                BuildPresetDropdownItems();
                presetDropdown.SetActive(true);
                presetDropdown.transform.SetAsLastSibling();
                PositionPresetDropdown();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 打开预设列表失败: " + e);
            }
        }

        // 把下拉列表摆到预设按钮正下方（世界坐标换算，不依赖 Camera）
        private void PositionPresetDropdown()
        {
            try
            {
                RectTransform popupRT = (RectTransform)presetDropdown.transform;
                RectTransform parentRT = (RectTransform)presetDropdown.transform.parent;
                RectTransform btnRT = (RectTransform)presetDropBtn.transform;
                Vector3 worldBottomLeft = btnRT.TransformPoint(new Vector3(btnRT.rect.xMin, btnRT.rect.yMin, 0f));
                Vector3 local = parentRT.InverseTransformPoint(worldBottomLeft);
                popupRT.localPosition = new Vector3(local.x, local.y - 4f, 0f);
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 定位预设列表失败: " + e);
            }
        }

        // 重建下拉列表选项
        private void BuildPresetDropdownItems()
        {
            try
            {
                for (int i = presetDropdown.transform.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(presetDropdown.transform.GetChild(i).gameObject);
                }
                List<string> names = ModManagerActions.ListPresets();
                if (names.Count == 0)
                {
                    TextMeshProUGUI empty = MakeText(presetDropdown, ModStrings.NoPresets, 13f, new Color(0.6f, 0.62f, 0.66f), TextAlignmentOptions.Center);
                    LayoutElement le = empty.GetComponent<LayoutElement>();
                    le.preferredWidth = 200f;
                    le.minWidth = 200f;
                    le.preferredHeight = 28f;
                    le.minHeight = 28f;
                    return;
                }
                foreach (string name in names)
                {
                    string itemName = name;
                    AddButton(presetDropdown, itemName, 200f, delegate() { SelectPreset(itemName); });
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 构建预设列表失败: " + e);
            }
        }

        private void SelectPreset(string name)
        {
            try
            {
                currentPreset = name;
                presetDropdown.SetActive(false);
                UpdatePresetBtnText();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 选择预设失败: " + e);
            }
        }

        private void UpdatePresetBtnText()
        {
            if (presetDropBtn == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(currentPreset))
            {
                SetButtonText(presetDropBtn, ModStrings.SelectPreset);
            }
            else
            {
                SetButtonText(presetDropBtn, currentPreset);
            }
        }

        // 列表刷新后：清掉已经不存在的预设名，更新按钮文字
        private void RefreshPresetSelection()
        {
            try
            {
                if (!string.IsNullOrEmpty(currentPreset))
                {
                    List<string> names = ModManagerActions.ListPresets();
                    if (!names.Contains(currentPreset))
                    {
                        currentPreset = "";
                    }
                }
                UpdatePresetBtnText();
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 刷新预设失败: " + e);
            }
        }

        // ==================== 备注弹窗 ====================

        private void BuildNotePopup()
        {
            notePopup = new GameObject("NotePopup");
            notePopup.transform.SetParent(transform, false);
            RectTransform popupRT = notePopup.AddComponent<RectTransform>();
            popupRT.anchorMin = Vector2.zero;
            popupRT.anchorMax = Vector2.one;
            popupRT.offsetMin = Vector2.zero;
            popupRT.offsetMax = Vector2.zero;
            notePopup.AddComponent<LayoutElement>().ignoreLayout = true;
            notePopup.SetActive(false);

            // 遮罩：点击关闭
            GameObject mask = new GameObject("Mask");
            mask.transform.SetParent(notePopup.transform, false);
            RectTransform maskRT = mask.AddComponent<RectTransform>();
            maskRT.anchorMin = Vector2.zero;
            maskRT.anchorMax = Vector2.one;
            maskRT.offsetMin = Vector2.zero;
            maskRT.offsetMax = Vector2.zero;
            Image maskImg = mask.AddComponent<Image>();
            maskImg.color = new Color(0f, 0f, 0f, 0.6f);
            maskImg.raycastTarget = true;
            Button maskBtn = mask.AddComponent<Button>();
            maskBtn.onClick.AddListener(delegate() { PlayClick(); CloseNotePopup(); });

            // 面板
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(notePopup.transform, false);
            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(540f, 300f);
            Image panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.13f, 0.14f, 0.17f, 1f);
            panelImg.raycastTarget = true;

            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 12, 12);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            TextMeshProUGUI title = MakeText(panel, ModStrings.NotePopupTitle, 18f, Color.white, TextAlignmentOptions.Center);
            title.GetComponent<LayoutElement>().preferredHeight = 34f;
            title.GetComponent<LayoutElement>().minHeight = 34f;

            noteInput = MakeInput(panel, 480f, ModStrings.NotePlaceholder);

            // 勾选行：Unity 标准 Toggle + 文字
            GameObject checkRow = new GameObject("CheckRow");
            checkRow.transform.SetParent(panel.transform, false);
            LayoutElement crLE = checkRow.AddComponent<LayoutElement>();
            crLE.preferredHeight = 32f;
            crLE.minHeight = 32f;
            HorizontalLayoutGroup crHLG = checkRow.AddComponent<HorizontalLayoutGroup>();
            crHLG.spacing = 8f;
            crHLG.padding = new RectOffset(8, 8, 0, 0);
            crHLG.childControlWidth = true;
            crHLG.childControlHeight = true;
            crHLG.childForceExpandWidth = false;
            crHLG.childForceExpandHeight = false;
            crHLG.childAlignment = TextAnchor.MiddleLeft;

            noteToggle = MakeToggle(checkRow);
            noteToggle.onValueChanged.AddListener(delegate(bool v) { PlayClick(); });
            TextMeshProUGUI checkText = MakeText(checkRow, ModStrings.NoteWriteBack, 14f, new Color(0.85f, 0.87f, 0.9f), TextAlignmentOptions.Left);
            LayoutElement ctLE = checkText.GetComponent<LayoutElement>();
            ctLE.preferredWidth = 260f;
            ctLE.minWidth = 260f;

            TextMeshProUGUI hint = MakeText(panel, ModStrings.NoteHint, 12f, new Color(0.55f, 0.57f, 0.62f), TextAlignmentOptions.Center);
            hint.GetComponent<LayoutElement>().preferredHeight = 20f;
            hint.GetComponent<LayoutElement>().minHeight = 20f;

            // 按钮行
            GameObject btnRow = new GameObject("BtnRow");
            btnRow.transform.SetParent(panel.transform, false);
            LayoutElement brLE = btnRow.AddComponent<LayoutElement>();
            brLE.preferredHeight = 44f;
            brLE.minHeight = 44f;
            HorizontalLayoutGroup brHLG = btnRow.AddComponent<HorizontalLayoutGroup>();
            brHLG.spacing = 12f;
            brHLG.padding = new RectOffset(0, 0, 0, 0);
            brHLG.childControlWidth = true;
            brHLG.childControlHeight = true;
            brHLG.childForceExpandWidth = false;
            brHLG.childForceExpandHeight = false;
            brHLG.childAlignment = TextAnchor.MiddleCenter;

            AddButton(btnRow, ModStrings.NoteSave, 130f, delegate() { OnNoteSave(); });
            AddButton(btnRow, ModStrings.Cancel, 130f, delegate() { CloseNotePopup(); });
        }

        // 打开备注弹窗
        private void OpenNotePopup(Mod mod)
        {
            try
            {
                noteMod = mod;
                noteInput.text = GetNote(mod);
                // 只有本地 mod 才能"写回标题"，创意工坊 mod 勾选框禁用，避免误导
                noteToggle.interactable = mod.IsLocal;
                noteToggle.isOn = false;
                notePopup.transform.SetAsLastSibling();
                notePopup.SetActive(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 打开备注弹窗失败: " + e);
            }
        }

        private void CloseNotePopup()
        {
            if (notePopup != null)
            {
                notePopup.SetActive(false);
            }
        }

        private void OnNoteSave()
        {
            try
            {
                if (noteMod == null)
                {
                    CloseNotePopup();
                    return;
                }
                bool writeTitle = noteToggle.isOn;
                ActionResult r = ModManagerActions.SetNote(noteMod, noteInput.text, writeTitle);
                ShowToast(r.message, r.ok);
                CloseNotePopup();
                if (r.ok)
                {
                    Rebuild();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 保存备注失败: " + e);
                CloseNotePopup();
            }
        }

        // ==================== 底部提示 ====================

        private void BuildToast()
        {
            GameObject toast = new GameObject("Toast");
            toast.transform.SetParent(transform, false);
            RectTransform toastRT = toast.AddComponent<RectTransform>();
            toastRT.anchorMin = new Vector2(0.5f, 0f);
            toastRT.anchorMax = new Vector2(0.5f, 0f);
            toastRT.pivot = new Vector2(0.5f, 0f);
            toastRT.anchoredPosition = new Vector2(0f, 48f);
            toastRT.sizeDelta = new Vector2(760f, 42f);
            toast.AddComponent<LayoutElement>().ignoreLayout = true;
            Image toastImg = toast.AddComponent<Image>();
            toastImg.color = new Color(0.05f, 0.06f, 0.08f, 0.85f);
            toastImg.raycastTarget = false;
            toastText = MakeText(toast, "", 15f, Color.white, TextAlignmentOptions.Center);
            toastText.raycastTarget = false;
            toast.SetActive(false);
        }

        // 显示提示（成功绿色/失败红色），3 秒后自动消失
        private void ShowToast(string msg, bool ok)
        {
            try
            {
                if (toastText == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(msg))
                {
                    msg = ModStrings.ToastDone;
                }
                toastText.text = msg;
                toastText.color = ok ? new Color(0.55f, 0.95f, 0.6f) : new Color(1f, 0.5f, 0.45f);
                toastText.transform.parent.gameObject.SetActive(true);
                toastText.transform.parent.SetAsLastSibling();
                if (toastRoutine != null)
                {
                    StopCoroutine(toastRoutine);
                }
                toastRoutine = StartCoroutine(HideToastRoutine());
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 显示提示失败: " + e);
            }
        }

        private IEnumerator HideToastRoutine()
        {
            yield return new WaitForSeconds(3f);
            if (toastText != null && toastText.transform != null && toastText.transform.parent != null)
            {
                toastText.transform.parent.gameObject.SetActive(false);
            }
        }

        // ==================== 数据小工具 ====================

        // 读备注：先查 notes.json，本地 mod 再从标题里提取 [备注]
        private string GetNote(Mod mod)
        {
            try
            {
                string key = mod.label.distribution_platform.ToString() + "|" + mod.label.id;
                // LoadNotes 内部有缓存，第一次读文件，之后直接用内存字典
                Dictionary<string, string> notes = ModManagerStore.LoadNotes();
                string note = "";
                if (notes != null && notes.ContainsKey(key))
                {
                    note = notes[key];
                }
                return note;
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 读备注失败: " + e);
                return "";
            }
        }

        private string GetVersion(Mod mod)
        {
            try
            {
                if (mod.packagedModInfo != null && !string.IsNullOrEmpty(mod.packagedModInfo.version))
                {
                    string v = mod.packagedModInfo.version;
                    if (v.StartsWith("V"))
                    {
                        return "v" + v.Substring(1);
                    }
                    if (!v.StartsWith("v"))
                    {
                        return "v" + v;
                    }
                    return v;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 读版本失败: " + e);
            }
            return "";
        }

        // ==================== 通用 UI 工具 ====================

        // 加按钮：优先克隆原版 ManageButton（外观和原版一致），
        // 模板不可用时退化成 Unity 标准 Button
        private GameObject AddButton(GameObject parent, string text, float width, System.Action onClick)
        {
            if (s_buttonTemplate != null)
            {
                GameObject go = UnityEngine.Object.Instantiate(s_buttonTemplate, parent.transform, false);
                go.name = "Btn_" + text;
                go.SetActive(true);
                KButton btn = go.GetComponent<KButton>();
                if (btn != null)
                {
                    btn.ClearOnClick();
                    btn.isInteractable = true;
                    // KButton.onClick 是 event，外部只能 += 绑定
                    btn.onClick += onClick;
                }
                LayoutElement le = go.GetComponent<LayoutElement>();
                if (le == null)
                {
                    le = go.AddComponent<LayoutElement>();
                }
                le.ignoreLayout = false;
                le.preferredWidth = width;
                le.minWidth = width;
                le.preferredHeight = 30f;
                le.minHeight = 30f;
                SetButtonText(go, text);
                return go;
            }
            return MakeUnityButton(parent, text, width, onClick);
        }

        // 创建按钮并忽略布局（绝对定位），宽高由参数指定，坐标由调用方设置
        private GameObject AddButtonAbsolute(GameObject parent, string text, float width, float height, System.Action onClick)
        {
            GameObject go = AddButton(parent, text, width, onClick);
            if (go == null)
            {
                return null;
            }
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.ignoreLayout = true;
                le.preferredHeight = height;
                le.minHeight = height;
            }
            RectTransform rt = go.transform as RectTransform;
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(width, height);
            }
            return go;
        }

        // 退化方案：Unity 标准 Button（深色块 + 文字 + 点击音效）
        private GameObject MakeUnityButton(GameObject parent, string text, float width, System.Action onClick)
        {
            GameObject go = new GameObject("Btn_" + text);
            go.transform.SetParent(parent.transform, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = 30f;
            le.minHeight = 30f;
            Color bg = new Color(0.26f, 0.28f, 0.34f);
            Image img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = MakeColors(bg, Lighten(bg), Darken(bg));
            btn.onClick.AddListener(delegate() { PlayClick(); if (onClick != null) onClick(); });
            MakeText(go, text, 13f, Color.white, TextAlignmentOptions.Center);
            return go;
        }

        // 改按钮文字（兼容克隆的 KButton 和退化的 Unity Button）
        private void SetButtonText(GameObject btnGo, string text)
        {
            if (btnGo == null)
            {
                return;
            }
            LocText lt = btnGo.GetComponentInChildren<LocText>(true);
            if (lt != null)
            {
                lt.key = "";
                if (Localization.FontAsset != null)
                {
                    lt.font = Localization.FontAsset;
                }
                lt.text = text;
                return;
            }
            TextMeshProUGUI tmp = btnGo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.text = text;
            }
        }

        private void SetButtonInteractable(GameObject btnGo, bool interactable)
        {
            if (btnGo == null)
            {
                return;
            }
            KButton kb = btnGo.GetComponent<KButton>();
            if (kb != null)
            {
                kb.isInteractable = interactable;
            }
            Button ub = btnGo.GetComponent<Button>();
            if (ub != null)
            {
                ub.interactable = interactable;
            }
        }

        // 条目内的灰色小字（来源/备注）
        private TextMeshProUGUI MakeRowLabel(GameObject parent, string text, float width, Color color)
        {
            TextMeshProUGUI tmp = MakeText(parent, text, 11f, color, TextAlignmentOptions.Left);
            LayoutElement le = tmp.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        // 创建文本（自动加 LayoutElement 供父级布局控制）
        private TextMeshProUGUI MakeText(GameObject parent, string text, float fontSize, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent.transform, false);
            // LayoutElement 必须在加文字组件之前就挂上——TMP 需要它报尺寸
            go.AddComponent<LayoutElement>();
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (Localization.FontAsset != null)
            {
                tmp.font = Localization.FontAsset;
            }
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        // 创建输入框（深色底 + 文本 + 占位文字）
        private TMP_InputField MakeInput(GameObject parent, float width, string placeholder)
        {
            GameObject go = new GameObject("Input");
            go.transform.SetParent(parent.transform, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = 32f;
            le.minHeight = 32f;
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.17f, 0.22f, 1f);
            img.raycastTarget = true;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRT = textGo.AddComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0f, 0f);
            textRT.anchorMax = new Vector2(1f, 1f);
            textRT.offsetMin = new Vector2(6f, 2f);
            textRT.offsetMax = new Vector2(-6f, -2f);
            TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
            if (Localization.FontAsset != null)
            {
                tmp.font = Localization.FontAsset;
            }
            tmp.fontSize = 14f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableWordWrapping = false;

            GameObject phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            RectTransform phRT = phGo.AddComponent<RectTransform>();
            phRT.anchorMin = new Vector2(0f, 0f);
            phRT.anchorMax = new Vector2(1f, 1f);
            phRT.offsetMin = new Vector2(6f, 2f);
            phRT.offsetMax = new Vector2(-6f, -2f);
            TextMeshProUGUI phTmp = phGo.AddComponent<TextMeshProUGUI>();
            if (Localization.FontAsset != null)
            {
                phTmp.font = Localization.FontAsset;
            }
            phTmp.text = placeholder;
            phTmp.fontSize = 13f;
            phTmp.color = new Color(0.55f, 0.57f, 0.62f, 1f);
            phTmp.alignment = TextAlignmentOptions.Left;
            phTmp.enableWordWrapping = false;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textComponent = tmp;
            field.placeholder = phTmp;
            field.characterLimit = 40;
            return field;
        }

        // 创建勾选开关（Unity 标准 Toggle，选中变绿；不能用 Klei 的 MultiToggle，
        // 纯代码创建它没初始化全，ChangeState 会 NullReferenceException）
        private Toggle MakeToggle(GameObject parent)
        {
            GameObject go = new GameObject("Toggle");
            go.transform.SetParent(parent.transform, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 30f;
            le.minWidth = 30f;
            le.preferredHeight = 30f;
            le.minHeight = 30f;
            Image img = go.AddComponent<Image>();
            img.raycastTarget = true;
            Toggle tg = go.AddComponent<Toggle>();
            tg.targetGraphic = img;
            tg.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = tg.colors;
            cb.normalColor = new Color(0.32f, 0.32f, 0.36f, 1f);
            cb.highlightedColor = new Color(0.4f, 0.42f, 0.48f, 1f);
            cb.pressedColor = new Color(0.2f, 0.2f, 0.24f, 1f);
            cb.selectedColor = new Color(0.3f, 0.62f, 0.38f, 1f);
            cb.disabledColor = new Color(0.15f, 0.15f, 0.17f, 1f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.1f;
            tg.colors = cb;
            tg.isOn = false;
            return tg;
        }

        // 按钮颜色配置（退化按钮用）
        private ColorBlock MakeColors(Color normal, Color highlight, Color pressed)
        {
            ColorBlock cb = new ColorBlock();
            cb.normalColor = normal;
            cb.highlightedColor = highlight;
            cb.pressedColor = pressed;
            cb.selectedColor = normal;
            cb.disabledColor = new Color(0.15f, 0.15f, 0.17f, 1f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.1f;
            return cb;
        }

        private Color Lighten(Color c)
        {
            return new Color(Mathf.Min(c.r + 0.08f, 1f), Mathf.Min(c.g + 0.08f, 1f), Mathf.Min(c.b + 0.08f, 1f), c.a);
        }

        private Color Darken(Color c)
        {
            return new Color(Mathf.Max(c.r - 0.08f, 0f), Mathf.Max(c.g - 0.08f, 0f), Mathf.Max(c.b - 0.08f, 0f), c.a);
        }

        private void PlayClick()
        {
            try
            {
                KMonoBehaviour.PlaySound(GlobalAssets.GetSound("HUD_Click"));
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 播放音效失败: " + e);
            }
        }
    }
}

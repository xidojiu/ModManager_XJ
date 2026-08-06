using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ModManager_XJ
{
    // 入口 patch：在原版 mods 列表界面（ModsScreen）注入"高级管理"按钮，点击后打开 ModManagerScreen
    // 注意：这里不写 [HarmonyPatch] 属性，由 OnLoad 里显式 harmony.Patch 注册（见 ModManager_XJ.cs）
    public class ModManagerEntry
    {
        private static ModsScreen s_modsScreen;

        // 刷新原版 ModsScreen（关掉 ModManager_XJ 后调用，让排序变化实时显示）
        public static void RefreshModsScreen()
        {
            try
            {
                if (s_modsScreen == null)
                {
                    Debug.Log("[MM] 热加载: ModsScreen 实例为空，跳过刷新");
                    return;
                }
                System.Reflection.MethodInfo buildDisplay = typeof(ModsScreen).GetMethod("BuildDisplay",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (buildDisplay != null)
                {
                    buildDisplay.Invoke(s_modsScreen, null);
                    Debug.Log("[MM] 热加载: 已刷新原版 ModsScreen");
                }
                else
                {
                    Debug.LogWarning("[MM] 热加载: 找不到 ModsScreen.BuildDisplay 方法");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[MM] 热加载 刷新失败: " + e);
            }
        }

        // OnActivate 可能被多次调用；不依赖静态标记，
        // 每次先确认按钮是否还挂在界面上（防 ModsScreen 被销毁重建后按钮丢失）
        public static void Postfix(ModsScreen __instance)
        {
            s_modsScreen = __instance;
            try
            {
                if (__instance == null)
                {
                    Debug.LogError("[MM] ModsScreen 实例为空，无法注入高级管理按钮");
                    return;
                }

                // 按钮已经在界面上就直接跳过（重复打开 ModsScreen 不会重复注入）
                if (__instance.transform.Find("AdvancedManageButton") != null)
                {
                    return;
                }

                // 1. 找一个"自带文字子物体"的原版按钮作为模板
                //    （克隆的按钮 GameObject 本体上是 KImage，不能再 AddComponent 文字组件，
                //     所以必须克隆一个文字在子物体上的按钮，克隆后直接改子物体文字即可）
                //    优先找顶部栏按钮（名字带 toggle/workshop），找不到再退而求其次
                KButton[] buttons = __instance.GetComponentsInChildren<KButton>(true);
                KButton template = null;
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] != null && buttons[i].GetComponentInChildren<LocText>(true) != null)
                    {
                        string n = buttons[i].name.ToLower();
                        if (n.Contains("toggle") || n.Contains("workshop"))
                        {
                            template = buttons[i];
                            break;
                        }
                    }
                }
                if (template == null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (buttons[i] != null && buttons[i].GetComponentInChildren<LocText>(true) != null)
                        {
                            template = buttons[i];
                            break;
                        }
                    }
                }
                if (template == null)
                {
                    Debug.LogError("[MM] 未找到带文字的原版按钮模板，无法注入高级管理按钮");
                    return;
                }

                // 2. 克隆模板按钮（IL2CPP 下克隆原版按钮是安全的，不能 new Canvas）
                KButton button = UnityEngine.Object.Instantiate<KButton>(template, template.transform.parent);
                button.name = "AdvancedManageButton";
                button.gameObject.SetActive(true);

                // 3. 清空原按钮的点击行为（KButton.onClick 是 event，外部不能整体赋值，
                //    用 KButton 自带的 ClearOnClick 整体清空，确保不触发原按钮逻辑）
                button.ClearOnClick();

                // 4. 不手动挪位置、不脱离布局：直接排到父容器最后一位，
                //    如果父容器是布局组件会自动排好位置（Mod 更新器的按钮也是这么加的）。
                //    手动改 anchoredPosition/ignoreLayout 反而会把按钮挪出可视区域（踩过坑）
                button.transform.SetAsLastSibling();

                // 5. 改文字（克隆按钮的文字在子物体上，直接改它；克隆到的不可能为空，
                //    但万一为空就放弃注入，避免空引用崩掉）
                LocText label = button.GetComponentInChildren<LocText>(true);
                if (label == null)
                {
                    Debug.LogError("[MM] 克隆按钮上没有文字组件，放弃注入");
                    UnityEngine.Object.Destroy(button.gameObject);
                    return;
                }
                // 清掉 key，避免 LocText Awake 时用原按钮的 key 覆盖我们的文字
                label.key = "";
                if (Localization.FontAsset != null)
                {
                    label.font = Localization.FontAsset;
                }
                label.text = ModStrings.AdvancedManage;

                // 6. ToolTip
                ToolTip tip = button.GetComponent<ToolTip>();
                if (tip == null)
                {
                    tip = button.gameObject.AddComponent<ToolTip>();
                }
                tip.toolTip = ModStrings.AdvancedManageTooltip;

                // 7. 绑定点击：打开 ModManagerScreen（Open 是 public static void，签名匹配 System.Action）
                button.onClick += ModManagerScreen.Open;

                Debug.Log("[MM] 已注入高级管理按钮");
            }
            catch (Exception e)
            {
                Debug.LogError("[MM] 注入高级管理按钮失败: " + e);
            }
        }
    }
}

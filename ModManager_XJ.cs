using System.Reflection;
using HarmonyLib;
using KMod;
using UnityEngine;

namespace ModManager_XJ
{
    public class ModManager_XJMod : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            Debug.Log("[MM] ModManager_XJ loading...");
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
    }
}

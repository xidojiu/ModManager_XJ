# Mod 管理器（ModManager_XJ）

缺氧（Oxygen Not Included）游戏内 mod 管理器。

## 它能干什么

- **一键转本地**：把已下载的创意工坊 mod 一键复制成本地 mod。复制之后，Steam 更新那个 mod 也不会覆盖你的改动。
- **给 mod 加备注**：本地 mod 可以写回标题（`原标题[备注]`），Steam mod 的备注存在工具自己的数据文件里，重启游戏都不丢。
- **搜索过滤**：界面顶部有搜索框，输入关键词后两栏列表实时过滤，按标题或备注匹配。
- **两栏列表**：自动把「已启用」和「未启用」的 mod 分成两栏，一眼看清。
- **排序**：已启用的 mod 可以用「上移 / 下移」按钮调整加载顺序（游戏启动时按这个顺序加载）。
- **预设（整合包）**：把「哪些 mod 启用 + 什么顺序」保存成一个预设，之后一键恢复。换整合包、备份配置都方便。

## 怎么用

1. 把 mod 放进 `mods\Local\ModManager_XJ\`（或从创意工坊订阅后由游戏加载）。
2. 启动游戏，主菜单 → Mods（模组）。
3. 列表窗口右上角会出现「**高级管理**」按钮，点它打开管理器。
4. 每条 mod 有按钮：
   - **启用 / 禁用**：切换后自动换栏
   - **备注**：填备注，本地 mod 可勾选「写回标题」
   - **转本地**（仅 Steam mod）：一键复制成本地 mod，原 Steam mod 自动禁用
   - **上 / 下**：调整已启用栏里的顺序
5. 底部预设区：输入名字 → 保存当前全部状态；选已有预设 → 应用 / 删除。

## 数据存在哪

- 工具自己的数据目录：`游戏存档目录\OniModManager\`
  - `notes.json`：所有 mod 的备注（Steam mod 只存这里；本地 mod 写回标题时也会同步存一份）
  - `presets\<预设名>.json`：保存的预设
- 本地 mod 的备注写回时，直接改它自己的 `mod.yaml` 标题。

## 目录结构（源码）

```
F:\t\ModManager_XJ\
├── ModManager_XJ.cs      入口：UserMod2.OnLoad，注册所有 patch
├── ModManagerStore.cs    数据层：读写备注、预设（不碰 UI）
├── ModManagerActions.cs  操作层：转本地、备注、开关、预设（不碰 UI）
├── ModManagerScreen.cs   UI 层：管理器界面（两栏、按钮、弹窗）
├── ModManagerEntry.cs    入口 patch：往原版 Mods 列表注入"高级管理"按钮
├── build.ps1             一键编译 + 部署到本地 mod 目录
├── mod.yaml / mod_info.yaml    mod 信息
└── README.md
```

## 怎么自己改 / 继续开发

三个层是分开的，加功能时按层改：

- **改界面**：`ModManagerScreen.cs`。每行一个条目、预设区、备注弹窗都在这里。
- **改逻辑**：`ModManagerActions.cs`。比如加「批量转本地」：在这里写个方法，返回 `ActionResult`（成功/失败 + 中文提示），UI 层调用就行。
- **改数据**：`ModManagerStore.cs`。想存新东西（比如 mod 冲突检测结果），在这里加读/写方法。

### 编译

装好游戏后直接跑：

```powershell
powershell -ExecutionPolicy Bypass -File F:\t\ModManager_XJ\build.ps1
```

编译产物会自动部署到 `mods\Local\ModManager_XJ\ModManager.dll`，重启游戏生效。

### 注意事项

- 用旧版 C# 5 语法写（游戏用的编译器不支持新语法：不能用 `$""`、`?.`、元组）。
- 所有日志带 `[MM]` 前缀，出问题去 `Player.log` 里搜 `[MM]`。
- 所有 mod 写入最终都走 `Global.Instance.modManager.Save()`，和原版 mod 列表完全兼容。

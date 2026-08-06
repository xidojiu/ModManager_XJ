$MANAGED = "F:\SteamLibrary\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed"
$CSC = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$SRC = "F:\t\ModManager_XJ"
$DEPLOY = "C:\Users\Administrator\Documents\Klei\OxygenNotIncluded\mods\Local\ModManager_XJ"
$OUT = "$DEPLOY\ModManager.dll"
$PLIB = "C:\Users\Administrator\Documents\Klei\OxygenNotIncluded\mods\Local\DisasterPlanet_XJ\PLib.dll"

# 确保部署目录存在
if (-not (Test-Path $DEPLOY)) { New-Item -ItemType Directory -Path $DEPLOY -Force | Out-Null }

& $CSC -target:library -out:$OUT `
  -reference:"$MANAGED\netstandard.dll" `
  -reference:"$MANAGED\0Harmony.dll" `
  -reference:"$MANAGED\Assembly-CSharp.dll" `
  -reference:"$MANAGED\Assembly-CSharp-firstpass.dll" `
  -reference:"$MANAGED\UnityEngine.dll" `
  -reference:"$MANAGED\UnityEngine.CoreModule.dll" `
  -reference:"$MANAGED\UnityEngine.UI.dll" `
  -reference:"$MANAGED\UnityEngine.UIModule.dll" `
  -reference:"$MANAGED\UnityEngine.TextRenderingModule.dll" `
  -reference:"$MANAGED\UnityEngine.InputLegacyModule.dll" `
  -reference:"$MANAGED\Unity.TextMeshPro.dll" `
  -reference:"$MANAGED\Newtonsoft.Json.dll" `
  -reference:"$PLIB" `
  "$SRC\ModStrings.cs" `
  "$SRC\ModManager_XJ.cs" `
  "$SRC\ModManagerStore.cs" `
  "$SRC\ModManagerActions.cs" `
  "$SRC\ModManagerScreen.cs" `
  "$SRC\ModManagerEntry.cs"

if ($LASTEXITCODE -ne 0) { Write-Host "编译失败，退出码 $LASTEXITCODE"; exit $LASTEXITCODE }

# 复制配置文件到部署目录（游戏加载 Local mod 必需）
# 用字节方式复制，绕过 PowerShell cmdlet 的路径限制
$files = @("mod.yaml", "mod_info.yaml")
foreach ($f in $files) {
    $src = Join-Path $SRC $f
    $dst = Join-Path $DEPLOY $f
    [System.IO.File]::WriteAllBytes($dst, [System.IO.File]::ReadAllBytes($src))
}

# 复制 strings 目录（.po 本地化文件）
$stringsSrc = Join-Path $SRC "strings"
$stringsDst = Join-Path $DEPLOY "strings"
if (Test-Path $stringsSrc) {
    if (-not (Test-Path $stringsDst)) { New-Item -ItemType Directory -Path $stringsDst -Force | Out-Null }
    foreach ($poFile in [System.IO.Directory]::GetFiles($stringsSrc, "*.po")) {
        $dstFile = Join-Path $stringsDst ([System.IO.Path]::GetFileName($poFile))
        [System.IO.File]::WriteAllBytes($dstFile, [System.IO.File]::ReadAllBytes($poFile))
    }
}

# 复制 PLib.dll（ModManager_XJ 依赖 PeterHan.PLib.Options）
$plibSrc = "C:\Users\Administrator\Documents\Klei\OxygenNotIncluded\mods\Local\DisasterPlanet_XJ\PLib.dll"
$plibDst = Join-Path $DEPLOY "PLib.dll"
if ([System.IO.File]::Exists($plibSrc)) {
    [System.IO.File]::WriteAllBytes($plibDst, [System.IO.File]::ReadAllBytes($plibSrc))
    Write-Host "已复制 PLib.dll"
}

Write-Host "编译成功，已部署到 $DEPLOY"

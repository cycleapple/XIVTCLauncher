# XIVTCLauncher (FFXIVSimpleLauncher)

[English](README_en.md) | [繁體中文](README.md)

專為 Final Fantasy XIV 台灣版設計的快速啟動器，支援**自動下載 Dalamud**，靈感來自 [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher)。

## 功能特色

- **快速登入** - 儲存帳號密碼，一鍵登入遊戲
- **OTP 支援** - 手動輸入或**自動 OTP**（自動產生驗證碼，不用再開手機 App！）
- **網頁登入** - 內建 WebView2 瀏覽器進行網頁驗證
- **自動下載 Dalamud** - 自動從 [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud) 下載並更新 Dalamud
- **自動下載 .NET Runtime** - 自動從 NuGet 下載 .NET 9.0 Runtime（與 XIVLauncherCN 相同）
- **繁體中文介面** - 採用 Material Design 設計風格

## 系統需求

- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（啟動器本身需要）
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
- 已安裝 Final Fantasy XIV 台灣版

> 注意：Dalamud 所需的 .NET 9.0 Runtime 會自動下載，無需手動安裝。

## 安裝方式

1. 從 [Releases](https://github.com/cycleapple/XIVTCLauncher/releases) 下載最新版本
2. 解壓縮到任意資料夾
3. 執行 `FFXIVSimpleLauncher.exe`
4. 在設定中配置遊戲路徑
5. 啟用 Dalamud（選用）- 會自動下載！

## 自動 OTP（一次性密碼）

不用再每次登入都開手機 App 了！啟動器可以自動幫你產生 OTP 驗證碼。

### 設定方式

1. 開啟**設定**
2. 勾選**啟用自動 OTP**
3. 輸入你的 OTP 密鑰（Base32 格式）
   - 可從 SE 帳號的 OTP 設定頁面取得
   - 通常顯示在 QR Code 下方
4. 點擊**儲存密鑰**
5. 完成！之後登入時會自動填入 OTP

### 功能特色

- **安全儲存** - 密鑰儲存於 Windows Credential Manager（非明文儲存）
- **自動更新** - 每 30 秒更新驗證碼，顯示倒數計時
- **自動填入** - 登入頁面自動填入 OTP 驗證碼
- **輕鬆清除** - 隨時可在設定中清除密鑰

### 手動 OTP

如果你不想儲存密鑰，也可以在登入頁面手動輸入 OTP。

## Dalamud 支援

### 自動模式（推薦）

啟動器可以自動下載和管理 Dalamud：

1. 開啟**設定**
2. 勾選**啟用 Dalamud**
3. 選擇**自動下載（推薦）**
4. 啟動遊戲 - 所有東西都會自動下載！

**會下載的內容：**
- Dalamud 從 [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud/releases)（約 31MB）
- .NET 9.0 Runtime 從 NuGet（約 70MB）
- Assets 從 [ottercorp](https://aonyx.ffxiv.wang)

**儲存位置：**
```
%AppData%\FFXIVSimpleLauncher\Dalamud\
├── Injector\     # Dalamud 檔案
├── Runtime\      # .NET 9.0 Runtime
├── Assets\       # Dalamud 資源
└── Config\       # 插件設定
```

### 手動模式

如果你想使用自己編譯的 Dalamud：

1. 從 [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud) 編譯或下載 Dalamud
2. 開啟**設定**
3. 勾選**啟用 Dalamud**
4. 選擇**手動指定路徑**
5. 瀏覽到你的 Dalamud 資料夾（包含 `Dalamud.Injector.exe`）

## 插件倉庫

由於 yanmucorp Dalamud 使用 API12，我們提供了相容版本的自訂插件倉庫：

**倉庫網址：**
```
https://raw.githubusercontent.com/cycleapple/DalamudPlugins-TW/main/repo.json
```

**可用插件：**

| 插件 | 版本 | 說明 |
|------|------|------|
| Penumbra | 1.2.0.0 | Mod 載入管理器 |
| Simple Tweaks | 1.10.10.0 | 生活品質改善 |
| Brio | 0.5.1.2 | 增強版 GPose 工具 |
| Glamourer | 1.4.0.1 | 外觀修改 |
| CustomizePlus | 2.0.7.22 | 角色自訂 |

**如何新增：**
1. 在遊戲中開啟 Dalamud 設定（`/xlsettings`）
2. 前往「實驗性」分頁
3. 將倉庫網址新增到「自訂插件倉庫」
4. 儲存並瀏覽插件安裝程式

更多插件請訪問：[DalamudPlugins-TW](https://github.com/cycleapple/DalamudPlugins-TW)

## 從原始碼編譯

```bash
# 複製專案
git clone https://github.com/cycleapple/XIVTCLauncher.git
cd XIVTCLauncher

# 編譯專案
dotnet build

# 執行應用程式
dotnet run

# 發佈版本
dotnet publish -c Release -r win-x64 --self-contained false
```

## 設定

設定檔儲存於 `%APPDATA%/FFXIVSimpleLauncher/settings.json`

### 遊戲路徑

台灣版 FFXIV 預設安裝路徑：
```
C:\Program Files\USERJOY GAMES\FINAL FANTASY XIV TC
```

## 免責聲明

XIVTCLauncher 的使用可能不符合遊戲的服務條款。我們盡力讓它對所有人都是安全的，據我們所知，目前沒有人因為使用 XIVTCLauncher 而遇到問題，但請注意這仍有可能發生。

**使用風險自負。**

### 插件使用準則

如果你使用 Dalamud 插件，請遵守以下準則：

- 不要使用以不公平方式自動化遊戲操作的插件
- 不要使用以未經授權方式與遊戲伺服器互動的插件
- 不要使用繞過任何遊戲限制或付費牆的插件

## 致謝

- [goatcorp/FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) - 原始靈感來源與 Dalamud 框架
- [goatcorp/Dalamud](https://github.com/goatcorp/Dalamud) - 插件框架
- [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud) - 中文客戶端 Dalamud 分支
- [ottercorp/FFXIVQuickLauncher](https://github.com/ottercorp/FFXIVQuickLauncher) - 中文客戶端啟動器與 Asset 伺服器
- [cycleapple/DalamudPlugins-TW](https://github.com/cycleapple/DalamudPlugins-TW) - 台灣客戶端插件倉庫
- [MaterialDesignInXAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI 工具包

## 法律聲明

XIVTCLauncher 與 SQUARE ENIX CO., LTD. 或 Gameflier International Corp. 無任何關聯或背書。

FINAL FANTASY 是 Square Enix Holdings Co., Ltd. 的註冊商標。

所有遊戲素材和商標均為其各自所有者的財產。

## 授權

本專案以原樣提供，僅供教育和個人使用。

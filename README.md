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

### 連線診斷

「設定 → 網路設定 → 連線診斷」可逐項檢查登入網頁、帳號服務、大廳、更新服務及 Dalamud／GitHub。每項最多等待 8 秒，支援取消、重新檢測及複製報告。報告顯示本機的單次回應耗時與檢測時間，不代表全體伺服器狀態或遊戲內延遲；TCP 可連線也不代表帳號登入或各個世界正常。下載類 HTTP 檢測使用設定視窗目前選擇的 DNS；TCP 檢測使用系統解析與直接連線。若未讀到本地遊戲版本，會標示版本檢查未檢測。

### 略過 Patch Server 檢查

啟動時會自動檢查遊戲更新。Patch Server 檢查失敗時，會再確認遊戲大廳連線；大廳可連且有本地版本時，主畫面提供「本次略過並登入」。此選擇僅用於該次登入嘗試，不寫入設定；取消或結束後清除，下次啟動仍正常檢查。已確認有更新時不提供這個選項。Patch Server 與大廳檢測各最多等待 8 秒，完整診斷仍可從設定手動執行。

更新伺服器無法連線時，可在「設定 → 進階設定」開啟「略過遊戲更新檢查（Patch Server）」。儲存後會略過啟動與登入時的 Patch Server 請求，保留正常帳號登入與本地更新包清理。此選項預設關閉，不代表遊戲已是最新版本；請確認遊戲已更新，伺服器恢復後關閉此選項。版本不相容或登入伺服器無法連線時仍可能無法進入遊戲。

### 下載 DNS

在「設定 → 進階設定 → 啟動器下載 DNS」選擇「系統預設」、Cloudflare（1.1.1.1）或 Google（8.8.8.8）。
儲存後重新開啟啟動器生效。一般使用者可先使用「GitHub 下載加速」檢測下載速度。

- 預設沿用系統 DNS 與 Proxy；舊版設定升級後維持此行為。
- Cloudflare／Google 使用 DNS over HTTPS，透過服務商 IP 建立連線，保留正常的 HTTPS 憑證驗證；不依賴系統 DNS 解析服務商名稱。
- 套用於啟動器更新、Dalamud、Assets、.NET Runtime 與遊戲更新下載。不修改 Windows DNS，不套用於登入網頁、登入 API、遊戲或遊戲內插件。
- 自訂 DNS 模式使用直接連線，不經系統 Proxy。需要 Proxy 時請選「系統預設」。解析失敗會回報錯誤，不會偷偷改用系統 DNS。
- DNS 無法保證改善路由壅塞；GitHub 下載速度可使用下方的「GitHub 下載加速」功能檢測。

### GitHub 下載加速

在「設定 → GitHub 下載加速」按「檢測下載速度」，會完整搜尋 Fastly 官方範圍內的候選位址，驗證 GitHub HTTPS，並與目前系統路線交錯測試三輪 Raw 與 Release 下載。
主畫面顯示目前與建議連線的速度比較；IP、每條路線成功次數及 Raw 回應時間收於進階詳情。候選路線必須各輪成功、至少 3 Mbps，且比穩定的目前路線快至少 25%，才會提供套用建議。

- 檢測時顯示動態進度條、目前步驟與已耗時。不修改設定，支援取消；完整檢測最多取樣約 30 MB 安裝包資料，另有 HTTPS 等連線開銷。
- 套用會要求管理員權限，並再次驗證位址屬於 Fastly 且能提供兩個 GitHub 網域的 HTTPS 內容。
- 只在 Windows hosts 寫入 XIVTCLauncher 標記區塊，指定 `raw.githubusercontent.com` 和 `release-assets.githubusercontent.com`。修改前保留 `hosts.xivtc-github.bak`；還原只移除自己的區塊，保留其他內容。
- 遇到其他工具設定相同網域時拒絕覆蓋，請先使用原工具還原。需要系統 Proxy 的網路不適合本功能的直接連線測試。
- 系統 hosts 可涵蓋遊戲內使用系統解析的 Dalamud；啟動器的自訂 DoH 對這兩個網域也會優先使用自己的 hosts 區塊。其他網域維持原 DNS 選擇。
- 套用或還原後請完整重開啟動器與遊戲。換網路或速度變慢時重新檢測；不會在每次登入自動掃描或修改。
- 不涵蓋 `github.com`、`api.github.com` 或其他插件作者的網站，不保證改善所有網路問題。

節點探索方式參考 [AFAlimru/fastly-hosts-adjust](https://github.com/AFAlimru/fastly-hosts-adjust)，MIT 授權聲明隨發布包附於 `licenses/fastly-hosts-adjust.txt`。

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

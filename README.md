# IcedSub

多源字幕搜索 / 下载 CLI（**.NET 10**，版本 `0.1.0`）。

- 默认命令为 **search**（直接跟查询词即可）
- **download** 默认下载全部匹配，**保留字幕源原名**（不解压压缩包）
- **name** 用 TMDB 按 IMDb id 生成中英双语文件名（电影 / 剧集）

## 字幕源

| Provider | 说明 | 启用条件 |
|---|---|---|
| **OpenSubtitles.com** | 国际库；对本地视频会算 **moviehash** | `OpenSubtitles:Enabled` + `ApiKey` |
| **ASSRT（伪射手）** | 中文友好；主站不可达时会尝试备用域名 | `Assrt:Enabled` + `Token` |

> 启用 ASSRT 时：字幕服务由 [assrt.net](https://assrt.net) 提供。

未配置任何源时会提示：`未配置任何字幕源（请设置 API Key / Token）`。启动时会打印：

```text
providers: OpenSubtitles=on|off  ASSRT=on|off
```

API 申请：

| 服务 | 地址 |
|---|---|
| OpenSubtitles | https://www.opensubtitles.com/en/consumers |
| ASSRT Token | https://assrt.net 用户面板 |
| TMDB（`name` 命令） | https://www.themoviedb.org/settings/api |

## 命令

| 用法 | 含义 |
|---|---|
| `icedsub <query>` | 等同 **search** |
| `icedsub search <query>` | 只搜索、列出候选 |
| `icedsub download <query>` | 搜索并下载（默认全部匹配） |
| `icedsub name <tt...>` | 生成规范目录名 `中文_English_(年份)_imdb-id_douban-id` |

`<query>` 可以是（可多个）：

| 类型 | 行为 |
|---|---|
| **关键字** | 文本搜索；无 moviehash |
| **视频文件** | 解析文件名 + 计算 OpenSubtitles moviehash；字幕默认下到视频同目录 |
| **目录** | 递归扫描已知视频扩展名后逐个处理 |

支持的视频扩展名（可用配置 `VideoExtensions` 覆盖）：

`.mkv` `.mp4` `.avi` `.mov` `.m4v` `.wmv` `.ts` `.m2ts` `.webm` `.mpeg` `.mpg` `.ogm` `.flv`

### 选项

| 选项 | 适用 | 说明 |
|---|---|---|
| `-o` / `--out` | search / download | 关键字查询时的输出目录；默认当前工作目录。视频文件仍优先下到视频所在目录 |
| `-l` / `--lang` | search / download | 语言优先级，如 `-l zh-CN en`；默认 `zh-CN zh-TW en` |
| `--top-only` | download | 只下载排名第一；默认下载全部 |
| `--overwrite` | download | 覆盖同名文件；默认冲突时改为 `文件名 (2).ext` |

## 示例

```powershell
# 开发运行（仓库根目录）
dotnet run --project src\IcedSub.Cli -- 火遮眼
dotnet run --project src\IcedSub.Cli -- search 火遮眼
dotnet run --project src\IcedSub.Cli -- download 火遮眼 -o D:\Subs
dotnet run --project src\IcedSub.Cli -- download "D:\Videos\movie.mkv"
dotnet run --project src\IcedSub.Cli -- download "D:\Videos" -l zh-CN --top-only
dotnet run --project src\IcedSub.Cli -- name tt33311069
```

发布后的 exe 用法相同：

```powershell
.\publish\win-x64\IcedSub.exe 火遮眼
.\publish\win-x64\IcedSub.exe download 火遮眼 -o D:\Subs
.\publish\win-x64\IcedSub.exe name tt33311069
```

### 下载与命名行为

- 搜索结果按语言优先级、格式、发行名相似度、下载量等打分排序，列表最多展示 **15** 条
- 下载文件名：**优先使用源站原名**；没有原名时才回退为 `视频名.语言.Provider-Id.ext`
- **不解压** zip / rar 等；原样保存
- 同名且未 `--overwrite`：自动 ` (2)`、` (3)`…；已存在时可能显示 `已存在，跳过`

### 命名模式（`name`）

模式（半角、下划线分段）：

```text
中文名_English_(年份)_imdb-ttid_douban-id
```

例：

```text
肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052
霸王别姬_Farewell My Concubine_(1993)_imdb-tt0106332_douban-1291546
```

- 年份用**半角括号**；id 带 `imdb-` / `douban-` 前缀
- 无中文时省略中文段；中英文相同时只留一段
- **不含导演**
- 标题/年份来自 **TMDB**（用 IMDb id 反查；电影取 `title`/`release_date`，剧集取 `name`/`first_air_date`）；**豆瓣 id 来自 Wikidata**（`P345` IMDb → `P4529` 豆瓣），查不到则省略 `_douban-…` 段
- `--douban <id>` 可手动指定豆瓣 id；只给豆瓣 id 时程序会先反查 IMDb id
- 配置 `Naming:TmdbApiKey` 或 `Naming:TmdbReadAccessToken`（Token 优先）；环境变量见下表

交互用法（默认，TTY 下）：

```text
> icedsub name
填空：IMDb id / 豆瓣 id 任填其一或都填（回车跳过）。
  IMDb id (tt...): tt0111161
  豆瓣 id: 1292052
正在查询元数据 ... 完成
逐项确认（回车接受反查值，输入新值覆盖，输入 - 清空）：
  中文名 [肖申克的救赎]:
  English [The Shawshank Redemption]:
  年份 [1994]:
  imdb-id [tt0111161]:
  douban-id [1292052]:

确认生成？[Y/n]:
目录名: 肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052
```

脚本用法（不走交互，直接输出）：

```powershell
icedsub name tt0111161 -y
icedsub name --douban 1292052 -y
```

> 输入被重定向（管道/CI）时自动进入脚本模式。

## 配置

### 加载顺序（后者覆盖前者）

1. 程序目录下的 `appsettings.json`（开发 / 发布输出旁）
2. 用户配置：`%AppData%\IcedSub\appsettings.json`
3. 环境变量（前缀 `ICEDSUB_`，以及下方显式密钥变量）

示例模板：`src\IcedSub.Cli\appsettings.example.json`

### `appsettings.json` 结构

```json
{
  "OpenSubtitles": {
    "Enabled": true,
    "ApiKey": "YOUR_OPENSUBTITLES_API_KEY",
    "Username": "optional_username",
    "Password": "optional_password",
    "UserAgent": "IcedSub v0.1",
    "BaseUrl": "https://api.opensubtitles.com/api/v1"
  },
  "Assrt": {
    "Enabled": true,
    "Token": "YOUR_ASSRT_TOKEN",
    "BaseUrl": "https://api.assrt.net",
    "AlternateBaseUrls": [
      "https://api.makedie.me",
      "http://api.assrt.net"
    ]
  },
  "Naming": {
    "TmdbApiKey": "YOUR_TMDB_API_KEY",
    "TmdbReadAccessToken": ""
  },
  "Defaults": {
    "Languages": [ "zh-CN", "zh-TW", "en" ],
    "PreferredFormats": [],
    "MaxResultsPerProvider": 15,
    "SkipIfExternalSrtExists": false,
    "DownloadMode": "All",
    "OverwriteExisting": false
  },
  "VideoExtensions": [
    ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv",
    ".ts", ".m2ts", ".webm", ".mpeg", ".mpg", ".ogm", ".flv"
  ]
}
```

| 配置项 | 说明 |
|---|---|
| `Defaults.Languages` | 默认语言优先级（可被 `-l` 覆盖） |
| `Defaults.PreferredFormats` | 可选格式偏好（仅影响排序 / OpenSubtitles 过滤）；空 = 不限 |
| `Defaults.MaxResultsPerProvider` | 每源最多结果（ASSRT 上限 15） |
| `Defaults.DownloadMode` | 配置默认值；CLI 的 download 实际由是否传 `--top-only` 决定（`All` / `Top`） |
| `Assrt.AlternateBaseUrls` | 主站失败时的备用 API 主机 |

### 环境变量

| 变量 | 对应配置 |
|---|---|
| `ICEDSUB_OPENSUBTITLES_APIKEY` | `OpenSubtitles:ApiKey` |
| `ICEDSUB_OPENSUBTITLES_USERNAME` | `OpenSubtitles:Username` |
| `ICEDSUB_OPENSUBTITLES_PASSWORD` | `OpenSubtitles:Password` |
| `ICEDSUB_ASSRT_TOKEN` | `Assrt:Token` |
| `ICEDSUB_TMDB_APIKEY` | `Naming:TmdbApiKey` |
| `ICEDSUB_TMDB_TOKEN` | `Naming:TmdbReadAccessToken` |

## 开发

```powershell
# 还原并编译
dotnet build IcedSub.slnx

# 运行测试
dotnet test IcedSub.slnx

# 运行 CLI
dotnet run --project src\IcedSub.Cli -- --help
```

### 快捷脚本

仓库根目录提供两个包装脚本，参数与退出码原样透传给 CLI：

```powershell
# run-debug：等价于 dotnet run --project src\IcedSub.Cli -- ...
.\run-debug.ps1 search 火遮眼

# run-release：运行 publish\win-x64\IcedSub.exe（不存在或源码更新时先自动 publish）
.\run-release.ps1 download "D:\Videos" -l zh-CN --top-only
```

## 如何 Publish（单文件 exe）

前置：安装 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

`IcedSub.Cli.csproj` 已默认：`PublishSingleFile`、`SelfContained`、原生库自解压、单文件压缩。

```powershell
# 推荐（Windows x64）
dotnet publish src\IcedSub.Cli\IcedSub.Cli.csproj `
  -c Release -r win-x64 `
  -p:DebugType=None -p:DebugSymbols=false `
  -o publish\win-x64
```

等价完整参数：

```powershell
dotnet publish src\IcedSub.Cli\IcedSub.Cli.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o publish\win-x64
```

| 项 | 说明 |
|---|---|
| 输出目录 | `publish\win-x64\`（已在 `.gitignore`） |
| 主程序 | `publish\win-x64\IcedSub.exe` |
| 配置 | 同目录可放 `appsettings.json`；也可使用 `%AppData%\IcedSub\` 或环境变量 |
| 运行时 | `win-x64` **自包含**，目标机无需预装 .NET |

其它 RID：

```powershell
dotnet publish src\IcedSub.Cli\IcedSub.Cli.csproj -c Release -r linux-x64 -o publish\linux-x64
dotnet publish src\IcedSub.Cli\IcedSub.Cli.csproj -c Release -r osx-arm64 -o publish\osx-arm64
```

## 项目结构

```text
IcedSub.slnx
src/IcedSub.Cli            # CLI 入口、配置加载、System.CommandLine
src/IcedSub.Core           # 扫描、哈希、打分、编排、命名
src/IcedSub.Providers      # OpenSubtitles、ASSRT
src/IcedSub.Metadata       # TMDB 双语命名
tests/IcedSub.Tests        # 单元测试
publish/win-x64              # 发布输出（gitignore）
```

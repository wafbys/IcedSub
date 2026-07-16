# IcedSubDL

多源字幕搜索 / 下载 CLI（.NET 10）。

## 源

| Provider | 说明 | 配置 |
|---|---|---|
| **OpenSubtitles.com** | 国际库，moviehash | `OpenSubtitles:ApiKey` |
| **ASSRT（伪射手）** | 中文友好 | `Assrt:Token` / `ICEDSUBDL_ASSRT_TOKEN` |

> 字幕服务由 [assrt.net](https://assrt.net) 提供（启用 ASSRT 时）

## 命令（简化）

| 用法 | 含义 |
|---|---|
| `icedsubdl <query>` | 等同 **search** |
| `icedsubdl search <query>` | 只搜索 |
| `icedsubdl download <query>` | 搜索并下载全部匹配（保留原名） |
| `icedsubdl name <query>` | **预留**（文件名正规化，未实现） |

`<query>` 可以是：

- 关键字：`火遮眼`
- 视频文件路径
- 目录（递归扫视频）

常用选项：

| 选项 | 说明 |
|---|---|
| `-o` / `--out` | 输出目录（关键字下载时） |
| `-l` / `--lang` | 语言优先级，如 `zh-CN en` |
| `--top-only` | 只下排名第一（默认全部） |
| `--overwrite` | 覆盖同名文件 |

## 示例

```powershell
cd C:\Users\YF\WAFBYS\Src\IcedSubDL

dotnet run --project src\IcedSubDL.Cli -- 火遮眼
dotnet run --project src\IcedSubDL.Cli -- search 火遮眼
dotnet run --project src\IcedSubDL.Cli -- download 火遮眼 -o D:\Subs
dotnet run --project src\IcedSubDL.Cli -- download "D:\Videos\movie.mkv"
dotnet run --project src\IcedSubDL.Cli -- name "D:\Videos"   # 预留
```

下载文件名：**保留字幕源原名**（不解压压缩包）。同名冲突时自动加 ` (2)`。

## 配置

```text
%AppData%\IcedSubDL\appsettings.json
ICEDSUBDL_ASSRT_TOKEN
ICEDSUBDL_OPENSUBTITLES_APIKEY
```

示例：`src\IcedSubDL.Cli\appsettings.example.json`

## 发布单文件 exe（Windows x64）

```powershell
cd C:\Users\YF\WAFBYS\Src\IcedSubDL
dotnet publish src\IcedSubDL.Cli\IcedSubDL.Cli.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o publish\win-x64
```

产物：`publish\win-x64\IcedSubDL.exe`（自包含，无需安装 .NET）

```powershell
.\publish\win-x64\IcedSubDL.exe 火遮眼
.\publish\win-x64\IcedSubDL.exe download 火遮眼 -o D:\Subs
```

## 项目结构

```text
src/IcedSubDL.Cli
src/IcedSubDL.Core
src/IcedSubDL.Providers      # OpenSubtitles, ASSRT
src/IcedSubDL.Metadata       # name 预留
tests/IcedSubDL.Tests
publish/win-x64              # 发布输出（gitignore）
```

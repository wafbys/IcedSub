# 产品规格（SPEC · 草案）

> 本文描述**目标形态**，不代表当前实现。决策与默认值见 `DECISIONS.md`。
> 状态：草案（2026-10-07）。未定项以 `待议` 标注。

---

## 1. 定位

一个**便携版命令行媒体库工具**：按目录收集影视剧/纪录片，目录名即身份；在目录内维护多个发行版；并为视频获取**能与该视频对齐**的字幕。

### 目标（Goals）
- 目录化媒体库：一目录 = 一作品（电影）或一剧（剧集）。
- 规范目录名，携带 IMDb id。
- 目录内允许多发行版/多编码，用文件名区分。
- 为缺中字的视频获取可用字幕，并尽量保证时间轴对齐。
- 单文件 exe + 便携旁置配置。

### 非目标（Non-Goals）
- 不做海报/剧照/简介等媒体库元数据展示（即不做完整“刮削器”）。
- 不做转码/压制。
- 不做 BT/下载器集成。
- 一期不做音频级自动同步（见 D9）。

---

## 2. 术语表（GLOSSARY）

| 术语 | 含义 |
|---|---|
| **身份（Identity）** | 一个作品在外部数据库中的唯一标识（imdb/tmdb id）。 |
| **发行版（Release）** | 同一作品的不同片源/编码/剪辑（WEB-DL、BluRay、REMUX…）。 |
| **探测（Probe）** | 用 ffprobe/MediaInfo 读取视频技术事实（时长/帧率/分辨率/内嵌字幕）。 |
| **内嵌字幕（Embedded）** | 封装在容器内的字幕轨（mkv 内封），非外挂文件。 |
| **外挂字幕（External/Sidecar）** | 与视频同目录、同名的独立字幕文件。 |
| **同步（Sync）** | 修正字幕时间轴（帧率校正 / 音频对齐）。 |
| **刮削（Scraping）** | 抓取元数据（海报/简介/NFO）以组织媒体库；本项目**不做**。 |
| **便携版（Portable）** | 单文件、配置随程序、不写 `%AppData%`/注册表。 |
| **ADR** | 架构决策记录（见 `DECISIONS.md`）。 |

---

## 3. 库结构规范（草案）

```
<Library>/
  肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052/   ← 电影：一目录一片
      The.Shawshank.Redemption.1994.2160p.WEB-DL.HDR.x265【基准版本】[KEEP].mkv
      The.Shawshank.Redemption.1994.1080p.BluRay.x264【不保留】[DROP].mkv
      The.Shawshank.Redemption.1994.2160p.WEB-DL.HDR.x265.zh-CN.srt           ← 只给 KEEP 配
  剧名_Show Name_(2019)_imdb-tt1234567_douban-xxxxxxx/                          ← 剧集：一剧一目录
      Show.Name.S01E01.1080p.WEB-DL.mkv
      Show.Name.S01E01.1080p.WEB-DL.zh-CN.srt
```

### 3.1 目录名（2026-10-08 更新，见 D12）
半角、下划线分段：

```
中文名_English_(年份)_imdb-ttid_douban-id
```

例：

```
肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052
霸王别姬_Farewell My Concubine_(1993)_imdb-tt0106332_douban-1291546
```

- 分段用 `_`；年份用**半角括号**；id 带 `imdb-` / `douban-` 前缀。
- **不含导演**（导演等信息以后放元数据/NFO）。
- 无中文 → 省略中文段；中英相同 → 只留一段。
- 豆瓣 id 来自 Wikidata `P4529`（`P345` 即 IMDb 反查）；查不到则省略 `_douban-…` 段。
- 标题/年份优先 TMDB（电影 `title`/`release_date`，剧集 `name`/`first_air_date`）；未配置 TMDB 或未收录时用 Wikidata 兜底（`zh`/`en` 标签 + `P577` 最早年份）。
- 剧集同形式，年份取首播年（`tv` 分支已实现）。
- 无 tt 兜底：`…_(年份)_tmdb-<id>` 或 `标题_年份`（待议）。

### 3.2 视频文件名
- 保留发行版信息（分辨率/来源/编码/HDR/音轨/组），由发布者命名。
- `rank_videos.py` 的状态后缀（【基准版本】[KEEP] / 【不保留】[DROP]）为发行版选择的权威标记。

### 3.3 字幕文件名
- 外挂字幕：`<视频文件名不含扩展名>.<lang>.<ext>`，`lang` 用 `zh-CN` / `zh-TW` / `en`。
- 一字幕对一视频；不跨发行版共用。

### 3.4 副产物
- 可选：`*.nfo` / 元数据缓存（待议，一期不做）。

---

## 4. 处理管线（目标形态）

```
输入：关键字 / 文件 / 目录 / 库根
  │
  ├─ 身份解析     目录名(tt) > 文件名(tt) > TMDB 反查   → Identity{imdb,tmdb,type,year,S/E}
  ├─ 探测(可选)   ffprobe → {duration, fps, resolution, embeddedSubs}
  │
  ├─ 检索         Parallel: OpenSubtitles(hash+id+year+type) · ASSRT(keyword)
  ├─ 归一化去重   统一语言/格式；标注 match_type: exact-hash | id-based | release | title-only
  ├─ 打分         相关性 × 置信度；id/年份不符直接排除；hash 命中置顶
  ├─ 交付前验证    时长/末时间戳比对；fps 推断 → 置信度 + 告警
  ├─ 解压/转码    解出 srt；编码探测转 UTF-8（治中文乱码）
  ├─ 同步(可选)    一期 fps 线性校正；二期 alass/ffsubsync
  └─ 落盘         <视频名>.<lang>.srt（若内嵌首选语言字幕且非 --force，则跳过）
```

---

## 5. 命令面（草案）

| 命令 | 作用 | 说明 |
|---|---|---|
| `name [tt…]` | 填空交互 → 规范目录名 | 输入 tt/豆瓣 id → 反查 → 逐项确认/修改 → 输出；`--douban`、`-y` 直接输出；支持电影 / 剧集；`--print`、`--mkdir` 待补 |
| `search <query…>` | 只搜索列候选 | 现有 |
| `download <query…> [-o dir]` | 搜索并下载 | 落盘命名改为 `<视频名>.<lang>.<ext>`；默认只下最优 |
| `rank`（待议） | 目录内发行版排序 | 现阶段仍由 `rank_videos.py` 承担 |
| `scan`（待议） | 扫描库目录，按目录身份批量处理 | 对应“目录收集 + 批量配字幕” |

全局约定：
- 默认命令仍为 `search`。
- 缺 ffprobe 时自动降级并提示。
- 提供 `--json` 与有意义的退出码（供脚本/自动化）。

---

## 6. 依赖与降级

| 依赖 | 必需性 | 缺失时的降级 |
|---|---|---|
| .NET 运行时 | 无（自包含） | — |
| `ffprobe` | 可选 | 无时长/帧率/内嵌检测；跳过校验与 fps 校正 |
| `ffmpeg` | 可选 | 无法做音频级同步（二期） |
| `MediaInfo` | 可选 | HDR/音轨细节降级为 ffprobe 推断 |

发现顺序（参考 `rank_videos.py`）：显式参数 → 环境变量 → PATH → Windows winget 安装目录 → 常见安装目录。

---

## 7. 分期路线（草案）

- **一期（先把骨架做对）**
  1. `name` 补剧集（已完成）+ 身份解析抽公共能力。
  2. 字幕落盘命名对齐视频；默认只下最优。
  3. 目录名解析出 tt 驱动检索；OS 传 `imdb_id/year/type`，读 `moviehash_match`。
  4. 解压 + 编码转 UTF-8。
  5. 内嵌字幕检测（有则跳过）。
- **二期（对齐质量）**
  6. 时长校验 + fps 线性校正。
  7. `MediaProbe` 服务化，供多处复用。
- **三期（可选增强）**
  8. 音频级自动同步（集成外部工具）。
  9. `scan` 批量库处理；`rank` 子命令。

---

## 8. 验收标准（草案）

- 给定一个规范目录名的电影目录，`download` 能仅凭目录名中的 `tt` 检索到正确作品的字幕。
- 落盘字幕能被 VLC/mpv 自动挂载（文件名与视频对齐）。
- 对同一视频，若已有首选语言内嵌字幕，默认不重复下载。
- 帧率不符（如 23.976 源配 25 字幕）时，能自动校正或明确告警。
- 缺 ffprobe 时，主流程仍可完成搜索与下载，仅提示能力降级。
- 所有失败场景有非零退出码。

---

## 9. 与其它文档的关系

- 决策与默认值：`DECISIONS.md`（冲突时以决策文件为准）。
- 现状评估：见对话/README；本 SPEC 描述目标形态。

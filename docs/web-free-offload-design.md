# 构想评估与插件设计：任务卸载到"网页免费版" + md 交接，能否节省 token？

> 评估对象：把当前对话中的"一般性推导任务 / 简单任务"发送到网页免费版对话（如 chat.deepseek.com、ChatGPT 免费版），将网页回复汇总为一个 md 文件，本地桌面端（DSH）阅读该 md 后继续运行，以达到节省 token（API 费用）的目的。
>
> 结论先行：**能达到节省 API token 费用的目的，但有严格的前提条件。** 在"回复可信或可执行验证"的机械任务上，边际节省约 85–90%；在"必须保证正确性的一般性推导"上，验证成本会吃掉大部分节省。"网页免费版"这个载体是最大风险点（ToS、验证码/PoW、限流、延迟），建议把载体换成"免费/低价 API 端点或本地小模型"，md 交接部分保留——收益相同甚至更大，风险低一个量级。

---

## 1. 构想还原

```
用户提问
   │
   ▼
DSH 桌面端（付费 reasoner 模型）
   │  ① 分类：这是"简单任务/一般性推导"？
   │  ② 调用 offload 工具
   ▼
网页免费版对话（浏览器自动化：Playwright 等）
   │  ③ 粘贴任务（含必要上下文）
   │  ④ 等待流式回复完成，抓取最终消息
   ▼
   │  ⑤ 汇总为 md 文件（写到 workspace）
   ▼
DSH 读 md 文件（作为工具结果/上下文注入）
   │  ⑥ 按 md 内容执行 / 组装回复
   ▼
回复用户
```

关键假设（本文按此设计，若不同请调整）：
- 桌面端是 **DSH**（DeepSeek Harness），付费 API 是 DeepSeek 官方 API（reasoner 为贵模型）。
- "网页免费版"指 chat.deepseek.com 这类网页对话（免费、无 API key），不是免费 API 额度。

---

## 2. 在 DSH 里能不能实现？——能，机制都齐了

基于对 DSH 插件体系的核查（`@deepseek-ai/dsh` 及 `node_modules` 内各包）：

| 需求 | DSH 现有机制 | 结论 |
|---|---|---|
| 注册一个新工具给模型 | 插件 = pnpm 包声明 `dsh.bundle.patch`，以 cordis 组合把服务注册进 host `tools` registry（如 `dsh-tool-web`、`dsh-tool-pwsh` 的写法） | ✅ |
| 工具内发 HTTP / 操控浏览器 | 工具执行器跑在 Node 进程内，可 `fetch`、可 spawn 进程（Playwright） | ✅ |
| 写 md 到工作区 | `ctx.fs` / `dsh-fs` 文件服务 | ✅ |
| 让模型读 md | 现有 fs 工具即可（`read`） | ✅ |
| 换个更便宜的模型做杂活 | `ctx.llm` 缝（`dsh-llm`/`dsh-llm-deepseek` 注册 provider 路由，model 字符串透传） | ✅ |
| 现有 token 压缩 | `dsh-compaction-basic`、`tool-result-pruner`、`spill` | ✅（md 交接与它同思路，见 §6） |

社区先例也证明网页版自动化可行但**有摩擦**：已有 [deepseek-free-api](https://github.com/Fly143/deepseek-free-api)（把 chat.deepseek.com 转成 OpenAI 兼容 API，需处理 **PoW 自动求解**、token 刷新）、[Playwright 自动化接入指南](https://blog.csdn.net/aocaiti5781/article/details/101458920) 等。即"能做，但不省心"。

---

## 3. 三个关键问题（决定构想成败）

### 3.1 验证成本问题 —— 对"一般性推导任务"是致命伤

免费网页模型会犯错。推导类任务的输出必须正确，于是付费模型被迫**检查**网页回复。而"检查一个推导"的成本 ≈ "做这个推导"的成本（验证者还要读一遍上下文、复推关键步骤）。

- 若验证成本被计入，**token 节省基本归零，甚至倒挂**（额外多一次网页往返的延迟和一次验证的 token）。
- 只有两类任务能免验证：
  - **回复可信**：低风险、一次性、孤立任务（写个正则、JSON 转 YAML、改格式、翻译）；
  - **可执行验证**：回复的正确性可以由"跑一遍"判定（生成的代码跑测试/lint、数据变换跑断言）。**执行验证是免费的**，不消耗模型 token。

> 推论：对"推导任务"做 offload，必须挂上 `verify: exec`（执行验证）或 `verify: skip`（用户显式接受风险）；否则不要 offload。

### 3.2 延迟问题

- 网页版走浏览器自动化：即使浏览器常驻，一轮也要 5–30 秒（粘贴、等流式、抓取、清洗），失败还要重试。
- 简单任务直接调 API（哪怕便宜的 `deepseek-chat`）只要 1–3 秒。
- 高频小任务场景下，延迟是主导成本，token 省下来的钱不值等待。

### 3.3 稳定性与合规问题

- 多数网页对话服务 ToS 禁止自动化/爬取；验证码、PoW、风控限流、封号风险真实存在（deepseek-free-api 里专门写了 PoW 求解和 token 刷新）。
- 网页 DOM 改版即失效，属于持续维护成本。
- 任务内容会明文发往第三方免费服务——涉及隐私/保密数据时需用户知情同意。

---

## 4. Token 账本（省不省、省多少，算给你看）

### 4.1 计价模型（约数，DeepSeek 官方 API，价格会变）

- reasoner（贵）：输入 ≈ $0.55/M，输出 ≈ $2.19/M，且输出含隐藏 CoT。
- chat（便宜）：约 1/2 输入价、1/2 输出价。
- 网页免费版：**0 元**。

### 4.2 一个典型"简单推导"任务（假设 reasoner 直接做：2k 输出 + 2k CoT）

| 路径 | 成本构成 | 约合 |
|---|---|---|
| 基准：reasoner 直接做 | (2k CoT + 2k 输出) × 输出价 + 输入 | ≈ $0.0088 |
| offload：网页做，md 交接 | 网页 0 元；付费模型读 md ≈ 1.5k × 输入价；组装回复 ≈ 0.3k × 输出价 | ≈ $0.0015 |
| offload + 付费模型验证 | 上者 + 验证 ≈ 2k 输出价 | ≈ $0.006，接近基准 |

**结论：免验证时边际节省 ≈ 85–90%；一加验证就只剩 ≈ 30%，还不算延迟。**

### 4.3 账单层面能省多少

会话账单中，reasoner 的输出 token（含 CoT）通常占大头。若其中 X% 的输出 token 属于"可 offload 的机械任务"，账单约下降 X%（输入 token 便宜，占比小）。**前提是 offload 工具不把网页回复全文塞回上下文**——这一点必须由工具设计保证（见 §5.2 第 4 步）。

### 4.4 两种"省 token"要分清

- **省 API 费用**：offload 的真实价值所在（网页 0 元 + 只读 md）。
- **省上下文窗口**：md 交接同时压缩上下文。但 DSH 已有 `compaction-basic` / `tool-result-pruner` 在做类似的事——**这部分价值 DSH 已经内置**，不是这个构想的增量。

---

## 5. 适用边界

| 适合 offload ✅ | 不适合 offload ❌ |
|---|---|
| 大块机械改写/翻译/重排/格式化（输出 token 大头） | 必须保证正确性的推导（数学证明、逻辑推导、法规/合同解读） |
| 可执行验证的任务（生成代码跑测试、数据变换跑断言） | 强依赖当前对话上下文的复杂任务（上下文要重发，白费） |
| 一次性、自包含的小任务（正则、JSON/YAML、小段总结） | 高频小任务（延迟主导，省的钱不值等待） |
| 长文本总结/提取（输出大、错误容忍度高） | 需要工具调用/结构化 JSON 的流程（免费网页版支持差，要返工） |

---

## 6. 插件设计（DSH 插件：`@yourname/dsh-tool-offload-md`）

### 6.1 组成

- **pnpm 包**，`package.json` 声明 `dsh.bundle.patch`（cordis patch），注册进 profile 后自动进入 layer 栈（与 `dsh-tool-web` 同款机制）。
- 注册两个服务：
  - **模型面工具** `offload`（agent-plane，per-session realm）；
  - **backend 层**（host-plane）：`offload` 工具经 `ctx.get` 解析，支持多 backend + 降级链。

### 6.2 数据流（每步都标注 token 影响）

1. 模型调用 `offload { task, context?, backend?, verify? }`。
   工具描述内嵌**分类指南**（"只用于机械/自包含/可验证任务"），让模型少做决策、少写无效参数——分类本身不花额外 token。
2. backend 执行：
   - `api` backend（**推荐**）：OpenAI 兼容端点，三选一：
     - 本地 **Ollama**（彻底免费、无 ToS、离线、无限流）；
     - **OpenRouter 免费模型 / Groq 免费层**（有额度，但走正规 API，无浏览器摩擦）；
     - 复用 `ctx.llm` 缝注册一个 `cheap` provider（deepseek-chat 而非 reasoner，约 1/2 价）。
   - `web` backend（原构想）：Playwright 驱动 chat.deepseek.com / ChatGPT 免费版，持久化 cookie 会话，等流式完成抓最终消息。PoW/验证码/限流 → 结构化错误 → 降级。
3. 网页/API 回复 → 写 md：`offload/YYYYMMDD-HHmmss-<slug>.md`。
   - frontmatter：`backend`、`model`、`time`、`estTokens`（api backend 直接用 usage 字段，web backend 估算）。
   - 正文 = 回复（可直接让网页版"用 markdown 输出"，省去二次汇总）。
4. 工具结果**只返回 `{ path, excerpt(≤500字), estTokens, backend }`，绝不贴全文**。
   > 这是本设计最重要的约束：网页回复若以工具结果全文进上下文，就会以 input token 计价、且每轮重发，节省当场蒸发。md 只按需被模型用 fs 工具读取。
5. 模型按需 `read` md → 执行/组装回复。
6. （可选）`verify: "exec"`：写入 md 后，按配置跑检查命令（`node t.js` / `pytest` / `jq -e .`），把 PASS/FAIL 追加进 md，模型只信 PASS 的结果——**推导类任务必须走这条**，否则拒绝 offload。

### 6.3 配置（settings 命名空间 `offload-md`，热更新走 `ctx.settings`）

```yaml
offload-md:
  backendOrder: [api, web]        # 降级链
  api:
    provider: openrouter          # openrouter | groq | ollama | deepseek-chat
    baseURL: ""                   # ollama 填 http://127.0.0.1:11434/v1
    model: "qwen3:8b"             # 或 deepseek-chat / meta-llama/llama-3.3-70b-instruct:free
    apiKeyEnv: OPENROUTER_API_KEY # 复用 dsh-credentials 缝，不落明文
    maxTokens: 4096
  web:
    url: "https://chat.deepseek.com"
    sessionDir: "$DSH_HOME/offload-sessions"  # cookie/登录态持久化
    timeoutMs: 60000
    hourlyBudget: 20              # 每时段最大次数，防限流/封号
  mdDir: "offload"                # workspace 下输出目录
  maxMdChars: 40000
  verifyTemplates: {}             # 如 { "py": "python {file}" }
```

### 6.4 失败与安全

- 降级链：`web` 失败（验证码/超时/限流）→ `api` → 本地 Ollama → 全部失败则返回结构化错误，**模型回退到本地直接做**；绝不静默伪造。
- 隐私：任务内容明文发第三方。默认对 `web` backend 弹一次用户确认（approval 钩子），可配置关闭。
- 凭证：cookie/密钥走 `dsh-credentials` 与 `$DSH_HOME`，不写进 workspace 明文。
- 计量：每个 md 记录 estTokens/usage，日志可汇总"本周 offload 节省了 X token"——**上线第一周必须做**，用真实数据验证账本。

### 6.5 与 DSH 现有机制的对照（别重复造轮子）

| 构想里的环节 | DSH 已有等价物 | 本插件的**新增价值** |
|---|---|---|
| md 交接压缩上下文 | compaction-basic / tool-result-pruner / spill | 无（复用即可） |
| 用便宜模型做杂活 | 模型选择器可换 model；subagent 支持 provider/model 覆盖 | 统一入口 + 自动降级 + 计量 |
| 网页免费版 | 无 | **web backend（可选，高风险高维护）** |
| 执行验证 | 无现成钩子 | **verify=exec 钩子（核心价值）** |
| 离线缓存工作产物 | 无 | md 工件目录管理 |

> 换句话说：构想中"便宜算力 + md 交接"的思路里，**真正缺的只有三块**——统一的 offload 工具、verify=exec 钩子、以及（可选的）web backend。其余 DSH 已有。

---

## 7. 实现路线（按风险递增）

1. **MVP（1–2 天，验证账本）**：只做 `api` backend（先用本地 Ollama 或 OpenRouter 免费模型）→ md → 路径+摘要返回 → 模型按需读 → `verify=exec` 对代码类任务跑测试。跑一周真实会话，统计节省率。
2. **P1**：接入 `ctx.llm` 缝注册 `deepseek-chat` cheap provider；md 工件目录 UI 展示；降级链与计量。
3. **P2（可选，原构想）**：`web` backend（Playwright），接受其维护成本与合规风险；默认关闭，用户显式开启。
4. **不做**：让网页版承载"必须正确的一般性推导"（除非 verify=exec 可执行）。

---

## 8. 结论

- **能省**：在"机械、自包含、可执行验证"的任务上，offload + md 交接能把对应输出的 API 费用降到接近 0（边际节省 ≈ 85–90%），且 md 工件可复用、可审计。
- **不能稳定省**："一般性推导任务"——免费模型回复不可信时，付费模型的验证成本 ≈ 重做成本，节省被吃掉；且网页自动化的延迟、ToS、PoW/验证码、限流会让体验和账本都变差。
- **关键设计纪律**：工具结果只回传"路径 + 摘要"；推导类任务强制 `verify=exec`；默认降级链到本地/便宜 API 而非网页；先上 api backend 验证账本，再考虑网页版。
- **一句话**：构想方向正确（便宜算力 + md 交接 = 省 token），但"网页免费版"是其中最脆弱的一环——建议用"本地模型或免费 API"替代它，保留 md 交接与验证钩子，同样达成目标且稳一个量级。

# 项目 NuGet 包清单

> 更新日期：2026-03-30  
> 数据来源：`Core/App/Build/Dependencies.props` + `Core/App/天命.csproj`  
> 包总数：**28个**

---

## 仅预览版可用（无稳定版）

| 包名 | 当前版本 | 说明 |
|------|----------|------|
| **Microsoft.SemanticKernel.Connectors.Google** | 1.74.0-alpha | 官方仅发布alpha版，跟随SK主版本同步 |

---

## 暂不升级

| 包名 | 当前版本 | 最新版 | 原因 |
|------|----------|--------|------|
| Markdig | 0.45.0 | 1.1.1 | 主版本跳跃，Markdig.Wpf 0.5.0.1 未适配 1.x，升级会导致不兼容 |
| Microsoft.CodeAnalysis.CSharp.Scripting | 5.0.0 | 5.3.0 | 5.3.0 依赖 Roslyn 4.12，当前 .NET SDK 仅有 4.11，升级产生 CS9057 警告 |

---

## 当前全部依赖包（28个）

### 核心依赖（Dependencies.props）
| 包名 | 版本 | 用途 |
|------|------|------|
| Microsoft.Extensions.DependencyInjection | 10.0.5 | 依赖注入容器 |
| System.Drawing.Common | 10.0.5 | GDI+绘图 |
| System.Management | 10.0.5 | WMI管理 |
| System.Security.Cryptography.ProtectedData | 10.0.5 | Windows数据保护API |
| System.Speech | 10.0.5 | 语音合成 |

### UI/显示相关
| 包名 | 版本 | 用途 |
|------|------|------|
| Emoji.Wpf | 0.3.4 | Emoji显示支持 |
| Markdig | 0.45.0 | Markdown解析 |
| Markdig.Wpf | 0.5.0.1 | WPF Markdown渲染 |
| ColorCode.Core | 2.0.15 | 代码高亮核心 |
| ColorCode.HTML | 2.0.15 | 代码高亮HTML输出 |
| QuestPDF | 2026.2.4 | PDF生成 |
| Microsoft.Web.WebView2 | 1.0.3856.49 | WebView2浏览器控件 |
| DiffPlex.Wpf | 1.9.1 | 章节改写可视化Diff |

### AI/机器学习
| 包名 | 版本 | 用途 |
|------|------|------|
| Microsoft.SemanticKernel | 1.74.0 | SK核心 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.74.0 | SK OpenAI连接器 |
| Microsoft.SemanticKernel.Agents.Core | 1.74.0 | SK Agent框架 |
| Microsoft.SemanticKernel.Connectors.Google | 1.74.0-alpha | SK Google连接器（仅alpha） |
| Microsoft.KernelMemory.Core | 0.98.250508.3 | Kernel Memory核心 |
| Microsoft.KernelMemory.AI.OpenAI | 0.98.250508.3 | Kernel Memory OpenAI |
| Microsoft.ML.OnnxRuntime | 1.24.4 | ONNX模型推理运行时 |

### 韧性/安全
| 包名 | 版本 | 用途 |
|------|------|------|
| Polly.Extensions | 8.6.6 | 重试/熔断/超时策略 |

### 工具/其他
| 包名 | 版本 | 用途 |
|------|------|------|
| Microsoft.CodeAnalysis.CSharp.Scripting | 5.0.0 | C#脚本执行 |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | Windows通知 |
| NAudio | 2.3.0 | 音频处理 |
| QRCoder | 1.7.0 | 二维码生成 |
| HtmlAgilityPack | 1.12.4 | HTML解析 |
| IP2Region.Net | 3.0.2 | IP地址归属地查询 |
| zxcvbn-core | 7.0.92 | 密码强度评估 |

---

## 本次升级记录（2026-03-30）

| 包名 | 旧版本 | 新版本 |
|------|--------|--------|
| Microsoft.SemanticKernel | 1.73.0 | 1.74.0 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.73.0 | 1.74.0 |
| Microsoft.SemanticKernel.Agents.Core | 1.73.0 | 1.74.0 |
| Microsoft.SemanticKernel.Connectors.Google | 1.73.0-alpha | 1.74.0-alpha |
| Microsoft.Extensions.DependencyInjection | 10.0.3 | 10.0.5 |
| System.Drawing.Common | 10.0.3 | 10.0.5 |
| System.Management | 10.0.3 | 10.0.5 |
| System.Security.Cryptography.ProtectedData | 10.0.3 | 10.0.5 |
| System.Speech | 10.0.3 | 10.0.5 |
| Microsoft.Web.WebView2 | 1.0.3800.47 | 1.0.3856.49 |
| Microsoft.ML.OnnxRuntime | 1.24.2 | 1.24.4 |
| ~~Microsoft.CodeAnalysis.CSharp.Scripting~~ | ~~5.0.0~~ | ~~5.3.0~~ (已回退，Roslyn版本不兼容) |
| NAudio | 2.2.1 | 2.3.0 |
| QuestPDF | 2026.2.3 | 2026.2.4 |
| IP2Region.Net | 3.0.0 | 3.0.2 |

---

## 历史升级记录（2026-03-05）

| 包名 | 旧版本 | 新版本 |
|------|--------|--------|
| Microsoft.SemanticKernel | 1.72.0 | 1.73.0 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.72.0 | 1.73.0 |
| Microsoft.SemanticKernel.Agents.Core | 1.72.0 | 1.73.0 |
| Microsoft.SemanticKernel.Connectors.Google | 1.72.0-alpha | 1.73.0-alpha |
| Polly.Extensions | 8.6.5 | 8.6.6 |
| QuestPDF | 2026.2.1 | 2026.2.3 |

### 未变更（已是最新稳定版）
Microsoft.Extensions.DependencyInjection 10.0.3、System.Drawing.Common 10.0.3、System.Management 10.0.3、System.Security.Cryptography.ProtectedData 10.0.3、System.Speech 10.0.3、Microsoft.Web.WebView2 1.0.3800.47、Microsoft.ML.OnnxRuntime 1.24.2、HtmlAgilityPack 1.12.4、Microsoft.KernelMemory.Core 0.98.250508.3、Microsoft.KernelMemory.AI.OpenAI 0.98.250508.3、Emoji.Wpf 0.3.4、Markdig.Wpf 0.5.0.1、ColorCode.Core 2.0.15、ColorCode.HTML 2.0.15、DiffPlex.Wpf 1.9.1、Microsoft.CodeAnalysis.CSharp.Scripting 5.0.0、Microsoft.Toolkit.Uwp.Notifications 7.1.3、NAudio 2.2.1、QRCoder 1.7.0、IP2Region.Net 3.0.0、zxcvbn-core 7.0.92

---

## 历史升级记录（2026-02-24）

| 包名 | 旧版本 | 新版本 |
|------|--------|--------|
| Microsoft.Extensions.DependencyInjection | 10.0.2 | 10.0.3 |
| System.Drawing.Common | 10.0.2 | 10.0.3 |
| System.Management | 10.0.2 | 10.0.3 |
| System.Security.Cryptography.ProtectedData | 9.0.4 | 10.0.3 |
| System.Speech | 10.0.2 | 10.0.3 |
| Microsoft.SemanticKernel | 1.70.0 | 1.72.0 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.70.0 | 1.72.0 |
| Microsoft.SemanticKernel.Agents.Core | 1.70.0 | 1.72.0 |
| Microsoft.SemanticKernel.Connectors.Google | 1.70.0-alpha | 1.72.0-alpha |
| Polly.Extensions | 8.5.2 | 8.6.5 |
| Markdig | 0.44.0 | 0.45.0 |
| QuestPDF | 2025.12.4 | 2026.2.1 |
| Microsoft.Web.WebView2 | 1.0.3719.77 | 1.0.3800.47 |
| Microsoft.ML.OnnxRuntime | 1.24.1 | 1.24.2 |

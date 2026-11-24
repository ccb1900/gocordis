思考了 20s

这份规范需要同时解决两个层面的问题：

* Cordis 框架本身：提供可靠的插件运行时、依赖组合、生命周期、扩展机制和 Web UI 宿主。

* 工业数据采集产品：基于 Cordis 插件体系，提供资源管理、工作流设计、数据采集、可靠交付和运行管理。

不能把工业业务逻辑塞进 Cordis Core，也不能只完成一个能运行 CSV→数据库的后台 Demo。最终交付物必须是一个有真实 Web UI、可安装插件、可编辑工作流、可持续运行并能处理异常恢复的工业应用。

下面是一份可以直接交给 Coding Agent 的项目实施规范。它既是架构约束，也是开发任务书、迁移要求和验收标准。

# Cordis 工业数据采集平台：Coding Agent 项目实施规范

**文档性质：** 项目总规范 / 架构约束 / Agent 执行任务书
**目标：** 完成基于 C# Cordis 的工业数据采集与工作流平台
**开发原则：** 源码优先、成熟库优先、插件化组合、真实业务闭环、禁止无意义重构

---

# 一、项目最终目标

## 1.1 产品定位

构建一个运行于 Windows 工业 PC 的插件化工业数据采集与集成平台。

用户通过 Web UI 配置资源、设计工作流、管理采集任务、查看执行情况。平台能够从本地文件、网络共享目录、数据库等数据源采集数据，进行解析、转换、校验，再交付到数据库或文件系统。

平台不是简单的 ETL 脚本工具，也不是一个只提供工作流画布的编辑器。

它需要具备：

- 可扩展的插件运行时。
- 可组合的工业数据处理能力。
- 可视化工作流设计器。
- 统一资源和凭据管理。
- 定时、手动和事件触发。
- 长时间停机后的历史补采。
- 文件级采集账本和目标级交付状态。
- 幂等写入、失败重试和最终一致性。
- 完整的 Web UI。
- 可观测、可维护、可升级的应用结构。

## 1.2 最终用户操作流程

最终用户应该能够完成以下操作：

1. 安装或启用所需插件。
2. 配置文件共享目录、数据库等资源。
3. 创建一个采集工作流。
4. 从节点面板选择文件读取、CSV 解析、字段映射、数据校验、数据库写入等节点。
5. 配置节点属性并连接节点。
6. 保存并校验工作流。
7. 发布工作流。
8. 配置定时或手动触发。
9. 查看执行记录、文件处理进度和错误信息。
10. 在设备停机恢复后自动补采遗漏数据。

简单采集任务应支持向导式创建，复杂任务进入工作流设计器。

**普通用户不应该被迫理解 DAG、Activity、Fiber 或插件内部实现。**

---

# 二、必须遵守的架构边界

## 2.1 总体架构

系统分成两个独立产品层次：

### A. Cordis Framework

负责通用插件运行平台：

- Runtime
- Plugin Lifecycle
- Dependency Resolution
- Provide / Require
- Reconciliation
- Plugin Packaging
- Plugin Installation
- Extension Contribution
- UI Extension Host
- Platform Integration

Cordis 不负责工业领域业务。

### B. Industrial Application

独立的工业应用，依赖 Cordis 提供的运行时和扩展能力。

负责：

- Resource Management
- Credential Management
- Workflow Definition
- Workflow Execution Integration
- Industrial Nodes
- Data Collection
- Data Transformation
- Data Delivery
- Collection Ledger
- Execution Management
- Industrial Web UI

禁止将工业领域机制反向放入 Cordis Core。

## 2.2 核心职责划分

| 子系统 | 负责内容 | 禁止承担 |
|---|---|---|
| Cordis Core | Fiber、Activation、Effect、Reconcile 等运行时语义 | 工业业务 |
| Plugin System | 插件声明、依赖、安装、启用 | 工作流业务 |
| Plugin Backend | 插件实际加载和运行 | 第二套生命周期 |
| Platform | 运行环境组合 | 领域业务 |
| UI Host | UI 扩展贡献的消费与渲染 | 工业页面业务 |
| Industrial | 工业业务能力 | 修改 Cordis Core 解决业务问题 |
| Elsa | 工作流执行、调度和持久化执行能力 | 插件安装与生命周期 |
| Ant Design | 产品 UI 组件 | 工作流图模型 |
| AntV G6 | 工作流图编辑和渲染 | 工作流领域模型 |

**所有业务能力都应该通过 Cordis 插件提供。**

内置服务同样是插件，不应因为它们随应用打包，就建立一套特殊的服务体系。

---

# 三、Cordis 框架实施规范

## 3.1 以现有 C# dotnet 分支为唯一审查对象

Agent 必须首先确认：

- 仓库：`ccb1900/gocordis`
- 分支：`dotnet`
- 实际 HEAD SHA
- 当前目录结构
- 当前测试状态
- 当前 CI 状态
- 最近提交的真实实现

不得把 `migration/*` 分支混入当前审查。

不得使用旧 SHA 的审查结论代替当前源码。

不得仅阅读 README、设计文档或提交记录就判断功能已经完成。

必须阅读实际实现及其调用链。

## 3.2 迁移参照体系

Cordis 的迁移与验证必须以四个维度为参照：

1. Dynamic Composable Runtime 论文。
2. JavaScript Cordis。
3. Koishi。
4. Gocordis。

四个参照对象承担不同职责：

| 参照 | 主要验证内容 |
|---|---|
| Dynamic Composable Runtime | 动态组合、时空结构、Fiber 和生命周期语义 |
| JavaScript Cordis | 插件 API、依赖声明、服务组合、扩展机制 |
| Koishi | 真实应用中的插件生态、服务、UI 和插件组合 |
| Gocordis | 已有 Go 实现中的工程结构、API 和迁移行为 |

不得将四者混为一谈。

论文语义不能替代产品 API 验证；JavaScript Cordis 的某个 API 也不能自动证明 C# 实现具备相同行为。

每项差异必须归类：

- 已完整迁移。
- 有意采用不同实现，但语义等价。
- 有意不迁移，并说明原因。
- 尚未迁移。
- 存在缺陷。
- 需要进一步验证。

禁止用“架构上支持”代替可执行验证。

## 3.3 插件之间的依赖规则

这是框架的核心约束。

插件 A 和插件 B 不允许通过语言级别的项目依赖直接联系。

禁止：

- A 直接引用 B 的实现项目。
- A 直接调用 B 的内部类。
- A 通过程序集引用绕开 Cordis 能力注册机制。
- 为了方便而将所有插件代码合并进 Core。

允许：

- 插件 A 在开发时引用插件 C 提供的契约程序集。
- 插件 A 通过 Cordis require 使用 C 提供的能力。
- 插件 B 以相同方式依赖 C。
- A、B 之间没有直接依赖。

示例：

```text
Plugin A ───────┐
                ├── Contract C
Plugin B ───────┘
                │
                ▼
         Cordis Runtime
                │
                ▼
          Provider C
```

这里必须区分五种依赖：

1. NuGet / 包依赖。
2. 编译期契约依赖。
3. 插件 Manifest 依赖。
4. Runtime Provide / Require 能力依赖。
5. 程序集加载依赖。

它们不是同一个概念。

### 契约设计

被依赖插件必须提供可供其他插件开发时引用的契约。

契约应该：

- 独立于实现程序集。
- 明确接口和版本。
- 不暴露内部实现类。
- 不要求消费者引用 Provider 的实现项目。
- 能够在运行时由 Cordis 解析到正确的 Provider。
- 有明确的兼容性规则。

插件 A、B 同时依赖 C 时，运行时应当只有一个符合解析规则的 C Provider 实例，而不是因为两个消费者各自依赖 C 就创建两份互相隔离的服务实例。

### 契约与热加载

不能只讨论插件程序集热加载，而忽略契约程序集。

Agent 必须验证：

- 契约程序集的类型身份如何保持一致。
- 共享契约程序集由哪个加载上下文提供。
- 插件 A、B、C 动态安装时如何解析契约。
- 契约版本不兼容时如何拒绝激活。
- 新契约如何进入当前运行时的解析环境。
- 已运行插件如何处理契约版本变化。

不能简单地把每个插件及其契约都放进独立 AssemblyLoadContext，否则可能出现同名类型不具有相同运行时类型身份的问题。

### 安装和运行时激活

产品必须考虑 Cordis 已启动的场景：

```text
Cordis 已启动
    │
    ▼
安装 A、B、C
    │
    ▼
解析包依赖和插件依赖
    │
    ▼
解析契约
    │
    ▼
检查依赖图
    │
    ▼
按依赖顺序激活
    │
    ▼
A、B 正常 require C
```

Agent 必须区分：

- 动态安装。
- 动态激活。
- 动态增加能力。
- 动态停用。
- 动态卸载。
- 动态替换版本。
- AssemblyLoadContext 卸载。

这些能力不是同一件事。

第一优先级是可靠地实现安装、依赖解析、激活和运行状态管理。对于无法安全卸载的程序集，不得伪装成已经支持热卸载。

如果某种更新必须重启，应明确告诉用户，并提供一致的安装状态和恢复机制。

**不能为了声称支持热插拔而牺牲运行时正确性。**

## 3.4 插件生命周期

所有插件生命周期必须由 Cordis 统一管理。

禁止：

- Extensions 自建第二套生命周期。
- 每个插件自行管理一套激活状态。
- 通过后台 fire-and-forget 任务掩盖生命周期未完成。
- 清理失败后直接宣布插件已经卸载。
- 新 Fiber 激活时忽略旧 Fiber 清理失败。

必须特别验证以下场景：

1. 插件正常安装和激活。
2. 依赖缺失。
3. 循环依赖。
4. 插件激活失败。
5. 插件停用。
6. 插件清理失败。
7. 插件依赖变化。
8. 插件重新激活。
9. 插件运行时动态增加。
10. 插件版本冲突。

此前源码审查指出的生命周期风险必须纳入回归测试：

- 过期步骤的补偿不能 fire-and-forget。
- Gone 之前必须完成必要清理。
- install / Divert 失败时，补偿失败必须可观察，状态必须明确。
- Rehome 清理不完整时，不允许直接激活新的 Fiber。

不能只测试成功路径。

## 3.5 Backend 与运行环境

Managed、Process、WASM 等不同执行方式不能各自实现一套生命周期。

它们应该在架构上通过统一的 Plugin Backend 抽象参与 Cordis 运行时。

但是当前项目阶段明确：

**暂不推进 WASM 和 Process 支持，优先完成 Managed 插件闭环。**

不得为了未来可能支持的执行模式引入大量当前不需要的抽象。

也不得让 Managed 插件获得不合理的特殊待遇，导致将来其他 Backend 无法接入统一的插件契约。

## 3.6 UI 必须属于插件体系

UI 不能从 Cordis 迁移中被排除。

必须区分：

- Framework UI：插件管理、运行时状态、扩展管理等框架级页面。
- Application UI：机器管理、采集任务、资源管理、工作流等业务页面。

两者都应该通过 Cordis UI 扩展机制注册。

UI 插件至少需要考虑：

- 页面注册。
- 路由注册。
- 导航和菜单贡献。
- 页面组件。
- 权限声明。
- 资源贡献。
- 插件启用时注册。
- 插件停用时撤销。
- 插件重新启用时恢复。

UI Host 负责消费这些贡献。

不要在 UI Host 中硬编码所有业务页面。

不得另建一套与 Cordis 插件管理无关的 UI 插件系统。

---

# 四、工业应用总体架构

## 4.1 技术栈

以下是当前确定的方向：

| 层次 | 技术 |
|---|---|
| 运行时 | C# Cordis |
| 工作流引擎 | Elsa 3，必须通过 PoC 验证 |
| 后端 | ASP.NET Core |
| 前端 | React + TypeScript |
| UI 组件库 | Ant Design |
| 工作流画布 | AntV G6 |
| 数据库访问 | EF Core 或成熟数据库驱动 |
| CSV | CsvHelper |
| Excel | ExcelDataReader 等成熟库 |
| SQL Server | Microsoft.Data.SqlClient |
| PostgreSQL | Npgsql |
| MySQL | MySqlConnector |
| 日志 | Microsoft.Extensions.Logging |
| 可观测性 | OpenTelemetry |
| 重试 | 根据 Elsa 和具体 Provider 的职责选择，避免重复重试 |

注意：

- 不能擅自切换 Vue。
- 不能引入 React Flow 代替 G6。
- 不能引入 Elsa Studio。
- 不能另建一套前端 UI 组件体系。
- 不能仅因为某个库流行就引入它。

上述技术栈中，必须先检查 Cordis 当前前端已有的路由、API Client、状态管理、权限、UI 扩展机制，尽可能复用。

## 4.2 应用架构

建议按职责划分：

```text
Industrial Application
│
├── Industrial.Contracts
│
├── Industrial.Platform
│
├── Industrial.Resource
│
├── Industrial.Credential
│
├── Industrial.Workflow
│
├── Industrial.Elsa
│
├── Industrial.Execution
│
├── Industrial.Collection
│
├── Industrial.Plugins
│   ├── Filesystem.Local
│   ├── Filesystem.Unc
│   ├── Reader.Csv
│   ├── Reader.Excel
│   ├── Transform.Mapping
│   ├── Transform.Convert
│   ├── Transform.Filter
│   ├── Transform.Validate
│   ├── Sink.SqlServer
│   ├── Sink.MySql
│   ├── Sink.PostgreSql
│   ├── Sink.File
│   └── Trigger.*
│
└── Industrial.Web
    ├── React
    ├── Ant Design
    └── AntV G6
```

这只是逻辑职责划分，不要求每个目录都变成独立项目或独立插件。

**插件边界应当由独立能力和生命周期决定，不应机械地按类或文件数量拆分。**

---

# 五、工业插件体系

## 5.1 插件分类

### 平台与业务基础能力

| 插件 | 职责 |
|---|---|
| industrial.persistence | 业务数据持久化能力 |
| industrial.resource | 资源注册、查询、引用和版本管理 |
| industrial.credential | 凭据安全存储 |
| industrial.workflow | 工业工作流定义、校验和发布 |
| industrial.execution | 执行视图、采集账本和运行状态 |
| industrial.elsa | Cordis 与 Elsa 的适配 |
| industrial.collection | 采集领域服务和协调能力 |

Scheduler 是否独立实现，需要通过 Elsa 3 的实际能力验证。

不要同时建设两套重复的调度系统。

### 数据源插件

- industrial.filesystem.local
- industrial.filesystem.unc
- industrial.reader.csv
- industrial.reader.excel
- 后续数据库读取插件

### 数据处理插件

- industrial.transform.mapping
- industrial.transform.convert
- industrial.transform.filter
- industrial.transform.validate

### 数据目标插件

- industrial.sink.sqlserver
- industrial.sink.mysql
- industrial.sink.postgresql
- industrial.sink.file

### 触发器插件

- industrial.trigger.manual
- industrial.trigger.schedule
- industrial.trigger.event

插件可以按业务能力组合，不要求机械地一项能力一个插件。

## 5.2 插件新增原则

新增一个 Reader 插件时，正常情况下只需要：

1. 实现对应的读取契约。
2. 提供插件 Manifest。
3. 注册对应工作流节点描述。
4. 提供对应 Activity。
5. 提供配置 Schema。
6. 提供测试。

不应该修改：

- Cordis Core。
- 工作流引擎核心。
- 工作流设计器核心。
- 资源系统核心。
- 其他 Reader 插件。

新增 Sink 也应遵循相同原则。

如果新增一个业务节点必须修改设计器核心，说明节点扩展边界设计存在问题。

---

# 六、资源、凭据和连接管理

这三个概念必须严格区分。

## 6.1 Resource

Resource 表示用户配置的外部资源。

例如：

- 本地目录。
- UNC 共享目录。
- SQL Server 数据库。
- MySQL 数据库。
- PostgreSQL 数据库。

Resource 应包含：

- ResourceId。
- ResourceType。
- Name。
- Provider。
- Configuration。
- Credential Reference。
- Version。
- Enabled。
- CreatedAt。
- UpdatedAt。

工作流只保存 ResourceId，不保存密码和敏感连接字符串。

## 6.2 Credential

Credential 是认证凭据。

要求：

- 独立管理。
- 加密保存。
- 不出现在普通 API 返回中。
- 不写入工作流 Definition。
- 不进入日志。
- 支持凭据轮换。
- 支持访问权限控制。

不能把用户名和密码直接写进工作流节点配置。

## 6.3 Connection / Lease

Connection 是运行时建立的连接或会话。

例如：

- SQL Connection。
- UNC SMB Session。
- 文件句柄。
- 外部系统会话。

Resource 不等于 Connection。

Credential 也不等于 Connection。

数据库驱动通常已经有连接池，不要重复实现数据库连接池。

UNC 则需要考虑 Windows SMB 会话和凭据管理。

## 6.4 UNC 共享

必须支持：

- 本地目录。
- UNC 路径。
- 指定用户名和密码访问共享目录。
- 多个工作流共享同一 Resource。
- 多个插件复用相同的共享访问能力。

不能要求用户必须依赖 Windows 当前登录账号访问 UNC。

指定账号访问 SMB 时，底层仍然使用 Windows SMB 客户端能力，并且必须满足共享权限和 NTFS 权限。

特别注意 Windows 同一登录会话对同一服务器使用不同凭据可能出现错误 1219。

UNC Provider 应统一协调目标服务器的会话和凭据使用，不能让每个工作流自行建立互相冲突的会话。

## 6.5 Resource 生命周期

必须验证：

- Provider 禁用时，Resource 配置不能丢失。
- Provider 卸载时，Resource 配置不能丢失。
- 被工作流引用的 Resource 不能被无提示删除。
- Resource 修改后能够识别版本变化。
- 工作流发布时能够确定资源配置快照或引用策略。
- 凭据轮换不会无意中破坏历史执行记录。

---

# 七、工作流设计器与执行体系

这是工业产品的核心。

## 7.1 设计原则

工作流是产品的核心交互模型。

不能把它做成一个简单的 DAG 展示页面。

用户需要能够通过节点组合完成真实采集业务。

设计器必须支持：

- 节点拖入。
- 节点选择。
- 节点连线。
- 端口连接。
- 节点属性编辑。
- 工作流校验。
- 保存。
- 发布。
- 运行。
- 执行结果查看。
- 错误定位。
- 工作流版本管理。

## 7.2 前端技术边界

**React + TypeScript + Ant Design + AntV G6。**

职责明确：

### Ant Design

负责：

- 页面布局。
- 顶部工具栏。
- 左侧节点面板。
- 右侧属性面板。
- 表单。
- 弹窗。
- 表格。
- 运行状态。
- 资源选择器。
- 数据预览。

### AntV G6

负责：

- 画布。
- 节点渲染。
- 边渲染。
- 节点拖动。
- 连线。
- 缩放。
- 平移。
- 选中。
- 框选。
- 布局。
- 图交互。

### 工作流领域模型

独立于 G6。

G6 只负责编辑交互和图形展示。

不能直接将 G6 内部 Graph Model 作为工作流持久化格式。

工作流 Definition 才是唯一业务数据源。

## 7.3 设计器布局

建议采用：

```text
┌──────────────────────────────────────────────────────────────┐
│ 工作流名称    保存    校验    发布    运行    执行记录       │
├──────────────┬──────────────────────────┬────────────────────┤
│              │                          │                    │
│ 节点面板     │                          │ 节点属性           │
│              │       G6 工作流画布      │                    │
│ 触发器       │                          │ 配置               │
│ 文件         │                          │                    │
│ 解析         │                          │ 参数               │
│ 转换         │                          │                    │
│ 写入         │                          │                    │
│              │                          │                    │
└──────────────┴──────────────────────────┴────────────────────┘
```

具体布局可以调整，但不能牺牲属性编辑、节点选择和执行状态展示。

## 7.4 节点描述协议

节点由后端插件提供描述。

至少包括：

- NodeTypeId。
- Name。
- Description。
- Category。
- Icon。
- Version。
- Input Ports。
- Output Ports。
- Configuration Schema。
- Activity Type。
- Compatibility Information。

端口需要支持类型定义。

例如：

- File。
- FileBatch。
- Dataset。
- Record。
- DeliveryResult。

端口不能只是任意字符串。

节点之间必须校验数据类型兼容性。

不能允许两个明显不兼容的数据端口仅因为都能连线就连接成功。

## 7.5 节点属性编辑

普通节点使用 Schema 驱动表单。

优先使用 Ant Design Form。

复杂节点可以使用专用编辑器，例如：

- 字段映射编辑器。
- SQL 表结构映射。
- 数据预览。
- 列类型转换。
- 文件匹配规则编辑器。

不能为了每个节点都开发一套独立 React 页面。

也不能因为追求通用而让复杂业务只能使用难以理解的 JSON 编辑器。

## 7.6 工作流定义

工业工作流定义是唯一编辑源。

Elsa Definition 是发布执行产物。

不能维护两份可以独立编辑的工作流定义。

工作流 Definition 至少需要：

- WorkflowId。
- Name。
- Version。
- Nodes。
- Edges。
- Node Configuration。
- Port Information。
- Layout Metadata。
- Trigger Configuration。
- Resource References。
- Validation Information。

示意：

```json
{
  "workflowId": "daily-collection",
  "version": 1,
  "nodes": [
    {
      "id": "source",
      "type": "industrial.filesystem.unc",
      "config": {
        "resourceId": "production-share"
      }
    },
    {
      "id": "reader",
      "type": "industrial.reader.csv",
      "config": {
        "encoding": "utf-8"
      }
    }
  ],
  "edges": [
    {
      "source": "source",
      "sourcePort": "files",
      "target": "reader",
      "targetPort": "input"
    }
  ],
  "layout": {}
}
```

以上只是格式示意，不是要求照搬字段命名。

正式 Schema 必须有版本和迁移策略。

布局数据不能影响工作流执行语义。

## 7.7 工作流校验

至少包括：

1. 节点类型存在。
2. 节点版本兼容。
3. 必填配置完整。
4. Resource 存在。
5. Resource 类型正确。
6. Credential 引用有效。
7. 端口存在。
8. 端口类型兼容。
9. 必要输入完整。
10. 图结构合法。
11. 触发器配置有效。
12. 插件依赖可用。
13. 发布所需的 Activity 可以注册。

错误必须定位到具体节点或连线。

不能仅返回一个笼统的“工作流无效”。

## 7.8 Elsa 3 的职责

Elsa 是候选执行引擎，必须通过 PoC 验证后确定最终集成方式。

Elsa 负责：

- 工作流控制流。
- Activity 执行。
- 执行持久化。
- 执行恢复。
- 工作流调度相关能力。
- 通用执行状态。

Elsa 不负责：

- Cordis 插件安装。
- Cordis 插件依赖解析。
- Resource 管理。
- Credential 管理。
- 工业采集账本。
- 文件发现规则。
- 数据交付账本。

不得把 Elsa 变成第二套插件管理系统。

工业插件通过 Cordis 提供能力，同时贡献对应 Activity 和节点描述。

建立 `Industrial.Elsa` 适配层，负责：

1. Cordis 插件能力与 Elsa Activity 注册的协调。
2. 工业 Definition 校验。
3. 工业 Definition 到 Elsa Definition 的转换。
4. 发布版本管理。
5. 运行时引用和状态映射。
6. 插件停用、版本变化时的执行兼容性处理。

## 7.9 数据流和控制流

必须明确区分：

- 控制流：决定下一步执行什么。
- 数据流：传递文件、记录集、数据集等业务数据。

Elsa Variables 不应保存大量实际数据。

例如几百万条记录不能作为工作流变量在节点之间传递。

应该传递引用：

- FileBatchRef。
- DatasetRef。
- RecordSetRef。
- DeliveryResultRef。

真实数据通过流式读取、批量处理或受控的中间存储传递。

工作流引擎负责协调执行，工业数据处理层负责数据吞吐。

不能把 Elsa 当作高吞吐 ETL 数据引擎。

---

# 八、可靠采集与交付

这部分是产品能否真正用于工业现场的关键。

## 8.1 典型采集链路

```text
Schedule
   │
   ▼
Historical Range Planner
   │
   ▼
UNC File Source
   │
   ▼
CSV Reader
   │
   ▼
Field Mapping
   │
   ▼
Type Conversion
   │
   ▼
Validation
   │
   ├──────────────┐
   ▼              ▼
SQL Server      File Archive
   │              │
   └──────┬───────┘
          ▼
    Delivery Ledger
```

## 8.2 历史补采

必须支持机器停机后恢复。

例如设备停机一年半，重新启动后需要补采所有尚未处理的历史文件。

不能只依赖 LastSuccessDate。

必须具备：

- 历史日期范围规划。
- 文件发现。
- 文件级处理状态。
- 迟到文件扫描。
- 已处理文件识别。
- 失败文件重试。
- 补采任务记录。

前提是上游文件仍然保留。

软件不能恢复上游已经删除的文件，也不能保证源盘物理损坏后的数据恢复。

## 8.3 时间概念

必须区分：

- `source_date`：文件所属业务日期。
- `record_time`：数据实际发生时间。
- `collected_at`：系统采集时间。

不能用系统采集时间代替业务时间。

定时采集日期边界应明确。

例如每天采集今天之前的数据，应采用明确的排他边界，避免重复采集当天尚未完成的数据。

## 8.4 文件身份

技术文件身份和业务元数据必须分离。

文件身份不能因为用户修改字段映射、文件命名规则或业务日期解析规则就改变。

需要设计稳定的 FileIdentity。

文件处理前应检查文件稳定性，避免读取仍在写入中的文件。

## 8.5 账本模型

至少考虑以下实体：

### Collection

表示一次采集任务或采集计划。

### CollectionFile

表示被发现和处理的文件。

### FileDelivery

表示一个文件向一个目标交付的状态。

一个文件可以有多个目标。

每个目标必须独立记录成功和失败。

示例：

```text
Collection
    │
    └── CollectionFile
            │
            ├── FileDelivery → SQL Server
            │
            ├── FileDelivery → MySQL
            │
            └── FileDelivery → Archive
```

不能只记录一个笼统的文件成功状态。

## 8.6 幂等性

数据库写入必须具备稳定的幂等键。

例如：

- 稳定 record_id。
- 唯一约束。
- 幂等批量写入。
- 必要时使用事务。

同一文件重试不能造成重复数据。

数据库写入成功后，不能因为归档失败而再次执行已经成功的数据库交付。

## 8.7 多目标交付

数据库和文件系统之间不存在天然的共同事务。

不要假设可以实现跨数据库和文件系统的原子提交。

目标是：

**持久化账本 + 幂等交付 + 独立重试 + 最终一致性。**

例如：

```text
文件读取成功
    │
    ▼
数据库写入成功
    │
    ▼
归档失败
    │
    ▼
账本记录数据库成功、归档失败
    │
    ▼
仅重试归档
```

归档操作应考虑临时文件、校验和原子重命名。

重试必须有边界、退避策略和可观测状态。

不能让 Elsa、Polly 和 Provider 各自无限重试同一个操作。

必须明确每一层的重试责任。

---

# 九、Web UI 实施规范

## 9.1 统一技术栈

固定使用：

- React。
- TypeScript。
- Ant Design。
- AntV G6。

Agent 必须先阅读 Cordis 当前 Web UI 源码，确认：

- React 版本。
- Ant Design 版本。
- 路由。
- API Client。
- 状态管理。
- 页面注册机制。
- UI 插件注册机制。
- 权限和菜单机制。
- 构建与打包方式。

优先复用已有方案。

禁止擅自引入 Vue、React Flow 或第二套 UI 组件库。

## 9.2 页面结构

至少需要以下页面：

### 框架页面

- Plugin Management。
- Runtime Status。
- Extension Management。

### 工业应用页面

- Dashboard。
- Machine Management。
- Resource Management。
- Workflow Management。
- Workflow Designer。
- Execution Center。
- Collection History。
- Credential Management。

每个页面应由合理的 UI 插件贡献。

不能把所有页面堆进一个巨大 App 组件。

## 9.3 第一阶段 UI 交付

至少要完成：

- UI Shell。
- Dashboard。
- 机器管理。
- 采集任务。

这些页面必须是真实可用页面，不是空白占位。

---

# 十、持久化与数据模型

Agent 必须先定义清楚数据所有权。

建议区分：

| 数据 | 所属子系统 |
|---|---|
| 插件安装状态 | Cordis Plugin System |
| 插件运行状态 | Cordis Runtime |
| Resource | Industrial Resource |
| Credential | Industrial Credential |
| Workflow Definition | Industrial Workflow |
| 发布版本 | Industrial Workflow |
| 执行状态 | Elsa / Industrial Execution |
| Collection | Industrial Collection |
| CollectionFile | Industrial Collection |
| FileDelivery | Industrial Collection |
| 页面布局 | UI 层 |

不能让两个子系统同时成为同一业务数据的权威来源。

数据库实体、API DTO、工作流 Definition、G6 图模型和 Elsa Definition 必须分离。

不能因为字段相似就让它们直接共享同一个持久化对象。

所有持久化结构必须考虑：

- 主键。
- 唯一约束。
- 外键。
- 索引。
- 并发更新。
- 版本迁移。
- 失败恢复。
- 删除约束。
- 历史记录保留。

---

# 十一、开发实施顺序

禁止同时铺开几十个插件。

必须按真实闭环逐步交付。

## Phase 0：源码基线与现状盘点

目标：建立准确的工程事实。

任务：

1. 确认 gocordis dotnet 分支 HEAD。
2. 确认 Cordis 当前测试和 CI 状态。
3. 盘点插件 Manifest、依赖、契约和 Backend。
4. 盘点 Runtime 生命周期。
5. 盘点 UI Host 和 UI 插件机制。
6. 盘点工业应用现有代码。
7. 盘点现有前端基础设施。
8. 输出缺失能力矩阵。

交付：

- `architecture/current-state.md`
- `architecture/gap-analysis.md`
- `architecture/decision-records/`

验收：

所有结论都有源码路径、类型、方法和测试证据。

不得根据 README 推断实现状态。

## Phase 1：Cordis 插件闭环

目标：证明插件机制真正可用。

完成：

- 插件 Manifest。
- 契约依赖。
- 插件依赖解析。
- Provide / Require。
- Provider 唯一性。
- 插件激活。
- 生命周期清理。
- UI 插件贡献。
- 插件安装和运行状态管理。

必须验证：

A、B 同时依赖 C。

A、B 不直接引用彼此。

C 只有一个符合预期的 Provider 实例。

停用 C 时，依赖关系得到正确处理。

UI 插件启用和停用时，页面、路由、菜单贡献正确增加和撤销。

验收：

能够运行真实的插件组合测试，而不是只测试 Manifest 解析。

## Phase 2：工业应用 Shell

目标：建立真正的工业 Web 应用。

完成：

- ASP.NET Core 宿主。
- Cordis Runtime 集成。
- Web UI Shell。
- 插件页面注册。
- Dashboard。
- Machine Management。
- Collection Task 页面。
- Plugin Management 页面。

验收：

安装、启用、停用业务 UI 插件时，页面行为与插件状态一致。

## Phase 3：Resource / Credential

目标：建立统一资源体系。

先实现：

- Local Filesystem Resource。
- UNC Resource。
- SQL Server Resource。
- Credential 管理。
- Resource Registry。
- Resource Provider。
- Connection / Lease 管理。

验收：

同一个 Resource 可以被多个工作流引用。

UNC 支持指定账号访问。

资源和凭据不进入工作流明文配置。

Provider 停用不能导致 Resource 数据丢失。

## Phase 4：工作流最小闭环 PoC

这是必须通过的技术验证阶段。

只实现少量节点：

1. Manual Trigger。
2. UNC File Source。
3. CSV Reader。
4. Mapping。
5. SQL Server Sink。

完成：

- 工业 Definition。
- 节点描述 API。
- G6 画布。
- Ant Design 属性编辑器。
- 工作流校验。
- 保存。
- 发布。
- Industrial Definition → Elsa Definition。
- Elsa 执行。
- 执行状态展示。

同时验证：

- Cordis 插件注册 Activity。
- 节点描述由后端提供。
- 新增节点不需要修改设计器核心。
- G6 模型不作为领域持久化模型。
- 大量数据不进入 Elsa Variables。

验收：

从 UI 创建工作流，保存、发布并成功执行，完整走通数据采集链路。

如果 Elsa 3 无法满足关键的执行持久化、恢复、插件 Activity 动态注册或版本兼容要求，应在本阶段明确记录问题并重新评估，而不是继续堆补丁。

## Phase 5：可靠采集

完成：

- 历史日期规划。
- 文件发现。
- 文件稳定性检查。
- 文件身份。
- Collection。
- CollectionFile。
- FileDelivery。
- 幂等数据库写入。
- 独立目标重试。
- 文件归档。
- 迟到文件扫描。
- 停机恢复。

验收：

模拟长时间停机、文件重复出现、数据库写入失败、归档失败、进程中断等情况。

必须验证不会重复交付已经成功的数据。

## Phase 6：扩展能力

在基础闭环稳定后，再增加：

- Excel Reader。
- MySQL Sink。
- PostgreSQL Sink。
- Event Trigger。
- 更多 Transform。
- 数据预览。
- 工作流向导。
- 批量管理。
- 执行诊断。

每个新增插件必须通过既有契约接入，不得不断修改核心系统。

---

# 十二、测试规范

测试不能只验证类方法返回值。

必须覆盖系统行为。

## 12.1 Cordis 测试

- 插件依赖解析。
- Provide / Require。
- 多消费者共享 Provider。
- 激活顺序。
- 生命周期补偿。
- 清理失败。
- 插件停用。
- 插件恢复。
- UI 扩展注册和撤销。
- 契约版本不兼容。
- 动态安装后的激活。

## 12.2 工作流测试

- Definition 序列化。
- Definition 反序列化。
- Schema 版本兼容。
- 节点注册。
- 端口类型检查。
- 图结构校验。
- G6 模型转换。
- 发布转换。
- Activity 执行。
- 执行恢复。
- 插件版本变化。
- 已发布工作流兼容性。

## 12.3 采集测试

- 文件正在写入。
- 文件重复发现。
- 文件内容变化。
- 迟到文件。
- 历史补采。
- 数据库断连。
- 数据库重复写入。
- 归档失败。
- 多目标部分成功。
- 进程异常退出。
- 重启恢复。

## 12.4 UI 测试

- 插件页面注册。
- 页面卸载。
- 路由权限。
- 节点拖放。
- 连线。
- 属性编辑。
- Definition 保存。
- 校验错误定位。
- 发布。
- 执行结果展示。

必须区分：

- Unit Test。
- Integration Test。
- Contract Test。
- End-to-End Test。

测试通过不能只看总数。

关键业务闭环必须有端到端测试。

---

# 十三、Agent 工作纪律

## 13.1 源码优先

每次接手任务，必须先确认当前代码。

不得假设上一次审查的结论仍然成立。

不得只看最近提交。

不得只修改用户指出的局部文件，而忽略其对全局的影响。

修改前必须分析：

- 调用者。
- 被调用者。
- 契约。
- 生命周期。
- 持久化。
- UI。
- 测试。
- 其他插件的影响。

## 13.2 禁止无意义重构

用户希望采用成熟方案，不希望反复造基础设施。

因此：

- 优先使用成熟库。
- 优先复用已有实现。
- 不为架构形式增加抽象。
- 不重复实现已有能力。
- 不保留无用代码。
- 不长期维护两套实现。
- 不为尚未纳入范围的 WASM / Process 提前建设复杂机制。
- 不因局部代码不够漂亮就重写稳定模块。

引入新库前必须说明：

1. 解决什么问题。
2. 当前项目是否已经有同类能力。
3. 为什么现有方案不够。
4. 新库带来的运行时和部署成本。
5. 如何测试。
6. 如何升级。

## 13.3 修改流程

每项工作按以下流程执行：

1. 阅读相关源码。
2. 阅读相关测试。
3. 确认实际问题。
4. 确认所属架构层。
5. 给出最小完整方案。
6. 实施代码。
7. 更新测试。
8. 运行测试。
9. 检查相关调用链。
10. 检查是否产生重复实现或无用代码。
11. 汇报修改结果。

不能永远停留在审查阶段。

也不能在没有明确问题的情况下直接大规模改写。

## 13.4 禁止局部修复

一个问题可能同时影响：

- 契约。
- Manifest。
- 依赖解析。
- Runtime。
- UI。
- 持久化。
- 测试。

Agent 必须检查完整链路。

例如新增一个工作流节点，不仅要检查 Activity 是否能够执行，还要检查：

- Cordis 插件能否注册。
- 契约是否可引用。
- 节点描述能否被 UI 获取。
- G6 是否能够正确呈现。
- 属性 Schema 是否正确。
- Definition 能否保存。
- 校验是否识别节点。
- 发布转换是否正确。
- 执行是否能恢复。
- 插件停用后如何处理已发布工作流。

## 13.5 每次交付报告

每个阶段必须提供：

- 本次完成内容。
- 修改文件。
- 架构变化。
- 新增和修改的契约。
- 测试结果。
- 未完成内容。
- 已知风险。
- 下一阶段任务。

不得使用“基本完成”“架构上已经支持”等模糊表述。

必须区分：

- 已实现。
- 已测试。
- 已集成。
- 已在真实环境验证。

---

# 十四、项目级禁止事项

以下行为视为违反项目规范：

1. 把工业业务逻辑加入 Cordis Core。
2. 让插件之间直接引用对方实现。
3. 为插件体系建设第二套生命周期。
4. 通过 fire-and-forget 掩盖生命周期清理。
5. 把 Elsa 当成插件管理器。
6. 把 Elsa 当成大数据量 ETL 引擎。
7. 把 G6 Graph Model 当成工作流领域模型。
8. 维护两份可独立编辑的工作流定义。
9. 将大量业务数据放入 Elsa Variables。
10. 把 Resource、Credential 和 Connection 混为一谈。
11. 把采集成功和交付成功混为一谈。
12. 用 LastSuccessDate 代替文件级补采账本。
13. 使用无边界的无限重试。
14. 重复造数据库连接池等成熟库已有能力。
15. 擅自切换 React、Ant Design、AntV G6 技术路线。
16. 引入 Vue、React Flow 或 Elsa Studio。
17. 为未来尚未实施的 WASM / Process 引入不必要的复杂性。
18. 只做后台 Demo，不做真实 UI。
19. 用大量单元测试掩盖缺失的端到端业务闭环。
20. 没有读取当前源码就声称功能已经实现。

---

# 十五、最终验收标准

项目完成不能以“编译通过”作为标准。

必须完成以下真实场景：

## 场景 A：插件扩展

安装一个新的 Reader 插件。

该插件不修改 Cordis Core、Workflow Core 和设计器核心。

节点自动出现在节点面板中。

用户可以配置、保存、发布并执行。

## 场景 B：资源共享

配置一个 UNC Resource 和一个 SQL Server Resource。

多个工作流可以共享这些资源。

用户不需要在每个节点重复输入密码。

## 场景 C：工作流创建

用户在 Web UI 中：

1. 创建工作流。
2. 拖入节点。
3. 连接端口。
4. 配置节点。
5. 保存。
6. 校验。
7. 发布。
8. 运行。

全过程不需要手工编辑 JSON 或 Elsa Definition。

## 场景 D：工业采集

系统从 UNC 共享目录读取 CSV。

完成解析、映射、转换和校验。

将数据幂等写入 SQL Server。

同时将源文件归档。

UI 能显示文件处理和目标交付状态。

## 场景 E：异常恢复

模拟数据库断连。

系统记录失败状态并按策略重试。

模拟数据库成功但归档失败。

系统只重试归档，不重复写数据库。

模拟进程中断。

重启后能够从持久化状态恢复。

## 场景 F：历史补采

模拟工业 PC 长时间离线。

恢复后系统扫描遗漏文件并补采。

已成功交付的文件不会重复产生业务数据。

## 场景 G：UI 插件生命周期

禁用某个 UI 插件。

对应页面、菜单和路由正确撤销。

重新启用后恢复。

不能出现页面残留、路由冲突或重复注册。

---

# 十六、Agent 首轮执行任务

收到本规范后，不要立即开始大规模重构。

第一轮只做以下工作：

1. 确认 Cordis `dotnet` 分支的实际 HEAD。
2. 确认工业应用仓库及当前分支。
3. 分别盘点 Cordis 和工业应用的现状。
4. 对照本规范建立完整的能力矩阵。
5. 标记已实现、未实现、部分实现、存在缺陷和待验证。
6. 检查 React、Ant Design、AntV G6 当前实际集成状态。
7. 检查 Elsa 3 的集成可行性和 PoC 范围。
8. 给出阶段实施计划和任务依赖图。
9. 列出真正阻塞后续工作的架构问题。
10. 形成第一批可以直接实施的任务。

第一轮不允许：

- 重写 Cordis Core。
- 大规模调整目录。
- 新建几十个插件。
- 重做已经稳定的模块。
- 只输出抽象架构图。
- 用设计文档替代源码审查。

第一轮结束后，必须进入实际实施。

---

# 十七、最终原则

这个项目的目标不是创造一套看起来很先进的框架，而是使用 Cordis 构建一个真正可用、可扩展、可靠的工业数据采集产品。

所有技术决策都必须服务于这个目标。

**Cordis 提供插件化动态组合能力。**

**Industrial 提供工业业务能力。**

**Elsa 提供工作流执行能力。**

**React + Ant Design 提供产品 UI。**

**AntV G6 提供工作流画布。**

**工业 Definition 负责表达用户的工作流。**

**采集账本负责保证数据最终可靠交付。**

Agent 的最终任务是把这些部分连接成一个真实、完整、可验证的产品，而不是不断增加抽象、重复实现基础设施，或者陷入无止境的局部修复。

以真实业务闭环作为最终完成标准。

### 我建议额外强调三个执行问题

这份规范已经可以作为 Agent 的总任务书，但真正开始执行时，有三个地方不能跳过。

第一，Cordis 与工业应用必须分开验收。 Cordis 的插件组合能力通过测试，不代表工业应用已经完成；工业应用能跑起来，也不代表 Cordis 的生命周期语义正确。两者需要独立验收，再做集成验收。

第二，Elsa 3 必须先做小规模 PoC。 不要先围绕 Elsa 建设完整工作流系统。先证明 Cordis 插件贡献 Activity、工业 Definition 转换、持久化执行、恢复和插件版本兼容能够形成闭环。

第三，先做一条真实采集链路。 不要先把所有节点、插件和页面铺开。优先实现 UNC → CSV → Mapping → SQL Server → Archive，把资源共享、工作流、插件、账本、重试、UI 全部串起来。这条链路通过后，再扩展 Excel、MySQL、PostgreSQL 等能力。

一个重要提醒： 规范中的技术选型和边界已经明确，但它不是对当前仓库实现情况的认定。Agent 必须从当前 `dotnet` 分支和工业应用实际源码开始，建立现状矩阵，再决定哪些需要开发、哪些已经存在。不能照着规范把已有功能重新造一遍。

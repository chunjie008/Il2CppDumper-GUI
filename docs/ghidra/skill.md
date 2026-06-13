---
name: ghidra
description: Ghidra 逆向工程框架 — 反汇编、反编译、脚本分析、无头批量处理
---

# Ghidra Reverse Engineering Framework

Ghidra 是 NSA 开发的开源逆向工程框架，支持 x86/x64、ARM/AArch64、MIPS、PowerPC、RISC-V 等多种架构的交互式和自动化分析。

## 项目结构

```
ghidra/
├── Ghidra/Processors/     # 处理器模块 (SLEIGH 规范)
│   ├── AARCH64/           # ARM64 (~54K 行 .sinc)
│   ├── ARM/ x86/ MIPS/    # 等 20+ 个处理器
│   └── ...
├── Ghidra/Features/       # 功能模块 (36 个)
│   ├── Base/              # 核心 + 无头 API
│   ├── Decompiler/        # 反编译器 (C 代码输出)
│   ├── PDB/               # Windows PDB 符号解析
│   ├── VersionTracking/   # 二进制差异对比
│   ├── BSim/              # 行为相似性搜索
│   ├── FunctionID/        # 库函数识别
│   ├── PyGhidra/          # Python 3 脚本支持
│   ├── GhidraServer/      # 团队协作服务器
│   ├── Swift/             # Swift 语言分析
│   ├── FileFormats/       # 文件格式解析
│   ├── SystemEmulation/   # 模拟执行
│   └── ...
├── Ghidra/Framework/      # 框架层 (GUI/文件系统/客户端/工具)
├── Ghidra/Debug/          # 调试器框架 (15 个子模块)
├── Ghidra/Extensions/     # 可选扩展 (Jython/SleighDevTools 等)
├── Ghidra/RuntimeScripts/ # 启动脚本 (analyzeHeadless/ghidraRun)
├── Ghidra/Test/           # 集成测试基础设施
├── GhidraBuild/           # 构建系统 (Gradle)
└── GhidraDocs/            # 文档/培训材料
```

## 无头模式 (Headless / `analyzeHeadless`)

在 `Ghidra/RuntimeScripts/support/analyzeHeadless` (或 `.bat`)：

### 两种操作模式

| 模式 | 说明 |
|------|------|
| `-import <file/dir>` | 导入新文件到项目，可选分析/脚本 |
| `-process [<file>]` | 处理项目中已有的文件 |

### 存储方式
- **本地项目**: `<project_location> <project_name>[/<folder_path>]`
- **Ghidra Server**: `ghidra://<server>[:<port>]/<repository_name>[/<folder_path>]`

### 脚本系统
```
-preScript <ScriptName.ext> [<arg>]*   # 分析前运行
-postScript <ScriptName.ext> [<arg>]*  # 分析后运行
-scriptPath "<path1>[;<path2>...]"      # 脚本搜索路径
-propertiesPath "<path1>[;<path2>...]"  # .properties 配置路径
-scriptlog <path>                       # 脚本日志
```

脚本通过 `getScriptArgs()` 获取命令行参数，或用 `.properties` 文件 + `askXxx()` 方法传值。

### 核心 API 类

**`ghidra.app.util.headless` 包：**

| 类 | 说明 |
|----|------|
| `AnalyzeHeadless` | 入口点，实现 `GhidraLaunchable`，解析 CLI 参数 |
| `HeadlessAnalyzer` | 单例协调器：项目创建/打开、导入、分析、脚本执行 |
| `HeadlessOptions` | 配置 Bean，持有全部 CLI 参数字段（通过 setter 访问） |
| `HeadlessScript` | 无头脚本基类（扩展 `GhidraScript`），提供： |
| | `setHeadlessContinuationOption()` — 控制程序处置（CONTINUE / ABORT / DELETE） |
| | `getHeadlessContinuationOption()` — 查询当前处置选项 |
| | `enableHeadlessAnalysis()` / `isHeadlessAnalysisEnabled()` — 分析开关 |
| | `setHeadlessImportDirectory()` — 更改导入保存目录 |
| | `isImporting()` — 判断是否 import 模式 |
| | `storeHeadlessValue()` / `getStoredHeadlessValue()` / `headlessStorageContainsKey()` — 脚本间传值 |
| | `analysisTimeoutOccurred()` — 检测分析超时 |
| `HeadlessErrorLogger` | 无 Log4j 时的文件日志写入器 |
| `HeadlessTimedTaskMonitor` | 实现 `-analysisTimeoutPerFile` 超时监控 |
| `GhidraScriptRunner` | 单脚本启动器（无项目/程序上下文） |

**`ghidra.framework` 包：**

| 类 | 说明 |
|----|------|
| `HeadlessGhidraApplicationConfiguration` | 无头应用初始化配置（跳过 GUI 初始化） |

**`ghidra.framework.client` 包：**

| 类 | 说明 |
|----|------|
| `HeadlessClientAuthenticator` | 无头模式 Ghidra Server 认证（控制台密码/SSH/PKI） |

### 常用 CLI 参数

| 参数 | 说明 |
|------|------|
| `-recursive [<depth>]` | 递归遍历目录/容器文件 |
| `-overwrite` | 覆盖已有文件 |
| `-readOnly` | 只读（不保存更改） |
| `-noanalysis` | 关闭自动分析 |
| `-processor <languageID>` | 强制指定处理器（如 `x86:LE:64:default`） |
| `-cspec <compilerSpecID>` | 编译器规范（如 `windows`, `gcc`） |
| `-analysisTimeoutPerFile <秒>` | 单文件分析超时 |
| `-max-cpu <cores>` | CPU 核心数限制 |
| `-loader <name>` | 指定加载器（Binary/ELF/PE/MachoLoader） |
| `-loader-<arg> <value>` | 加载器参数（如 `-loader-baseAddr 0x1000`） |
| `-deleteProject` | 完成后删除新创建的项目 |
| `-commit ["<comment>"]` | 提交更改到 Ghidra Server |
| `-okToDelete` | 允许 `-process` 模式中删除已有程序 |

### HeadlessScript 程序处置选项

通过 `setHeadlessContinuationOption()` 控制：

| 选项 | import 模式 | process 模式 |
|------|------------|-------------|
| `CONTINUE` (默认) | 继续处理并保存 | 继续处理并保存更改 |
| `CONTINUE_THEN_DELETE` | 继续处理但不导入 | 继续处理后删除 |
| `ABORT` | 跳过后续脚本/分析但导入 | 跳过后续并保存 |
| `ABORT_AND_DELETE` | 跳过且不导入 | 跳过且删除 |

多脚本组合规则：ABORT > ABORT_AND_DELETE > CONTINUE_THEN_DELETE > CONTINUE

### 完整命令行格式
```
analyzeHeadless <project_location> <project_name>[/<folder_path>] |
                ghidra://<server>[:<port>]/<repository_name>[/<folder_path>]
    [[-import [<directory>|<file>]+] | [-process [<project_file>]]]
    [-preScript <ScriptName.ext> [<arg>]*]
    [-postScript <ScriptName.ext> [<arg>]*]
    [-scriptPath "<path1>[;<path2>...]"]
    [-propertiesPath "<path1>[;<path2>...]"]
    [-scriptlog <path>] [-log <path>]
    [-overwrite] [-mirror] [-recursive [<depth>]] [-readOnly]
    [-deleteProject] [-noanalysis]
    [-processor <languageID>] [-cspec <compilerSpecID>]
    [-analysisTimeoutPerFile <timeout>]
    [-keystore <KeystorePath>] [-connect [<userID>]] [-p]
    [-commit ["<comment>"]] [-okToDelete]
    [-max-cpu <cores>]
    [-librarySearchPaths <path1>[;<path2>...]]
    [-loader <name>] [-loader-<arg> <value>]
```

### 脚本级 API 示例

```java
// 无头脚本基类
public class MyScript extends HeadlessScript {
    protected void run() throws Exception {
        // 控制程序处置
        setHeadlessContinuationOption(HeadlessContinuationOption.ABORT_AND_DELETE);
        // 开关分析
        enableHeadlessAnalysis(false);
        // 更改导入目录
        setHeadlessImportDirectory("subdir/packets");
        // 脚本间共享
        storeHeadlessValue("key", someObject);
        Object val = getStoredHeadlessValue("key");
        // 检测超时
        if (analysisTimeoutOccurred()) { ... }
    }
}
```

## GhidraScript 编程 API

### 事务与修改

```java
int txId = currentProgram.startTransaction("描述");
// ... 对程序的修改 ...
currentProgram.endTransaction(txId, true); // true=提交, false=回滚
```

### 地址操作

```java
Address addr = toAddr(0x1000);         // 或 toAddr("0x1000")
Address current = currentAddress;       // 当前光标
Address min = currentProgram.getMinAddress();
Address max = currentProgram.getMaxAddress();
```

### 函数管理
FunctionManager fm = currentProgram.getFunctionManager();
Function func = fm.getFunctionAt(addr);
Function func = fm.getFunctionContaining(addr);
for (Function f : fm.getFunctions(true)) { ... }

// 符号表
SymbolTable st = currentProgram.getSymbolTable();
List<Symbol> syms = st.getSymbols("functionName", null);

// 程序段
MemoryBlock[] blocks = currentProgram.getMemory().getBlocks();

// 指令/数据访问 (Listing)
Listing listing = currentProgram.getListing();
Instruction ins = listing.getInstructionAt(addr);
Data data = listing.getDataAt(addr);
CodeUnit cu = listing.getCodeUnitAt(addr);

// 函数交叉引用 (入向)
ReferenceIterator refs = currentProgram.getReferenceManager()
    .getReferencesTo(addr);
// 函数交叉引用 (出向) — 从函数出发的引用
Reference[] outgoing = currentProgram.getReferenceManager()
    .getReferencesFrom(addr);

// 书签
BookmarkManager bm = currentProgram.getBookmarkManager();
bm.setBookmark(addr, "type", "category", "comment");

// 数据类型创建 (ghidra.program.model.data.DataUtilities)
import ghidra.program.model.data.DataUtilities;
if (DataUtilities.isUndefinedData(currentProgram, addr)) {
    DataUtilities.createData(currentProgram, addr, DWordDataType.dataType, 4, false,
        ClearDataMode.CLEAR_ALL_CONFLICT_DATA);
}
```

### 反编译器

```java
DecompInterface dec = new DecompInterface();
dec.openProgram(currentProgram);
dec.setSimplificationStyle("normalize");
DecompileResults res = dec.decompileFunction(func, 30, monitor);
if (res != null && res.getDecompiledFunction() != null) {
    String cCode = res.getDecompiledFunction().getC();
    // res.getHighFunction() — 高级中间表示
    // res.isTimedOut() / getNumErrors() / getErrorMessage(i)
}
dec.dispose(); // 释放资源

### 查找函数调用者（交叉引用追踪）

```java
ReferenceIterator refs = currentProgram.getReferenceManager()
    .getReferencesTo(func.getEntryPoint());
while (refs.hasNext()) {
    Reference ref = refs.next();
    if (ref.getReferenceType().isCall()) {
        Function caller = fm.getFunctionContaining(ref.getFromAddress());
    }
}
```

### 常用判断
- `func.isThunk()` — 是否跳转桩函数
- `func.isExternal()` — 是否外部导入函数（来自其他 DLL）
- `func.isLibrary()` — 是否库函数
- `func.getBody().getNumAddresses()` — 函数体指令数

## 处理器模块结构

处理器定义在 `Ghidra/Processors/<name>/` 下：

```
data/languages/
├── <proc>.slaspec             # SLEIGH 规范入口 (含 @include 其他 .sinc)
├── <proc>BE.slaspec           # 大端变体 (若适用)
├── <proc>_Variant.slaspec     # 平台变体 (如 AppleSilicon)
├── <proc>instructions.sinc    # 寄存器/空间定义、枚举、include 汇总
├── <proc>base.sinc            # 核心整数/分支/系统指令
├── <proc>neon.sinc            # SIMD/FP 指令 (手写)
├── <proc>ldst.sinc            # 加载/存储指令
├── <proc>sve.sinc             # 向量扩展 (如 SVE)
├── <proc>_AMXext.sinc         # 平台扩展 (如 Apple AMX)
├── <proc>.ldefs               # 语言定义 (id/endian/size/variant/version)
├── <proc>.pspec               # 处理器规范 (寄存器/上下文/评级)
├── <proc>.cspec               # 默认编译器规范 (调用约定)
├── <proc>_platform.cspec      # 平台规范 (如 _win/_apple/_golang)
├── <proc>.dwarf               # DWARF 寄存器映射
├── <proc>_golang.register.info # Go 寄存器使用信息
├── <proc>.opinion             # 加载器映射 (ELF EM_/Mach-O CPU_/PE machine → 语言)
├── patterns/                  # 函数入口/前置模式 XML
└── manuals/                   # 处理器手册索引
```

`AARCH64` 示例（Platinum 评级，`AARCH64.ldefs` + `AppleSilicon.ldefs`）— 5 种语言变体：
- `AARCH64:LE:64:v8A` — 小端 64 位 (v8.5-A)
- `AARCH64:BE:64:v8A` — 大端数据 (指令仍小端)
- `AARCH64:LE:32:ilp32` — ILP32 ABI (32 位指针)
- `AARCH64:BE:32:ilp32` — 大端数据 ILP32
- `AARCH64:LE:64:AppleSilicon` — Apple Silicon (+ AMX 扩展)
编译器规范: `default` (Linux) / `windows` / `apple` / `golang` / `ilp32`


## 分析类型（AutoAnalysisManager 报告）

典型分析阶段及耗时参考（~17MB DLL）：
- Disassemble Entry Points (~50s)
- Stack Analysis (~115s)
- Decompiler Switch Analysis (~109s)
- x86 Constant Reference (~118s)
- Scalar Operand References (~18s)
- Function ID (~19s)
- Data Reference (~6s)
- **总计约 500-600s** (视文件大小)

## 常见加载器参数

| 加载器 | 参数 |
|--------|------|
| **BinaryLoader** | `-loader-blockName <name>` `-loader-baseAddr <addr>` `-loader-fileOffset <offs>` `-loader-length <len>` `-loader-applyLabels <true\|false>` `-loader-anchorLabels <true\|false>` |
| **ElfLoader** | `-loader-imagebase <base>` `-loader-dataImageBase <base>` `-loader-applyRelocations <true\|false>` `-loader-applyUndefinedData <true\|false>` `-loader-loadLibraries <true\|false>` `-loader-libraryLoadDepth <n>` `-loader-linkExistingProjectLibraries <true\|false>` `-loader-projectLibrarySearchFolder <path>` `-loader-libraryDestinationFolder <path>` `-loader-applyLabels <true\|false>` `-loader-anchorLabels <true\|false>` `-loader-includeOtherBlocks <true\|false>` `-loader-maxSegmentDiscardSize <0..255>` |
| **PeLoader** | `-loader-loadLibraries <true\|false>` `-loader-libraryLoadDepth <n>` `-loader-linkExistingProjectLibraries <true\|false>` `-loader-projectLibrarySearchFolder <path>` `-loader-libraryDestinationFolder <path>` `-loader-ordinalLookup <true\|false>` `-loader-parseCliHeaders <true\|false>` `-loader-applyLabels <true\|false>` `-loader-anchorLabels <true\|false>` `-loader-showDebugLineNumbers <true\|false>` |
| **MachoLoader** | `-loader-loadLibraries <true\|false>` `-loader-libraryLoadDepth <n>` `-loader-linkExistingProjectLibraries <true\|false>` `-loader-projectLibrarySearchFolder <path>` `-loader-libraryDestinationFolder <path>` `-loader-applyLabels <true\|false>` `-loader-anchorLabels <true\|false>` `-loader-reexport <true\|false>` |

## 编写 GhidraScript 注意事项

1. **BOM 问题**: 脚本文件须为 UTF-8 **无 BOM** 编码，BOM 会导致 OSGi 类加载器编译失败（实验中验证，非官方文档）
2. **写入方式**: 用 `[System.IO.File]::WriteAllText(path, content, [System.Text.UTF8Encoding]::new($false))` 写入无 BOM 文件；`Set-Content -Encoding UTF8` 会引入 BOM
3. **类名匹配**: `public class 类名` 必须与文件名 `类名.java` 一致
4. **事务**: 任何对程序的修改必须在事务中执行（`startTransaction` / `endTransaction`）
5. **反编译器**: `DecompInterface` 每次使用后须调用 `dispose()` 释放资源；超时参数（秒）控制每个函数的最大反编译时间
6. **Il2CPP 游戏**: Unity Il2CPP 编译的函数名可能为 `FUN_1801XXXXX` 格式，需通过交叉引用分析；baselib socket 函数可能为导出桩，内部无调用者

## 适用场景
- 二进制导入/分析自动化（批量处理固件、恶意软件、游戏）
- 反编译 + 交叉引用分析（封包协议、加密算法逆向）
- 特征搜索（常量、字符串、指令模式）
- CI/CD 集成：自动化分析 + 报告输出

## 其他核心功能模块

| 模块 | 路径 | 用途 |
|------|------|------|
| **GUI 交互分析** | Ghidra 主程序 | 图形化反汇编/反编译/数据类型编辑 (需 JDK 21+) |
| **Ghidra Server** | `Ghidra/Features/GhidraServer/` | 团队协作仓库，支持版本控制/共享项目 |
| **调试器** | `Ghidra/Debug/` (15 子模块) | 用户态/内核态实时调试 (WinDbg/GDB/lldb 后端) |
| **PDB 符号** | `Ghidra/Features/PDB/` | Windows PDB 符号文件加载与类型恢复 |
| **Version Tracking** | `Ghidra/Features/VersionTracking/` | 二进制差异对比/补丁分析/签名匹配 |
| **BSim** | `Ghidra/Features/BSim/` | 行为相似性搜索 (大规模二进制去重) |
| **Function ID** | `Ghidra/Features/FunctionID/` | 静态库函数识别 (基于哈希) |
| **PyGhidra** | `Ghidra/Features/PyGhidra/` | Python 3 原生脚本 (替代 Jython)，支持 `pyghidra.launch()` |
| **File Formats** | `Ghidra/Features/FileFormats/` | 非可执行文件格式解析 (Android OAT/DEX/ISO/UBIFS 等) |
| **Swift** | `Ghidra/Features/Swift/` | Swift 二进制分析/命名还原 |
| **System Emulation** | `Ghidra/Features/SystemEmulation/` | 模拟执行/用户态系统调用模拟 |
| **Jython** | `Ghidra/Extensions/Jython/` | Python 2.7 脚本 (已逐步被 PyGhidra 取代) |
| **SleighDevTools** | `Ghidra/Extensions/SleighDevTools/` | SLEIGH 处理器开发辅助工具 |

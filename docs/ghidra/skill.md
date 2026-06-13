---
name: ghidra
description: Ghidra 逆向工程框架 — 反汇编、反编译、脚本分析、无头批量处理
---

# Ghidra Reverse Engineering Framework

Ghidra 是 NSA 开发的开源逆向工程框架，支持 x86/x64、ARM/AArch64、MIPS、PowerPC、RISC-V 等多种架构的交互式和自动化分析。

## 项目结构

> **安装目录**：`D:\yingyong\ghidra_12.1.2_PUBLIC`（以下结构基于安装目录，非源码仓库）

```
ghidra_12.1.2_PUBLIC/
├── ghidraRun.bat / ghidraRun          # GUI 启动脚本
├── support/                           # 无头/调试/构建工具
│   ├── analyzeHeadless.bat            # 无头模式入口
│   ├── ghidraDebug.bat                # 调试模式
│   └── ...
├── GPL/                               # GPL 依赖
├── Ghidra/Processors/                 # 处理器模块 (SLEIGH 规范)
│   ├── AARCH64/                       # ARM64 (~54K 行 .sinc)
│   ├── ARM/ x86/ MIPS/                # 等 20+ 个处理器
│   └── ...
├── Ghidra/Features/                   # 功能模块 (36 个)
│   ├── Base/                          # 核心 + 无头 API
│   ├── Decompiler/                    # 反编译器 (C 代码输出)
│   ├── PDB/                           # Windows PDB 符号解析
│   ├── VersionTracking/               # 二进制差异对比
│   ├── BSim/                          # 行为相似性搜索
│   ├── FunctionID/                    # 库函数识别
│   ├── PyGhidra/                      # Python 3 脚本支持
│   ├── GhidraServer/                  # 团队协作服务器
│   ├── Swift/                         # Swift 语言分析
│   ├── FileFormats/                   # 文件格式解析
│   ├── SystemEmulation/               # 模拟执行
│   └── ...
├── Ghidra/Framework/                  # 框架层 (GUI/文件系统/客户端/工具)
├── Ghidra/Debug/                      # 调试器框架 (15 个子模块)
├── Ghidra/Extensions/                 # 可选扩展 (Jython/SleighDevTools 等)
├── Ghidra/RuntimeScripts/             # 启动脚本
├── Ghidra/Test/                       # 集成测试基础设施
├── Ghidra/Docs/                       # 文档/培训材料
├── docs/                              # API 文档 (GhidraAPI_javadoc.zip)
└── Extensions/Eclipse/                # Eclipse 插件
```

## 环境要求

- **JDK 21+**（Ghidra 12.1 必须；GUI 和 headless 均需要）
  - 实测 JDK 25 也可用（仅有 `sun.misc.Unsafe` 已废弃警告，不影响功能）
- `JAVA_HOME` 须指向 JDK 21+ 安装目录
- `GHIDRA_HOME` 为安装目录（如 `D:\yingyong\ghidra_12.1.2_PUBLIC`），`analyzeHeadless.bat` 会自动推断
- 脚本搜索路径：`-scriptPath` 支持绝对路径和相对于 `GHIDRA_HOME` 的路径

## 无头模式 (Headless / `analyzeHeadless`)

入口位于安装目录下 `support/analyzeHeadless.bat`（或 `support/analyzeHeadless`）：

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
| `-mirror` | 镜像模式：项目文件路径镜像源文件目录结构 |
| `-log <path>` | 应用日志输出路径（XML 格式） |
| `-scriptlog <path>` | 脚本日志输出路径 |
| `-keystore <path>` | PKI 密钥库路径（Ghidra Server 认证） |
| `-connect [<userID>]` | 连接 Ghidra Server，可选指定用户 ID |
| `-p` | 交互式密码输入（须与 `-connect` 配合） |
| `-librarySearchPaths <path1>[;<path2>...]` | 外部库搜索路径（加载 .dll/.so 依赖时使用） |

### 实战命令示例

> `GHIDRA_HOME` = `D:\yingyong\ghidra_12.1.2_PUBLIC`

**场景 1 — 导入 PE/DLL 并运行分析脚本**
```
& "$env:GHIDRA_HOME\support\analyzeHeadless.bat" D:\projects TempProject -import target.dll -postScript ExportFunctions.java -scriptPath D:\scripts -overwrite -deleteProject
```

**场景 2 — 指定处理器/加载器导入裸二进制**
```
& "$env:GHIDRA_HOME\support\analyzeHeadless.bat" D:\projects TempProject -import firmware.bin -processor x86:LE:64:default -loader BinaryLoader -loader-baseAddr 0x1000 -overwrite -deleteProject
```

**场景 3 — 批量导入目录，递归分析，带超时**
```
& "$env:GHIDRA_HOME\support\analyzeHeadless.bat" D:\projects BatchProject -import D:\samples -recursive -postScript BatchReport.java -scriptPath D:\scripts -analysisTimeoutPerFile 600 -overwrite -deleteProject
```

### 常用 LanguageID 速查

| 架构 | LanguageID (`-processor`) | 说明 |
|------|--------------------------|------|
| **x86 32-bit** | `x86:LE:32:default` | 32 位小端，PE/ELF 通常自动检测 |
| **x86 64-bit** | `x86:LE:64:default` | 64 位小端（可配合 `-cspec windows`） |
| **ARM 32-bit** | `ARM:LE:32:v8` | ARMv8 32 位模式 |
| **ARM Thumb** | `ARM:LE:32:v8T` | ARM Thumb 指令集 |
| **AARCH64** | `AARCH64:LE:64:v8A` | ARM64 小端（Linux 默认） |
| **AARCH64 Apple** | `AARCH64:LE:64:AppleSilicon` | Apple Silicon（+AMX 扩展） |
| **MIPS 32-bit** | `MIPS:BE:32:default` | 大端 MIPS32 |
| **MIPSEL 32** | `MIPS:LE:32:default` | 小端 MIPS32（常见嵌入式 Linux） |
| **PowerPC 32** | `PowerPC:BE:32:default` | 大端 PPC32 |
| **PowerPC 64** | `PowerPC:BE:64:default` | 大端 PPC64 |
| **RISC-V 64** | `RISCV:LE:64:default` | RV64GC |

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

### 完整 HeadlessScript 模板（可直接使用）

```java
// ExportFunctions.java — 导出所有函数名及其反编译代码到文件
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;

public class ExportFunctions extends HeadlessScript {
    @Override
    protected void run() throws Exception {
        String outPath = getScriptArgs()[0];  // 从命令行参数获取输出路径
        PrintWriter pw = new PrintWriter(new FileWriter(outPath));

        FunctionManager fm = currentProgram.getFunctionManager();
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        dec.setSimplificationStyle("normalize");

        for (Function func : fm.getFunctions(true)) {
            if (func.isThunk() || func.isExternal()) continue;

            pw.println("// ===== " + func.getName() + " @ " + func.getEntryPoint() + " =====");

            DecompileResults res = dec.decompileFunction(func, 30, monitor);
            if (res != null && res.decompileCompleted()) {
                pw.println(res.getDecompiledFunction().getC());
            } else {
                pw.println("// decompilation failed or timed out");
            }
            pw.println();
        }

        dec.dispose();
        pw.close();
        printf("Exported %d functions to %s\n", fm.getFunctionCount(), outPath);
    }
}
```

> **调用方式**: `-postScript ExportFunctions.java D:\out\result.c -scriptPath D:\scripts`

### HeadlessScript 高级 API

| 方法 | 说明 |
|------|------|
| `setHeadlessContinuationOption(HeadlessContinuationOption.ABORT_AND_DELETE)` | 控制程序处置 |
| `enableHeadlessAnalysis(false)` | 开关自动分析 |
| `setHeadlessImportDirectory("subdir")` | 更改导入保存目录 |
| `storeHeadlessValue("key", obj)` / `getStoredHeadlessValue("key")` | 脚本间传值 |
| `analysisTimeoutOccurred()` | 检测是否分析超时 |
| `isImporting()` | 判断当前是否 import 模式 |

## GhidraScript 编程 API

> 以下示例依赖的核心 import（GhidraScript 中已自动可用，编写 Java 工具类时需显式导入）：
> ```java
> import ghidra.program.model.listing.*;
> import ghidra.program.model.address.*;
> import ghidra.program.model.symbol.*;
> import ghidra.program.model.mem.*;
> import ghidra.program.model.data.*;
> import ghidra.app.decompiler.*;
> ```

### 事务与修改

```java
int txId = currentProgram.startTransaction("描述");
// ... 对程序的修改 ...
currentProgram.endTransaction(txId, true); // true=提交, false=回滚
```

### 地址操作

```java
Address addr = toAddr(0x1000);         // 或 toAddr("0x1000")
// currentAddress — 当前光标（headless 模式下为 null，不可用）
// Address current = currentAddress;
Address min = currentProgram.getMinAddress();
Address max = currentProgram.getMaxAddress();
```

### 函数管理

```java
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

> ⚠ `-noanalysis` 下函数体为空，反编译前需手动 `disassemble(addr)` 以生成指令流。

```java
DecompInterface dec = new DecompInterface();
dec.openProgram(currentProgram);
dec.setSimplificationStyle("normalize");

// 确保函数已反汇编
disassemble(func.getEntryPoint());

DecompileResults res = dec.decompileFunction(func, 30, monitor);
if (res != null && res.getDecompiledFunction() != null) {
    String cCode = res.getDecompiledFunction().getC();
    // res.getHighFunction() — 高级中间表示
    // res.isTimedOut() / getNumErrors() / getErrorMessage(i)
}
dec.dispose(); // 释放资源
```

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
                                                                                           
### 内存搜索与数据读取

**字节序列搜索** — 搜索已知常量（AES S-box、CRC 表、TEA delta 等）：

```java
// 搜索精确字节序列（返回首次匹配地址）
byte[] pattern = {0x63, 0x7C, 0x77, 0x7B, (byte)0xF2, 0x6B, 0x6F, (byte)0xC5};
Address hit = find(currentProgram.getMinAddress(), pattern);

// 搜索正则字节模式（支持通配符 ??）
Address[] hits = findBytes(currentProgram.getMinAddress(), "558bec83ec??", 100);

// 搜索 TEA delta 常量（0x9E3779B9 的 little-endian 字节序）
byte[] teaDelta = {(byte)0xB9, 0x79, 0x37, (byte)0x9E};
Address teaAddr = find(currentProgram.getMinAddress(), teaDelta);
```

**原始内存读取**：

```java
byte[] bytes = getBytes(addr, 256);       // 读取 256 字节
int val32 = getInt(addr);                  // 读取 32 位整数
short val16 = getShort(addr);             // 读取 16 位整数
long val64 = getLong(addr);               // 读取 64 位整数
```

**字符串搜索**：

```java
// 搜索长度 ≥ 8 的 ASCII 字符串（1 字节对齐，不要求 null 终止符）
List<FoundString> strings = findStrings(
    currentProgram.getMemory().getLoadedAndInitializedAddressSet(),
    8, 1, false, false);
for (FoundString s : strings) {
    if (s.getString().contains("encrypt")) {
        printf("Found: %s @ %s\n", s.getString(), s.getAddress());
    }
}
```

**实战：常量特征定位加密函数**

```java
// 搜索 AES S-box 并通过交叉引用定位调用者
byte[] aesSbox = {
    0x63, 0x7C, 0x77, 0x7B, (byte)0xF2, 0x6B, 0x6F, (byte)0xC5,
    0x30, 0x01, 0x67, 0x2B, (byte)0xFE, (byte)0xD7, (byte)0xAB, 0x76
};
Address sboxAddr = find(currentProgram.getMinAddress(), aesSbox);
if (sboxAddr != null) {
    printf("AES S-box at %s\n", sboxAddr);
    ReferenceIterator refs = currentProgram.getReferenceManager()
        .getReferencesTo(sboxAddr);
    while (refs.hasNext()) {
        Reference ref = refs.next();
        Function caller = fm.getFunctionContaining(ref.getFromAddress());
        if (caller != null) {
            printf("  → used by: %s @ %s\n", 
                caller.getName(), caller.getEntryPoint());
        }
    }
}
```

### 指令模式分析

**遍历函数体内指令**：

```java
// 方法 1 — 从函数首指令用 getInstructionAfter 遍历
Instruction ins = getFirstInstruction(func);
while (ins != null && func.getBody().contains(ins.getAddress())) {
    String mnemonic = ins.getMnemonicString();
    int numOps = ins.getNumOperands();
    Address addr = ins.getAddress();
    // ... 分析 ...
    ins = getInstructionAfter(ins);
}
```

**获取操作数**：

```java
// 操作数字符串表示
String opStr = ins.getDefaultOperandRepresentation(0);  // 第一个操作数
// 标量立即数（如 EAX=0x1 中的 0x1）
Scalar scalar = ins.getScalar(0);
if (scalar != null) {
    long imm = scalar.getUnsignedValue();
}
// 操作数个数
int numOps = ins.getNumOperands();
```

**反调试检测 — CPUID / RDTSC / NOP 陷阱**：

```java
// 遍历每个函数的指令，匹配反调试特征
for (Function func : fm.getFunctions(true)) {
    if (func.isThunk() || func.isExternal()) continue;
    Instruction ins = getFirstInstruction(func);
    boolean hasAntiDebug = false;
    while (ins != null && func.getBody().contains(ins.getAddress())) {
        String mnem = ins.getMnemonicString().toUpperCase();
        // CPUID — 常用于检测 hypervisor
        if (mnem.equals("CPUID")) {
            hasAntiDebug = true;
            break;
        }
        // RDTSC / RDTSCP — 时间检测反调试
        if (mnem.equals("RDTSC") || mnem.equals("RDTSCP")) {
            hasAntiDebug = true;
            break;
        }
        // 大量 NOP 连续 — Intel Pin / 调试器痕迹
        if (mnem.equals("NOP")) {
            // 检查上下文是否连续 NOP
        }
        // INT3 (0xCC) — 软件断点
        if (mnem.equals("INT3") || mnem.equals("INT")) {
            Scalar s = ins.getScalar(0);
            if (s != null && s.getUnsignedValue() == 3) {
                hasAntiDebug = true;
                break;
            }
        }
        ins = getInstructionAfter(ins);
    }
    if (hasAntiDebug) {
        printf("Potential anti-debug: %s @ %s\n", func.getName(), func.getEntryPoint());
    }
}
```

**反 VM 检测 — IN 指令 + 魔数端口**：

```java
// VMware/VirtualBox 常用 I/O 端口
//   0x5658 (VX) / 0x5659 (VY)     — VMware
//   0x5040 (P@)                     — VBox
if (mnem.equals("IN")) {
    Scalar s = ins.getScalar(1);  // 端口号（第二个操作数）
    if (s != null) {
        long port = s.getUnsignedValue();
        if (port == 0x5658 || port == 0x5659 || port == 0x5040) {
            hasAntiVM = true;
        }
    }
}
```

**CRC 检测 — XOR/ROL/ROR 循环特征**：

```java
// 在一个函数内统计 XOR/ROL/ROR 密集度
int xorCount = 0, rolCount = 0, totalInstr = 0;
Instruction ins = getFirstInstruction(func);
while (ins != null && func.getBody().contains(ins.getAddress())) {
    switch (ins.getMnemonicString().toUpperCase()) {
        case "XOR": xorCount++; break;
        case "ROL": case "ROR": rolCount++; break;
    }
    totalInstr++;
    ins = getInstructionAfter(ins);
}
// XOR 密集 + 位移混合 → 可能是 CRC/哈希函数
if (totalInstr > 10 && (double)xorCount / totalInstr > 0.15) {
    printf("XOR-heavy func (possible CRC/crypto): %s (%d/%d)\n",
        func.getName(), xorCount, totalInstr);
}
```

### 代码修改与导出（KeyPatch 等价操作）

所有修改必须在事务中执行。**优先使用汇编器**（写助记符，由 Ghidra 自动编码），避免手写原始字节导致 ARM64 等定长指令架构编码错误。

**汇编器 — 用助记符写指令**：

```java
import ghidra.app.plugin.assembler.Assembler;
import ghidra.app.plugin.assembler.Assemblers;

// 获取当前程序的汇编器（自动匹配架构：x86→x86，ARM64→AArch64）
Assembler asm = Assemblers.getAssembler(currentProgram);

// ARM64 示例
byte[] nop = asm.assembleLine(addr, "NOP");               // 0xD503201F
byte[] mov = asm.assembleLine(addr, "MOV X0, X1");        // 寄存器搬移
byte[] ret = asm.assembleLine(addr, "RET");               // 返回
byte[] b   = asm.assembleLine(addr, "B #0x140001000");    // 无条件跳转
byte[] bl  = asm.assembleLine(addr, "BL #0x140002000");   // 函数调用

// x86 同样支持
byte[] x86nop = asm.assembleLine(addr, "NOP");            // 0x90
byte[] x86mov = asm.assembleLine(addr, "MOV EAX, 0x1");   // B8 01 00 00 00

// 批量汇编 + 直接写入程序
asm.patchProgram(asm.assembleLine(addr, "NOP"), addr);
```

> **⚠️ 指令长度对齐**：替换指令时必须确保新指令字节数 ≤ 原指令，否则会覆盖相邻指令导致反汇编错乱。
>
> | 架构 | 指令长度 | 替换策略 |
> |------|:--------:|----------|
> | **ARM64 / AArch64** | 固定 4 字节 | 1:1 替换天然安全，无需额外处理 |
> | **ARM32 (ARM 模式)** | 固定 4 字节 | 同上 |
> | **ARM32 (Thumb 模式)** | 2 或 4 字节 | 需检查 `ins.getLength()`，Thumb B.L 可能 4 字节 |
> | **x86 / x64** | 1~15 字节 | **必须检查长度**，不足时用 NOP 填充 |

```java
// 获取原指令长度 → 确保替换安全
Instruction origIns = listing.getInstructionAt(addr);
int origLen = origIns.getLength();                  // 原指令字节数
byte[] newBytes = asm.assembleLine(addr, "NOP");    // 新指令字节数

if (newBytes.length <= origLen) {
    // 安全：新指令不超长，多余字节用 NOP 填充（仅 x86 需要，ARM64 天然等长）
    clearListing(addr);
    setBytes(addr, newBytes);
    if (newBytes.length < origLen) {
        // x86 场景：填充剩余字节为 NOP
        for (int i = newBytes.length; i < origLen; i++) {
            setByte(addr.add(i), (byte)0x90);
        }
    }
    disassemble(addr);
} else {
    printf("WARNING: replacement too long (%d > %d) at %s\n",
        newBytes.length, origLen, addr);
}
```

**清除代码 + 汇编 + 重新反汇编**：

```java
int txId = currentProgram.startTransaction("patch");
try {
    clearListing(addr);                               // 清除旧代码
    byte[] insn = asm.assembleLine(addr, "NOP");      // 汇编新指令
    setBytes(addr, insn);                             // 写入
    disassemble(addr);                                // 重新反汇编
} finally {
    currentProgram.endTransaction(txId, true);
}
```

**ARM64 NOP 掉反调试调用（B / B.L → NOP）**：

```java
Assembler asm = Assemblers.getAssembler(currentProgram);

for (Function func : fm.getFunctions(true)) {
    if (!func.isExternal()) continue;
    if (func.getName().equals("IsDebuggerPresent")
        || func.getName().equals("_ptrace")) {
        ReferenceIterator refs = currentProgram.getReferenceManager()
            .getReferencesTo(func.getEntryPoint());
        while (refs.hasNext()) {
            Address callAddr = refs.next().getFromAddress();
            int txId = currentProgram.startTransaction("nop anti-debug");
            try {
                // ARM64: 每条指令固定 4 字节，1 条 NOP 覆盖即可
                byte[] insn = asm.assembleLine(callAddr, "NOP");
                setBytes(callAddr, insn);
                clearListing(callAddr);
                disassemble(callAddr);
            } finally {
                currentProgram.endTransaction(txId, true);
            }
        }
    }
}
```

**原始字节写入（仅当确知编码时使用）**：

```java
setByte(addr, (byte)0x90);                             // 单字节
setBytes(addr, new byte[]{0x90, 0x90, 0x90});          // 字节数组
setInt(addr, 0xD503201F);                              // 32 位（ARM64 NOP）
setLong(addr, 0xD503201FD503201FL);                    // 双 ARM64 NOP
```

**导出修改后的二进制**：

```java
import ghidra.app.util.exporter.BinaryExporter;
import ghidra.app.util.exporter.OriginalFileExporter;

// 导出为原始格式（PE/ELF/Mach-O — 保留文件头，可运行）
new OriginalFileExporter().export(
    new java.io.File("D:\\out\\patched.elf"),
    currentProgram, currentProgram.getMemory(), monitor);

// 导出为裸字节
new BinaryExporter().export(
    new java.io.File("D:\\out\\patched.bin"),
    currentProgram, currentProgram.getMemory(), monitor);
```

### 符号重命名与类型标注

所有标注操作必须在事务中执行。

**重命名函数**：

```java
import ghidra.program.model.symbol.SourceType;

int txId = currentProgram.startTransaction("rename");
try {
    Function func = fm.getFunctionAt(toAddr("0x140001000"));
    func.setName("decrypt_payload", SourceType.USER_DEFINED);
} finally {
    currentProgram.endTransaction(txId, true);
}
```

**重命名数据标签**：

```java
// 清除旧标签 + 创建新标签
removeSymbol(addr, "DAT_140001234");
createLabel(addr, "aes_sbox", true);   // true = 设为主标签

// 或直接用 createLabel 覆盖
createLabel(addr, "crc32_table", true);
```

**设置注释**（PLATE / PRE / POST / EOL）：

```java
setPlateComment(addr,  "=== AES-256-CBC decrypt ===");  // 函数头部注释
setPreComment(addr,    "key expansion round 1");         // 指令上方注释
setPostComment(addr,   "X0 = ciphertext, X1 = plaintext"); // 指令下方注释
setEOLComment(addr,    "check padding");                 // 行尾注释
```

**设置函数返回类型**：

```java
import ghidra.program.model.data.DataType;
import ghidra.program.model.symbol.SourceType;

// 获取数据类型管理器
DataTypeManager dtm = currentProgram.getDataTypeManager();
DataType voidType = dtm.getDataType(new CategoryPath("/"), "void");
DataType intType  = dtm.getDataType(new CategoryPath("/"), "int");
DataType uint64Type = dtm.getDataType(new CategoryPath("/"), "uint64");

func.setReturnType(intType, SourceType.USER_DEFINED);
```

**设置函数参数类型**：

```java
import ghidra.program.model.listing.Parameter;

Parameter[] params = func.getParameters();
for (Parameter p : params) {
    if (p.getOrdinal() == 0) {
        p.setDataType(dtm.getDataType(new CategoryPath("/"), "char *"), 
            SourceType.USER_DEFINED);
    }
}
```

**重命名局部变量**：

```java
import ghidra.program.model.listing.Variable;

for (Variable var : func.getAllVariables()) {
    if (var.getSymbol().getName().contains("local_")) {
        int txId = currentProgram.startTransaction("rename var");
        try {
            var.setName("ciphertext_buf", SourceType.USER_DEFINED);
        } finally {
            currentProgram.endTransaction(txId, true);
        }
    }
}
```

**批量标注实战**：

```java
// 遍历外部函数，为已知 API 添加注释说明
for (Function func : fm.getExternalFunctions()) {
    String name = func.getName();
    int txId = currentProgram.startTransaction("annotate");
    try {
        switch (name) {
            case "IsDebuggerPresent":
                func.setComment("Anti-debug: check if debugger attached");
                break;
            case "VirtualAlloc":
                func.setReturnType(voidPtrType, SourceType.USER_DEFINED);
                break;
            case "memcpy":
                setPlateComment(func.getEntryPoint(), "standard C memcpy");
                break;
        }
    } finally {
        currentProgram.endTransaction(txId, true);
    }
}
```

### 常用判断
- `func.isThunk()` — 是否跳转桩函数
- `func.isExternal()` — 是否外部导入函数（来自其他 DLL）
- `func.isLibrary()` — 是否库函数
- `func.getBody().getNumAddresses()` — 函数体指令数

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

### ⚠ `-noanalysis` 的副作用

使用 `-noanalysis` 跳过自动分析后：

| 影响 | 说明 |
|------|------|
| 函数体为空 | `createFunction()` 创建的函数 body size = 1（仅入口标签），反编译器返回空 |
| 交叉引用为 0 | 无反汇编就没有引用关系 |
| 指令遍历不可用 | `getFirstInstruction()` 等返回 null |

**解决**：在脚本中对需要反编译的函数手动调用 `disassemble(addr)`。

### ⚠ Il2Cpp / Android .so 的 GccExceptionAnalyzer 风暴

Unity Il2Cpp 编译的 Linux ELF 文件在全量分析时，`GccExceptionAnalyzer` 会对异常处理表产生数万条 `ERROR Failed to disassemble at 0x...`，导致分析极其缓慢（可能 >10 分钟）。**建议 `-noanalysis` + 脚本按需反汇编。**

## 常见加载器参数

| 加载器 | 常用参数 | 完整参数（用 `-loader-<arg> <value>` 传递，见 Ghidra 帮助） |
|--------|----------|------|
| **BinaryLoader** | `-loader-blockName` `-loader-baseAddr` `-loader-fileOffset` `-loader-length` | 另支持 `-applyLabels` `-anchorLabels` |
| **ElfLoader** | `-loader-imagebase` `-loader-loadLibraries` `-loader-applyRelocations` `-loader-libraryLoadDepth` | 另支持 `-dataImageBase` `-applyUndefinedData` `-linkExistingProjectLibraries` `-projectLibrarySearchFolder` `-libraryDestinationFolder` `-applyLabels` `-anchorLabels` `-includeOtherBlocks` `-maxSegmentDiscardSize` |
| **PeLoader** | `-loader-loadLibraries` `-loader-libraryLoadDepth` `-loader-ordinalLookup` `-loader-parseCliHeaders` | 另支持 `-linkExistingProjectLibraries` `-projectLibrarySearchFolder` `-libraryDestinationFolder` `-applyLabels` `-anchorLabels` `-showDebugLineNumbers` |
| **MachoLoader** | `-loader-loadLibraries` `-loader-libraryLoadDepth` `-loader-reexport` | 另支持 `-linkExistingProjectLibraries` `-projectLibrarySearchFolder` `-libraryDestinationFolder` `-applyLabels` `-anchorLabels` |

## 编写 GhidraScript 注意事项

1. **BOM 问题**: 脚本文件须为 UTF-8 **无 BOM** 编码。BOM 会导致 OSGi 类加载器编译失败，原因是 OSGi 内部通过 `javax.tools.JavaCompiler`（JSR-199）动态编译脚本，而 JSR-199 编译器对文件开头的 BOM 字节敏感，将其视为非法字符，导致编译报错（实验中验证，非官方文档）
2. **写入方式**: 用 `[System.IO.File]::WriteAllText(path, content, [System.Text.UTF8Encoding]::new($false))` 写入无 BOM 文件；`Set-Content -Encoding UTF8` 会引入 BOM
3. **类名匹配**: `public class 类名` 必须与文件名 `类名.java` 一致
4. **事务**: 任何对程序的修改必须在事务中执行（`startTransaction` / `endTransaction`）
5. **反编译器**: `DecompInterface` 每次使用后须调用 `dispose()` 释放资源；超时参数（秒）控制每个函数的最大反编译时间。`-noanalysis` 模式下函数体为空，反编译前须调用 `disassemble(addr)`。
6. **Il2CPP 游戏**: Unity Il2CPP 编译的函数名可能为 `FUN_1801XXXXX` 格式，需通过交叉引用分析；baselib socket 函数可能为导出桩，内部无调用者
7. **headless 输出**: `printf()` / `println()` 输出到 stdout（可被 `-scriptlog` 重定向捕获）；如需结构化结果，用 `java.io.FileWriter` / `PrintWriter` 写文件
8. **monitor 变量**: `monitor` 是 `GhidraScript` 继承的成员变量，在 headless 脚本中直接可用（如传给 `decompileFunction()`），无需手动创建
9. **JDK 兼容**: JDK 25 实测可用，运行中会出现 `sun.misc.Unsafe` 已废弃警告（来自 Felix OSGi），不影响正常功能。

## 适用场景
- 二进制导入/分析自动化（批量处理固件、恶意软件、游戏）
- 反编译 + 交叉引用分析（封包协议、加密算法逆向）
- 特征搜索（常量、字符串、指令模式）
- CI/CD 集成：自动化分析 + 报告输出

## headless 相关功能模块

| 模块 | 路径 | headless 用途 |
|------|------|---------------|
| **Base** | `Ghidra/Features/Base/` | 无头 API 核心（HeadlessScript、分析器、加载器） |
| **Decompiler** | `Ghidra/Features/Decompiler/` | `DecompInterface` — headless 反编译 |
| **PDB 符号** | `Ghidra/Features/PDB/` | 自动加载 PDB 恢复符号/类型 |
| **Function ID** | `Ghidra/Features/FunctionID/` | 库函数识别（分析阶段自动运行） |
| **File Formats** | `Ghidra/Features/FileFormats/` | Android OAT/DEX、UBIFS 等格式解析 |
| **Swift** | `Ghidra/Features/Swift/` | Swift 二进制命名/类型还原 |

## PyGhidra — Python 原生 headless（独立方案）

PyGhidra **不依赖 `analyzeHeadless.bat`**，而是在 Python 进程中直接启动 Ghidra：

- **与 `analyzeHeadless` 的区别**:

| | `analyzeHeadless.bat` | PyGhidra |
|---|---|---|
| 运行方式 | Java 进程，通过 `-postScript` 调 Java 脚本 | Python 进程，直接调 Ghidra Java API |
| 脚本语言 | Java（HeadlessScript） | Python 3 |
| 生态 | JVM 生态 | Python 生态（numpy/pandas/requests） |
| 适用 | 自动化流水线、CI/CD | 交互式分析、数据处理 |

- **安装**: `pip install pyghidra`（需 JDK 21+ 和 `GHIDRA_INSTALL_DIR` 环境变量；实测 JDK 25 兼容）
- **API**: 与 Ghidra Java API 1:1 映射，`flat_api` 提供 `getCurrentProgram()`、`toAddr()`、`getMonitor()` 等

```python
import pyghidra
from ghidra.app.decompiler import DecompInterface

with pyghidra.open_program("binary.dll", analyze=True) as flat_api:
    program = flat_api.getCurrentProgram()
    fm = program.getFunctionManager()

    for func in fm.getFunctions(True):
        if func.isThunk() or func.isExternal():
            continue
        print(func.getName(), func.getEntryPoint())

    # 反编译指定函数
    dec = DecompInterface()
    dec.openProgram(program)
    func = fm.getFunctionAt(flat_api.toAddr(0x140001000))
    res = dec.decompileFunction(func, 30, flat_api.getMonitor())
    if res.decompileCompleted():
        print(res.getDecompiledFunction().getC())
```

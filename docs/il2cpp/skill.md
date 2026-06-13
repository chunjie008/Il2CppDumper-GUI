---
name: il2cpp-ghidra
description: Unity Il2Cpp 游戏逆向 — Il2CppDumper dump + Ghidra 导入分析完整工作流
---

# Il2CppDumper + Ghidra 集成工作流

## 相关项目路径

- Il2CppDumper-GUI: `E:\vscodeProject\Il2CppDumper-GUI`
- Ghidra 安装:     `D:\yingyong\ghidra_12.1.2_PUBLIC`
- Zygisk-Il2CppDumper: `E:\vscodeProject\Zygisk-Il2CppDumper`

---

## 快速工作流

```
1. Il2CppDumper CLI dump ──→ 2. Ghidra headless 自动导入 → 3. 函数重命名 → 4. 反编译输出
```

---

## 第一步：Il2CppDumper dump

```powershell
# dump 到 il2cpp_dump 目录
# ⚠ 必须使用 net6.0-windows 子目录中的 exe（父目录 exe 缺少配套 DLL）
& "E:\vscodeProject\Il2CppDumper-GUI\Il2CppDumper\bin\Release\net6.0-windows\Il2CppDumper GUI.exe" `
    "libil2cpp.so" "global-metadata.dat" "il2cpp_dump" --scripts

# ⚠ 如果 struct 文件 (script.json 等) 未出现在输出目录，
#   去上层目录找（CLI 的 TrimEnd bug）
```

| 选项 | 说明 |
|------|------|
| `--dump-addr <hex>` | ELF 内存 dump 的基址 |
| `--code-reg <hex>` | 手动 CodeRegistration 地址 |
| `--metadata-reg <hex>` | 手动 MetadataRegistration 地址 |
| `--force-version <ver>` | 强制 il2cpp 版本（如 `29`、`24.3`） |
| `--no-dump-cs` | 跳过 dump.cs 生成 |
| `--no-struct` | 跳过结构体文件生成 |
| `--no-dummy-dll` | 跳过 DummyDll 生成 |
| `--no-ai` | 跳过 `dump_ai.json` 生成 |
| `--fast` | 启用快速 struct 模式 |
| `--threads <N>` | struct 生成的工作线程数（`0`=自动） |
| `--scripts` | 拷贝分析脚本到输出目录 |

输出文件：

| 文件 | 用途 |
|------|------|
| `script.json` | **Ghidra 脚本主输入**（函数地址+签名） |
| `dump_ai.json` | 紧凑 JSON，类/方法/字段/偏移/字符串 |
| `dump.cs` | C# 伪代码，人工阅读 |
| `il2cpp.h` | 结构体定义 C 头文件 |
| `stringliteral.json` | 字符串字面量 |
| `DummyDll/` | 还原的 .NET 程序集 |

---

## 第二步：Ghidra 无头全自动分析（推荐）

### 分析策略

⚠ **关键问题**：`-noanalysis` 模式下 Ghidra 不做反汇编，函数体为空（`body size = 1`），反编译器无法工作。必须按需反汇编。

**推荐两步法**：
1. 导入 + 全量重命名（快速，无需分析）
2. 按关键词/模式/大小筛选目标函数，对其做反汇编 + 反编译

### 导入（快速，无分析）

```powershell
$GHIDRA = "D:\yingyong\ghidra_12.1.2_PUBLIC"
$DUMP   = "E:\vscodeProject\test\il2cpp_dump"
$BINARY = "E:\vscodeProject\test\libil2cpp.so"
$PROJ   = "E:\vscodeProject\test\ghidra_proj"

# 首次导入
& "$GHIDRA\support\analyzeHeadless.bat" $PROJ GameAnalysis `
    -import $BINARY -overwrite -noanalysis
```

### 通用 Il2CppAnalyze v2（无硬编码地址）

**功能**：自动重命名全部 Il2Cpp 函数 + 灵活筛选目标反编译

```powershell
# 默认：反编译 top-10 最大函数
& "$GHIDRA\support\analyzeHeadless.bat" $PROJ GameAnalysis `
    -process libil2cpp.so -noanalysis `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    -scriptPath $DUMP

# 按关键词筛选（网络通信相关）
& "$GHIDRA\support\analyzeHeadless.bat" ... `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    --keywords Socket,Network,Message,Recv,Send,Connect,Packet

# 按正则匹配
& "$GHIDRA\support\analyzeHeadless.bat" ... `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    --pattern '.*NetLogic.*'

# 反编译 top 100 最大函数（适合全量分析）
& "$GHIDRA\support\analyzeHeadless.bat" ... `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    --top 100 --max 100 --timeout 15

# 按指定地址反编译
& "$GHIDRA\support\analyzeHeadless.bat" ... `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    --addresses 0x1F1CD0,0x1F0540,0x1F1A50
```

**选项说明**：

| 选项 | 说明 |
|------|------|
| `--keywords <csv>` | 反编译名称包含任一关键词的函数 |
| `--pattern <regex>` | 反编译名称匹配正则的函数 |
| `--top <N>` | 反编译按 body size 最大的 N 个函数 |
| `--addresses <csv>` | 反编译指定十六进制地址（覆盖其他筛选） |
| `--max <N>` | 最多反编译 N 个函数（默认 50，防爆） |
| `--timeout <N>` | 每个函数的反编译超时秒数（默认 30） |
| `--no-disasm` | 跳过反汇编（只重命名，不做反编译） |

> 不指定筛选参数时，默认反编译 top-10 最大函数。

### Il2CppAnalyze.java 脚本源码

> 保存到 `scriptPath` 目录（如 `E:\vscodeProject\test\il2cpp_dump\Il2CppAnalyze.java`）

脚本已更新为通用版本，完整源码见：
`E:\vscodeProject\test\il2cpp_dump\Il2CppAnalyze.java`

### 输出示例

```
Il2CppAnalyze v2 (universal)
  script.json: E:\...\il2cpp_dump\script.json
  out dir:     E:\...\decompile
  keywords: Socket, Network, Message, Recv, Send
Renamed 137113 methods.
Decompiling 42 of 42 matched functions...
  [1/42] UnityEngine.Networking.NetworkTransport$$Send  (keyword 'Send')
  -> decompile/UnityEngine_Networking_NetworkTransport_Send.c (10234 chars)
  [2/42] SocketClient$$OnRecieveMessageDeal  (keyword 'Recv')
  -> decompile/SocketClient_OnRecieveMessageDeal.c (8451 chars)
  ...
Results: 38 decompiled, 4 failed  => E:\...\decompile
Done!
```

---

## 方式二：Ghidra GUI 交互分析

1. 打开 Ghidra，导入并分析 `libil2cpp.so` 或 `GameAssembly.dll`
2. 打开 Script Manager → 运行 `ghidra_with_struct.py`
3. 选择 Il2CppDumper 输出的 `script.json`
4. 脚本自动重命名 5.5 万+ 函数、导入结构体

---

## 方式三：`dump_ai.json` → 自定义脚本

`dump_ai.json` 包含完整结构化数据，可用 Jython 脚本处理：

```python
import json
f = askFile("dump_ai.json", "Open")
data = json.loads(open(f.absolutePath, 'rb').read().decode('utf-8'))
baseAddress = currentProgram.getImageBase()
USER_DEFINED = ghidra.program.model.symbol.SourceType.USER_DEFINED
for t in data["t"]:
    for m in t.get("mts", []):
        if "a" not in m: continue
        addr = baseAddress.add(int(m["a"], 16))
        try: createLabel(addr, m["n"].replace(' ', '-'), True, USER_DEFINED)
        except: pass
        if getFunctionAt(addr) is None:
            createFunction(addr, None)
```

---

## Ghidra 脚本开发注意事项

1. **BOM 问题**: Java 脚本必须是 UTF-8 **无 BOM**，否则 OSGi 编译失败
   ```powershell
   # 正确的写入方式（无 BOM）
   [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
   # Set-Content -Encoding UTF8 会带 BOM，不要用
   ```
2. **Java vs Python**: Ghidra 无头模式支持两种脚本：
   | 后缀 | 引擎 | 无头可用 | 说明 |
   |------|------|----------|------|
   | `.java` | Java | ✅ | 开箱即用，推荐 |
   | `.py` | PyGhidra (Python 3) | ❌ | 需额外配置 |
   | `.jpy` | Jython (Python 2.7) | ✅ | 内置但不如 Java 稳定 |
3. **类名匹配**: `public class X` 必须匹配文件名 `X.java`
4. **事务**: 任何修改程序的操作必须在 `startTransaction` / `endTransaction` 内
5. **反编译器前提**: `-noanalysis` 模式下函数体为空（body size = 1），反编译前必须调用 `disassemble(addr)`。Il2CppAnalyze.java 自动处理。
6. **反编译器**: `DecompInterface` 用完必须 `dispose()`
7. **JDK 兼容**: 实测 Ghidra 12.1.2 兼容 JDK 25（仅有 `sun.misc.Unsafe` deprecated 警告）
8. **Il2CPP 内存模型**: Il2Cpp 对象实例字段偏移从 `0x10` 开始，`0x00`=klass, `0x08`=monitor

---

## 常见问题

### 函数名为 FUN_1800XXXXX
Il2Cpp 编译后的函数名本身就是匿名的，运行 `Il2CppAnalyze.java` 或 `ghidra_with_struct.py` 后自动重命名。

### 反编译器报错 / body size = 1
`-noanalysis` 模式下 Ghidra 不做反汇编，函数体仅含入口标签，反编译器无法工作。`Il2CppAnalyze.java` 会自动对目标函数调用 `disassemble()` 解决，但全量反汇编 13 万函数会极慢。**推荐用 `--keywords`/`--top` 筛选目标。**

### GccExceptionAnalyzer 风暴（数万条 ERROR）
Unity Il2Cpp 编译的 Linux `.so` 在全量分析时，`GccExceptionAnalyzer` 会对大量地址反复报错，分析极慢（可能超过 10 分钟）。这是正常现象，不影响结果。建议 `-noanalysis` + 按需反汇编。

### 保护/混淆的游戏
1. 先用内存 dump 工具 dump `libil2cpp.so`
2. 用 `--dump-addr` 指定 dump 基址
3. 再用 Il2CppDumper 处理 dump 文件

### `ERROR: Can't use auto mode`
PC 平台是 `GameAssembly.dll` 而非 `libil2cpp.so`。提供 `--code-reg` 和 `--metadata-reg` 进入手动模式，或使用 `PE` 加载器。

### script.json 未生成
ClI 的 `TrimEnd` 会去掉输出路径末尾的分隔符，导致文件写入到**上层目录**。去输出目录的父目录找，或手动指定路径时不要加末尾斜杠。

### Il2CppDumper GUI.exe 无法执行
`.exe` 为 .NET 单文件发布但 DLL 在 `net6.0-windows` 子目录。请直接运行子目录中的 `.exe`：
```powershell
& "Il2CppDumper-GUI\Il2CppDumper\bin\Release\net6.0-windows\Il2CppDumper GUI.exe" ...
```

### 模拟执行验证
需要分析 Il2Cpp 函数行为时，可以用 `unidbg` (`E:\vscodeProject\unidbg`) 模拟执行 libil2cpp.so 中的函数。

---

## 网络封包分析（实战参考）

从反编译结果推导出的 TCP 封包帧格式：

```
[protobuf_length: int32][msg_id: int32][protobuf_body: byte[length]]
```

| MsgID | 值 | 方向 | 说明 |
|-------|-----|------|------|
| 0 | 0x00 | 双向 | MSG_AUTH (AuthRequest/Response) |
| 1 | 0x01 | S→C | MSG_SYNC_PID (SyncPid) |
| 188 | 0xBC | C→S | MSG_HEARTBEAT (心跳) |
| 200 | 0xC8 | S→C | MSG_BROADCAST (BroadCast oneof) |
| 201 | 0xC9 | S→C | MSG_SYNC_PLAYERS (SyncPlayers) |
| 202 | 0xCA | S→C | MSG_PLAYER_LEAVE |
| 203 | 0xCB | S→C | MSG_DAMAGE (DamageNotify) |
| 204 | 0xCC | S→C | MSG_HP_UPDATE (HPUpdate) |
| 205 | 0xCD | S→C | MSG_KICK (KickNotify) |

查看反编译后 `SocketClient_NetLogic.c` 中的 `switch(param_2)` 即可获得精确值。
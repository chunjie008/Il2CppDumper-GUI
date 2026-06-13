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
& "E:\vscodeProject\Il2CppDumper-GUI\bin\Release\Il2CppDumper GUI.exe" `
    "GameAssembly.dll" "global-metadata.dat" "il2cpp_dump" --scripts

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

**一条命令完成**：导入 → 自动分析 → Il2Cpp 函数重命名 → 网络函数反编译输出

```powershell
$GHIDRA = "D:\yingyong\ghidra_12.1.2_PUBLIC"
$DUMP   = "D:\path\to\il2cpp_dump"
$BINARY = "D:\path\to\GameAssembly.dll"

# 首次：导入 + 分析 + 重命名 + 反编译
& "$GHIDRA\support\analyzeHeadless.bat" `
    "D:\ghidra_proj" "GameAnalysis" `
    -import $BINARY -overwrite `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    -scriptPath $DUMP

# 后续：仅重命名 + 反编译（项目已存在）
& "$GHIDRA\support\analyzeHeadless.bat" `
    "D:\ghidra_proj" "GameAnalysis" `
    -process GameAssembly.dll -noanalysis `
    -postScript Il2CppAnalyze.java "$DUMP\script.json" "$DUMP\decompile" `
    -scriptPath $DUMP
```

### Il2CppAnalyze.java 脚本

功能：
1. 从 `script.json` 读取 5.5 万+ 个 Il2Cpp 函数地址，自动重命名 + 创建函数
2. 反编译 15 个网络关键函数到 `.c` 文件（可根据游戏修改 `KEY_FUNCS` 数组）

脚本源码（保存为 `Il2CppAnalyze.java` 放到 `scriptPath` 目录）：

```java
// @category: Il2Cpp
import java.io.*;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.SymbolTable;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.address.Address;
import ghidra.app.decompiler.*;
import ghidra.app.util.headless.HeadlessScript;

public class Il2CppAnalyze extends HeadlessScript {
    // 网络关键函数地址（按需修改）
    static final long[] KEY_FUNCS = {
        0x1F1CD0L, 0x1F0540L, 0x1F1A50L, 0x1F25C0L,
        0x1F0320L, 0x1F20D0L, 0x1F1460L, 0x1F16F0L,
        0x1F1AD0L, 0x1EAA40L, 0x1EAB20L, 0x1E8BC0L,
        0x1ECD90L, 0x1ECBC0L, 0x1F3760L,
    };

    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            println("Usage: Il2CppAnalyze.java <script.json> [decompile_out_dir]");
            return;
        }
        int count = renameFromJson(args[0]);
        if (args.length > 1) decompileFunctions(args[1]);
        println("Done! Renamed " + count + " methods.");
    }

    private int renameFromJson(String jsonPath) throws Exception {
        File f = new File(jsonPath);
        if (!f.exists()) { println("ERROR: file not found"); return 0; }
        println("Reading " + f.length() + " bytes...");
        byte[] raw = new byte[(int)f.length()];
        new FileInputStream(f).read(raw);
        String content = new String(raw, "UTF-8");

        int sm = content.indexOf("\"ScriptMethod\"");
        if (sm < 0) { println("ERROR: ScriptMethod not found"); return 0; }
        int col = content.indexOf(':', sm);
        int as = content.indexOf('[', col);
        if (as < 0) { println("ERROR: no bracket"); return 0; }

        Address base = currentProgram.getImageBase();
        SymbolTable st = currentProgram.getSymbolTable();
        FunctionManager fm = currentProgram.getFunctionManager();
        SourceType ud = SourceType.USER_DEFINED;
        int count = 0, pos = as + 1;

        while (pos < content.length()) {
            int ob = content.indexOf('{', pos);
            if (ob < 0) break;
            int cb = matchBrace(content, ob);
            if (cb < 0) break;
            String obj = content.substring(ob, cb + 1);
            String name = getStr(obj, "\"Name\"");
            String addrStr = getNum(obj, "\"Address\"");
            if (name != null && addrStr != null) {
                try {
                    Address addr = base.add(Long.parseLong(addrStr));
                    String label = name.replace(' ', '-');
                    try { st.createLabel(addr, label, ud); } catch (Exception e) { }
                    if (fm.getFunctionAt(addr) == null)
                        try { createFunction(addr, label); } catch (Exception e) { }
                    count++;
                } catch (Exception e) {
                    println("Error: " + name + " - " + e.getMessage());
                }
            }
            if (count % 2000 == 0) {
                monitor.setMessage(count + " renamed...");
                if (monitor.isCancelled()) break;
            }
            pos = cb + 1;
        }
        println("Renamed " + count + " methods.");
        return count;
    }

    private void decompileFunctions(String outDir) throws Exception {
        new File(outDir).mkdirs();
        Address base = currentProgram.getImageBase();
        FunctionManager fm = currentProgram.getFunctionManager();
        DecompInterface dc = new DecompInterface();
        dc.openProgram(currentProgram);

        for (long off : KEY_FUNCS) {
            Address addr = base.add(off);
            Function func = fm.getFunctionAt(addr);
            if (func == null) { println("NOT FOUND: 0x" + Long.toHexString(off)); continue; }
            String name = func.getName();
            println("Decompiling: " + name);

            DecompileResults res = dc.decompileFunction(func, 60, monitor);
            String code;
            if (res != null && res.getDecompiledFunction() != null)
                code = res.getDecompiledFunction().getC();
            else {
                code = "// Decompilation failed";
                if (res != null && res.getErrorMessage() != null && !res.getErrorMessage().isEmpty())
                    code += "\n// " + res.getErrorMessage();
            }

            String fn = name.replace("$$", "_").replace(' ', '_')
                           .replaceAll("[^a-zA-Z0-9_.-]", "_");
            if (!fn.endsWith(".c")) fn += ".c";

            PrintWriter pw = new PrintWriter(new File(outDir, fn), "UTF-8");
            pw.println("// " + name + " @ " + addr);
            pw.println(code); pw.close();
            println("  -> " + outDir + "/" + fn + " (" + code.length() + " chars)");
        }
        dc.dispose();
        println("Decompiled " + KEY_FUNCS.length + " functions.");
    }

    private int matchBrace(String s, int start) {
        int depth = 1; boolean inStr = false;
        for (int i = start + 1; i < s.length(); i++) {
            char c = s.charAt(i);
            if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; }
            else { if (c == '"') inStr = true; else if (c == '{') depth++;
                   else if (c == '}') { depth--; if (depth == 0) return i; } }
        }
        return -1;
    }

    private String getStr(String s, String key) {
        int idx = s.indexOf(key);
        if (idx < 0) return null;
        int q1 = s.indexOf('"', s.indexOf(':', idx) + 1);
        int q2 = s.indexOf('"', q1 + 1);
        return (q1 < 0 || q2 < 0) ? null : s.substring(q1 + 1, q2);
    }

    private String getNum(String s, String key) {
        int idx = s.indexOf(key);
        if (idx < 0) return null;
        int pos = s.indexOf(':', idx) + 1;
        while (pos < s.length() && s.charAt(pos) <= ' ') pos++;
        StringBuilder sb = new StringBuilder();
        while (pos < s.length() && (Character.isDigit(s.charAt(pos)) || s.charAt(pos) == '-'))
            sb.append(s.charAt(pos++));
        return sb.length() > 0 ? sb.toString() : null;
    }
}
```

### 输出示例

```
Il2CppAnalyze.java> Reading 24066071 bytes...
Il2CppAnalyze.java> ScriptMethod array starts at offset 21
Il2CppAnalyze.java> Renamed 55998 methods.
Il2CppAnalyze.java> Decompiling: SocketClient$$OnRecieveMessageDeal
Il2CppAnalyze.java>   -> decompile/SocketClient_OnRecieveMessageDeal.c (1180 chars)
Il2CppAnalyze.java> Decompiling: SocketClient$$NetLogic
Il2CppAnalyze.java>   -> decompile/SocketClient_NetLogic.c (19731 chars)
...
Il2CppAnalyze.java> Decompiled 15 functions.
Il2CppAnalyze.java> Done! Renamed 55998 methods.
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
5. **反编译器**: `DecompInterface` 用完必须 `dispose()`
6. **Il2CPP 内存模型**: Il2Cpp 对象实例字段偏移从 `0x10` 开始，`0x00`=klass, `0x08`=monitor

---

## 常见问题

### 函数名为 FUN_1800XXXXX
Il2Cpp 编译后的函数名本身就是匿名的，运行 `Il2CppAnalyze.java` 或 `ghidra_with_struct.py` 后自动重命名。

### 保护/混淆的游戏
1. 先用内存 dump 工具 dump `libil2cpp.so`
2. 用 `--dump-addr` 指定 dump 基址
3. 再用 Il2CppDumper 处理 dump 文件

### `ERROR: Can't use auto mode`
PC 平台是 `GameAssembly.dll` 而非 `libil2cpp.so`。提供 `--code-reg` 和 `--metadata-reg` 进入手动模式，或使用 `PE` 加载器。

### script.json 未生成
ClI 的 `TrimEnd` 会去掉输出路径末尾的分隔符，导致文件写入到**上层目录**。去输出目录的父目录找，或手动指定路径时不要加末尾斜杠。

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
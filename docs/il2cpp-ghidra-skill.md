---
name: il2cpp-ghidra
description: Unity Il2Cpp 游戏逆向 — Il2CppDumper dump + Ghidra 导入分析完整工作流
---

# Il2CppDumper + Ghidra 集成工作流

## 相关项目路径

- Il2CppDumper-GUI: `E:\vscodeProject\Il2CppDumper-GUI`
- Ghidra 源码:     `E:\vscodeProject\ghidra`
- Zygisk-Il2CppDumper: `E:\vscodeProject\Zygisk-Il2CppDumper`

## 快速工作流（推荐）

```
1. Il2CppDumper CLI dump ──→ 2. ghidra_with_struct.py 导入 Ghidra
```

### 第一步：Il2CppDumper dump

```bash
Il2CppDumper <binary> <metadata> <output> [options]
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
| `--fast` | 启用快速 struct 模式（跳过 metadata-usage 扫描） |
| `--threads <N>` | struct 生成的工作线程数（`0`=自动） |
| `--scripts` | 拷贝分析脚本（ghidra.py/ida.py/hopper-py3.py 等）到输出目录 |

### 第二步：Ghidra 导入

**方式一：Python 脚本（推荐）**

在 Ghidra 的 Script Manager 中运行 `ghidra_with_struct.py`：
1. 打开 Ghidra，导入并分析 `libil2cpp.so`
2. 打开 Script Manager → 运行 `ghidra_with_struct.py`（或拖入 Ghidra 窗口）
3. 选择 Il2CppDumper 输出的 `script.json`
4. 脚本会自动重命名函数、导入结构体

**方式二：`dump_ai.json` → 自定义 Ghidra 脚本**

`dump_ai.json` 包含完整的结构化数据，可用 PyGhidra 编写脚本处理：

```python
# @category: Il2Cpp
# @python
# @runtime Jython
import json

f = askFile("dump_ai.json", "Open")
data = json.loads(open(f.absolutePath, 'rb').read().decode('utf-8'))

baseAddress = currentProgram.getImageBase()
functionManager = currentProgram.getFunctionManager()
listing = currentProgram.getListing()
USER_DEFINED = ghidra.program.model.symbol.SourceType.USER_DEFINED

for t in data["t"]:
    for m in t.get("mts", []):
        if "a" not in m:
            continue
        va = int(m["a"], 16)
        addr = baseAddress.add(va)
        # 重命名
        try:
            createLabel(addr, m["n"].replace(' ', '-'), True, USER_DEFINED)
        except:
            pass
        # 创建函数
        func = getFunctionAt(addr)
        if func is None:
            createFunction(addr, None)
```

## Il2CppDumper CLI 详细说明

### 输出文件

| 文件 | 用途 |
|------|------|
| `script.json` | Ghidra/IDA/Binja 脚本主输入 |
| `dump_ai.json` | 紧凑 JSON，含类层级/方法/字段/偏移/字符串 |
| `dump.cs` | C# 伪代码，人工阅读 |
| `il2cpp.h` | 结构体定义 C 头文件 |
| `stringliteral.json` | 字符串字面量 |
| `DummyDll/` | 还原的 .NET 程序集 |

### dump_ai.json 键名说明

| 键 | 含义 | 类型 |
|----|------|------|
| `v` | il2cpp 版本 | number |
| `u` | Unity 版本范围 | string |
| `ts/ms/fs/ss` | 类型/方法/字段/字符串总数 | number |
| `imgs[]` | 程序集列表 | array |
| `t[]` | 所有类型 | array |
| `t[].n/ns/p/img` | 类型名/命名空间/父类/镜像 | string |
| `t[].v/ab/sl/if/en/vt` | 可见性/抽象/密封/接口/枚举/值类型 | bool |
| `t[].fds[].n/t/o` | 字段名/类型/偏移 | string/number |
| `t[].mts[].n/s/a/ra` | 方法名/签名/VA/RVA | string |
| `t[].mts[].v/st/vr/ab/sl` | 可见性/静态/虚/抽象/槽 | bool/number |
| `t[].pts[].n/g/s` | 属性名/getter/setter | string |
| `t[].nts[]` | 嵌套类型索引列表 | array |
| `strs[].id/v` | 字符串 ID/值 | number/string |

## Ghidra 脚本开发注意事项

1. **BOM 问题**: Ghidra 脚本必须是 UTF-8 **无 BOM**，否则 OSGi 编译失败
   ```powershell
   [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
   ```
2. **事务**: 任何修改程序的操作必须在 `startTransaction` / `endTransaction` 内
3. **类名匹配**: GhidraScript 的 `public class X` 必须匹配文件名 `X.java`
4. **PyGhidra vs Jython**: PyGhidra（Python 3 原生）优先于 Jython（Python 2）
5. **反编译器**: `DecompInterface` 用完必须 `dispose()`

## 常见问题

### 函数名为 FUN_1800XXXXX
Il2Cpp 编译后的函数名就是匿名的，`ghidra_with_struct.py` 执行后会自动重命名。

### 保护/混淆的游戏
1. 先用 `GameGuardian` 从内存 dump `libil2cpp.so`
2. 用 `--dump-addr` 指定 dump 基址
3. 再用 Il2CppDumper 处理 dump 文件

### `ERROR: Can't use auto mode`
PC 平台的二进制是 `GameAssembly.dll` 而非 `libil2cpp.so`。提供 `--code-reg` 和 `--metadata-reg` 进入手动模式。

### 模拟执行验证
需要分析 Il2Cpp 函数行为时，可以用 `unidbg` (`E:\vscodeProject\unidbg`) 模拟执行 libil2cpp.so 中的函数。

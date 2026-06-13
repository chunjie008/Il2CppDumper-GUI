# Il2CppDumper GUI

![Screenshot](Screenshot.png)

[![](https://img.shields.io/github/downloads/AndnixSH/Il2CppDumper-GUI/total?style=for-the-badge)](https://github.com/AndnixSH/Il2CppDumper-GUI/releases) [![](https://img.shields.io/github/v/release/andnixsh/Il2CppDumper-GUI?style=for-the-badge)](https://github.com/AndnixSH/APKToolGUI/releases)

Perfare 的 Il2CppDumper 的 GUI 版本。基于 WPF + ModernWpfUI 深色主题。支持拖放二进制文件和 global-metadata.dat 进行文件选择，支持 APK、APKS、XAPK、ZIP 及解密后的 IPA 文件自动 dump。

同时支持 **命令行模式（CLI）**，可在终端中直接调用，无需打开 GUI。

# 注意事项

由于许多游戏使用了各种保护与加密手段，本工具无法对这些受保护的游戏提供支持。因此 Issues 已关闭，仅接受 Pull Request。

# 功能特性

- 完整恢复 DLL（不含代码），可用于提取 MonoBehaviour 和 MonoScript
- 支持 ELF、ELF64、Mach-O、PE、NSO 和 WASM 格式
- 支持 Unity 5.3 - 6000
- 支持 Metadata 16 - 39
- 支持生成 IDA、Ghidra 和 Binary Ninja 脚本，辅助分析 il2cpp 文件
- 支持生成结构体头文件
- 支持 Android 内存 dump 的 libil2cpp.so 文件绕过保护
- 支持绕过简单 PE 保护
- 设置输出目录
- 手动设置注册偏移
- 支持拖放操作
- 性能设置
- 快速模式（跳过耗时的 metadata-usage 二进制扫描）
- 支持 APK 和 IPA 自动 dump
- **CLI 命令行模式** — 无需 GUI，终端直接运行
- **AI 友好输出** — 生成结构化 JSON (`dump_ai.json`)，专为 AI/LLM 解析优化

# 环境要求

- Windows 7 及以上
- .NET 6.0 Desktop Runtime (Windows)：https://dotnet.microsoft.com/en-us/download/dotnet/6.0

# 下载

> 注意：杀毒软件可能会将本工具标记为恶意程序，这是误报，无需担心。他们把所有的 modding 工具都标记为恶意，这是他们的商业模式。

Il2CppDumper GUI：https://github.com/AndnixSH/Il2CppDumper-GUI/releases

# 使用方法

## GUI 模式

将 APK、APKS、XAPK、ZIP 或解密后的 IPA 文件拖放到 Start 按钮上即可 dump。

手动选择文件：将二进制文件和 global-metadata.dat 拖放到文本框或 Select 按钮上，或点击 Select 选择文件，然后点击 Start 开始 dump。

获取 CodeRegistration 和 MetadataRegistration 的教程：
- https://tomorrowisnew.com/posts/Finding-CodeRegistration-and-MetadataRegistration/
- https://il2cppdumper.com/reverse/examining-the-binary

## CLI 命令行模式

直接不带参数运行将启动 GUI；传入参数则进入 CLI 模式：

```
Il2CppDumper <binary> <metadata> <output> [选项]
```

**选项：**

| 选项 | 说明 |
|------|------|
| `--dump-addr <hex>` | ELF 文件 dump 基址（十六进制） |
| `--code-reg <hex>` | CodeRegistration 地址（手动模式） |
| `--metadata-reg <hex>` | MetadataRegistration 地址（手动模式） |
| `--force-version <ver>` | 强制指定 il2cpp 版本 |
| `--no-dump-cs` | 跳过 dump.cs 生成 |
| `--no-struct` | 跳过结构体文件生成 |
| `--no-dummy-dll` | 跳过 DummyDll 生成 |
| `--no-ai` | 跳过 AI dump 输出 |
| `--fast` | 快速结构体生成模式 |
| `--threads <N>` | 工作线程数（0=自动） |
| `--scripts` | 拷贝分析脚本到输出目录 |

**示例：**

```bash
# 基本用法
Il2CppDumper.exe libil2cpp.so global-metadata.dat output/

# 带 dump 地址（ELF 内存 dump）
Il2CppDumper.exe libil2cpp.so global-metadata.dat output/ --dump-addr 0x7A00000000

# 手动模式
Il2CppDumper.exe GameAssembly.dll global-metadata.dat output/ --code-reg 0x180000000 --metadata-reg 0x180000100

# 只生成 AI dump + dummy dll
Il2CppDumper.exe libil2cpp.so global-metadata.dat output/ --no-dump-cs --no-struct --scripts
```

# 输出文件

#### dump.cs
C# 伪代码，便于人工阅读。

#### dump_ai.json（新增）
紧凑的结构化 JSON，使用短键名优化，专为 AI/LLM 解析设计。包含：
- 所有类型及其完整层级、接口、可见性
- 所有方法签名、VA 地址、RVA 地址、修饰符
- 所有字段名称、类型、偏移
- 所有属性及 getter/setter
- 嵌套类型索引
- 字符串字面量

> 键名说明：`n`=名称, `v`=可见性, `t`=类型, `a`=VA地址, `ra`=RVA, `o`=偏移, `s`=签名, `p`=父类, `tk`=Token, `sz`=大小, `ab`=抽象, `sl`=密封, `vt`=值类型, `en`=枚举, `if`=接口, `st`=静态, `vr`=虚方法

#### DummyDll
文件夹，包含所有还原的 dll 文件。使用 [dnSpy](https://github.com/0xd4d/dnSpy)、[ILSpy](https://github.com/icsharpcode/ILSpy) 或其他 .Net 反编译器查看。可用于提取 Unity `MonoBehaviour` 和 `MonoScript`，配合 [UtinyRipper](https://github.com/mafaca/UtinyRipper)、[UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor) 使用。

#### ida.py
适用于 IDA（Python 2 / IDAPython 2，旧版 IDA）。

#### ida_py3.py
适用于 IDA（Python 3 / IDAPython 3，IDA 7.4 及更新版本）。与 `ida.py` 相同，更新了 Python 3 语法。

#### ida_with_struct.py
适用于 IDA，读取 il2cpp.h 并应用结构体信息（Python 2 版）。

#### ida_with_struct_py3.py
适用于 IDA，读取 il2cpp.h 并应用结构体信息（Python 3 版，IDA 7.4+）。

#### il2cpp.h
结构体信息头文件。

#### ghidra.py
适用于 Ghidra。

#### ghidra_with_struct.py
适用于 Ghidra，读取 il2cpp.h 并应用结构体/函数签名信息。

#### ghidra_wasm.py
适用于 Ghidra，配合 [ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin) 使用。

#### il2cpp_header_to_ghidra.py
适用于 Ghidra，在 Ghidra 的 Script Manager 中运行，解析 `il2cpp.h` 并将所有 il2cpp 结构体导入 Ghidra 的 Data Type Manager。

#### Il2CppBinaryNinja
适用于 Binary Ninja。

#### il2cpp_header_to_binja.py
适用于 Binary Ninja，将 `il2cpp.h` 转换为 Binary Ninja 兼容的头文件。

#### hopper-py3.py
适用于 [Hopper Disassembler](https://www.hopperapp.com/)（Python 3）。读取 `script.json` 并重命名 Hopper 中的地址/方法。

#### script.json
供 ida.py、ghidra.py 和 Il2CppBinaryNinja 使用。

#### stringliteral.json
包含所有字符串字面量信息。

# 常见错误

#### `ERROR: Metadata file supplied is not valid metadata file.`
请确保选择了正确的文件。部分游戏可能对 global-metadata.dat 进行了混淆保护，此类反混淆不在本工具处理范围内，**请勿**提交相关 Issue。

#### `ERROR: Can't use auto mode to process file, try manual mode.`
注意 PC 平台的可执行文件为 `GameAssembly.dll` 或 `*Assembly.dll`。

#### `ERROR: This file may be protected.`
Il2CppDumper 检测到可执行文件已被保护。可使用 `GameGuardian` 从游戏内存中 dump `libil2cpp.so`，然后用 Il2CppDumper 加载并按照提示操作，可绕过大部分保护。

# 致谢

- Axey（Unity 6 / Metadata v39 升级、性能选项）
- AndnixSH（GUI 相关）
- Perfare [Il2CppDumper](https://github.com/Perfare/Il2CppDumper)
- djkaty（协助修复问题，部分代码来源于 [Il2CppInspector](https://github.com/djkaty/Il2CppInspector)）
- T5ive（部分代码引用）[Il2CppDumper-GUI](https://github.com/T5ive/Il2CppDumper-GUI)

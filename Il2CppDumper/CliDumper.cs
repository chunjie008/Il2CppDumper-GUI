using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Il2CppDumper
{
    internal static class CliDumper
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: Il2CppDumper <binary> <metadata> <output> [options]");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Options:");
                Console.Error.WriteLine("  --dump-addr <hex>       Dump base address for ELF files");
                Console.Error.WriteLine("  --code-reg <hex>        CodeRegistration address (manual mode)");
                Console.Error.WriteLine("  --metadata-reg <hex>    MetadataRegistration address (manual mode)");
                Console.Error.WriteLine("  --force-version <ver>   Override il2cpp version");
                Console.Error.WriteLine("  --no-dump-cs            Skip dump.cs generation");
                Console.Error.WriteLine("  --no-struct             Skip struct file generation");
                Console.Error.WriteLine("  --no-dummy-dll          Skip dummy DLL generation");
                Console.Error.WriteLine("  --no-ai                 Skip AI dump generation (all formats)");
                Console.Error.WriteLine("  --ai-format <fmt>       AI dump format: json, sqlite, all (default: all)");
                Console.Error.WriteLine("  --fast                  Enable fast struct generation mode");
                Console.Error.WriteLine("  --threads <N>           Worker thread count (0=auto)");
                Console.Error.WriteLine("  --scripts               Copy analysis scripts to output");
                return 1;
            }

            var binaryPath = args[0];
            var metadataPath = args[1];
            var outputDir = args[2].TrimEnd('\\', '/');

            ulong? dumpAddr = null;
            ulong? codeReg = null;
            ulong? metadataReg = null;
            double? forceVersion = null;
            bool genDumpCs = true;
            bool genStruct = true;
            bool genDummyDll = true;
            string aiFormat = "all";
            bool fastMode = false;
            int workerThreads = 0;
            bool copyScripts = false;

            for (int i = 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dump-addr":
                        dumpAddr = ParseHex(args[++i]);
                        break;
                    case "--code-reg":
                        codeReg = ParseHex(args[++i]);
                        break;
                    case "--metadata-reg":
                        metadataReg = ParseHex(args[++i]);
                        break;
                    case "--force-version":
                        forceVersion = double.Parse(args[++i]);
                        break;
                    case "--no-dump-cs":
                        genDumpCs = false;
                        break;
                    case "--no-struct":
                        genStruct = false;
                        break;
                    case "--no-dummy-dll":
                        genDummyDll = false;
                        break;
                    case "--no-ai":
                        aiFormat = "none";
                        break;
                    case "--ai-format":
                        aiFormat = args[++i].ToLowerInvariant();
                        if (aiFormat != "json" && aiFormat != "sqlite" && aiFormat != "all" && aiFormat != "none")
                        {
                            Console.Error.WriteLine($"Invalid AI format: {args[i]}. Valid: json, sqlite, all, none");
                            return 1;
                        }
                        break;
                    case "--fast":
                        fastMode = true;
                        break;
                    case "--threads":
                        workerThreads = int.Parse(args[++i]);
                        break;
                    case "--scripts":
                        copyScripts = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        return 1;
                }
            }

            if (!File.Exists(binaryPath))
            {
                Console.Error.WriteLine($"Binary file not found: {binaryPath}");
                return 1;
            }
            if (!File.Exists(metadataPath))
            {
                Console.Error.WriteLine($"Metadata file not found: {metadataPath}");
                return 1;
            }
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var basePath = Path.GetDirectoryName(AppContext.BaseDirectory);
            var configPath = Path.Combine(basePath, "config.json");
            var config = File.Exists(configPath)
                ? JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath))
                : new Config();

            if (forceVersion.HasValue)
            {
                config.ForceIl2CppVersion = true;
                config.ForceVersion = forceVersion.Value;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.WriteLine("Initializing metadata...");
            var metadataBytes = File.ReadAllBytes(metadataPath);
            var metadata = new Metadata(new MemoryStream(metadataBytes));
            Console.WriteLine($"Metadata Version: {metadata.Version}");
            var unityRange = UnityVersionMap.GetUnityVersionRange(metadata.Version);
            Console.WriteLine($"Detected Unity Version: {unityRange}");
            if (metadata.Version >= 38)
                Console.WriteLine("Unity 6 metadata detected - using variable-width index mode");

            Console.WriteLine("Initializing il2cpp file...");
            var il2cppBytes = File.ReadAllBytes(binaryPath);
            var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
            var il2CppMemory = new MemoryStream(il2cppBytes);
            Il2Cpp il2Cpp;

            switch (il2cppMagic)
            {
                default:
                    Console.Error.WriteLine("ERROR: il2cpp file not supported.");
                    return 1;
                case 0x6D736100:
                    var web = new WebAssembly(il2CppMemory);
                    il2Cpp = web.CreateMemory();
                    break;
                case 0x304F534E:
                    var nso = new NSO(il2CppMemory);
                    il2Cpp = nso.UnCompress();
                    break;
                case 0x905A4D:
                    il2Cpp = new PE(il2CppMemory);
                    break;
                case 0x464C457f:
                    if (il2cppBytes[4] == 2)
                        il2Cpp = new Elf64(il2CppMemory);
                    else
                        il2Cpp = new Elf(il2CppMemory);
                    break;
                case 0xCAFEBABE:
                case 0xBEBAFECA:
                    var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                    var index = 1;
                    var magic = machofat.fats[index].magic;
                    il2cppBytes = machofat.GetMacho(index);
                    il2CppMemory = new MemoryStream(il2cppBytes);
                    if (magic == 0xFEEDFACF)
                        goto case 0xFEEDFACF;
                    else
                        goto case 0xFEEDFACE;
                case 0xFEEDFACF:
                    il2Cpp = new Macho64(il2CppMemory);
                    break;
                case 0xFEEDFACE:
                    il2Cpp = new Macho(il2CppMemory);
                    break;
            }

            var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
            il2Cpp.SetProperties(version, metadata.metadataUsagesCount);
            Console.WriteLine($"Il2Cpp Version: {il2Cpp.Version}");

            if (config.ForceDump || il2Cpp.CheckDump())
            {
                if (il2Cpp is ElfBase elf)
                {
                    if (dumpAddr.HasValue)
                    {
                        Console.WriteLine("Inputted address: " + dumpAddr.Value.ToString("X"));
                        if (dumpAddr.Value != 0)
                        {
                            il2Cpp.ImageBase = dumpAddr.Value;
                            il2Cpp.IsDumped = true;
                            if (!config.NoRedirectedPointer)
                                elf.Reload();
                        }
                    }
                    else
                    {
                        il2Cpp.IsDumped = true;
                    }
                }
            }

            Console.WriteLine("Searching...");
            try
            {
                var methodCount = metadata.methodDefs.Count(x => x.methodIndex >= 0);
                var flag = il2Cpp.PlusSearch(methodCount, metadata.typeDefs.Length, metadata.imageDefs.Length);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!flag && il2Cpp is PE)
                    {
                        Console.WriteLine("Use custom PE loader");
                        il2Cpp = PELoader.Load(binaryPath);
                        il2Cpp.SetProperties(version, metadata.metadataUsagesCount);
                        flag = il2Cpp.PlusSearch(methodCount, metadata.typeDefs.Length, metadata.imageDefs.Length);
                    }
                }

                if (!flag)
                    flag = il2Cpp.Search();
                if (!flag)
                    flag = il2Cpp.SymbolSearch();

                if (!flag)
                {
                    if (!codeReg.HasValue || !metadataReg.HasValue)
                    {
                        Console.Error.WriteLine("ERROR: Can't use auto mode to process file.");
                        Console.Error.WriteLine("Provide --code-reg and --metadata-reg for manual mode.");
                        return 1;
                    }
                    Console.WriteLine("Using manual mode...");
                    il2Cpp.Init(codeReg.Value, metadataReg.Value);
                }

                if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
                {
                    var typeDef = metadata.typeDefs[0];
                    var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
                    metadata.ImageBase = il2CppType.data.typeHandle - metadata.header.typeDefinitionsOffset;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("An error occurred while processing.");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }

            var executor = new Il2CppExecutor(metadata, il2Cpp);

            if (genDumpCs)
            {
                Console.WriteLine("Dumping...");
                var decompiler = new Il2CppDecompiler(executor);
                decompiler.Decompile(config, outputDir);
                Console.WriteLine("Done!");
            }

            if (config.GenerateStruct && genStruct)
            {
                Console.WriteLine("Generate struct...");
                try
                {
                    var scriptGenerator = new StructGenerator(executor);
                    scriptGenerator.WriteScript(outputDir, fastMode, workerThreads);
                    Console.WriteLine("Done!");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error generating struct: " + ex.Message);
                }
            }

            if (config.GenerateDummyDll && genDummyDll)
            {
                try
                {
                    Console.WriteLine("Generating dummy dll...");
                    DummyAssemblyExporter.Export(executor, outputDir, config.DummyDllAddToken);
                    Console.WriteLine("Done!");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error generating dummy dll: " + ex.Message);
                }
                Directory.SetCurrentDirectory(basePath);
            }

            if (aiFormat != "none")
            {
                var fmt = string.IsNullOrEmpty(config.AIDumpFormat) ? "all" : config.AIDumpFormat;
                if (aiFormat != "all") fmt = aiFormat;
                if (fmt != "none")
                {
                    Console.WriteLine("Generate AI dump...");
                    try
                    {
                        new Il2CppAIDumper(executor).Dump(outputDir, fmt);
                        Console.WriteLine("Done!");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Error generating AI dump: " + ex.Message);
                    }
                }
            }

            if (copyScripts)
                CopyScripts(basePath, outputDir);

            return 0;
        }

        private static ulong ParseHex(string s)
        {
            if (s.StartsWith("0x") || s.StartsWith("0X"))
                return Convert.ToUInt64(s.Substring(2), 16);
            return Convert.ToUInt64(s, 16);
        }

        private static void CopyScripts(string basePath, string outputPath)
        {
            var scripts = new[]
            {
                "ghidra.py", "ghidra_wasm.py", "ghidra_with_struct.py",
                "hopper-py3.py", "ida.py", "ida_py3.py",
                "ida_with_struct.py", "ida_with_struct_py3.py",
                "il2cpp_header_to_binja.py", "il2cpp_header_to_ghidra.py"
            };
            foreach (var script in scripts)
            {
                var src = Path.Combine(basePath, script);
                if (File.Exists(src))
                {
                    File.Copy(src, Path.Combine(outputPath, script), true);
                    Console.WriteLine($"Copied {script}");
                }
                else
                {
                    Console.WriteLine($"Warning: {script} not found");
                }
            }
        }
    }
}

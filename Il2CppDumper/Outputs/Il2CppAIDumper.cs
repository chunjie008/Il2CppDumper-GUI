using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class Il2CppAIDumper
    {
        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private readonly Il2Cpp il2Cpp;

        public Il2CppAIDumper(Il2CppExecutor il2CppExecutor)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;
        }

        public void Dump(string outputDir)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            MainForm.Log("Generate AI dump (dump_ai.json)...");
            var path = Path.Combine(outputDir, "dump_ai.json");

            var typeImageNames = new Dictionary<int, string>();
            foreach (var imageDef in metadata.imageDefs)
            {
                var imgName = metadata.GetStringFromIndex(imageDef.nameIndex);
                for (int i = imageDef.typeStart; i < imageDef.typeStart + imageDef.typeCount; i++)
                    typeImageNames[i] = imgName;
            }

            var methodPtrs = new Dictionary<int, (ulong va, ulong rva)>();
            if (il2Cpp.Version >= 24.2)
            {
                foreach (var imageDef in metadata.imageDefs)
                {
                    var imgName = metadata.GetStringFromIndex(imageDef.nameIndex);
                    for (int ti = imageDef.typeStart; ti < imageDef.typeStart + imageDef.typeCount; ti++)
                    {
                        var td = metadata.typeDefs[ti];
                        for (int mi = td.methodStart; mi < td.methodStart + td.method_count; mi++)
                        {
                            var md = metadata.methodDefs[mi];
                            if (md.token == 0) continue;
                            try
                            {
                                var ptr = il2Cpp.GetMethodPointer(imgName, md);
                                if (ptr > 0) methodPtrs[mi] = (ptr, il2Cpp.GetRVA(ptr));
                            }
                            catch { }
                        }
                    }
                }
            }
            else
            {
                for (int mi = 0; mi < metadata.methodDefs.Length; mi++)
                {
                    var md = metadata.methodDefs[mi];
                    if (md.methodIndex >= 0 && md.methodIndex < il2Cpp.methodPointers.Length)
                    {
                        var ptr = il2Cpp.methodPointers[md.methodIndex];
                        if (ptr > 0) methodPtrs[mi] = (ptr, 0);
                    }
                }
            }

            using var fs = File.Create(path);
            using var jw = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
            jw.WriteStartObject();

            // --- Meta ---
            jw.WriteNumber("v", metadata.Version);
            jw.WriteString("u", UnityVersionMap.GetUnityVersionRange(metadata.Version));
            jw.WriteNumber("ts", metadata.typeDefs.Length);
            jw.WriteNumber("ms", metadata.methodDefs.Length);
            jw.WriteNumber("fs", metadata.fieldDefs.Length);
            jw.WriteNumber("ss", metadata.stringLiterals?.Length ?? 0);

            jw.WriteStartArray("imgs");
            foreach (var img in metadata.imageDefs)
            {
                jw.WriteStartObject();
                jw.WriteString("n", metadata.GetStringFromIndex(img.nameIndex));
                jw.WriteNumber("ts", img.typeStart);
                jw.WriteNumber("tc", img.typeCount);
                jw.WriteEndObject();
            }
            jw.WriteEndArray();

            // --- Types ---
            jw.WriteStartArray("t");
            for (int ti = 0; ti < metadata.typeDefs.Length; ti++)
            {
                var td = metadata.typeDefs[ti];
                jw.WriteStartObject();
                jw.WriteNumber("id", ti);
                jw.WriteString("n", executor.GetTypeDefName(td, true, false));

                var ns = metadata.GetStringFromIndex(td.namespaceIndex);
                if (!string.IsNullOrEmpty(ns)) jw.WriteString("ns", ns);

                if (td.parentIndex >= 0 && td.parentIndex < il2Cpp.types.Length)
                {
                    var pn = executor.GetTypeName(il2Cpp.types[td.parentIndex], false, false);
                    if (!string.IsNullOrEmpty(pn) && pn != "object") jw.WriteString("p", pn);
                }
                if (typeImageNames.TryGetValue(ti, out var im)) jw.WriteString("img", im);

                if (td.interfaces_count > 0)
                {
                    jw.WriteStartArray("is");
                    for (int ii = 0; ii < td.interfaces_count; ii++)
                    {
                        var ifIdx = metadata.interfaceIndices[td.interfacesStart + ii];
                        jw.WriteStringValue(executor.GetTypeName(il2Cpp.types[ifIdx], false, false));
                    }
                    jw.WriteEndArray();
                }

                var vis = td.flags & TYPE_ATTRIBUTE_VISIBILITY_MASK;
                jw.WriteString("v", vis switch
                {
                    TYPE_ATTRIBUTE_PUBLIC or TYPE_ATTRIBUTE_NESTED_PUBLIC => "public",
                    TYPE_ATTRIBUTE_NESTED_PRIVATE => "private",
                    TYPE_ATTRIBUTE_NESTED_FAMILY => "protected",
                    _ => "internal"
                });
                if ((td.flags & TYPE_ATTRIBUTE_ABSTRACT) != 0) jw.WriteBoolean("ab", true);
                if ((td.flags & TYPE_ATTRIBUTE_SEALED) != 0) jw.WriteBoolean("sl", true);
                if ((td.flags & TYPE_ATTRIBUTE_INTERFACE) != 0) jw.WriteBoolean("if", true);
                if (td.IsEnum) jw.WriteBoolean("en", true);
                if (td.IsValueType) jw.WriteBoolean("vt", true);
                jw.WriteNumber("tk", td.token);
                jw.WriteNumber("sz", td.bitfield);

                // Fields
                if (td.field_count > 0)
                {
                    jw.WriteStartArray("fds");
                    for (int fi = td.fieldStart; fi < td.fieldStart + td.field_count && fi < metadata.fieldDefs.Length; fi++)
                    {
                        var fd = metadata.fieldDefs[fi];
                        jw.WriteStartObject();
                        jw.WriteString("n", metadata.GetStringFromIndex(fd.nameIndex));
                        if (fd.typeIndex >= 0 && fd.typeIndex < il2Cpp.types.Length)
                            jw.WriteString("t", executor.GetTypeName(il2Cpp.types[fd.typeIndex], false, false));
                        try
                        {
                            var off = il2Cpp.GetFieldOffsetFromIndex(ti, fi - td.fieldStart, fi, td.IsValueType, false);
                            if (off >= 0) jw.WriteNumber("o", off);
                        }
                        catch { }
                        jw.WriteEndObject();
                    }
                    jw.WriteEndArray();
                }

                // Methods
                if (td.method_count > 0)
                {
                    jw.WriteStartArray("mts");
                    for (int mi = td.methodStart; mi < td.methodStart + td.method_count && mi < metadata.methodDefs.Length; mi++)
                    {
                        var md = metadata.methodDefs[mi];
                        jw.WriteStartObject();
                        jw.WriteString("n", metadata.GetStringFromIndex(md.nameIndex));
                        try
                        {
                            var rt = executor.GetTypeName(il2Cpp.types[md.returnType], false, false);
                            var ps = new List<string>();
                            for (int pj = 0; pj < md.parameterCount; pj++)
                            {
                                var pd = metadata.parameterDefs[md.parameterStart + pj];
                                ps.Add(executor.GetTypeName(il2Cpp.types[pd.typeIndex], false, false));
                            }
                            var sig = $"{rt} {metadata.GetStringFromIndex(md.nameIndex)}({string.Join(", ", ps)})";
                            jw.WriteString("s", sig);
                        }
                        catch { }
                        if (methodPtrs.TryGetValue(mi, out var pi))
                        {
                            jw.WriteString("a", $"0x{pi.va:X}");
                            if (pi.rva > 0) jw.WriteString("ra", $"0x{pi.rva:X}");
                        }
                        var mf = md.flags;
                        jw.WriteString("v", (mf & METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK) switch
                        {
                            METHOD_ATTRIBUTE_PUBLIC => "public",
                            METHOD_ATTRIBUTE_PRIVATE => "private",
                            METHOD_ATTRIBUTE_FAMILY => "protected",
                            _ => "internal"
                        });
                        if ((mf & METHOD_ATTRIBUTE_STATIC) != 0) jw.WriteBoolean("st", true);
                        if ((mf & METHOD_ATTRIBUTE_VIRTUAL) != 0) jw.WriteBoolean("vr", true);
                        if ((mf & METHOD_ATTRIBUTE_ABSTRACT) != 0) jw.WriteBoolean("ab", true);
                        jw.WriteNumber("tk", md.token);
                        if (md.slot != ushort.MaxValue) jw.WriteNumber("sl", md.slot);
                        jw.WriteEndObject();
                    }
                    jw.WriteEndArray();
                }

                // Properties
                if (td.property_count > 0)
                {
                    jw.WriteStartArray("pts");
                    for (int pi = td.propertyStart; pi < td.propertyStart + td.property_count && pi < metadata.propertyDefs.Length; pi++)
                    {
                        var pd = metadata.propertyDefs[pi];
                        jw.WriteStartObject();
                        jw.WriteString("n", metadata.GetStringFromIndex(pd.nameIndex));
                        if (pd.get >= 0) jw.WriteString("g", metadata.GetStringFromIndex(metadata.methodDefs[td.methodStart + pd.get].nameIndex));
                        if (pd.set >= 0) jw.WriteString("s", metadata.GetStringFromIndex(metadata.methodDefs[td.methodStart + pd.set].nameIndex));
                        jw.WriteNumber("tk", pd.token);
                        jw.WriteEndObject();
                    }
                    jw.WriteEndArray();
                }

                // Nested types
                if (td.nested_type_count > 0)
                {
                    jw.WriteStartArray("nts");
                    for (int ni = 0; ni < td.nested_type_count; ni++)
                        jw.WriteNumberValue(metadata.nestedTypeIndices[td.nestedTypesStart + ni]);
                    jw.WriteEndArray();
                }
                jw.WriteEndObject();
            }
            jw.WriteEndArray();

            // Strings
            if (metadata.stringLiterals != null && metadata.stringLiterals.Length > 0)
            {
                jw.WriteStartArray("strs");
                for (uint i = 0; i < metadata.stringLiterals.Length; i++)
                {
                    try
                    {
                        var value = metadata.GetStringLiteralFromIndex(i);
                        if (!string.IsNullOrEmpty(value))
                        {
                            jw.WriteStartObject();
                            jw.WriteNumber("id", i);
                            jw.WriteString("v", value);
                            jw.WriteEndObject();
                        }
                    }
                    catch { }
                }
                jw.WriteEndArray();
            }
            jw.WriteEndObject();
            sw.Stop();
            MainForm.Log($"  [ai] done in {sw.ElapsedMilliseconds}ms -> {path}");
        }
    }
}

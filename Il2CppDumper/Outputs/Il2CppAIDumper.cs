using Microsoft.Data.Sqlite;
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

        public void Dump(string outputDir, string format = "all")
        {
            if (format == "none") return;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var typeImageNames = BuildTypeImageMap();
            var methodPtrs = BuildMethodPointerMap();

            if (format == "all" || format == "json")
            {
                MainForm.Log("Generate AI dump (dump_ai.json)...");
                var path = Path.Combine(outputDir, "dump_ai.json");
                DumpJson(path, typeImageNames, methodPtrs);
                MainForm.Log($"  [ai] done in {sw.ElapsedMilliseconds}ms -> {path}");
            }

            if (format == "all" || format == "sqlite")
            {
                MainForm.Log("Generate AI dump (dump_ai.db)...");
                var path = Path.Combine(outputDir, "dump_ai.db");
                DumpSqlite(path, typeImageNames, methodPtrs);
                MainForm.Log($"  [ai] done in {sw.ElapsedMilliseconds}ms -> {path}");
            }

            sw.Stop();
        }

        private Dictionary<int, string> BuildTypeImageMap()
        {
            var map = new Dictionary<int, string>();
            foreach (var imageDef in metadata.imageDefs)
            {
                var imgName = metadata.GetStringFromIndex(imageDef.nameIndex);
                for (int i = imageDef.typeStart; i < imageDef.typeStart + imageDef.typeCount; i++)
                    map[i] = imgName;
            }
            return map;
        }

        private Dictionary<int, (ulong va, ulong rva)> BuildMethodPointerMap()
        {
            var map = new Dictionary<int, (ulong, ulong)>();
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
                                if (ptr > 0) map[mi] = (ptr, il2Cpp.GetRVA(ptr));
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
                        if (ptr > 0) map[mi] = (ptr, 0);
                    }
                }
            }
            return map;
        }

        private static string AiPrompt = """
This file contains structured metadata from a Unity Il2Cpp dumped game, optimized for AI/LLM parsing.

KEY MAPPINGS:
v=metadataVersion, u=unityVersion, ts=typeCount, ms=methodCount, fs=fieldCount, ss=stringCount
imgs=[{n:assemblyName, ts:typeStart, tc:typeCount}]
t=[type objects]

TYPE OBJECT KEYS:
n=name, ns=namespace, img=assembly, p=parent, v=visibility, ab=abstract
sl=sealed, if=interface, en=enum, vt=valueType, tk=token, sz=size
is=[interfaces], fds=[{n:name, t:type, o:offset}]
mts=[{n:name, s:signature, v:visibility, st=static, vr=virtual, ab=abstract, a=va, ra=rva, tk=token, sl=slot}]
pts=[{n:name, g=getter, s=setter, tk=token}]
nts=[nestedTypeIds]

USAGE:
1. Search for a type by name: grep '"n":"TargetType"' dump_ai.json
2. Read the surrounding ~100 lines for fields/methods/properties
3. Look up parent types via the "p" field for inheritance chains
4. Use VA addresses ("a") to set breakpoints or locate in disassembler
5. Use field offsets ("o") for struct recovery in Ghidra/IDA
6. Query SQLite for faster targeted lookups: sqlite3 dump_ai.db "SELECT * FROM methods WHERE type_id=<id>"
""";

        // ======================================================================
        // JSON OUTPUT
        // ======================================================================

        private void DumpJson(string path, Dictionary<int, string> typeImageNames, Dictionary<int, (ulong va, ulong rva)> methodPtrs)
        {
            using var fs = File.Create(path);
            using var jw = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
            jw.WriteStartObject();

            jw.WriteString("prompt", AiPrompt);

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
        }

        // ======================================================================
        // SQLITE OUTPUT
        // ======================================================================

        private void DumpSqlite(string path, Dictionary<int, string> typeImageNames, Dictionary<int, (ulong va, ulong rva)> methodPtrs)
        {
            if (File.Exists(path)) File.Delete(path);

            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();

            using var tx = conn.BeginTransaction();

            CreateSqliteTables(conn);

            InsertMeta(conn, typeImageNames);

            var imageIdMap = InsertImages(conn);

            for (int ti = 0; ti < metadata.typeDefs.Length; ti++)
            {
                var td = metadata.typeDefs[ti];

                var typeId = InsertType(conn, ti, td, typeImageNames);
                InsertTypeFlags(conn, typeId, td);

                InsertFields(conn, ti, td, typeId);
                InsertMethods(conn, ti, td, typeId, methodPtrs);
                InsertProperties(conn, ti, td, typeId);
                InsertNestedTypes(conn, td, typeId);
                InsertInterfaces(conn, td, typeId);
            }

            InsertStrings(conn);

            tx.Commit();

            conn.Close();
        }

        private static void CreateSqliteTables(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS images (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    type_start INTEGER,
                    type_count INTEGER
                );
                CREATE TABLE IF NOT EXISTS types (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    ns TEXT,
                    image TEXT,
                    parent TEXT,
                    visibility TEXT,
                    is_abstract INTEGER DEFAULT 0,
                    is_sealed INTEGER DEFAULT 0,
                    is_interface INTEGER DEFAULT 0,
                    is_enum INTEGER DEFAULT 0,
                    is_valuetype INTEGER DEFAULT 0,
                    token INTEGER,
                    size INTEGER
                );
                CREATE TABLE IF NOT EXISTS type_interfaces (
                    type_id INTEGER NOT NULL,
                    interface_name TEXT NOT NULL,
                    PRIMARY KEY (type_id, interface_name)
                );
                CREATE TABLE IF NOT EXISTS methods (
                    id INTEGER PRIMARY KEY,
                    type_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    signature TEXT,
                    va TEXT,
                    rva TEXT,
                    visibility TEXT,
                    is_static INTEGER DEFAULT 0,
                    is_virtual INTEGER DEFAULT 0,
                    is_abstract INTEGER DEFAULT 0,
                    token INTEGER,
                    slot INTEGER
                );
                CREATE TABLE IF NOT EXISTS fields (
                    id INTEGER PRIMARY KEY,
                    type_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    field_type TEXT,
                    offset INTEGER
                );
                CREATE TABLE IF NOT EXISTS properties (
                    id INTEGER PRIMARY KEY,
                    type_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    getter TEXT,
                    setter TEXT,
                    token INTEGER
                );
                CREATE TABLE IF NOT EXISTS nested_types (
                    type_id INTEGER NOT NULL,
                    nested_type_id INTEGER NOT NULL,
                    PRIMARY KEY (type_id, nested_type_id)
                );
                CREATE TABLE IF NOT EXISTS strings (
                    id INTEGER PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_types_name ON types(name);
                CREATE INDEX IF NOT EXISTS idx_types_image ON types(image);
                CREATE INDEX IF NOT EXISTS idx_methods_type ON methods(type_id);
                CREATE INDEX IF NOT EXISTS idx_methods_name ON methods(name);
                CREATE INDEX IF NOT EXISTS idx_fields_type ON fields(type_id);
                CREATE INDEX IF NOT EXISTS idx_properties_type ON properties(type_id);
            """;
            cmd.ExecuteNonQuery();
        }

        private static void InsertMeta(SqliteConnection conn, Dictionary<int, string> typeImageNames)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO meta (key, value) VALUES (@k, @v)";
            foreach (var kv in new Dictionary<string, string>
            {
                ["prompt"] = AiPrompt,
                ["format_version"] = "1",
            })
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@k", kv.Key);
                cmd.Parameters.AddWithValue("@v", kv.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private Dictionary<int, int> InsertImages(SqliteConnection conn)
        {
            var map = new Dictionary<int, int>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO images (id, name, type_start, type_count) VALUES (@id, @n, @ts, @tc)";
            for (int i = 0; i < metadata.imageDefs.Length; i++)
            {
                var img = metadata.imageDefs[i];
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@id", i);
                cmd.Parameters.AddWithValue("@n", metadata.GetStringFromIndex(img.nameIndex));
                cmd.Parameters.AddWithValue("@ts", img.typeStart);
                cmd.Parameters.AddWithValue("@tc", img.typeCount);
                cmd.ExecuteNonQuery();
                map[i] = i;
            }
            return map;
        }

        private int InsertType(SqliteConnection conn, int ti, Il2CppTypeDefinition td, Dictionary<int, string> typeImageNames)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO types (id, name, ns, image, parent, visibility, token, size)
                VALUES (@id, @n, @ns, @img, @p, @v, @tk, @sz)
            """;
            var pn = "";
            if (td.parentIndex >= 0 && td.parentIndex < il2Cpp.types.Length)
            {
                pn = executor.GetTypeName(il2Cpp.types[td.parentIndex], false, false);
                if (pn == "object") pn = "";
            }
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", ti);
            cmd.Parameters.AddWithValue("@n", executor.GetTypeDefName(td, true, false));
            cmd.Parameters.AddWithValue("@ns", (object)metadata.GetStringFromIndex(td.namespaceIndex) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@img", typeImageNames.TryGetValue(ti, out var im) ? (object)im : DBNull.Value);
            cmd.Parameters.AddWithValue("@p", string.IsNullOrEmpty(pn) ? DBNull.Value : (object)pn);
            cmd.Parameters.AddWithValue("@v", GetTypeVisibility(td.flags));
            cmd.Parameters.AddWithValue("@tk", td.token);
            cmd.Parameters.AddWithValue("@sz", td.bitfield);
            cmd.ExecuteNonQuery();
            return ti;
        }

        private void InsertTypeFlags(SqliteConnection conn, int typeId, Il2CppTypeDefinition td)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE types SET is_abstract=@ab, is_sealed=@sl, is_interface=@if, is_enum=@en, is_valuetype=@vt WHERE id=@id";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", typeId);
            cmd.Parameters.AddWithValue("@ab", (td.flags & TYPE_ATTRIBUTE_ABSTRACT) != 0 ? 1 : 0);
            cmd.Parameters.AddWithValue("@sl", (td.flags & TYPE_ATTRIBUTE_SEALED) != 0 ? 1 : 0);
            cmd.Parameters.AddWithValue("@if", (td.flags & TYPE_ATTRIBUTE_INTERFACE) != 0 ? 1 : 0);
            cmd.Parameters.AddWithValue("@en", td.IsEnum ? 1 : 0);
            cmd.Parameters.AddWithValue("@vt", td.IsValueType ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        private void InsertFields(SqliteConnection conn, int ti, Il2CppTypeDefinition td, int typeId)
        {
            if (td.field_count <= 0) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO fields (type_id, name, field_type, offset) VALUES (@tid, @n, @t, @o)";
            for (int fi = td.fieldStart; fi < td.fieldStart + td.field_count && fi < metadata.fieldDefs.Length; fi++)
            {
                var fd = metadata.fieldDefs[fi];
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@tid", typeId);
                cmd.Parameters.AddWithValue("@n", metadata.GetStringFromIndex(fd.nameIndex));
                cmd.Parameters.AddWithValue("@t", (fd.typeIndex >= 0 && fd.typeIndex < il2Cpp.types.Length)
                    ? executor.GetTypeName(il2Cpp.types[fd.typeIndex], false, false) : "");
                try
                {
                    var off = il2Cpp.GetFieldOffsetFromIndex(ti, fi - td.fieldStart, fi, td.IsValueType, false);
                    cmd.Parameters.AddWithValue("@o", off >= 0 ? (object)off : DBNull.Value);
                }
                catch
                {
                    cmd.Parameters.AddWithValue("@o", DBNull.Value);
                }
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertMethods(SqliteConnection conn, int ti, Il2CppTypeDefinition td, int typeId, Dictionary<int, (ulong va, ulong rva)> methodPtrs)
        {
            if (td.method_count <= 0) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO methods (type_id, name, signature, va, rva, visibility, is_static, is_virtual, is_abstract, token, slot) VALUES (@tid, @n, @s, @a, @ra, @v, @st, @vr, @ab, @tk, @sl)";
            for (int mi = td.methodStart; mi < td.methodStart + td.method_count && mi < metadata.methodDefs.Length; mi++)
            {
                var md = metadata.methodDefs[mi];
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@tid", typeId);
                cmd.Parameters.AddWithValue("@n", metadata.GetStringFromIndex(md.nameIndex));
                try
                {
                    var rt = executor.GetTypeName(il2Cpp.types[md.returnType], false, false);
                    var ps = new List<string>();
                    for (int pj = 0; pj < md.parameterCount; pj++)
                    {
                        var pd = metadata.parameterDefs[md.parameterStart + pj];
                        ps.Add(executor.GetTypeName(il2Cpp.types[pd.typeIndex], false, false));
                    }
                    cmd.Parameters.AddWithValue("@s", $"{rt} {metadata.GetStringFromIndex(md.nameIndex)}({string.Join(", ", ps)})");
                }
                catch
                {
                    cmd.Parameters.AddWithValue("@s", DBNull.Value);
                }
                if (methodPtrs.TryGetValue(mi, out var pi))
                {
                    cmd.Parameters.AddWithValue("@a", $"0x{pi.va:X}");
                    cmd.Parameters.AddWithValue("@ra", pi.rva > 0 ? $"0x{pi.rva:X}" : DBNull.Value);
                }
                else
                {
                    cmd.Parameters.AddWithValue("@a", DBNull.Value);
                    cmd.Parameters.AddWithValue("@ra", DBNull.Value);
                }
                var mf = md.flags;
                cmd.Parameters.AddWithValue("@v", (mf & METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK) switch
                {
                    METHOD_ATTRIBUTE_PUBLIC => "public",
                    METHOD_ATTRIBUTE_PRIVATE => "private",
                    METHOD_ATTRIBUTE_FAMILY => "protected",
                    _ => "internal"
                });
                cmd.Parameters.AddWithValue("@st", (mf & METHOD_ATTRIBUTE_STATIC) != 0 ? 1 : 0);
                cmd.Parameters.AddWithValue("@vr", (mf & METHOD_ATTRIBUTE_VIRTUAL) != 0 ? 1 : 0);
                cmd.Parameters.AddWithValue("@ab", (mf & METHOD_ATTRIBUTE_ABSTRACT) != 0 ? 1 : 0);
                cmd.Parameters.AddWithValue("@tk", md.token);
                cmd.Parameters.AddWithValue("@sl", md.slot != ushort.MaxValue ? (object)md.slot : DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertProperties(SqliteConnection conn, int ti, Il2CppTypeDefinition td, int typeId)
        {
            if (td.property_count <= 0) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO properties (type_id, name, getter, setter, token) VALUES (@tid, @n, @g, @s, @tk)";
            for (int pi = td.propertyStart; pi < td.propertyStart + td.property_count && pi < metadata.propertyDefs.Length; pi++)
            {
                var pd = metadata.propertyDefs[pi];
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@tid", typeId);
                cmd.Parameters.AddWithValue("@n", metadata.GetStringFromIndex(pd.nameIndex));
                cmd.Parameters.AddWithValue("@g", pd.get >= 0
                    ? metadata.GetStringFromIndex(metadata.methodDefs[td.methodStart + pd.get].nameIndex) : DBNull.Value);
                cmd.Parameters.AddWithValue("@s", pd.set >= 0
                    ? metadata.GetStringFromIndex(metadata.methodDefs[td.methodStart + pd.set].nameIndex) : DBNull.Value);
                cmd.Parameters.AddWithValue("@tk", pd.token);
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertNestedTypes(SqliteConnection conn, Il2CppTypeDefinition td, int typeId)
        {
            if (td.nested_type_count <= 0) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO nested_types (type_id, nested_type_id) VALUES (@tid, @ntid)";
            for (int ni = 0; ni < td.nested_type_count; ni++)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@tid", typeId);
                cmd.Parameters.AddWithValue("@ntid", metadata.nestedTypeIndices[td.nestedTypesStart + ni]);
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertInterfaces(SqliteConnection conn, Il2CppTypeDefinition td, int typeId)
        {
            if (td.interfaces_count <= 0) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO type_interfaces (type_id, interface_name) VALUES (@tid, @if)";
            for (int ii = 0; ii < td.interfaces_count; ii++)
            {
                var ifIdx = metadata.interfaceIndices[td.interfacesStart + ii];
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@tid", typeId);
                cmd.Parameters.AddWithValue("@if", executor.GetTypeName(il2Cpp.types[ifIdx], false, false));
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertStrings(SqliteConnection conn)
        {
            if (metadata.stringLiterals == null || metadata.stringLiterals.Length <= 0) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO strings (id, value) VALUES (@id, @v)";
            for (uint i = 0; i < metadata.stringLiterals.Length; i++)
            {
                try
                {
                    var value = metadata.GetStringLiteralFromIndex(i);
                    if (!string.IsNullOrEmpty(value))
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@id", i);
                        cmd.Parameters.AddWithValue("@v", value);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { }
            }
        }

        private static string GetTypeVisibility(uint flags)
        {
            return (flags & TYPE_ATTRIBUTE_VISIBILITY_MASK) switch
            {
                TYPE_ATTRIBUTE_PUBLIC or TYPE_ATTRIBUTE_NESTED_PUBLIC => "public",
                TYPE_ATTRIBUTE_NESTED_PRIVATE => "private",
                TYPE_ATTRIBUTE_NESTED_FAMILY => "protected",
                _ => "internal"
            };
        }
    }
}

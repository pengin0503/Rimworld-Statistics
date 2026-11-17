// Checks every [HarmonyPatch(typeof(T), "Method")] class in a built mod DLL against the audited RimWorld index:
// the target type and method exist, the name resolves to exactly one method (as AccessTools.Method needs), and every
// argument a Prefix/Postfix/Finalizer asks Harmony to inject exists on the target with a type it can be bound to
// (#313): a target parameter (by name or __N), __instance, ___field, __result and the other special arguments, by value
// or by reference, and the patch method's own return type. The rules are stricter than Harmony's own where Harmony would
// reinterpret memory (two different value types) or a type cannot be proven assignable from the index.
// The reference checker (refcheck.py) cannot do this: a patch target is a type token plus a string, not a member reference.
//
// usage: PatchTargetCheck <mod.dll> <index dir> [--index directory] [--target PatchClass:Type:method:type|type] [--field Type:field]...
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PatchTargetCheck <mod.dll> <index dir> [--index directory] [--target PatchClass:Type:method:types] [--field Type:field]...");
    return 2;
}

var index = LoadIndex(args[1]);
var failures = new List<string>();
var checkedTargets = 0;

var optional = new Dictionary<string, List<(string Type, string Method, List<string> Parameters)>>(StringComparer.Ordinal);
var fields = new List<(string Type, string Field)>();
var extraIndexes = new List<string>();
for (var i = 2; i < args.Length; i++)
{
    var option = args[i];
    if (i + 1 >= args.Length)
    {
        failures.Add($"missing value for {option}");
        break;
    }
    var value = args[++i];
    switch (option)
    {
        case "--index":
            extraIndexes.Add(value);
            break;
        case "--target" when value.Split(':', 4) is [var patch, var type, var method, var parameters]:
            if (!optional.TryGetValue(patch, out var targets))
                optional.Add(patch, targets = []);
            targets.Add((type, method, parameters.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList()));
            break;
        case "--field" when value.Split(':', 2) is [var type, var field]:
            fields.Add((type, field));
            break;
        default:
            failures.Add($"bad argument {option} {value}");
            break;
    }
}

// Compiler-generated helper types are shared by unrelated assemblies.
// Only import explicitly audited target types from optional indexes, and the base types and interfaces they derive
// from that the main index lacks (a MOD class deriving from a framework class), so __instance can be checked.
var targetTypes = optional.Values.SelectMany(targets => targets).Select(target => target.Type).ToHashSet(StringComparer.Ordinal);
var extraEntries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
foreach (var directory in extraIndexes)
    foreach (var (name, entry) in LoadIndex(directory))
        if (targetTypes.Contains(name) && !index.TryAdd(name, entry))
            failures.Add($"duplicate target type {name} in {directory}");
        else
            extraEntries.TryAdd(name, entry);
var importing = new Queue<string>(targetTypes);
while (importing.TryDequeue(out var imported))
    if (index.TryGetValue(imported, out var importedEntry))
        foreach (var parent in Parents(importedEntry).Select(GenericDefinition))
            if (!index.ContainsKey(parent) && extraEntries.TryGetValue(parent, out var parentEntry))
            {
                index.Add(parent, parentEntry);
                importing.Enqueue(parent);
            }

using (var pe = new PEReader(File.OpenRead(args[0])))
{
    var md = pe.GetMetadataReader();
    var provider = new AttributeTypeProvider();
    foreach (var th in md.TypeDefinitions)
    {
        var type = md.GetTypeDefinition(th);
        var patchClass = md.GetString(type.Name);
        foreach (var ah in type.GetCustomAttributes())
        {
            var attr = md.GetCustomAttribute(ah);
            if (AttributeTypeName(md, attr) != "HarmonyLib.HarmonyPatch")
                continue;
            var value = attr.DecodeValue(provider);
            if (value.FixedArguments.Length == 0
                && type.GetMethods().Select(md.GetMethodDefinition).Any(m => md.GetString(m.Name) is "TargetMethod" or "TargetMethods")
                && optional.Remove(patchClass, out var targets))
            {
                foreach (var target in targets)
                {
                    checkedTargets++;
                    Check(patchClass, target.Type, target.Method, target.Parameters, PatchMethods(md, type));
                }
                continue;
            }
            if (value.FixedArguments.Length < 2 || value.FixedArguments[0].Value is not string serializedType || value.FixedArguments[1].Value is not string methodName)
            {
                failures.Add($"{patchClass}: unsupported [HarmonyPatch] form (expected typeof(T), \"method\"[, Type[]])");
                continue;
            }
            var targetType = serializedType.Split(',')[0].Trim();   // "RimWorld.ShipCountdown, Assembly-CSharp, Version=…"
            List<string>? argTypes = null;
            if (value.FixedArguments.Length >= 3 && value.FixedArguments[2].Value is ImmutableArray<CustomAttributeTypedArgument<string>> arr)
                argTypes = arr.Select(a => IndexTypeName((string)a.Value!)).ToList();
            // HarmonyLib.ArgumentType: Normal 0, Ref 1, Out 2, Pointer 3 (by-ref parameters are "T&" in the index).
            if (argTypes != null && value.FixedArguments.Length >= 4 && value.FixedArguments[3].Value is ImmutableArray<CustomAttributeTypedArgument<string>> kinds)
                for (var k = 0; k < kinds.Length && k < argTypes.Count; k++)
                    if (kinds[k].Value is int kind && kind is 1 or 2)
                        argTypes[k] += "&";
            checkedTargets++;
            Check(patchClass, targetType, methodName, argTypes, PatchMethods(md, type));
        }
    }
}

foreach (var patch in optional.Keys)
    failures.Add($"configured target was not used: {patch}");
foreach (var (type, field) in fields)
{
    if (FindField(type, field) is null)
        failures.Add($"field {type}.{field}: not found");
    else
        Console.WriteLine($"matched field  {type}.{field}");
}

Console.WriteLine($"patch targets checked: {checkedTargets}, failures: {failures.Count}");
foreach (var f in failures)
    Console.WriteLine("FAIL " + f);
return failures.Count == 0 && checkedTargets > 0 ? 0 : 1;

void Check(string patchClass, string targetType, string methodName, List<string>? argTypes, List<PatchMethod> patchMethods)
{
    var candidates = new List<(JsonElement Method, string Owner)>();
    for (var t = targetType; t != null && index.TryGetValue(t, out var entry); t = BaseOf(entry))
    {
        foreach (var m in entry.GetProperty("methods").EnumerateArray())
            if (m.GetProperty("name").GetString() == methodName)
                candidates.Add((m, t));
        if (candidates.Count > 0)
            break;   // AccessTools.Method takes the most derived declaration
    }
    if (!index.ContainsKey(targetType))
    {
        failures.Add($"{patchClass}: type {targetType} not in the index");
        return;
    }
    if (argTypes != null)
        candidates = candidates.Where(c => ParamTypes(c.Method).Select(SystemNeutral).SequenceEqual(argTypes.Select(SystemNeutral))).ToList();
    if (candidates.Count != 1)
    {
        failures.Add($"{patchClass}: {targetType}::{methodName} resolves to {candidates.Count} methods");
        return;
    }
    var (method, owner) = candidates[0];
    var targetParams = method.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToList();
    var targetParamTypes = ParamTypes(method).ToList();
    var returnType = method.GetProperty("signature").GetProperty("returnType").GetString()!;
    var isStatic = method.GetProperty("attributes").GetString()!.Contains("Static");
    var target = $"{targetType}::{methodName}";
    foreach (var patch in patchMethods)
    {
        var where = $"{patchClass}.{patch.Name}";
        CheckPatchReturn(where, patch);
        foreach (var (n, type) in patch.Parameters)
        {
            switch (n)
            {
                case "__state":
                    continue;   // the patch's own storage
                case "__result":
                    if (Norm(returnType) == "void")
                        failures.Add($"{where}: __result on void {target}");
                    else if (!Binds(returnType, type, boxedRef: true))
                        failures.Add($"{where}: __result {type} cannot bind the {returnType} result of {target}{Unindexed(returnType)}");
                    continue;
                case "__originalMethod":
                    Special(where, n, type, "System.Reflection.MethodBase", "System.Reflection.MethodInfo", "object");
                    continue;
                case "__args":
                    Special(where, n, type, "object[]");
                    continue;
                case "__runOriginal":
                    Special(where, n, type, "bool");
                    continue;
                case "__exception":
                    Special(where, n, type, "System.Exception", "object");
                    continue;
                case "__instance":
                    if (isStatic)
                        failures.Add($"{where}: __instance on static {target}");
                    else if (!Binds(owner, type))
                        failures.Add($"{where}: __instance {type} cannot bind {owner}, the declaring type of {target}{Unindexed(owner)}");
                    continue;
            }
            if (n.StartsWith("___", StringComparison.Ordinal))
            {
                // Harmony looks the field up from the declaring type of the patched method.
                var name = n[3..];
                if (FindField(owner, name) is not { } field)
                    failures.Add($"{where}: field {name} not on {owner}");
                else if (!field.TryGetProperty("type", out var fieldType) || fieldType.GetString() is not { } fieldTypeName)
                    failures.Add($"{where}: the index has no type for field {owner}.{name} (rebuild it with tools/RefMetadataIndex)");
                else if (isStatic && !(field.TryGetProperty("attributes", out var a) && a.GetString()!.Contains("Static")))
                    failures.Add($"{where}: instance field {name} injected into static {target}");
                else if (!Binds(fieldTypeName, type))
                    failures.Add($"{where}: {n} {type} cannot bind field {owner}.{name} of type {fieldTypeName}{Unindexed(fieldTypeName)}");
                continue;
            }
            var position = n.StartsWith("__", StringComparison.Ordinal) && int.TryParse(n.AsSpan(2), out var p) ? p : targetParams.IndexOf(n);
            if (position < 0 || position >= targetParams.Count)
                failures.Add($"{where}: parameter '{n}' is not a parameter of {target}({string.Join(", ", targetParams)})");
            else if (!Binds(targetParamTypes[position], type))
                failures.Add($"{where}: parameter '{n}' {type} cannot bind {targetParamTypes[position]} {targetParams[position]} of {target}{Unindexed(targetParamTypes[position])}");
        }
    }
    Console.WriteLine($"matched        {owner}::{methodName}({string.Join(", ", ParamTypes(method))})  <- {patchClass} [{string.Join(", ", patchMethods.Select(p => p.Name))}]");
}

// Harmony calls a Prefix for its bool (run the original or not), a Finalizer for the exception to rethrow, and a
// Postfix that returns a value only as a pass-through of its first argument.
void CheckPatchReturn(string where, PatchMethod patch)
{
    var returned = Norm(patch.ReturnType);
    if (returned == "void")
        return;
    var valid = patch.Name switch
    {
        "Prefix" => returned == "bool",
        "Finalizer" => returned == "System.Exception",
        _ => patch.Parameters.Count > 0 && Norm(patch.Parameters[0].Type) == returned,
    };
    if (!valid)
        failures.Add($"{where}: returns {patch.ReturnType}, which Harmony does not accept from a {patch.Name}");
}

void Special(string where, string name, string type, params string[] allowed)
{
    if (type.EndsWith('&') || !allowed.Contains(Norm(type)))
        failures.Add($"{where}: {name} must be {string.Join(" or ", allowed)} by value, not {type}");
}

// Whether Harmony can pass a value of type source (a target parameter, the instance, a field or the result) to a patch
// parameter of type requested. By reference, the types must be the same (Harmony boxes a value into "ref object" only for
// __result); by value, the requested type may also be object or a base type or interface of an indexed source type.
bool Binds(string source, string requested, bool boxedRef = false)
{
    var have = Norm(source.TrimEnd('&'));
    var want = Norm(requested.TrimEnd('&'));
    if (have == want || IsGenericParameter(have) || IsGenericParameter(want))
        return true;
    // The declaring type of a method of a generic class is its definition (List`1); the patch names an instance of it.
    if (have.Contains('`') && !have.Contains('<') && want.StartsWith(have + "<", StringComparison.Ordinal))
        return true;
    if (requested.EndsWith('&'))
        return boxedRef && want == "object";
    return want == "object" || Ancestors(have).Contains(want);
}

// For a failure: the types in the source's hierarchy that the index lacks (add their assembly's index with --index).
string Unindexed(string source)
{
    var unknown = new SortedSet<string>(StringComparer.Ordinal);
    Ancestors(Norm(source.TrimEnd('&')), unknown);
    return unknown.Count == 0 ? "" : $" (not in the index: {string.Join(", ", unknown)})";
}

// The type, its base types and interfaces, as far as the index knows them; types the index lacks are added to unknown.
HashSet<string> Ancestors(string type, ISet<string>? unknown = null)
{
    var found = new HashSet<string>(StringComparer.Ordinal) { type };
    var pending = new Stack<string>();
    pending.Push(type);
    while (pending.TryPop(out var current))
    {
        if (!index.TryGetValue(GenericDefinition(current), out var entry))
        {
            if (current.Contains('.') && !current.StartsWith("System.", StringComparison.Ordinal))
                unknown?.Add(current);
            continue;
        }
        foreach (var parent in Parents(entry))
            if (found.Add(parent))
                pending.Push(parent);
    }
    return found;
}

JsonElement? FindField(string typeName, string field)
{
    for (var t = typeName; t != null && index.TryGetValue(t, out var entry); t = BaseOf(entry))
        foreach (var f in entry.GetProperty("fields").EnumerateArray())
            if (f.GetProperty("name").GetString() == field)
                return f;
    return null;
}

static string? BaseOf(JsonElement entry)
{
    if (!entry.TryGetProperty("baseType", out var b) || b.ValueKind != JsonValueKind.String)
        return null;
    var s = b.GetString()!;
    return s.StartsWith("[Assembly-CSharp]", StringComparison.Ordinal) ? s["[Assembly-CSharp]".Length..] : null;
}

static IEnumerable<string> ParamTypes(JsonElement method)
    => method.GetProperty("signature").GetProperty("parameters").EnumerateArray().Select(p => p.GetString()!);

// "[A, asm],[B, asm]" → "A, asm" and "B, asm" (brackets nest for generic arguments that are generic themselves).
static IEnumerable<string> SplitGenericArguments(string list)
{
    var depth = 0;
    var start = -1;
    for (var i = 0; i < list.Length; i++)
    {
        if (list[i] == '[')
        {
            if (depth++ == 0)
                start = i + 1;
        }
        else if (list[i] == ']' && --depth == 0)
        {
            yield return list[start..i];
        }
    }
}

// Reference assemblies place BCL types in facades ([System.Runtime], [System.Collections], …); the runtime type is the same.
static string SystemNeutral(string indexName)
    => System.Text.RegularExpressions.Regex.Replace(indexName, @"\[System(\.[A-Za-z]+)*\]", "[System]");

static string IndexTypeName(string serialized)
{
    // A generic instance ("System.Nullable`1[[Verse.DamageInfo, Assembly-CSharp, …]], System.Private.CoreLib") becomes
    // "[System.Runtime]System.Nullable`1<[Assembly-CSharp]Verse.DamageInfo>", as the index writes it.
    var open = serialized.IndexOf("[[", StringComparison.Ordinal);
    if (open > 0)
    {
        var close = serialized.LastIndexOf("]]", StringComparison.Ordinal);
        var outer = serialized[..open];
        var args = SplitGenericArguments(serialized[(open + 1)..(close + 1)]).Select(IndexTypeName);
        return (outer.StartsWith("System.", StringComparison.Ordinal) ? "[System.Runtime]" : "[Assembly-CSharp]") + outer + "<" + string.Join(",", args) + ">";
    }
    var name = serialized.Split(',')[0].Trim();
    return name switch
    {
        "System.Boolean" => "bool", "System.Int32" => "int32", "System.Single" => "float32", "System.String" => "string",
        "System.Double" => "float64", "System.Int64" => "int64", "System.Object" => "object",
        _ => serialized.Contains("Assembly-CSharp", StringComparison.Ordinal) ? "[Assembly-CSharp]" + name : name,
    };
}

static string AttributeTypeName(MetadataReader md, CustomAttribute attr)
{
    EntityHandle owner = attr.Constructor.Kind == HandleKind.MemberReference
        ? md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent
        : md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor).GetDeclaringType();
    if (owner.Kind != HandleKind.TypeReference)
        return "";
    var tr = md.GetTypeReference((TypeReferenceHandle)owner);
    return md.GetString(tr.Namespace) + "." + md.GetString(tr.Name);
}

static List<PatchMethod> PatchMethods(MetadataReader md, TypeDefinition type)
{
    var list = new List<PatchMethod>();
    var types = new TypeRenderer(md);
    foreach (var mh in type.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var name = md.GetString(m.Name);
        if (name is not ("Prefix" or "Postfix" or "Finalizer"))
            continue;
        var signature = m.DecodeSignature(types, null);
        var names = m.GetParameters().Select(ph => md.GetParameter(ph)).Where(p => p.SequenceNumber > 0)
            .OrderBy(p => p.SequenceNumber).Select(p => md.GetString(p.Name)).ToList();
        list.Add(new PatchMethod(name, signature.ReturnType,
            names.Select((n, i) => (n, signature.ParameterTypes[i])).ToList()));
    }
    return list;
}

// Type names without their assembly ("[Assembly-CSharp]Verse.Pawn" → "Verse.Pawn"): reference assemblies, the game and
// MOD indexes and the patch DLL name the same type from different assemblies (facades, mscorlib, netstandard). BCL types
// named in a base type list are written as the signature primitives ("System.Object" → "object").
static string Norm(string name)
{
    var bare = System.Text.RegularExpressions.Regex.Replace(name, @"\[[A-Za-z0-9_.\-]+\]", "");
    return System.Text.RegularExpressions.Regex.Replace(bare, @"(?<![\w.+`])System\.(Object|String|Boolean|Int32|Int64|Single|Double|Char|Byte|SByte|Int16|UInt16|UInt32|UInt64|Void)(?![\w.`])",
        m => m.Groups[1].Value switch
        {
            "Object" => "object", "String" => "string", "Boolean" => "bool", "Int32" => "int32", "Int64" => "int64",
            "Single" => "float32", "Double" => "float64", "Char" => "char", "Byte" => "uint8", "SByte" => "int8",
            "Int16" => "int16", "UInt16" => "uint16", "UInt32" => "uint32", "UInt64" => "uint64", _ => "void",
        });
}

static bool IsGenericParameter(string name) => name.Contains('!');

static string GenericDefinition(string name) => name.IndexOf('<') is var lt and >= 0 ? name[..lt] : name;

// The base type and interfaces of an index entry (the audited index may write an interface as {"type": …}).
static IEnumerable<string> Parents(JsonElement entry)
{
    if (entry.TryGetProperty("baseType", out var b) && b.ValueKind == JsonValueKind.String)
        yield return Norm(b.GetString()!);
    if (entry.TryGetProperty("interfaces", out var interfaces) && interfaces.ValueKind == JsonValueKind.Array)
        foreach (var i in interfaces.EnumerateArray())
            if (i.ValueKind == JsonValueKind.String)
                yield return Norm(i.GetString()!);
            else if (i.ValueKind == JsonValueKind.Object && i.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                yield return Norm(t.GetString()!);
}

static Dictionary<string, JsonElement> LoadIndex(string dir)
{
    var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    foreach (var file in Directory.EnumerateFiles(dir, "chunk-*.json.gz").Order(StringComparer.Ordinal))
    {
        using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
            if (e.TryGetProperty("fullName", out var n) && n.GetString() is { } name)
                map[name] = e.Clone();
    }
    if (map.Count == 0)
        throw new InvalidOperationException($"no index chunks in {dir}");
    return map;
}

/// <summary>A Prefix, Postfix or Finalizer: its return type and its parameters (name, type as the index writes it).</summary>
sealed record PatchMethod(string Name, string ReturnType, List<(string Name, string Type)> Parameters);

/// <summary>Represents types in decoded attribute arguments by their (serialized) names.</summary>
sealed class AttributeTypeProvider : ICustomAttributeTypeProvider<string>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSystemType() => "System.Type";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        return reader.GetString(tr.Namespace) + "." + reader.GetString(tr.Name);
    }
    public string GetTypeFromSerializedName(string name) => name;
    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;   // HarmonyLib.MethodType, ArgumentType
    public bool IsSystemType(string type) => type == "System.Type";
}

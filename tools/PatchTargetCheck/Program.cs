// Checks every [HarmonyPatch(typeof(T), "Method")] class in a built mod DLL against the audited RimWorld index:
// the target type and method exist, the name resolves to exactly one method (as AccessTools.Method needs), and every
// argument a Prefix/Postfix asks Harmony to inject (by parameter name, __instance, ___field) exists on the target.
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
            foreach (var (name, entry) in LoadIndex(value))
                if (!name.StartsWith('<') && !index.TryAdd(name, entry))
                    failures.Add($"duplicate type {name} in {value}");
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

void Check(string patchClass, string targetType, string methodName, List<string>? argTypes, List<(string Name, List<string> Params)> patchMethods)
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
    var isStatic = method.GetProperty("attributes").GetString()!.Contains("Static");
    foreach (var (patchMethod, names) in patchMethods)
    {
        foreach (var n in names)
        {
            if (n is "__state" or "__result" or "__originalMethod" or "__args" or "__runOriginal" or "__exception")
                continue;
            if (n == "__instance")
            {
                if (isStatic) failures.Add($"{patchClass}.{patchMethod}: __instance on static {targetType}::{methodName}");
                continue;
            }
            if (n.StartsWith("___", StringComparison.Ordinal))
            {
                if (FindField(targetType, n[3..]) is null) failures.Add($"{patchClass}.{patchMethod}: field {n[3..]} not on {targetType}");
                continue;
            }
            if (n.StartsWith("__", StringComparison.Ordinal) && int.TryParse(n.AsSpan(2), out var position)
                && position >= 0 && position < targetParams.Count)
                continue;
            if (!targetParams.Contains(n))
                failures.Add($"{patchClass}.{patchMethod}: parameter '{n}' is not a parameter of {targetType}::{methodName}({string.Join(", ", targetParams)})");
        }
    }
    Console.WriteLine($"matched        {owner}::{methodName}({string.Join(", ", ParamTypes(method))})  <- {patchClass} [{string.Join(", ", patchMethods.Select(p => p.Name))}]");
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

static List<(string, List<string>)> PatchMethods(MetadataReader md, TypeDefinition type)
{
    var list = new List<(string, List<string>)>();
    foreach (var mh in type.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var name = md.GetString(m.Name);
        if (name is not ("Prefix" or "Postfix" or "Finalizer"))
            continue;
        var names = m.GetParameters().Select(ph => md.GetParameter(ph)).Where(p => p.SequenceNumber > 0)
            .OrderBy(p => p.SequenceNumber).Select(p => md.GetString(p.Name)).ToList();
        list.Add((name, names));
    }
    return list;
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

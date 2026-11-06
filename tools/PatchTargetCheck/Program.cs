// Checks every [HarmonyPatch(typeof(T), "Method")] class in a built mod DLL against the audited RimWorld index:
// the target type and method exist, the name resolves to exactly one method (as AccessTools.Method needs), and every
// argument a Prefix/Postfix asks Harmony to inject (by parameter name, __instance, ___field) exists on the target.
// The reference checker (refcheck.py) cannot do this: a patch target is a type token plus a string, not a member reference.
//
// usage: PatchTargetCheck <mod.dll> <index dir: assembly/Assembly-CSharp> [--field Namespace.Type:fieldName]...
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PatchTargetCheck <mod.dll> <index dir> [--field Type:field]...");
    return 2;
}

var index = LoadIndex(args[1]);
var failures = new List<string>();
var checkedTargets = 0;

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
            if (value.FixedArguments.Length < 2 || value.FixedArguments[0].Value is not string serializedType || value.FixedArguments[1].Value is not string methodName)
            {
                failures.Add($"{patchClass}: unsupported [HarmonyPatch] form (expected typeof(T), \"method\"[, Type[]])");
                continue;
            }
            var targetType = serializedType.Split(',')[0].Trim();   // "RimWorld.ShipCountdown, Assembly-CSharp, Version=…"
            List<string>? argTypes = null;
            if (value.FixedArguments.Length >= 3 && value.FixedArguments[2].Value is ImmutableArray<CustomAttributeTypedArgument<string>> arr)
                argTypes = arr.Select(a => (string)a.Value!).ToList();
            checkedTargets++;
            Check(patchClass, targetType, methodName, argTypes, PatchMethods(md, type));
        }
    }
}

for (var i = 2; i < args.Length; i++)
{
    if (args[i] != "--field" || i + 1 >= args.Length)
    {
        failures.Add($"bad argument {args[i]}");
        continue;
    }
    var spec = args[++i].Split(':');
    if (FindField(spec[0], spec[1]) is null)
        failures.Add($"field {spec[0]}.{spec[1]}: not found");
    else
        Console.WriteLine($"matched field  {spec[0]}.{spec[1]}");
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
        candidates = candidates.Where(c => ParamTypes(c.Method).SequenceEqual(argTypes.Select(IndexTypeName))).ToList();
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

static string IndexTypeName(string serialized)
{
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

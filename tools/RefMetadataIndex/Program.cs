// Development-only, no game launch. Convert the installed NuGet RimWorld reference
// DLL into the same structural index consumed by tools/PatchTargetCheck: types with their base type and interfaces,
// fields (name, attributes, type) and methods (name, attributes, parameter names, signature).
// The real-game R2 audit remains authoritative; this is a complementary CI guard.
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: RefMetadataIndex <Assembly-CSharp.dll> <directory>");
    return 2;
}
var dll = args[0];
var output = args[1];
Directory.CreateDirectory(output);
using var pe = new PEReader(File.OpenRead(dll));
var md = pe.GetMetadataReader();
var types = new TypeRenderer(md);
var entries = new List<object>(md.TypeDefinitions.Count);
foreach (var h in md.TypeDefinitions)
{
    var def = md.GetTypeDefinition(h);
    var fields = new List<object>();
    foreach (var fh in def.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        // The type and attributes let PatchTargetCheck check a ___field injection, as in the audited R2 index.
        fields.Add(new { name = md.GetString(f.Name), attributes = f.Attributes.ToString(), type = f.DecodeSignature(types, null) });
    }
    var methods = new List<object>();
    foreach (var mh in def.GetMethods())
    {
        var method = md.GetMethodDefinition(mh);
        var sig = method.DecodeSignature(types, (object?)null);
        var pars = method.GetParameters()
            .Select(ph => md.GetParameter(ph))
            .Where(par => par.SequenceNumber > 0)
            .OrderBy(par => par.SequenceNumber)
            .Select(par => new { name = md.GetString(par.Name) })
            .ToArray();
        methods.Add(new
        {
            name = md.GetString(method.Name),
            attributes = method.Attributes.ToString(),
            parameters = pars,
            signature = new { parameters = sig.ParameterTypes, returnType = sig.ReturnType },
        });
    }
    var parent = def.BaseType.IsNil ? null : types.TypeName(def.BaseType);
    var interfaces = def.GetInterfaceImplementations()
        .Select(ih => types.TypeName(md.GetInterfaceImplementation(ih).Interface)).ToArray();
    entries.Add(new
    {
        fullName = types.DefinitionName(h),
        baseType = parent,
        interfaces,
        fields,
        methods,
    });
}
var outFile = Path.Combine(output, "chunk-0000.json.gz");
using (var stream = new GZipStream(File.Create(outFile), CompressionLevel.Optimal))
    JsonSerializer.Serialize(stream, new { entries });
Console.WriteLine($"CI reference index: {entries.Count} types, {outFile}");
return 0;

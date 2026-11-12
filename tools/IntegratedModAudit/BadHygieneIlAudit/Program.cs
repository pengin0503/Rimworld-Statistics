// Inspection only: Mono.Cecil reads metadata/IL and never executes BadHygiene code.
using Mono.Cecil;

if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the pinned public BadHygiene.dll path");
using var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadSymbols = false });
string[] focus = [
    "DubsBadHygiene.PlumbingNet",
    "DubsBadHygiene.CompWaterStorage",
    "DubsBadHygiene.CompWaterTrader",
    "DubsBadHygiene.HygienePipeMapComp",
    "DubsBadHygiene.MapComponent_Hygiene"
];
string[] methodNeedles = [
    "WaterStorage","WaterCap","WaterPumpedPerTick","PullWater","PushWater",
    "TickWater","FillTank","ExposeData","MapComponentTick","GetInspectString","CompInspectString",
    "get_space","get_WaterPerTick","get_CapPercent"
];
var count = 0;
foreach (var type in assembly.MainModule.Types.Where(t => focus.Contains(t.FullName, StringComparer.Ordinal)))
{
    Console.WriteLine("FOCUS " + type.FullName + " base=" + type.BaseType);
    foreach (var field in type.Fields)
        Console.WriteLine($" FIELD {field.FieldType.FullName} {field.Name} public={field.IsPublic} static={field.IsStatic}");
    foreach (var property in type.Properties)
        Console.WriteLine($" PROPERTY {property.PropertyType.FullName} {property.Name} getter={property.GetMethod?.IsPublic}");
    foreach (var method in type.Methods.Where(m => methodNeedles.Any(n => m.Name.Contains(n,StringComparison.Ordinal))))
    {
        Console.WriteLine($" METHOD {method.FullName} public={method.IsPublic} static={method.IsStatic}");
        if (!method.HasBody) continue;
        var instructions = method.Body.Instructions;
        var cap = method.Name is "TickWater" or "MapComponentTick" ? 180 : 100;
        foreach (var ins in instructions.Take(cap))
        {
            var op = ins.Operand is Mono.Cecil.Cil.Instruction target ? target.Offset.ToString("X4") : ins.Operand?.ToString() ?? "";
            Console.WriteLine("   IL " + ins.OpCode + " " + op);
        }
        if (instructions.Count>cap) Console.WriteLine($"   ... omitted {instructions.Count-cap} instructions");
        count++;
    }
}

// Static callsite index: identifies where water flows actually mutate rather than
// inferring lifetime production or consumption from storage snapshots or capacities.
var observed = 0;
foreach (var type in assembly.MainModule.Types)
{
    foreach (var method in type.Methods.Concat(type.NestedTypes.SelectMany(n => n.Methods)))
    {
        if (!method.HasBody) continue;
        var instructions = method.Body.Instructions;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].Operand is not MethodReference call ||
                call.DeclaringType.FullName != "DubsBadHygiene.PlumbingNet" ||
                call.Name is not ("PullWater" or "PushWater" or "TickWater"))
                continue;
            Console.WriteLine($"CALLSITE {method.FullName} -> {call.Name}");
            foreach (var ins in instructions.Skip(Math.Max(0, i - 4)).Take(8))
                Console.WriteLine("  NEAR " + ins.OpCode + " " + ins.Operand);
            if (++observed >= 80) break;
        }
        if (observed >= 80) break;
    }
    if (observed >= 80) break;
}
Console.WriteLine("waterTransferCallsites="+observed);


// Enforce every exact public runtime member accessed by the addon. Merely compiling
// against RimWorld reference assemblies does not validate DBH's reflected ABI.
Mono.Cecil.TypeDefinition RequiredType(string fullName) =>
    assembly.MainModule.Types.Single(t => t.FullName == fullName);
var pipe = RequiredType("DubsBadHygiene.PlumbingNet");
var storage = RequiredType("DubsBadHygiene.CompWaterStorage");
var storageProps = RequiredType("DubsBadHygiene.CompProperties_WaterStorage");
var pipeMap = RequiredType("DubsBadHygiene.HygienePipeMapComp");
foreach (var field in new[] { "PumpingCapacitySum", "GroundWaterCapacitySum" })
    if (!pipe.Fields.Any(f => f.Name == field && f.IsPublic && f.FieldType.FullName == "System.Single"))
        throw new InvalidDataException("Missing public DBH float " + field);
foreach (var field in new[] { "WaterTowers", "WaterPumps", "WaterWells" })
    if (!pipe.Fields.Any(f => f.Name == field && f.IsPublic && f.FieldType.FullName.StartsWith("System.Collections.Generic.List")))
        throw new InvalidDataException("Missing public DBH list " + field);
if (!pipeMap.Fields.Any(f => f.Name == "PipeNets" && f.IsPublic && f.FieldType.FullName == "DubsBadHygiene.PlumbingNet[]") ||
    !storage.Fields.Any(f => f.Name == "WaterStorage" && f.IsPublic && f.FieldType.FullName == "System.Single") ||
    !storageProps.Fields.Any(f => f.Name == "WaterStorageCap" && f.IsPublic && f.FieldType.FullName == "System.Single") ||
    !storage.Properties.Any(p => p.Name == "Props" && p.PropertyType.FullName == storageProps.FullName &&
        p.GetMethod is { IsPublic: true }))
    throw new InvalidDataException("DBH capacity/source snapshot reflection ABI changed");
Console.WriteLine("DBH P0 water snapshot public ABI and numeric field types: PASS");

// JobCondition.Succeeded is 2 in the RimWorld 1.7 reference assembly.
var auditedMvids = new[] { new Guid("85b03536-f71c-4780-8aaa-3c790e11de9a"), new Guid("7f4b7428-565f-4495-86c7-9c01a1dac461") };
if (!auditedMvids.Contains(assembly.MainModule.Mvid))
    throw new InvalidDataException("Unreviewed DBH module: " + assembly.MainModule.Mvid);
foreach (var name in new[] { "UseToilet", "takeShower", "washHands", "useWashBucket", "washAtCell" })
{
    var driver = RequiredType("DubsBadHygiene.JobDriver_" + name);
    var makeToils = driver.Methods.Single(m => m.Name == "MakeNewToils");
    var iterator = driver.NestedTypes.Single(t => t.Name.StartsWith("<MakeNewToils>d__", StringComparison.Ordinal));
    var moveNext = iterator.Methods.Single(m => m.Name == "MoveNext");
    var il = moveNext.Body.Instructions;
    var end = il.Single(i => i.Operand is MethodReference call && call.Name == "AddEndCondition");
    var condition = end.Previous.Previous.Operand as MethodDefinition;
    if (makeToils.IsStatic || condition?.ReturnType.FullName != "Verse.AI.JobCondition" ||
        !condition.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "get_CurLevel") ||
        !condition.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_2) ||
        !condition.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_4))
        throw new InvalidDataException("DBH terminal need/success condition changed: " + name);
    Console.WriteLine("DBH audited terminal condition: " + name);
}

Console.WriteLine("auditedMethodBodies="+count);
return count >= 5 ? 0 : 2;

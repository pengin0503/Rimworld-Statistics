// Read-only audit of the exact Saurid/Phytokin 1.6 DLLs that load in RimWorld 1.7.
// Checks the target ABI and the post-spawn success structure without executing game code.
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2 || args.Any(path => !File.Exists(path)))
    throw new ArgumentException("Pass the selected 1.7 Saurid and Phytokin DLL paths.");

using var saurid = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadSymbols = false });
using var phytokin = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { ReadSymbols = false });

static void Require(bool value, string message)
{
    if (!value) throw new InvalidDataException("Race birth IL contract: " + message);
}

static TypeDefinition Type(AssemblyDefinition assembly, string fullName) =>
    assembly.MainModule.Types.Single(t => t.FullName == fullName);

static MethodDefinition Hatch(TypeDefinition type) =>
    type.Methods.Single(m => m.Name == "Hatch" && m.Parameters.Count == 0 &&
        !m.IsStatic && m.HasBody && m.ReturnType.FullName == "System.Void");

static bool Calls(Instruction instruction, string name) =>
    instruction.Operand is MethodReference callee && callee.Name == name;

static bool HasField(TypeDefinition type, string name, string kind) =>
    type.Fields.Any(f => f.Name == name && f.FieldType.FullName == kind);

static (int Call, IReadOnlyList<Instruction> Il) SpawnCall(MethodDefinition method)
{
    var il = method.Body.Instructions;
    var match = il.Select((instruction, index) => (instruction, index))
        .Where(x => x.instruction.Operand is MethodReference callee &&
            callee.Name == "TrySpawnHatchedOrBornPawn" &&
            callee.DeclaringType.Name == "PawnUtility" &&
            callee.ReturnType.FullName == "System.Boolean" &&
            callee.Parameters.Count == 2 &&
            callee.Parameters[0].ParameterType.FullName == "Verse.Pawn" &&
            callee.Parameters[1].ParameterType.FullName == "Verse.Thing")
        .Select(x => x.index).ToArray();
    Require(match.Length == 1, method.FullName +
        ": expected exactly one boolean PawnUtility.TrySpawnHatchedOrBornPawn(Pawn, Thing) call");
    int index = match[0];
    // Both upstream methods test the boolean before running the success body.
    // The compiler may insert a local store/load before the conditional branch.
    Require(il.Skip(index + 1).Take(6).Any(x =>
        x.OpCode.Code is Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S),
        method.FullName + ": spawn boolean is not checked by a conditional branch");
    return (index, il);
}

Require(saurid.Name.Name == "VRESaurids", "Saurid assembly identity changed");
Require(phytokin.Name.Name == "VanillaRacesExpanded-Phytokin", "Phytokin assembly identity changed");
var sauridType = Type(saurid, "VRESaurids.Comp_HumanHatcher");
Require(sauridType.BaseType?.FullName == "Verse.ThingComp", "Saurid hatcher base type changed");
Require(HasField(sauridType, "hatchee", "Verse.Pawn"), "Saurid hatchee field changed");
Require(HasField(sauridType, "hatched", "System.Boolean"), "Saurid hatched field changed");
var sauridHatch = Hatch(sauridType);
var (sauridSpawn, sauridIl) = SpawnCall(sauridHatch);
Require(sauridIl.Skip(sauridSpawn + 1).Any(x =>
    x.OpCode.Code == Code.Stfld && x.Operand is FieldReference f && f.Name == "hatched"),
    "Saurid hatch completion flag not set after the conditional spawn");
Require(sauridIl.Skip(sauridSpawn + 1).Any(x =>
    x.Operand is MethodReference m && m.Name == "SendLetter"),
    "Saurid expected post-spawn notification was removed");

var sapling = Type(phytokin, "VanillaRacesExpandedPhytokin.Building_SaplingChild");
Require(sapling.BaseType?.FullName == "Verse.Building", "Phytokin sapling base type changed");
Require(HasField(sapling, "successfulBirth", "System.Boolean"),
    "Phytokin successfulBirth field changed");
var saplingHatch = Hatch(sapling);
var (saplingSpawn, saplingIl) = SpawnCall(saplingHatch);
Require(saplingHatch.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally),
    "Phytokin Hatch no longer has a finally block");
Require(saplingIl.Skip(saplingSpawn + 1).Any(x =>
    x.OpCode.Code == Code.Stfld && x.Operand is FieldReference f && f.Name == "successfulBirth"),
    "Phytokin finally completion flag changed");
Require(saplingIl.Skip(saplingSpawn + 1).Any(x => Calls(x, "AddGene")),
    "Phytokin no longer assigns genes after the spawn");
Require(saplingIl.Skip(saplingSpawn + 1).Any(x => Calls(x, "SetXenotype")),
    "Phytokin no longer selects the resulting xenotype after spawn");

Console.WriteLine("Selected RimWorld 1.7 Saurid and Phytokin DLLs: Hatch ABI / verified spawn / post-spawn gene and completion IL PASS");

// Audit the exact VPE and VEF assemblies cached in read-only R2 for RimWorld 1.7.
// Mono.Cecil reads method metadata and IL; no game or mod code is executed.
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2 || !File.Exists(args[0]) || !File.Exists(args[1]))
    throw new ArgumentException("Pass the pinned VPE.dll and VEF.dll");
using var vpe = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadSymbols = false });
using var vef = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { ReadSymbols = false });
void Ensure(bool valid, string msg)
{
    if (!valid) throw new InvalidDataException("VPE 1.7 confirmed cast audit: " + msg);
}
TypeDefinition Type(AssemblyDefinition assembly, string fullName) =>
    assembly.MainModule.Types.Single(t => t.FullName == fullName);
MethodDefinition Method(TypeDefinition type, string method, params string[] paramTypes) =>
    type.Methods.Single(m => m.Name == method &&
        m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(paramTypes) &&
        !m.IsStatic && m.HasBody && m.ReturnType.FullName == "System.Void");
bool Calls(MethodDefinition method, string name, string? owner = null) =>
    method.Body.Instructions.Any(i => i.Operand is MethodReference callee &&
        callee.Name == name && (owner is null || callee.DeclaringType.FullName == owner));

var psiExt = Type(vpe, "VanillaPsycastsExpanded.AbilityExtension_Psycast");
var psiCast = Method(psiExt, "Cast", "RimWorld.Planet.GlobalTargetInfo[]", "VEF.Abilities.Ability");
Ensure(psiExt.BaseType?.FullName == "VEF.Abilities.AbilityExtension_AbilityMod",
    "Psycast hook owner must be the VEF cast extension");
Ensure(Calls(psiCast, "UseAbility", "VanillaPsycastsExpanded.Hediff_PsycastAbilities"),
    "VPE Cast must call Hediff.UseAbility to confirm focus/entropy consumption");
Ensure(psiCast.Body.Instructions.Count(i => i.Operand is MethodReference callee &&
    callee.Name == "UseAbility") == 1, "VPE extension must consume once per cast");

var vefAbility = Type(vef, "VEF.Abilities.Ability");
Ensure(vefAbility.Fields.Any(f => f.Name == "pawn" && f.FieldType.FullName == "Verse.Pawn") &&
    vefAbility.Fields.Any(f => f.Name == "def" && f.FieldType.FullName == "VEF.Abilities.AbilityDef"),
    "VEF ability pawn and def identity fields must remain readable");
var dispatcher = Method(vefAbility, "Cast", "RimWorld.Planet.GlobalTargetInfo[]");
var extensionCalls = dispatcher.Body.Instructions
    .Where(i => i.Operand is MethodReference callee &&
        callee.Name == "Cast" && callee.DeclaringType.FullName == "VEF.Abilities.AbilityExtension_AbilityMod")
    .ToList();
Ensure(extensionCalls.Count == 2, "VEF Cast must invoke one of two extension dispatch branches");
Ensure(extensionCalls.All(i => i.OpCode.Code is Code.Callvirt or Code.Call),
    "VEF dispatch must call extension Cast, not queue a separate command");
Ensure(Calls(dispatcher, "PostCast"), "VEF dispatcher must finish cast effects before notifying PostCast");

var extBase = Type(vef, "VEF.Abilities.AbilityExtension_AbilityMod");
var legacy = Method(extBase, "Cast", "Verse.LocalTargetInfo", "VEF.Abilities.Ability");
Ensure(Calls(legacy, "Cast", "VEF.Abilities.AbilityExtension_AbilityMod"),
    "VEF local-target compatibility wrapper must forward to global-target virtual Cast");
Console.WriteLine("VPE and VEF 1.7 R2-identical IL confirms unique consumed Cast dispatch and abilities: PASS");

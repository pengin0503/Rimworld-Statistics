// Read-only Mono.Cecil metadata and IL audit for pinned RimWorld 1.7 selected MOD assemblies
// and optional Game-Windows 1.7.5011 / NuGet Assembly-CSharp bridge signatures.
// Never loads or executes target MOD or game code.
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

var root = ".devinputs/integrated";
string? gameDll = null;
var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
var inspects = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--game-dll" && i + 1 < args.Length)
    {
        gameDll = args[++i];
    }
    else if (args[i] == "--override" && i + 1 < args.Length)
    {
        var kv = args[++i].Split('=', 2);
        if (kv.Length != 2)
            throw new ArgumentException("--override expects <checkout>=<dll-path>");
        overrides[kv[0]] = kv[1];
    }
    else if (args[i] == "--inspect" && i + 1 < args.Length)
    {
        inspects.Add(args[++i]);
    }
    else if (!args[i].StartsWith('-'))
    {
        root = args[i];
    }
    else
    {
        throw new ArgumentException("Unknown flag: " + args[i]);
    }
}

if (inspects.Count > 0)
{
    foreach (var spec in inspects)
    {
        var parts = spec.Split(':', 3);
        using var asm = AssemblyDefinition.ReadAssembly(parts[0], new ReaderParameters { ReadSymbols = false });
        foreach (var t in asm.MainModule.GetTypes())
        {
            if (parts.Length > 1 && parts[1].Length > 0 && !t.FullName.Contains(parts[1], StringComparison.Ordinal))
                continue;
            foreach (var f in t.Fields)
            {
                if (parts.Length == 2 || (parts.Length > 2 && f.Name.Contains(parts[2], StringComparison.Ordinal)))
                    Console.WriteLine($"[{asm.Name.Name}] FIELD {t.FullName}::{f.Name} : {f.FieldType.FullName}");
            }
            foreach (var m in t.Methods)
            {
                if (parts.Length > 2 && parts[2].Length > 0 && !m.Name.Contains(parts[2], StringComparison.Ordinal))
                    continue;
                Console.WriteLine($"[{asm.Name.Name}] {t.FullName}::{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.FullName))}) -> {m.ReturnType.FullName}");
                if (!m.HasBody) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr)
                        Console.WriteLine($"   {ins.OpCode} {mr.DeclaringType.FullName}::{mr.Name}({string.Join(", ", mr.Parameters.Select(p => p.ParameterType.FullName))})");
                    else if (ins.Operand is FieldReference fr)
                        Console.WriteLine($"   {ins.OpCode} FIELD {fr.DeclaringType.FullName}::{fr.Name} : {fr.FieldType.FullName}");
                }
            }
        }
    }
    return;
}

if (!Directory.Exists(root))
    throw new DirectoryNotFoundException("Integrated MOD audit directory not found: " + root);

var checkedAssemblies = 0;
var checkedAssertions = 0;

void Ensure(bool valid, string context, string message)
{
    checkedAssertions++;
    if (!valid)
    {
        Console.Error.WriteLine($"AdapterSignatureAudit FAILED: [{context}] {message}");
        Environment.Exit(1);
    }
}

string DllPath(string checkout, string relativeDll)
{
    if (overrides.TryGetValue(checkout, out var overridden))
        return overridden;
    return Path.Combine(root, checkout, relativeDll.Replace('/', Path.DirectorySeparatorChar));
}

void CheckAboutPackageId(string checkout, string expectedPackageId)
{
    var aboutPath = Path.Combine(root, checkout, "About", "About.xml");
    Ensure(File.Exists(aboutPath), checkout, $"About.xml missing at {aboutPath}");
    var doc = XDocument.Load(aboutPath);
    var pkg = doc.Root?.Elements().FirstOrDefault(e =>
        string.Equals(e.Name.LocalName, "packageId", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
    Ensure(string.Equals(pkg, expectedPackageId, StringComparison.OrdinalIgnoreCase),
        checkout, $"About.xml packageId expected '{expectedPackageId}', got '{pkg}'");
}

AssemblyDefinition OpenModAssembly(string checkout, string relativeDll, string expectedAssemblyName, string? expectedPackageId = null)
{
    if (expectedPackageId is not null && !overrides.ContainsKey(checkout))
        CheckAboutPackageId(checkout, expectedPackageId);

    var path = DllPath(checkout, relativeDll);
    Ensure(File.Exists(path), checkout, $"DLL missing: {path}");
    var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
    Ensure(string.Equals(asm.Name.Name, expectedAssemblyName, StringComparison.Ordinal),
        checkout, $"Assembly name expected '{expectedAssemblyName}', got '{asm.Name.Name}'");
    checkedAssemblies++;
    return asm;
}

TypeDefinition RequireType(AssemblyDefinition asm, string fullName)
{
    var type = asm.MainModule.GetType(fullName)
        ?? asm.MainModule.Types.FirstOrDefault(t => t.FullName == fullName);
    Ensure(type is not null, asm.Name.Name, $"Missing type '{fullName}'");
    return type!;
}

TypeDefinition RequireTypeByShortName(AssemblyDefinition asm, string shortName)
{
    var matches = asm.MainModule.Types.Where(t => t.Name == shortName).ToList();
    Ensure(matches.Count == 1, asm.Name.Name, $"Expected single type named '{shortName}', found {matches.Count}");
    return matches[0];
}

FieldDefinition RequireField(TypeDefinition type, string name, string? expectedFieldType = null)
{
    var field = type.Fields.FirstOrDefault(f => f.Name == name);
    Ensure(field is not null, type.FullName, $"Missing field '{name}'");
    if (expectedFieldType is not null)
        Ensure(field!.FieldType.FullName == expectedFieldType, type.FullName,
            $"Field '{name}' expected type '{expectedFieldType}', got '{field.FieldType.FullName}'");
    return field!;
}

bool InheritsFrom(AssemblyDefinition asm, TypeDefinition type, string ancestorFullName)
{
    for (TypeReference? cur = type.BaseType; cur is not null;)
    {
        if (cur.FullName == ancestorFullName)
            return true;
        var resolved = asm.MainModule.GetType(cur.FullName);
        cur = resolved?.BaseType;
    }
    return false;
}

PropertyDefinition RequireProperty(TypeDefinition type, string name, string? expectedPropertyType = null, bool requireGetter = true)
{
    PropertyDefinition? prop = null;
    for (TypeDefinition? cur = type; cur is not null && prop is null;)
    {
        prop = cur.Properties.FirstOrDefault(p => p.Name == name);
        cur = cur.BaseType is null ? null : type.Module.GetType(cur.BaseType.FullName);
    }
    Ensure(prop is not null, type.FullName, $"Missing property '{name}'");
    if (requireGetter)
        Ensure(prop!.GetMethod is not null, type.FullName, $"Property '{name}' missing getter");
    if (expectedPropertyType is not null)
        Ensure(prop!.PropertyType.FullName == expectedPropertyType, type.FullName,
            $"Property '{name}' expected type '{expectedPropertyType}', got '{prop.PropertyType.FullName}'");
    return prop!;
}

MethodDefinition RequireMethod(TypeDefinition type, string name, string? returnType = null, params string[]? paramTypes)
{
    var candidates = type.Methods.Where(m => m.Name == name).ToList();
    if (paramTypes is not null)
        candidates = candidates.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(paramTypes)).ToList();
    Ensure(candidates.Count == 1, type.FullName,
        $"Expected single method '{name}({(paramTypes is null ? "*" : string.Join(", ", paramTypes))})', found {candidates.Count}");
    var method = candidates[0];
    if (returnType is not null)
        Ensure(method.ReturnType.FullName == returnType, type.FullName,
            $"Method '{name}' expected return '{returnType}', got '{method.ReturnType.FullName}'");
    return method;
}

bool CallsMethod(MethodDefinition caller, string calleeName, string? declaringTypeContains = null) =>
    caller.HasBody && caller.Body.Instructions.Any(i => i.Operand is MethodReference mr
        && mr.Name == calleeName
        && (declaringTypeContains is null || mr.DeclaringType.FullName.Contains(declaringTypeContains, StringComparison.Ordinal)));

// 1. GiddyUp 2 (GiddyUpMountStatistics)
using (var asm = OpenModAssembly("giddyup", "1.7/Assemblies/GiddyUpCore.dll", "GiddyUpCore", "MemeGoddess.GiddyUp"))
{
    var util = RequireType(asm, "GiddyUp.MountUtility");
    Ensure(util.Methods.Any(m => m.IsStatic && m.Name == "Mount"), "GiddyUpCore", "MountUtility.Mount missing");
    var storage = RequireType(asm, "GiddyUp.StorageUtility");
    RequireMethod(storage, "IsMounted", "System.Boolean", "Verse.Thing");
    RequireMethod(storage, "GetExtendedPawnData", "GiddyUp.ExtendedPawnData", "Verse.Pawn");
    var data = RequireType(asm, "GiddyUp.ExtendedPawnData");
    RequireProperty(data, "Mount", "Verse.Pawn");
    RequireType(asm, "GiddyUp.Jobs.JobDriver_Mount");
}

// 2. Project RimFactory Revived (RimFactoryStatistics)
using (var asm = OpenModAssembly("rimfactory", "Assemblies/ProjectRimFactory.dll", "ProjectRimFactory", "spdskatr.projectrimfactory"))
{
    var util = RequireType(asm, "ProjectRimFactory.Common.PlaceThingUtility");
    Ensure(util.Methods.Any(m => m.IsStatic && m.Name == "PRFTryPlaceThing" && m.ReturnType.FullName == "System.Boolean"),
        "ProjectRimFactory", "PlaceThingUtility.PRFTryPlaceThing missing");
}

// 3. Dubs Rimatomics (RimatomicsByproductStatistics & RimatomicsStage2Outcomes)
using (var asm = OpenModAssembly("rimatomics", "1.7/Assemblies/Rimatomics.dll", "Rimatomics", "Dubwise.Rimatomics"))
{
    var proc = RequireTypeByShortName(asm, "Building_PlutoniumProc");
    var job = RequireTypeByShortName(asm, "JobDriver_UnloadPlutonium");
    Ensure(job.BaseType?.FullName == "Verse.AI.JobDriver", "Rimatomics", "JobDriver_UnloadPlutonium must derive from JobDriver");
    var takeOut = RequireMethod(proc, "TakeOutFuel", "Verse.Thing");
    Ensure(CallsMethod(takeOut, "TryPlaceThing", "GenPlace"), "Rimatomics", "TakeOutFuel must place plutonium byproducts via GenPlace.TryPlaceThing");

    var hel = RequireType(asm, "Rimatomics.Verb_HELBeam");
    var energyWeapon = RequireType(asm, "Rimatomics.Building_EnergyWeapon");
    var sabot = RequireType(asm, "Rimatomics.WorldObject_Sabot");
    var core = RequireType(asm, "Rimatomics.reactorCore");
    var popped = RequireType(asm, "Rimatomics.Building_PoppedCore");
    RequireMethod(hel, "TryCastShot", "System.Boolean");
    RequireField(energyWeapon, "KillCounter", "System.Int32");
    RequireMethod(sabot, "Arrived", "System.Void");
    RequireField(sabot, "arrived", "System.Boolean");
    RequireField(sabot, "railgun");
    RequireField(sabot, "initialTile", "RimWorld.Planet.PlanetTile");
    RequireMethod(core, "SCRAM", "System.Void");
    RequireField(core, "IsShutdown", "System.Boolean");
    RequireField(core, "FuelCount", "System.Int32");
    var coreDestroy = RequireMethod(core, "Destroy", "System.Void", "Verse.DestroyMode");
    RequireMethod(popped, "GoBang", "System.Void", "System.Int32", "Verse.Map");
    Ensure(CallsMethod(coreDestroy, "GoBang", "Rimatomics.Building_PoppedCore"), "Rimatomics", "reactorCore.Destroy must invoke Building_PoppedCore.GoBang on meltdown");
}

// 4. Dubs Rimefeller (RimefellerExtractionStatistics & RimefellerFuelUsageStatistics)
using (var asm = OpenModAssembly("rimefeller", "1.7/Assemblies/Rimefeller.dll", "Rimefeller", "Dubwise.Rimefeller"))
{
    var derrick = RequireType(asm, "Rimefeller.CompOilDerrick");
    var well = RequireType(asm, "Rimefeller.Building_WellHead");
    var oilField = RequireType(asm, "Rimefeller.OilField");
    var pump = RequireMethod(derrick, "Pump", "System.Void");
    Ensure(CallsMethod(pump, "Used", "Rimefeller.OilField"), "Rimefeller", "CompOilDerrick.Pump must debit OilField.Used");
    RequireField(derrick, "well", "Rimefeller.Building_WellHead");
    RequireField(well, "oilField", "Rimefeller.OilField");
    RequireField(oilField, "CurrentCap", "System.Double");

    var pipe = RequireType(asm, "Rimefeller.CompPipe");
    var refinery = RequireType(asm, "Rimefeller.CompRefinery");
    var plant = RequireType(asm, "Rimefeller.FuelPowerplant");
    var net = RequireType(asm, "Rimefeller.PipelineNet");
    Ensure(refinery.BaseType?.FullName == "Rimefeller.CompPipe", "Rimefeller", "CompRefinery must inherit CompPipe");
    RequireMethod(pipe, "CompTick", "System.Void");
    RequireMethod(refinery, "CompTick", "System.Void");
    RequireMethod(plant, "CompTick", "System.Void");
    RequireProperty(pipe, "pipeNet", "Rimefeller.PipelineNet");
    RequireField(plant, "PipeComp", "Rimefeller.CompPipe");
    RequireField(net, "FuelAvailable", "System.Double");
}

// 5. Vanilla Achievements Expanded (VaeAchievementsStatistics)
using (var asm = OpenModAssembly("vae", "1.7/Assemblies/AchievementsExpanded.dll", "AchievementsExpanded", "vanillaexpanded.achievements"))
{
    var card = RequireType(asm, "AchievementsExpanded.AchievementCard");
    var mgr = RequireType(asm, "AchievementsExpanded.AchievementPointManager");
    RequireMethod(card, "UnlockCard", null, "System.Boolean", "Verse.Map");
    RequireMethod(mgr, "CheckUnlocks", "System.Void");
    RequireField(card, "unlocked", "System.Boolean");
    RequireField(card, "def");
    RequireField(mgr, "totalEarnedPoints", "System.Int32");
}

// 6. Vanilla Aspirations Expanded (VaeAspirationsStatistics)
using (var asm = OpenModAssembly("vaspire", "1.7/Assemblies/VAspirE.dll", "VAspirE", "VanillaExpanded.VanillaAspirationsExpanded"))
{
    var need = RequireType(asm, "VAspirE.Need_Fulfillment");
    RequireType(asm, "VAspirE.AspirationDef");
    RequireMethod(need, "Complete", null, "VAspirE.AspirationDef");
    RequireMethod(need, "CheckCompletion", null, "System.Boolean");
    RequireMethod(need, "IsComplete", "System.Boolean", "VAspirE.AspirationDef");
    RequireField(need, "Aspirations");
}

// 7. VEF PipeSystem + Hiring (VefProcessingStatistics, VceDeepchemStatistics, VnpeGrinderStatistics, VfePirateHiringStatistics)
using (var asm = OpenModAssembly("vef-outposts", "1.7/Assemblies/PipeSystem.dll", "PipeSystem"))
{
    var proc = RequireType(asm, "PipeSystem.Process");
    Ensure(proc.Methods.Any(m => m.Name == "SpawnOrPushToNet"), "PipeSystem", "Process.SpawnOrPushToNet missing");
    RequireField(proc, "parent");
    RequireField(proc, "def");
    RequireField(proc, "processCount", "System.Int32");

    var extractor = RequireType(asm, "PipeSystem.CompDeepExtractor");
    var resource = RequireType(asm, "PipeSystem.CompResource");
    var resProps = RequireType(asm, "PipeSystem.CompProperties_Resource");
    var deepProps = RequireType(asm, "PipeSystem.CompProperties_DeepExtractor");
    var net = RequireType(asm, "PipeSystem.PipeNet");
    RequireMethod(extractor, "TryProducePortion", "System.Void");
    Ensure(net.Methods.Any(m => m.Name == "DistributeAmongStorage"), "PipeSystem", "PipeNet.DistributeAmongStorage missing");
    RequireProperty(resource, "PipeNet", "PipeSystem.PipeNet");
    RequireField(net, "def");
    RequireField(deepProps, "deepThing");
    RequireField(resProps, "pipeNet");
}
using (var asm = OpenModAssembly("vef-outposts", "1.7/Assemblies/VEF.dll", "VEF"))
{
    var dialog = RequireType(asm, "VEF.Planet.Dialog_Hire");
    var tracker = RequireType(asm, "VEF.Planet.HiringContractTracker");
    RequireType(asm, "VEF.Planet.HireableFactionDef");
    RequireMethod(dialog, "OnAcceptKeyPressed", "System.Void");
    RequireField(dialog, "targetMap", "Verse.Map");
    RequireField(dialog, "curFaction", "VEF.Planet.HireableFactionDef");
    RequireField(tracker, "Instance", "VEF.Planet.HiringContractTracker");
    RequireField(tracker, "pawns");
}

// 8. VFE Classical (VfeClassicalStatistics & VfeClassicalPerkStatistics)
using (var asm = OpenModAssembly("vfec", "1.6/Assemblies/VFEC.dll", "VFEC", "OskarPotocki.VFE.Classical"))
{
    var senators = RequireType(asm, "VFEC.Senators.WorldComponent_Senators");
    var info = RequireType(asm, "VFEC.Senators.SenatorInfo");
    var perks = RequireType(asm, "VFEC.Perks.GameComponent_PerkManager");
    RequireMethod(senators, "GainFavorOf", "System.Void", "Verse.Pawn", "RimWorld.Faction");
    RequireMethod(senators, "InfoFor", "VFEC.Senators.SenatorInfo", "Verse.Pawn", "RimWorld.Faction");
    RequireField(info, "Favored", "System.Boolean");
    Ensure(perks.Methods.Any(m => m.Name == "AddPerk" && m.Parameters.Count == 1), "VFEC", "AddPerk missing");
    RequireField(perks, "ActivePerks");
}

// 9. VFE Deserters (VfeDesertersStatistics)
using (var asm = OpenModAssembly("vfed", "1.6/Assemblies/VFED.dll", "VFED", "OskarPotocki.VFE.Deserters"))
{
    var dialog = RequireType(asm, "VFED.Dialog_DeserterNetwork");
    RequireMethod(dialog, "TrySpendIntel", "System.Boolean", "System.Int32", "System.Int32");
    RequireField(dialog, "Map", "Verse.Map");
}

// 10. VFE Empire (VfeEmpireStatistics)
using (var asm = OpenModAssembly("vfeempire", "1.7/Assemblies/VFEEmpire.dll", "VFEEmpire", "OskarPotocki.VFE.Empire"))
{
    var ritual = RequireType(asm, "VFEEmpire.RitualOutcomeEffectWorker_BestowHonor");
    var util = RequireType(asm, "VFEEmpire.HonorUtility");
    var tracker = RequireType(asm, "VFEEmpire.HonorsTracker");
    var honor = RequireType(asm, "VFEEmpire.Honor");
    Ensure(ritual.Methods.Any(m => m.Name == "Apply"), "VFEEmpire", "RitualOutcomeEffectWorker_BestowHonor.Apply missing");
    RequireMethod(util, "Honors", "VFEEmpire.HonorsTracker", "Verse.Pawn");
    RequireProperty(tracker, "PendingHonors");
    RequireProperty(tracker, "Honors");
    RequireField(honor, "def");
}

// 11. VFE Insectoids 2 (VfeInsectoidsStatistics)
using (var asm = OpenModAssembly("vfeinsectoids", "1.7/Assemblies/VFEInsectoids.dll", "VFEInsectoids", "OskarPotocki.VFE.Insectoid2"))
{
    var hive = RequireType(asm, "VFEInsectoids.CompHive");
    RequireMethod(hive, "CompTick", "System.Void");
    RequireMethod(hive, "DoSpawn", null, "System.Boolean");
    RequireField(hive, "insects");
}

// 12. VFE Medieval 2 (VfeMedieval2Statistics)
using (var asm = OpenModAssembly("vfemedieval", "1.6/Assemblies/VFEMedieval.dll", "VFEMedieval", "OskarPotocki.VFE.Medieval2"))
{
    var driver = RequireType(asm, "VFEMedieval.JobDriver_TakeHoneyOutOfApiary");
    var apiary = RequireType(asm, "VFEMedieval.Building_Apiary");
    var toil = RequireMethod(driver, "<MakeNewToils>b__5_1", "System.Void");
    Ensure(CallsMethod(toil, "TryPlaceThing", "GenPlace"), "VFEMedieval", "Honey unload toil must call GenPlace.TryPlaceThing");
    RequireProperty(driver, "Apiary", "VFEMedieval.Building_Apiary");
    RequireProperty(apiary, "HoneyReady", "System.Boolean");
}

// 13. VFE Pirates (VfePiratesStatistics)
using (var asm = OpenModAssembly("vfepirates", "1.7/Assemblies/VFEPirates.dll", "VFEPirates", "OskarPotocki.VFE.Pirates"))
{
    var foundry = RequireType(asm, "VFEPirates.Building_WarcasketFoundry");
    var project = RequireType(asm, "VFEPirates.WarcasketProject");
    RequireType(asm, "VFEPirates.JobDriver_DoWelding");
    RequireMethod(foundry, "FinishWarCasketProject", "System.Void");
    RequireField(foundry, "curWarcasketProject", "VFEPirates.WarcasketProject");
    RequireField(foundry, "occupant", "Verse.Pawn");
    RequireField(project, "armorDef");
    RequireField(project, "helmetDef");
    RequireField(project, "shoulderPadsDef");
    RequireField(project, "currentWorkAmountDone", "System.Single");
    RequireField(project, "totalWorkAmount", "System.Single");
}

// 14. VFE Settlers (VfeSettlersStatistics)
using (var asm = OpenModAssembly("vfesettlers", "1.6/Assemblies/VFE_Settlers.dll", "VFE_Settlers", "OskarPotocki.VanillaFactionsExpanded.SettlersModule"))
{
    var owner = RequireType(asm, "VFE_Settlers.Utilities.UtilityEvent");
    var closure = owner.NestedTypes.FirstOrDefault(nt => nt.Methods.Any(m => m.Name == "<CommandTurnInWanted>b__0"));
    Ensure(closure is not null, "VFE_Settlers", "UtilityEvent turn-in closure missing");
    var turnIn = RequireMethod(closure!, "<CommandTurnInWanted>b__0", "System.Void");
    Ensure(CallsMethod(turnIn, "GiveThing", "CaravanInventoryUtility"), "VFE_Settlers", "Turn-in closure must call CaravanInventoryUtility.GiveThing");
    RequireField(closure!, "caravan", "RimWorld.Planet.Caravan");
    RequireField(closure!, "criminal", "Verse.Pawn");
    RequireField(closure!, "reward", "System.Int32");
}

// 15. VFE Tribals (VfeTribalsStatistics)
using (var asm = OpenModAssembly("vfetribals", "1.6/Assemblies/VFETribals.dll", "VFETribals", "OskarPotocki.VFE.Tribals"))
{
    var comp = RequireType(asm, "VFETribals.GameComponent_Tribals");
    var era = RequireType(asm, "VFETribals.EraAdvancementDef");
    RequireType(asm, "VFETribals.CornerstoneDef");
    RequireMethod(comp, "AdvanceToEra", "System.Void", "VFETribals.EraAdvancementDef");
    RequireMethod(comp, "AddCornerstone", "System.Void", "VFETribals.CornerstoneDef");
    RequireField(comp, "playerTechLevel");
    RequireField(era, "newTechLevel");
    RequireField(comp, "cornerstones");
    RequireField(comp, "availableCornerstonePoints", "System.Int32");
}

// 16. VFE Factory (VfeFactoryStatistics)
using (var asm = OpenModAssembly("vfefactory", "1.6/Assemblies/VanillaFurnitureExpandedFactory.dll", "VanillaFurnitureExpandedFactory", "VanillaExpanded.VFEFactory"))
{
    var autofarmer = RequireType(asm, "VanillaFurnitureExpandedFactory.Building_Autofarmer");
    RequireMethod(autofarmer, "ProcessRow", "System.Void", "System.Int32");
    RequireMethod(autofarmer, "GetRowCells", null, "System.Int32");
    RequireField(autofarmer, "harvestStorageThings");
    RequireField(autofarmer, "plantDefToGrow", "Verse.ThingDef");
}

// 17. VFE Security (VfeSecurityStatistics & VfeSecurityArtilleryStatistics)
using (var asm = OpenModAssembly("vfesecurity", "1.6/Assemblies/VFESecurity.dll", "VFESecurity", "VanillaExpanded.VFESecurity"))
{
    var rearm = RequireType(asm, "VFESecurity.CompRearmable");
    RequireMethod(rearm, "Rearm", "System.Void");
    RequireField(rearm, "armed", "System.Boolean");

    var shell = RequireType(asm, "VFESecurity.WorldObject_ArtilleryProjectile");
    RequireMethod(shell, "OnArrival", "System.Void");
    RequireField(shell, "startTile");
    RequireField(shell, "targetTile");
    RequireField(shell, "projectileDef", "Verse.ThingDef");
    RequireField(shell, "launcher");
}

// 18. Vanilla Genetics Expanded (VgeExtractionStatistics & VgeHybridOutcomeStatistics)
using (var asm = OpenModAssembly("vge", "1.7/Assemblies/GeneticRim.dll", "GeneticRim", "VanillaExpanded.VGeneticsE"))
{
    var extract = RequireType(asm, "GeneticRim.CompTargetEffect_Extract");
    var doEffect = RequireMethod(extract, "DoEffectOn", "System.Void", "Verse.Pawn", "Verse.Thing");
    Ensure(CallsMethod(doEffect, "TryPlaceThing", "GenPlace"), "GeneticRim", "DoEffectOn must place extracted genome via GenPlace.TryPlaceThing");

    var womb = RequireType(asm, "GeneticRim.CompElectroWomb");
    RequireMethod(womb, "CompTick", "System.Void");
    RequireField(womb, "growingResult");
    RequireField(womb, "progress");
    RequireField(womb, "hoursProcess");
    RequireField(womb, "failure", "System.Boolean");
    RequireField(womb, "compPowerTrader");

    var hybrid = RequireType(asm, "GeneticRim.Building_Mechahybridizer");
    RequireMethod(hybrid, "Tick", "System.Void");
    RequireField(hybrid, "progress");
    RequireField(hybrid, "duration");
    RequireField(hybrid, "innerContainer");
}

// 19. Vanilla Nutrient Paste Expanded (VnpeGrinderStatistics)
using (var asm = OpenModAssembly("vnpe", "1.6/Assemblies/VNPE.dll", "VNPE", "VanillaExpanded.VNutrientE"))
{
    var grinder = RequireType(asm, "VNPE.Building_NutrientGrinder");
    var produce = RequireMethod(grinder, "TryProducePaste", "System.Boolean");
    Ensure(CallsMethod(produce, "DistributeAmongStorage", "PipeSystem.PipeNet"), "VNPE", "TryProducePaste must call PipeNet.DistributeAmongStorage");
    RequireField(grinder, "resourceComp", "PipeSystem.CompResource");
}

// 20. VQE Ancients (VqeAncientsStatistics & VqeAncientsGeneSuccessStatistics)
using (var asm = OpenModAssembly("vqeancients", "1.6/Assemblies/VanillaQuestsExpandedAncients.dll", "VanillaQuestsExpandedAncients", "vanillaquestsexpanded.ancients"))
{
    var injector = RequireType(asm, "VanillaQuestsExpandedAncients.Building_ArchogenInjector");
    var outcome = RequireType(asm, "VanillaQuestsExpandedAncients.ArchiteInjectionOutcomeDef");
    RequireMethod(injector, "FinishProcess", "System.Void");
    RequireMethod(injector, "HandleSuccessOutcome", "System.Void", "Verse.Pawn");
    RequireMethod(injector, "PostGeneSelectionSuccess", "System.Void", "Verse.Pawn");
    RequireProperty(injector, "Occupant", "Verse.Pawn");
    Ensure(InheritsFrom(asm, injector, "RimWorld.Building_Enterable"),
        "VQEAncients", "Building_ArchogenInjector must inherit RimWorld.Building_Enterable");
    RequireField(injector, "outcome", "VanillaQuestsExpandedAncients.ArchiteInjectionOutcomeDef");
    RequireField(outcome, "pawnKind");
}

// 21. VQE Cryptoforge (VqeCryptoforgeStatistics & VqeCryptogeneratorStatistics)
using (var asm = OpenModAssembly("vqecryptoforge", "1.6/Assemblies/VanillaQuestsExpandedCryptoforge.dll", "VanillaQuestsExpandedCryptoforge", "vanillaquestsexpanded.cryptoforge"))
{
    var scannable = RequireType(asm, "VanillaQuestsExpandedCryptoforge.Scannable");
    RequireMethod(scannable, "Tick", "System.Void");
    RequireMethod(scannable, "Notify_EndScan", "System.Void");
    RequireField(scannable, "tickCounter", "System.Int32");
    RequireField(scannable, "scanning", "System.Boolean");
    RequireField(scannable, "totalTicks", "System.Int32");

    var restartable = RequireType(asm, "VanillaQuestsExpandedCryptoforge.Restartable");
    RequireMethod(restartable, "Notify_Restarted", "System.Void");
}

// 22. VQE Drone Factory (VqeDroneFactoryStatistics)
using (var asm = OpenModAssembly("vqedronefactory", "1.6/Assemblies/VanillaQuestsExpandedDroneFactory.dll", "VanillaQuestsExpandedDroneFactory", "vanillaquestsexpanded.dronefactory"))
{
    var assembler = RequireType(asm, "VanillaQuestsExpandedDroneFactory.Building_AutomatedDroneAssembler");
    RequireMethod(assembler, "TickInterval", "System.Void", "System.Int32");
    RequireField(assembler, "tickCounter", "System.Int32");
    RequireField(assembler, "currentTimer", "System.Int32");
    RequireField(assembler, "currentPawn");

    var hatcher = RequireType(asm, "VanillaQuestsExpandedDroneFactory.CompDroneHatcher");
    var comp = RequireType(asm, "VanillaQuestsExpandedDroneFactory.CompDrone");
    RequireMethod(hatcher, "Hatch", "System.Void");
    RequireField(comp, "isPlayerDrone", "System.Boolean");
}

// 23. VQE The Generator (VqeGeneratorStatistics)
using (var asm = OpenModAssembly("vqegenerator", "1.6/Assemblies/VanillaQuestsExpandedTheGenerator.dll", "VanillaQuestsExpandedTheGenerator", "vanillaquestsexpanded.generator"))
{
    var genetron = RequireType(asm, "VanillaQuestsExpandedTheGenerator.Building_Genetron");
    RequireMethod(genetron, "SpawnSetup", "System.Void", "Verse.Map", "System.Boolean");
}

// 24. VRE Android (VreAndroidAwakeningStatistics & VreAndroidManufacturingStatistics)
using (var asm = OpenModAssembly("vreandroid", "1.6/Assemblies/VREAndroids.dll", "VREAndroids", "vanillaracesexpanded.android"))
{
    var gene = RequireType(asm, "VREAndroids.Gene_SyntheticBody");
    var utils = RequireType(asm, "VREAndroids.Utils");
    Ensure(gene.Methods.Any(m => m.Name == "Awaken"), "VREAndroids", "Gene_SyntheticBody.Awaken missing");
    RequireMethod(gene, "TickInterval", "System.Void", "System.Int32");
    RequireMethod(utils, "IsAwakened", "System.Boolean", "Verse.Pawn");

    var station = RequireType(asm, "VREAndroids.Building_AndroidCreationStation");
    RequireMethod(station, "FinishAndroidProject", "System.Void");
    RequireField(station, "curAndroidProject");
}

// 25. Vanilla Skills Expanded (VseExpertiseStatistics)
using (var asm = OpenModAssembly("vse", "1.7/Assemblies/VSE.dll", "VSE", "vanillaexpanded.skills"))
{
    var tracker = RequireType(asm, "VSE.ExpertiseTracker");
    var record = RequireType(asm, "VSE.ExpertiseRecord");
    RequireType(asm, "VSE.Expertise.ExpertiseDef");
    var panel = RequireType(asm, "VSE.ExpertiseUIUtility");
    RequireMethod(tracker, "AddExpertise", null, "VSE.Expertise.ExpertiseDef");
    RequireMethod(record, "Learn", "System.Void", "System.Single");
    RequireProperty(tracker, "AllExpertise");
    RequireProperty(tracker, "Pawn", "Verse.Pawn");
    RequireField(record, "def", "VSE.Expertise.ExpertiseDef");
    RequireProperty(record, "Level", "System.Int32");
    RequireProperty(record, "Pawn", "Verse.Pawn");
    RequireMethod(panel, "DoExpertisePanel", null, "UnityEngine.Rect", "Verse.Pawn");
}

// 26. Vanilla Social Interactions Expanded (VsieStatistics & VsieTeachingStatistics)
using (var asm = OpenModAssembly("vsie", "1.7/Assemblies/VanillaSocialInteractionsExpanded.dll", "VanillaSocialInteractionsExpanded", "VanillaExpanded.VanillaSocialInteractionsExpanded"))
{
    var mgr = RequireType(asm, "VanillaSocialInteractionsExpanded.SocialInteractionsManager");
    var asp = RequireType(asm, "VanillaSocialInteractionsExpanded.Aspiration");
    RequireMethod(mgr, "Notify_AspirationProgress", "System.Void", "Verse.Pawn");
    RequireField(mgr, "activeAspirations");
    RequireField(asp, "count", "System.Int32");
    RequireField(asp, "finalCount", "System.Int32");
    RequireField(asp, "inspirationDef");

    var teaching = RequireType(asm, "VanillaSocialInteractionsExpanded.InteractionWorker_Teaching");
    Ensure(teaching.Methods.Any(m => m.Name == "Interacted"), "VSIE", "InteractionWorker_Teaching.Interacted missing");
}

// 27. Altered Carbon 2: ReSleeved (ResleevingOutcomes & AlteredCarbonStage2Outcomes)
using (var asm = OpenModAssembly("ac2", "1.6/Assemblies/AlteredCarbon.dll", "AlteredCarbon", "hlx.UltratechAlteredCarbon"))
{
    var incubator = RequireType(asm, "AlteredCarbon.Building_Incubator");
    var gestator = RequireType(asm, "AlteredCarbon.Building_SleeveGestator");
    var stateEnum = RequireType(asm, "AlteredCarbon.IncubatorState");
    RequireMethod(gestator, "FinishGrowth", "System.Void");
    RequireProperty(gestator, "InnerPawn", "Verse.Pawn");
    RequireField(incubator, "incubatorState", stateEnum.FullName);

    var remoteStack = RequireType(asm, "AlteredCarbon.Hediff_RemoteStack");
    var needlecastable = RequireType(asm, "AlteredCarbon.INeedlecastable");
    RequireMethod(remoteStack, "Needlecast", "System.Void", needlecastable.FullName);
    RequireProperty(remoteStack, "Needlecasted", "System.Boolean");
}

// 28. Alpha Genes (PopularOutcomes & AlphaGenesStage2Outcomes)
using (var asm = OpenModAssembly("alphagenes", "1.7/Assemblies/AlphaGenes.dll", "AlphaGenes", "sarg.alphagenes"))
{
    var syphon = RequireType(asm, "AlphaGenes.CompGeneSyphon");
    RequireMethod(syphon, "Apply", "System.Void", "Verse.LocalTargetInfo", "Verse.LocalTargetInfo");

    var teratogenic = RequireType(asm, "AlphaGenes.CompTeratogenicHediff");
    var teratogenicProps = RequireType(asm, "AlphaGenes.CompProperties_TeratogenicHediff");
    RequireMethod(teratogenic, "Apply", "System.Void", "Verse.LocalTargetInfo", "Verse.LocalTargetInfo");
    RequireProperty(teratogenic, "Props", teratogenicProps.FullName);
    RequireField(teratogenicProps, "targetOther", "System.Boolean");
    RequireField(teratogenicProps, "hediff", "Verse.HediffDef");
}

// 29. Alpha Mechs (AlphaMechsStatistics & AlphaMechsStage2Outcomes)
using (var asm = OpenModAssembly("alphamechs", "1.6/Assemblies/AlphaMechs.dll", "AlphaMechs", "sarg.alphamechs"))
{
    var conv = RequireType(asm, "AlphaMechs.CompMechConversion");
    RequireMethod(conv, "Apply", "System.Void", "Verse.LocalTargetInfo", "Verse.LocalTargetInfo");
}

// 30. Empire Refactored (EmpireOutcomes, EmpireBattleOutcomes, EmpireTaxStage2Outcomes)
using (var asm = OpenModAssembly("empire", "1.7/Assemblies/Empire.dll", "Empire", "Matathias.Empire"))
{
    var delivery = RequireType(asm, "FactionColonies.util.DeliveryEvent");
    var fcEvent = RequireType(asm, "FactionColonies.FCEvent");
    var fcEventDef = RequireType(asm, "FactionColonies.FCEventDef");
    var action = RequireMethod(delivery, "Action", "System.Void", fcEvent.FullName, "System.Boolean");
    RequireMethod(delivery, "CreateDeliveryEvent", "System.Void", fcEvent.FullName);
    Ensure(CallsMethod(action, "SpawnOnTaxSpot", "FactionColonies.util.DeliveryLogistics"), "Empire", "DeliveryEvent.Action must delegate through DeliveryLogistics.SpawnOnTaxSpot");
    RequireField(fcEvent, "def", fcEventDef.FullName);
    RequireField(fcEvent, "goods");
    RequireField(fcEvent, "customDescription", "System.String");
}

// 31. Vanilla Gravship Expanded (GravshipOutcomes & GravshipStage2Outcomes)
using (var asm = OpenModAssembly("vgravship", "1.7/Assemblies/VanillaGravshipExpanded.dll", "VanillaGravshipExpanded", "vanillaexpanded.gravship"))
{
    var landingPatch = RequireType(asm, "VanillaGravshipExpanded.WorldComponent_GravshipController_LandingEnded_Patch");
    var gravUtil = RequireType(asm, "VanillaGravshipExpanded.GravdataUtility");
    var researchUtil = RequireType(asm, "VanillaGravshipExpanded.GravshipResearchUtility");
    var worldExpose = RequireType(asm, "VanillaGravshipExpanded.World_ExposeData_Patch");

    var applyYield = RequireMethod(landingPatch, "ApplyGravDataYield", "System.Void", "RimWorld.Planet.Gravship", "System.Int32&");
    RequireMethod(gravUtil, "CalculateGravdataYield", "System.Int32", "System.Single", "System.Single", "RimWorld.Building_GravEngine", "Verse.Pawn");
    Ensure(CallsMethod(applyYield, "CalculateGravdataYield", "VanillaGravshipExpanded.GravdataUtility"), "VGE", "ApplyGravDataYield must call GravdataUtility.CalculateGravdataYield");
    RequireMethod(researchUtil, "ResearchPerformed", "System.Void", "System.Single", "Verse.Pawn");
    RequireField(worldExpose, "currentGravtechProject", "Verse.ResearchProjectDef");
}

// 32. World Domination 2.0 (WorldCampaignOutcomes & WorldDominationStage2Outcomes)
using (var asm = OpenModAssembly("worlddom", "1.7/Assemblies/TSA_WorldDomination.dll", "TSA_WorldDomination", "TSA.WorldDominationExperimental"))
{
    var raidSim = RequireType(asm, "TSA_WorldDomination.Raid_Simulated");
    var intercept = RequireType(asm, "TSA_WorldDomination.Patch_InterceptDefeat");
    var goodwill = RequireType(asm, "TSA_WorldDomination.NpcRaidKillGoodwillUtility");
    var clash = RequireType(asm, "TSA_WorldDomination.WD_MapComponent_CaravanClash");

    Ensure(raidSim.Methods.Any(m => m.Name == "ResolveVictoryWithPlayerConquestChoices" && m.Parameters.Count == 15),
        "WorldDomination", "Raid_Simulated.ResolveVictoryWithPlayerConquestChoices 15-arg signature missing");
    Ensure(raidSim.Methods.Any(m => m.Name == "ApplyLossesAndLog" && m.Parameters.Count == 23 && m.Parameters[12].ParameterType.FullName == "System.Boolean"),
        "WorldDomination", "Raid_Simulated.ApplyLossesAndLog 23-arg signature missing");
    RequireMethod(intercept, "Prefix", "System.Boolean", "RimWorld.Planet.Settlement");
    Ensure(goodwill.Methods.Any(m => m.Name == "TryRewardFromSnapshot" && m.Parameters.Count == 8),
        "WorldDomination", "NpcRaidKillGoodwillUtility.TryRewardFromSnapshot 8-arg signature missing");
    RequireMethod(clash, "NotifyAirborneSurvivorsLeftThisClash", "System.Void");
    RequireMethod(clash, "MapRemoved", "System.Void");
    RequireProperty(clash, "PlayerHasWon", "System.Boolean");
    RequireProperty(clash, "EncounterActive", "System.Boolean");
    RequireField(clash, "enemyFaction", "RimWorld.Faction");
}

// 33. Prison Labor (PrisonLaborOutcomes)
using (var asm = OpenModAssembly("prison-labor", "1.7/Assemblies/PrisonLabor.dll", "PrisonLabor", "avius.prisonlabor"))
{
    var resoc = RequireType(asm, "PrisonLabor.Core.MainButton_Window.ColumnWorker_Resocialization");
    var needTreatment = RequireType(asm, "PrisonLabor.Core.Needs.Need_Treatment");
    var interrogation = RequireType(asm, "PrisonLabor.Core.Interrogation.Ritual.RitualOutcomeEffectWorker_Interrogation");
    var prisonerComp = RequireType(asm, "PrisonLabor.Core.Components.PrisonerComp");

    var convert = RequireMethod(resoc, "ConvertPrisoner", "System.Void", "Verse.Pawn");
    Ensure(CallsMethod(convert, "Recruit", "RimWorld.RecruitUtility"), "PrisonLabor", "ConvertPrisoner must recruit via RecruitUtility.Recruit");
    RequireProperty(needTreatment, "ResocializationReady", "System.Boolean");

    var apply = RequireMethod(interrogation, "Apply", "System.Void",
        "System.Single", "System.Collections.Generic.Dictionary`2<Verse.Pawn,System.Int32>", "RimWorld.LordJob_Ritual");
    Ensure(CallsMethod(apply, "set_HasIntel", "PrisonLabor.Core.Components.PrisonerComp"), "PrisonLabor", "Interrogation Apply must update PrisonerComp.HasIntel");
    RequireProperty(prisonerComp, "HasIntel", "System.Boolean");
}

// 34. MedPod (MedPodOutcomes)
using (var asm = OpenModAssembly("medpod", "1.7/Assemblies/MedPod.dll", "MedPod", "sumghai.Medpod"))
{
    var pod = RequireType(asm, "MedPod.Building_BedMedPod");
    var statusEnum = pod.NestedTypes.FirstOrDefault(t => t.Name == "MedPodStatus");
    Ensure(statusEnum is { IsEnum: true }, "MedPod", "Nested enum MedPodStatus missing");
    checkedAssertions++;
    var healingFinished = statusEnum!.Fields.FirstOrDefault(f => f.Name == "HealingFinished" && f.HasConstant);
    Ensure(healingFinished is not null && Convert.ToInt32(healingFinished.Constant) == 4, "MedPod", "MedPodStatus.HealingFinished must equal 4");
    checkedAssertions++;
    RequireField(pod, "status", "MedPod.Building_BedMedPod/MedPodStatus");
    RequireMethod(pod, "DischargePatient", "System.Void", "Verse.Pawn", "System.Boolean");
}

// 35. Alpha Implants (AlphaImplantsOutcomes)
using (var asm = OpenModAssembly("alpha-implants", "1.6/Assemblies/AlphaImplants.dll", "AlphaImplants", "sarg.alphaimplants"))
{
    var cureScars = RequireType(asm, "AlphaImplants.Hediff_CureScars");
    var postMake = RequireMethod(cureScars, "PostMake", "System.Void");
    Ensure(CallsMethod(postMake, "Cure", "Verse.HealthUtility"), "AlphaImplants", "Hediff_CureScars.PostMake must cure injuries via HealthUtility.Cure");
}

// 36. Biomes! Islands (BiomesIslandsOutcomes)
using (var asm = OpenModAssembly("biomes-islands", "1.7/Assemblies/BiomesIslands.dll", "BiomesIslands", "BiomesTeam.BiomesIslands"))
{
    Ensure(asm.MainModule.Types.Count > 1, "BiomesIslands", "BiomesIslands 1.7 assembly must contain compiled types");
    checkedAssertions++;
}

// 37. RimQuest (Continued) (RimQuestOutcomes)
using (var asm = OpenModAssembly("rimquest", "1.7/Assemblies/RimQuest.dll", "RimQuest", "Mlie.RimQuest"))
{
    var dialog = RequireType(asm, "RimQuest.Dialog_QuestGiver");
    var tracker = RequireType(asm, "RimQuest.RimQuestTracker");
    RequireType(asm, "RimQuest.QuestPawn");

    var execute = RequireMethod(dialog, "ExecuteSelectedQuest", "System.Void");
    Ensure(CallsMethod(execute, "RemoveQuestPawnFromTracker", "RimQuest.Dialog_QuestGiver"), "RimQuest", "ExecuteSelectedQuest must remove questPawn from tracker");
    Ensure(CallsMethod(execute, "receiveSilver", "RimQuest.Dialog_QuestGiver"), "RimQuest", "ExecuteSelectedQuest must call receiveSilver");
    RequireField(dialog, "questPawn", "RimQuest.QuestPawn");
    RequireField(dialog, "interactor", "Verse.Pawn");
    RequireField(dialog, "selectedQuest", "System.Object");
    RequireField(dialog, "actualSilverCost", "System.Int32");
    RequireField(tracker, "Instance", "RimQuest.RimQuestTracker");
    RequireField(tracker, "questPawns", "System.Collections.Generic.List`1<RimQuest.QuestPawn>");
}

// 38. Horrors (Continued) (HorrorsOutcomes)
using (var asm = OpenModAssembly("horrors", "1.7/Assemblies/Horrors.dll", "Horrors", "Mlie.Horrors"))
{
    var hive = RequireType(asm, "Horrors.HorrorHive");
    var den = RequireType(asm, "Horrors.HorrorDen");
    RequireMethod(hive, "DeSpawn", "System.Void", "Verse.DestroyMode");
    RequireMethod(den, "DeSpawn", "System.Void", "Verse.DestroyMode");
}

// Optional Steam-only Workshop override check: Hospital (Adamas.Hospital)
if (overrides.ContainsKey("hospital"))
{
    using var asm = OpenModAssembly("hospital", "1.7/Assemblies/Hospital.dll", "Hospital");
    var comp = RequireType(asm, "Hospital.HospitalMapComponent");
    var data = RequireType(asm, "Hospital.PatientData");
    var leaves = RequireMethod(comp, "PatientLeaves", "System.Void", "Verse.Pawn");
    Ensure(CallsMethod(leaves, "TryPlaceThing", "Verse.GenPlace"), "Hospital", "PatientLeaves must place silver via GenPlace.TryPlaceThing");
    RequireField(comp, "Patients");
    RequireField(data, "Dismissed", "System.Boolean");
    RequireField(data, "HasPendingSurgeryBill", "System.Boolean");
}

// Optional RimWorld Assembly-CSharp bridge verification (supports both Game-Windows 1.7.5011 rev24 and NuGet 1.7.4985).
if (gameDll is not null)
{
    Ensure(File.Exists(gameDll), "Assembly-CSharp", $"Game DLL missing: {gameDll}");
    using var game = AssemblyDefinition.ReadAssembly(gameDll, new ReaderParameters { ReadSymbols = false });
    checkedAssemblies++;

    var genPlace = RequireType(game, "Verse.GenPlace");
    Ensure(genPlace.Methods.Any(m => m.Name == "TryPlaceThing" && m.Parameters.Count == 8 && m.ReturnType.FullName == "System.Boolean"),
        "Assembly-CSharp", "GenPlace.TryPlaceThing 8-parameter overload missing");

    var genSpawn = RequireType(game, "Verse.GenSpawn");
    RequireMethod(genSpawn, "Spawn", "Verse.Thing", "Verse.Thing", "Verse.IntVec3", "Verse.Map", "Verse.WipeMode");
    RequireMethod(genSpawn, "Spawn", "Verse.Thing",
        "Verse.Thing", "Verse.IntVec3", "Verse.Map", "Verse.Rot4", "Verse.WipeMode", "System.Boolean", "System.Boolean");

    var frame = RequireType(game, "RimWorld.Frame");
    RequireMethod(frame, "CompleteConstruction", "System.Void", "Verse.Pawn");

    var caravanInv = RequireType(game, "RimWorld.Planet.CaravanInventoryUtility");
    RequireMethod(caravanInv, "GiveThing", "System.Void", "RimWorld.Planet.Caravan", "Verse.Thing");

    var pawnUtil = RequireType(game, "RimWorld.PawnUtility");
    RequireMethod(pawnUtil, "TrySpawnHatchedOrBornPawn", "System.Boolean",
        "Verse.Pawn", "Verse.Thing", "System.Nullable`1<Verse.IntVec3>");

    var skill = RequireType(game, "RimWorld.SkillRecord");
    RequireMethod(skill, "Learn", "System.Void", "System.Single", "System.Boolean", "System.Boolean");
    RequireField(skill, "xpSinceLastLevel", "System.Single");

    var ritual = RequireType(game, "RimWorld.LordJob_Ritual");
    RequireMethod(ritual, "PawnWithRole", "Verse.Pawn", "System.String");

    var pawn = RequireType(game, "Verse.Pawn");
    RequireProperty(pawn, "IsPrisonerOfColony", "System.Boolean");
    var guest = RequireType(game, "RimWorld.Pawn_GuestTracker");
    RequireProperty(guest, "IsPrisoner", "System.Boolean");
    RequireProperty(guest, "HostFaction", "RimWorld.Faction");

    var enterable = RequireType(game, "RimWorld.Building_Enterable");
    RequireField(enterable, "innerContainer", "Verse.ThingOwner");

    var bossgroup = RequireType(game, "RimWorld.GameComponent_Bossgroup");
    RequireMethod(bossgroup, "Notify_PawnKilled", "System.Void", "Verse.Pawn");
    RequireProperty(bossgroup, "AllBosses");

    var ability = RequireType(game, "RimWorld.Ability");
    RequireMethod(ability, "Activate", "System.Boolean", "Verse.LocalTargetInfo", "Verse.LocalTargetInfo");
    RequireMethod(ability, "Activate", "System.Boolean", "RimWorld.Planet.GlobalTargetInfo");
    RequireField(ability, "lastCastTick", "System.Int32");
}

Console.WriteLine($"AdapterSignatureAudit: verified {checkedAssertions} contract assertions across {checkedAssemblies} assemblies: PASS");

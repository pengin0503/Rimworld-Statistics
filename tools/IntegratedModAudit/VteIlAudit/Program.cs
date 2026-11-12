// Inspect exact VTE 1.6 DLL loaded by the game's v1.7 loadFolders rule.
// Mono.Cecil never loads or executes target-game code.
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the VTE 1.6 DLL selected for RimWorld 1.7");

using var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadSymbols = false });
var module = assembly.MainModule;
TypeDefinition RequiredType(string name) =>
    module.Types.Single(t => t.FullName == "VanillaTradingExpanded." + name);
MethodDefinition RequiredMethod(string owner, string name, params string[] parameters)
{
    var method = RequiredType(owner).Methods.Single(m => m.Name == name &&
        m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
    if (method.IsStatic || !method.HasBody || method.ReturnType.FullName != "System.Void")
        throw new InvalidDataException("Unexpected VTE instance method: " + owner + "." + name);
    return method;
}
void RequiredFields(string owner, params string[] fieldNames)
{
    var type = RequiredType(owner);
    foreach (var name in fieldNames)
        if (!type.Fields.Any(f => f.Name == name && !f.IsStatic))
            throw new InvalidDataException("VTE field missing: " + type.FullName + "." + name);
}
bool Calls(MethodDefinition method, string name) =>
    method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == name);
bool Writes(MethodDefinition method, string name) =>
    method.Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld &&
        i.Operand is FieldReference f && f.Name == name);
void Assert(bool condition, string description)
{
    if (!condition) throw new InvalidDataException("VTE IL: " + description);
}

RequiredFields("Bank", "depositAmount", "parentFaction", "loans", "bankExtension");
var deposit = RequiredMethod("Bank", "DepositSilver",
    "System.Collections.Generic.List\`1<Verse.Thing>", "System.Int32");
var withdraw = RequiredMethod("Bank", "WithdrawSilver", "Verse.Pawn", "System.Int32");
var loan = RequiredMethod("Bank", "TakeLoan",
    "Verse.Pawn", "System.Int32", "System.Int32", "System.Int32",
    "VanillaTradingExpanded.LoanOption");
Assert(Writes(deposit, "depositAmount") && Calls(deposit, "Destroy"),
    "DepositSilver must commit silver balance after destroying physical stacks");
Assert(Writes(withdraw, "depositAmount") && Calls(withdraw, "SpawnDropPod"),
    "WithdrawSilver must debit bank and launch physical silver");
Assert(Calls(loan, "Add") && Calls(loan, "SpawnDropPod"),
    "TakeLoan must add loan to balance sheet and issue physical silver");
Assert(RequiredType("Bank").Properties.Any(p => p.Name == "DepositAmount" &&
    p.PropertyType.FullName == "System.Int32"), "Bank.DepositAmount int property");

RequiredFields("TradingManager", "npcSubmittedContracts", "npcContractsToBeCompleted",
    "playerSubmittedContracts");
var complete = RequiredMethod("TradingManager", "CompleteContract", "VanillaTradingExpanded.Contract");
Assert(Calls(complete, "FoundItemsInMap") && Calls(complete, "Destroy"),
    "Immediate NPC buy order must transfer items before recording");
Assert(Calls(complete, "Add") && Calls(complete, "Remove"),
    "Delayed NPC buy order must schedule a caravan, not claim fulfillment");
RequiredFields("LordJob_GrabItemsAndLeave", "contract", "caravanSent");
var exit = RequiredMethod("LordJob_GrabItemsAndLeave", "ExitCaravan");
Assert(Writes(exit, "caravanSent") && Calls(exit, "PassToWorld") &&
    Calls(exit, "SpawnDropPod") == false,
    "Caravan exit must consume collected items before creating reward dialog");

RequiredFields("Contract", "item", "stuff", "amount", "reward", "expiresInTicks", "mapToTakeItems");
var found = RequiredType("Contract").Methods.Single(m =>
    m.Name == "FoundItemsInMap" && m.Parameters.Count == 1 &&
    m.Parameters[0].ParameterType.FullName == "Verse.Map" && !m.IsStatic && m.HasBody);
Assert(found.ReturnType.FullName.StartsWith("System.Collections.Generic.List"),
    "FoundItemsInMap must return a list of Thing stacks");

RequiredFields("TransactionProcess", "amountToSpend", "transactionCost", "postTransactionAction");
var perform = RequiredMethod("TransactionProcess", "PerformTransaction");
Assert(Calls(perform, "set_DepositAmount") && Calls(perform, "Invoke"),
    "Bank funding and success callback are prerequisites for completed player purchase");
var closure = RequiredType("TradingManager").NestedTypes
    .Where(t => t.Fields.Any(f => f.FieldType.FullName == "VanillaTradingExpanded.Contract"))
    .SelectMany(t => t.Methods)
    .Any(m => m.Name.Contains("ProcessContracts", StringComparison.Ordinal));
Assert(closure, "Player-posted completion callback closure must capture its contract");

Console.WriteLine("VTE 1.7-selected binary target signatures, mutations, settlement callbacks: PASS");

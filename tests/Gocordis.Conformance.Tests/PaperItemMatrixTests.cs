using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Falsifiable traceability for every paper item C01-C40 in
/// docs/migration/semantic-mapping.md §2. A row is either backed by a real test
/// method (reflected from this assembly) or explicitly marked 待补 / 接受差异
/// with a documented reason. An empty assertion cannot make this matrix green.
/// </summary>
public sealed class PaperItemMatrixTests
{
    private const string MatrixRelativePath = "docs/migration/paper-coverage-matrix.md";

    private static readonly string[] ExpectedIds =
        Enumerable.Range(1, 40).Select(index => $"C{index:00}").ToArray();

    public static IEnumerable<object[]> PaperItems()
    {
        // id | paper anchor (semantic-mapping.md §2) | status | test method OR documented reason
        yield return Item("C01", "§1.1 p.4", "已覆盖", "C01_TemporalRecoveryAndSpatialResolutionAreOneContract");
        yield return Item("C02", "§3.1 p.9", "已覆盖", "C02_EffectInverseIsLeftInverseNotRightInverse");
        yield return Item("C03", "Def 2 p.10；Thm 7 p.11", "已覆盖", "Thm68_Matrix");
        yield return Item("C04", "Def 1/3/6 pp.9–11；Thm 5", "已覆盖", "C04_EffectCompositionHasIdentityAndReverseOrderInverse");
        yield return Item("C05", "Def 8/9 pp.12–13；Thm 10–11", "已覆盖", "C05_WitnessEffectMovesDeltaToGammaAndBack");
        yield return Item("C06", "Thm 16 p.15；Def 17/18 pp.15–16", "已覆盖", "C06_StrictLifoRecoveryLeavesNoResidue");
        yield return Item("C07", "Def 19/20 p.17", "已覆盖", "C07_CoeffectGetSetPreconditionsAndReversibleProvision");
        yield return Item("C08", "Def 21/22 p.18", "已覆盖", "C08_DeclarationGuardHasThreeObservableNotificationPhases");
        yield return Item("C09", "Def 23 p.19", "已覆盖", "C09_InSituProvisionAndDerivedIsolationBothRestoreOnUnload");
        yield return Item("C10", "Def 24/25 pp.19–20；§4.4 p.56", "已覆盖", "IsolateCannotHijackAKeyOwnedByAnotherRealm");
        yield return Item("C11", "Def 26/27 pp.20–21；§6.3 p.72", "已覆盖", "C11_MetaInterceptionIsRightBiasedAndDoesNotReload");
        yield return Item("C12", "Def 28–30 pp.21–22", "已覆盖", "C12_OneContextMediatesCapabilitiesEffectsMetaDataEventsAndChildren");
        yield return Item("C13", "Def 31–37 pp.23–25", "已覆盖", "C13_ObservationEquivalenceIgnoresGenerativeFiberIds");
        yield return Item("C14", "Def 40/42 pp.26–27；Thm 43/45/47", "已覆盖", "C14_IndependentInversesCommuteAcrossPermittedOrders");
        yield return Item("C15", "Def 48 p.31", "已覆盖", "C15_DeclaredComponentSurfaceGatesAndMatchesTheRuntimeView");
        yield return Item("C16", "Def 49 p.32", "已覆盖", "C16_FiberStatesAndCommittedProviderViewAreObservable");
        yield return Item("C17", "Def 50/51 p.32–33；Eq.46", "已覆盖", "C17_UniqueProviderAndBrokerBasedMultiImplementation");
        yield return Item("C18", "Def 52 p.35", "已覆盖", "C18_InstantiationRetiresChildrenBeforeTheirOwner");
        yield return Item("C19", "§4.2.1 p.34", "已覆盖", "PC01_DeclarationReconcileIsIdempotent");
        yield return Item("C20", "Def 53 p.35；Eq.48/49", "已覆盖", "C20_TargetBindingAndQuiescenceAreObservable");
        yield return Item("C21", "§4.2.2 pp.36–37；Table 1 p.40", "已覆盖", "C21_LifecycleRulesAreObservableForIterativeActivation");
        yield return Item("C22", "Def 54 p.36；§4.4 Config p.57", "已覆盖", "C22_DependencyLossIsWithdrawalNotFailure");
        yield return Item("C23", "Def 55/56 pp.38–39；Lemma 57", "已覆盖", "C23_EffectsMayOnlyWriteOwnStateAndDeclaredProviderSlots");
        yield return Item("C24", "Def 63 p.43", "已覆盖", "Thm64_Matrix");
        yield return Item("C25", "Thm 64 p.43", "已覆盖", "Thm64_Matrix");
        yield return Item("C26", "Thm 68 p.46；Cor 69", "已覆盖", "Thm68_Matrix");
        yield return Item("C27", "Thm 70 pp.47–48", "已覆盖", "Thm70_Matrix");
        yield return Item("C28", "Thm 71 p.48", "已覆盖", "C28_IterativeStepsShareOneResolutionAndRecoverOnDivert");
        yield return Item("C29", "Def 72 p.49；Thm 73 pp.49–50", "已覆盖", "Thm73_Matrix");
        yield return Item("C30", "Def 74–76 pp.51–52；Thm 80 pp.54–55", "已覆盖", "Thm80_Matrix");
        yield return Item("C31", "Lemmas 59–62 pp.40–42", "已覆盖", "C31_WritePartitionIsInvariantUnderRenamingAndVestigialRecords");
        yield return Item("C32", "§4.4 Asynchrony pp.55–56", "已覆盖", "C32_InertialIteratorLandsCancellableStepBeforeItsInverse");
        yield return Item("C33", "§4.4 Failure p.56", "已覆盖", "C33_FailureIsIsolatedOnTheFiberAndBlocksReentry");
        yield return Item("C34", "§4.4 Isolation/Configuration pp.56–57", "已覆盖", "C34_RealmReassignmentIsARetireReinsertComposite");
        yield return Item("C35", "§6.1 pp.70–71", "已覆盖", "C35_ReversibleBoundaryCoversOwnedEffectsButNotExternalEmission");
        yield return Item("C36", "§6.5 p.74", "已覆盖", "C36_DependencyCyclesAreRejectedAtMountWithAnExplicitChain");
        yield return Item("C37", "§6.3 p.72", "已覆盖", "C37_DeclaredCapabilitiesAreArbitratedAndMetaCanRefinePolicy");
        yield return Item("C38", "§6.6 p.75", "接受差异",
            "C# 用 VersionedKey<T> + NuGet 版本范围，不实现 K×P 名义命名空间/peer 结构兼容（semantic-mapping.md:C38）。");
        yield return Item("C39", "§6.4/§6.7 pp.73–77", "已覆盖", "C39_BackendNeutralFactoriesComposeThroughTheCoreContract");
        yield return Item("C40", "§5.2.1 pp.65–69", "已覆盖", "PC12_PluginRegistryNeverCreatesASecondLifecycle");
    }

    private static object[] Item(string id, string anchor, string status, string evidence) =>
        [id, anchor, status, evidence];

    [Theory]
    [MemberData(nameof(PaperItems))]
    public void PaperItemHasFalsifiableMapping(string id, string paperAnchor, string status, string evidence)
    {
        Assert.Contains(id, ExpectedIds);
        var row = MatrixRow(id);

        // The document row must agree with the executable data, not merely exist.
        Assert.Contains(paperAnchor, row);
        Assert.Contains($"| {status} |", row);
        Assert.Contains(evidence, row);

        if (status == "已覆盖")
        {
            // A claimed test must really exist; renaming/deleting it fails this test.
            Assert.Contains("tests/Gocordis.Conformance.Tests/", row);
            Assert.True(TestMethodExists(evidence), $"{id} claims missing test method '{evidence}'");
        }
        else
        {
            Assert.True(status is "待补" or "接受差异", $"{id} has unknown status '{status}'");
            Assert.Contains($"semantic-mapping.md:{id}", evidence);
            Assert.Contains("—（无 C# 测试）", row);
        }
    }

    [Fact]
    public void MatrixEnumeratesAllFortyPaperItemsExactlyOnce()
    {
        var ids = PaperItems().Select(row => (string)row[0]).ToArray();
        Assert.Equal(ExpectedIds, ids);
    }

    [Fact]
    public void MatrixDeclaresHonestStatusCounts()
    {
        var rows = PaperItems().ToArray();
        var covered = rows.Count(row => (string)row[2] == "已覆盖");
        var pending = rows.Count(row => (string)row[2] == "待补");
        var accepted = rows.Count(row => (string)row[2] == "接受差异");

        Assert.Equal(40, covered + pending + accepted);
        Assert.Contains($"已覆盖 {covered} / 待补 {pending} / 接受差异 {accepted}", ReadMatrix());
    }

    private static bool TestMethodExists(string name) =>
        typeof(PaperItemMatrixTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Any(method => method.Name == name);

    private static string MatrixRow(string id)
    {
        var line = ReadMatrix()
            .Split('\n')
            .FirstOrDefault(row => row.TrimStart().StartsWith($"| {id} |", StringComparison.Ordinal));
        return line ?? throw new XunitException($"paper-coverage-matrix.md has no row for {id}");
    }

    private static string ReadMatrix()
    {
        var relative = Path.Combine(MatrixRelativePath.Split('/'));
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new XunitException($"cannot locate {MatrixRelativePath} from {AppContext.BaseDirectory}");
    }
}

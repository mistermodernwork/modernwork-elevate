using AppForge.Core.Approval;
using AppForge.Core.Domain;

namespace AppForge.Tests.Core;

public class ApprovalClassifierTests
{
    private static ApprovalContext KnownGoodUpdate(DeploymentRing ring) => new()
    {
        Operation = ApprovalOperation.BuildAndDistribute,
        TargetRing = ring,
        ProfilePreviouslyApproved = true,
        ProfileChangedSinceLastApproval = false,
        VendorSignatureValid = true,
        VendorSignerChanged = false,
        AllTestsPassed = true,
    };

    [Fact]
    public void New_vendor_version_of_approved_profile_into_test_ring_is_level0()
    {
        var c = ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test));
        Assert.Equal(0, c.Level);
        Assert.False(c.RequiresApproval);
    }

    [Theory]
    [InlineData(DeploymentRing.Pilot)]
    [InlineData(DeploymentRing.Production)]
    public void Distribution_to_pilot_or_production_is_level1(DeploymentRing ring)
    {
        var c = ApprovalClassifier.Classify(KnownGoodUpdate(ring));
        Assert.Equal(1, c.Level);
        Assert.Contains(c.Reasons, r => r.Contains(ring.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_tests_block_the_request()
    {
        var c = ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { AllTestsPassed = false });
        Assert.True(c.Blocked);
        Assert.False(c.RequiresApproval);
    }

    [Fact]
    public void Changed_vendor_signer_is_level2_even_for_test_ring()
    {
        var c = ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { VendorSignerChanged = true });
        Assert.Equal(2, c.Level);
    }

    [Fact]
    public void Invalid_vendor_signature_is_level2()
    {
        Assert.Equal(2, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { VendorSignatureValid = false }).Level);
    }

    [Fact]
    public void Hash_rules_custom_code_and_unapproved_blocks_are_level2()
    {
        Assert.Equal(2, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Production) with { RequiresHashRules = true }).Level);
        Assert.Equal(2, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { ContainsCustomCode = true }).Level);
        Assert.Equal(2, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { UsesUnapprovedBuildingBlock = true }).Level);
    }

    [Fact]
    public void New_product_is_level2()
    {
        Assert.Equal(2, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { IsNewProduct = true }).Level);
        Assert.Equal(2, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.NewProduct }).Level);
    }

    [Fact]
    public void Profile_changed_since_approval_needs_level1_even_in_test_ring()
    {
        var c = ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { ProfileChangedSinceLastApproval = true });
        Assert.Equal(1, c.Level);
    }

    [Fact]
    public void Never_approved_profile_is_not_automatic()
    {
        var c = ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { ProfilePreviouslyApproved = false });
        Assert.Equal(1, c.Level);
    }

    [Fact]
    public void Missing_tests_or_signature_check_is_not_automatic()
    {
        Assert.Equal(1, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { AllTestsPassed = null }).Level);
        Assert.Equal(1, ApprovalClassifier.Classify(KnownGoodUpdate(DeploymentRing.Test) with { VendorSignatureValid = null }).Level);
    }

    [Theory]
    [InlineData(ApprovalOperation.SignRemediation)]
    [InlineData(ApprovalOperation.SignPlatformScript)]
    [InlineData(ApprovalOperation.SignComplianceScript)]
    [InlineData(ApprovalOperation.ResignForeignFiles)]
    [InlineData(ApprovalOperation.BuildingBlockChange)]
    public void System_scripts_and_foreign_files_are_level2(ApprovalOperation op)
    {
        Assert.Equal(2, ApprovalClassifier.Classify(new ApprovalContext { Operation = op }).Level);
    }

    [Fact]
    public void Highest_rule_wins_remediation_with_test_certificate_stays_level2()
    {
        var c = ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.SignRemediation, UsesTestCertificate = true });
        Assert.Equal(2, c.Level);
    }

    [Theory]
    [InlineData(ApprovalOperation.BasePolicyChange)]
    [InlineData(ApprovalOperation.PolicyEnforce)]
    [InlineData(ApprovalOperation.CertificateChange)]
    [InlineData(ApprovalOperation.ApprovalRulesChange)]
    [InlineData(ApprovalOperation.RoleChange)]
    [InlineData(ApprovalOperation.UnblockProduct)]
    public void Policy_certificate_and_governance_changes_are_level3(ApprovalOperation op)
    {
        Assert.Equal(3, ApprovalClassifier.Classify(new ApprovalContext { Operation = op, UsesTestCertificate = true }).Level);
    }

    [Fact]
    public void Script_signing_levels()
    {
        Assert.Equal(0, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.SignScript, UsesTestCertificate = true }).Level);
        Assert.Equal(0, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.SignScript, FromProtectedPipeline = true }).Level);
        Assert.Equal(1, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.SignScript }).Level);
        Assert.Equal(1, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.SignBinary }).Level);
    }

    [Fact]
    public void Profile_change_and_supplemental_audit_update_are_level1()
    {
        Assert.Equal(1, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.ProfileChange }).Level);
        Assert.Equal(1, ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.SupplementalPolicyUpdate }).Level);
    }

    [Fact]
    public void Level3_lists_all_reasons()
    {
        var c = ApprovalClassifier.Classify(new ApprovalContext { Operation = ApprovalOperation.CertificateChange, VendorSignerChanged = true });
        Assert.Equal(3, c.Level);
        Assert.Equal(2, c.Reasons.Count);
    }
}

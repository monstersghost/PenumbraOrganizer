namespace PenumbraOrganizer.Tests.Classification;

using FluentAssertions;
using PenumbraOrganizer.Core.Classification;

public sealed class ModPathClassifierTests
{
    [Theory]
    [InlineData("chara/equipment/e0755/model/c0101e0755_top.mdl", ModCategory.Gear)]
    [InlineData("chara/equipment/e0633/model/c0101e0633_met.mdl", ModCategory.Gear)]
    [InlineData("chara/equipment/e0387/model/c0101e0387_glv.mdl", ModCategory.Gear)]
    [InlineData("chara/equipment/e0633/model/c0101e0633_dwn.mdl", ModCategory.Gear)]
    [InlineData("chara/equipment/e0387/model/c0101e0387_sho.mdl", ModCategory.Gear)]
    [InlineData("chara/accessory/a0145/model/c0101a0145_ear.mdl", ModCategory.Gear)]
    [InlineData("chara/accessory/a0001/model/c0101a0001_ril.mdl", ModCategory.Gear)]
    [InlineData("chara/weapon/w0101/obj/body/b0117/model/w0101b0117.mdl", ModCategory.Weapon)]
    [InlineData("chara/human/c0101/obj/face/f0001/model/c0101f0001_fac.mdl", ModCategory.Face)]
    [InlineData("chara/human/c0101/obj/hair/h0001/model/c0101h0001_hir.mdl", ModCategory.Hair)]
    [InlineData("chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl", ModCategory.Body)]
    [InlineData("chara/human/c0101/obj/body/b0001/texture/c0101b0001_top_d.tex", ModCategory.Skin)]
    [InlineData("chara/human/c0701/obj/tail/t0001/model/c0701t0001_til.mdl", ModCategory.Body)]
    [InlineData("chara/human/c1701/obj/zear/z0001/model/c1701z0001_zer.mdl", ModCategory.Body)]
    [InlineData("chara/human/c1304/obj/body/b0001/model/c1304b0001_top.mdl", ModCategory.NPC)]
    [InlineData("chara/human/c0804/obj/hair/h0001/model/c0804h0001_hir.mdl", ModCategory.NPC)]
    [InlineData("chara/human/c0101/animation/a0001/bt_common/emote/pose01.pap", ModCategory.Animation)]
    [InlineData("chara/monster/m8355/obj/body/b0001/model/m8355b0001.mdl", ModCategory.Minion)]
    [InlineData("chara/monster/m0466/obj/body/b0001/texture/v01_m0466b0001_d.tex", ModCategory.Mount)]
    [InlineData("chara/demihuman/d0001/obj/body/b0001/model/d0001b0001.mdl", ModCategory.Mount)]
    [InlineData("chara/monster/m7102/obj/body/b0001/model/m7102b0001.mdl", ModCategory.Pet)]
    [InlineData("chara/monster/m6002/obj/body/b0001/model/m6002b0001.mdl", ModCategory.Ornament)]
    [InlineData("chara/monster/m9999/obj/body/b0001/model/m9999b0001.mdl", ModCategory.Others)]
    [InlineData("bgcommon/hou/indoor/general/0424/texture/fun_b0_m0424_0a_d.tex", ModCategory.Furniture)]
    [InlineData("vfx/common/eff/wp_case01.avfx", ModCategory.VFX)]
    [InlineData("sound/zingle/zpc_lb.scd", ModCategory.Sound)]
    public void ClassifyThenResolve_SinglePath_ResolvesExpectedCategory(string path, ModCategory expected)
    {
        var targets = ModPathClassifier.Classify(new[] { path });
        var (category, _) = ModPathClassifier.Resolve(targets);

        category.Should().Be(expected);
    }

    [Fact]
    public void ClassifyManipulationSlot_Head_ResolvesToGearWithNoGameTarget()
    {
        var targets = ModPathClassifier.Classify(new[] { "slot:Head" });

        targets.Should().ContainSingle();
        targets[0].Category.Should().Be(ModCategory.Gear);
        targets[0].TargetKind.Should().Be(CanonicalTargetKind.MetaManipulation);
        targets[0].GameTarget.Should().BeNull();
    }

    [Fact]
    public void ClassifyManipulationSlot_MainHand_ResolvesToWeapon()
    {
        var targets = ModPathClassifier.Classify(new[] { "slot:MainHand" });

        targets[0].Category.Should().Be(ModCategory.Weapon);
    }

    [Fact]
    public void ClassifyManipulationSlot_UnknownSlot_ResolvesToOthers()
    {
        var targets = ModPathClassifier.Classify(new[] { "slot:Bracer" });

        targets[0].Category.Should().Be(ModCategory.Others);
    }

    [Fact]
    public void Resolve_MixedGearAndAccessory_StaysGear()
    {
        var paths = new[]
        {
            "chara/equipment/e0387/model/c0101e0387_sho.mdl",
            "chara/accessory/a0001/model/c0101a0001_ril.mdl",
            "chara/accessory/a0002/model/c0101a0002_nek.mdl",
        };

        var (category, _) = ModPathClassifier.Resolve(ModPathClassifier.Classify(paths));

        category.Should().Be(ModCategory.Gear);
    }

    [Fact]
    public void Resolve_MixedGearAndVfx_RollsUpToGearButKeepsBothTargets()
    {
        var paths = new[]
        {
            "chara/equipment/e0387/model/c0101e0387_sho.mdl",
            "vfx/common/eff/wp_case01.avfx",
        };

        var targets = ModPathClassifier.Classify(paths);
        var (category, _) = ModPathClassifier.Resolve(targets);

        category.Should().Be(ModCategory.Gear);
        targets.Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_WeaponAloneDoesNotTriggerGear()
    {
        var targets = ModPathClassifier.Classify(new[] { "chara/weapon/w0201/obj/body/b0155/model/w0201b0155.mdl" });
        var (category, _) = ModPathClassifier.Resolve(targets);

        category.Should().Be(ModCategory.Weapon);
    }

    [Fact]
    public void Resolve_EmptyTargets_FallsBackToOthers()
    {
        var (category, subcategory) = ModPathClassifier.Resolve(Array.Empty<ModTargetClassification>());

        category.Should().Be(ModCategory.Others);
        subcategory.Should().BeNull();
    }

    [Fact]
    public void ClassifyPath_UnmappedMonsterId_HasExplanatoryNote()
    {
        var targets = ModPathClassifier.Classify(new[] { "chara/monster/m9999/obj/body/b0001/model/m9999b0001.mdl" });

        targets.Should().ContainSingle();
        targets[0].Category.Should().Be(ModCategory.Others);
        targets[0].Notes.Should().Contain("m9999");
    }

    [Fact]
    public void Resolve_GearTarget_CarriesRawSlotSuffixAsSubcategory()
    {
        var targets = ModPathClassifier.Classify(new[] { "chara/equipment/e0387/model/c0101e0387_sho.mdl" });
        var (_, subcategory) = ModPathClassifier.Resolve(targets);

        subcategory.Should().Be("sho");
    }
}

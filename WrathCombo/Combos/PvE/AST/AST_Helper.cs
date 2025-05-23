﻿using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using System.Collections.Generic;
using System.Linq;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;
using WrathCombo.Extensions;
using ECommons.GameHelpers;
using WrathCombo.Services;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
namespace WrathCombo.Combos.PvE;

internal partial class AST
{
    internal static readonly List<uint>
        MaleficList = [Malefic, Malefic2, Malefic3, Malefic4, FallMalefic],
        GravityList = [Gravity, Gravity2];
    internal static Dictionary<uint, ushort>
        CombustList = new()
        {
            { Combust, Debuffs.Combust },
            { Combust2, Debuffs.Combust2 },
            { Combust3, Debuffs.Combust3 }
        };
    public static ASTOpenerMaxLevel1 Opener1 = new();

    public static ASTGauge Gauge => GetJobGauge<ASTGauge>();
    public static CardType DrawnCard { get; set; }

    public static Dictionary<byte, int> JobPriorities = new()
    {
    { SAM.JobID, 1 },
    { NIN.JobID, 2 },
    { VPR.JobID, 3 },
    { DRG.JobID, 4 },
    { MNK.JobID, 5 },
    { DRK.JobID, 6 },
    { RPR.JobID, 7 },
    { PCT.JobID, 8 },
    { SMN.JobID, 9 },
    { MCH.JobID, 10 },
    { BRD.JobID, 11 },
    { RDM.JobID, 12 },
    { DNC.JobID, 13 },
    { BLM.JobID, 14 }
    };

    public static int SpellsSinceDraw()
    {
        if (ActionWatching.CombatActions.Count == 0)
            return 0;

        uint spellToCheck = Gauge.ActiveDraw == DrawType.Astral ? UmbralDraw : AstralDraw;
        int idx = ActionWatching.CombatActions.LastIndexOf(spellToCheck);
        if (idx == -1)
            idx = 0;

        int ret = 0;
        for (int i = idx; i < ActionWatching.CombatActions.Count; i++)
        {
            if (ActionWatching.GetAttackType(ActionWatching.CombatActions[i]) == ActionWatching.ActionAttackType.Spell)
                ret++;
        }
        return ret;
    }

    public static WrathOpener Opener()
    {
        if (Opener1.LevelChecked)
            return Opener1;

        return WrathOpener.Dummy;
    }

    internal static void InitCheckCards() => Svc.Framework.Update += CheckCards;

    private static void CheckCards(IFramework framework)
    {
        if (Svc.ClientState.LocalPlayer is null || Svc.ClientState.LocalPlayer.ClassJob.RowId != 33)
            return;

        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.Unconscious])
        {
            QuickTargetCards.SelectedRandomMember = null;
            return;
        }

        if (DrawnCard != Gauge.DrawnCards[0])
        {
            DrawnCard = Gauge.DrawnCards[0];
        }

        if (IsEnabled(CustomComboPreset.AST_Cards_QuickTargetCards) &&
            (QuickTargetCards.SelectedRandomMember is null || BetterTargetAvailable()))
        {
            if (ActionReady(Play1))
                QuickTargetCards.Invoke();
        }

        if (DrawnCard == CardType.None)
            QuickTargetCards.SelectedRandomMember = null;
    }

    private static bool BetterTargetAvailable()
    {
        if (QuickTargetCards.SelectedRandomMember is null ||
            QuickTargetCards.SelectedRandomMember.IsDead ||
            OutOfRange(Balance, QuickTargetCards.SelectedRandomMember))
            return true;

        IBattleChara? m = QuickTargetCards.SelectedRandomMember as IBattleChara;
        if (DrawnCard is CardType.Balance && JobIDs.Melee.Any(x => x == m.ClassJob.RowId) ||
            DrawnCard is CardType.Spear && JobIDs.Ranged.Any(x => x == m.ClassJob.RowId))
            return false;

        List<IBattleChara> targets = new();
        for (int i = 1; i <= 8; i++) //Checking all 8 available slots and skipping nulls & DCs
        {
            if (PartyUITargeting.GetPartySlot(i) is not IBattleChara member)
                continue;
            if (member.GameObjectId == QuickTargetCards.SelectedRandomMember.GameObjectId)
                continue;
            if (member is null)
                continue; //Skip nulls/disconnected people
            if (member.IsDead)
                continue;
            if (OutOfRange(Balance, member))
                continue;

            if (HasStatusEffect(Buffs.BalanceBuff, member, true)) continue;
            if (HasStatusEffect(Buffs.SpearBuff, member, true)) continue;

            if (Config.AST_QuickTarget_SkipDamageDown && TargetHasDamageDown(member))
                continue;
            if (Config.AST_QuickTarget_SkipRezWeakness && TargetHasRezWeakness(member))
                continue;

            if (member.GetRole() is CombatRole.Healer or CombatRole.Tank)
                continue;

            targets.Add(member);
        }

        if (targets.Count == 0)
            return false;
        if (DrawnCard is CardType.Balance && targets.Any(x => JobIDs.Melee.Any(y => y == x.ClassJob.RowId)) ||
            DrawnCard is CardType.Spear && targets.Any(x => JobIDs.Ranged.Any(y => y == x.ClassJob.RowId)))
        {
            QuickTargetCards.SelectedRandomMember = null;
            return true;
        }

        return false;
    }

    internal static void DisposeCheckCards() => Svc.Framework.Update -= CheckCards;

    internal class QuickTargetCards : CustomComboFunctions
    {
        internal static List<IGameObject> PartyTargets = [];

        internal static IGameObject? SelectedRandomMember;

        public static void Invoke()
        {
            if (DrawnCard is not CardType.None)
            {
                if (PartyUITargeting.GetPartySlot(2) is not null)
                {
                    _ = SetTarget();
                    Svc.Log.Debug($"Set card to {SelectedRandomMember?.Name}");
                }
                else
                {
                    Svc.Log.Debug($"Setting card to {Player.Name}");
                    SelectedRandomMember = Player.Object;
                }
            }
            else
            {
                SelectedRandomMember = null;
            }
        }

        private static bool SetTarget()
        {
            if (Gauge.DrawnCards[0].Equals(CardType.None))
                return false;
            CardType cardDrawn = Gauge.DrawnCards[0];
            PartyTargets.Clear();
            for (int i = 1; i <= 8; i++) //Checking all 8 available slots and skipping nulls & DCs
            {
                if (PartyUITargeting.GetPartySlot(i) is not IBattleChara member)
                    continue;
                if (member is null)
                    continue; //Skip nulls/disconnected people
                if (member.IsDead)
                    continue;
                if (OutOfRange(Balance, member))
                    continue;

                if (HasStatusEffect(Buffs.BalanceBuff, member, true)) continue;
                if (HasStatusEffect(Buffs.SpearBuff, member, true)) continue;

                if (Config.AST_QuickTarget_SkipDamageDown && TargetHasDamageDown(member))
                    continue;
                if (Config.AST_QuickTarget_SkipRezWeakness && TargetHasRezWeakness(member))
                    continue;

                PartyTargets.Add(member);
            }
            //The inevitable "0 targets found" because of debuffs
            if (PartyTargets.Count == 0)
            {
                for (int i = 1; i <= 8; i++) //Checking all 8 available slots and skipping nulls & DCs
                {
                    if (PartyUITargeting.GetPartySlot(i) is not IBattleChara member)
                        continue;
                    if (member is null)
                        continue; //Skip nulls/disconnected people
                    if (member.IsDead)
                        continue;
                    if (OutOfRange(Balance, member))
                        continue;

                    if (HasStatusEffect(Buffs.BalanceBuff, member, true))
                        continue;
                    if (HasStatusEffect(Buffs.SpearBuff, member, true))
                        continue;

                    PartyTargets.Add(member);
                }
            }

            if (SelectedRandomMember is not null)
            {
                if (PartyTargets.Any(x => x.GameObjectId == SelectedRandomMember.GameObjectId))
                {
                    //TargetObject(SelectedRandomMember);
                    return true;
                }
            }

            //Grok is a scary SOB
            if (PartyTargets.Count > 0)
            {
                //Start of AST Fixed Prio
                if (Config.AST_QuickTarget_Prio)
                {
                    PartyTargets.Sort((x, y) =>
                    {
                        int GetPriority(IGameObject obj)
                        {
                            if (obj is not IBattleChara chara)
                                return int.MaxValue;

                            return JobPriorities.TryGetValue((byte)chara.ClassJob.RowId, out int priority) ? priority : 99;

                        }

                        return GetPriority(x).CompareTo(GetPriority(y));
                    });
                }
                else
                {
                    PartyTargets.Shuffle();
                }
                //End of AST Fixed prio

                IGameObject? suitableDps = null;
                IGameObject? unsuitableDps = null;
                IGameObject? backupTarget = null;

                foreach (IGameObject partyMember in PartyTargets)
                {
                    if (partyMember is null) continue;
                    byte job = partyMember is IBattleChara chara ? (byte)chara.ClassJob.RowId : (byte)0;

                    // Suitable DPS (highest priority)
                    if (cardDrawn is CardType.Balance && JobIDs.Melee.Contains(job) ||
                        cardDrawn is CardType.Spear && JobIDs.Ranged.Contains(job))
                    {
                        suitableDps = partyMember;
                        break; // Found the best option, stop searching
                    }
                    // Unsuitable DPS (medium priority)
                    else if (cardDrawn is CardType.Balance && JobIDs.Ranged.Contains(job) ||
                             cardDrawn is CardType.Spear && JobIDs.Melee.Contains(job))
                    {
                        unsuitableDps = partyMember; // Store but keep looking for suitable DPS
                    }
                    // Healers/Tanks (lowest priority, if enabled)
                    else if (IsEnabled(CustomComboPreset.AST_Cards_QuickTargetCards_TargetExtra) &&
                             (cardDrawn is CardType.Balance && JobIDs.Tank.Contains(job) ||
                              cardDrawn is CardType.Spear && JobIDs.Healer.Contains(job)))
                    {
                        backupTarget = partyMember; // Store but keep looking for DPS
                    }
                }

                // Set SelectedRandomMember based on priority
                if (suitableDps is not null)
                {
                    SelectedRandomMember = suitableDps;
                    return true;
                }
                if (unsuitableDps is not null)
                {
                    SelectedRandomMember = unsuitableDps;
                    return true;
                }
                if (backupTarget is not null)
                {
                    SelectedRandomMember = backupTarget;
                    return true;
                }
            }
            return false;
        }
    }

    internal class ASTOpenerMaxLevel1 : WrathOpener
    {
        public override List<uint> OpenerActions { get; set; } =
        [
            EarthlyStar,
            FallMalefic,
            Combust3,
            Lightspeed,
            FallMalefic,
            FallMalefic,
            Divination,
            Balance,
            FallMalefic,
            LordOfCrowns,
            UmbralDraw,
            FallMalefic,
            Spear,
            Oracle,
            FallMalefic,
            FallMalefic,
            FallMalefic,
            FallMalefic,
            FallMalefic,
            Combust3,
            FallMalefic
        ];
        public override int MinOpenerLevel => 92;
        public override int MaxOpenerLevel => 109;

        internal override UserData? ContentCheckConfig => Config.AST_ST_DPS_Balance_Content;

        public override bool HasCooldowns()
        {
            if (GetCooldown(EarthlyStar).CooldownElapsed >= 4f)
                return false;

            if (!ActionReady(Lightspeed))
                return false;

            if (!ActionReady(Divination))
                return false;

            if (!ActionReady(Balance))
                return true;

            if (!ActionReady(LordOfCrowns))
                return false;

            if (!ActionReady(UmbralDraw))
                return false;

            return true;
        }
    }

    #region ID's

    internal const byte JobID = 33;

    internal const uint
        //DPS
        Malefic = 3596,
        Malefic2 = 3598,
        Malefic3 = 7442,
        Malefic4 = 16555,
        FallMalefic = 25871,
        Gravity = 3615,
        Gravity2 = 25872,
        Oracle = 37029,
        EarthlyStar = 7439,
        DetonateStar = 8324,

        //Cards
        AstralDraw = 37017,
        UmbralDraw = 37018,
        Play1 = 37019,
        Play2 = 37020,
        Play3 = 37021,
        Arrow = 37024,
        Balance = 37023,
        Bole = 37027,
        Ewer = 37028,
        Spear = 37026,
        Spire = 37025,
        MinorArcana = 37022,
        LordOfCrowns = 7444,
        LadyOfCrown = 7445,

        //Utility
        Divination = 16552,
        Lightspeed = 3606,

        //DoT
        Combust = 3599,
        Combust2 = 3608,
        Combust3 = 16554,

        //Healing
        Benefic = 3594,
        Benefic2 = 3610,
        AspectedBenefic = 3595,
        Helios = 3600,
        AspectedHelios = 3601,
        HeliosConjuction = 37030,
        Ascend = 3603,
        EssentialDignity = 3614,
        CelestialOpposition = 16553,
        CelestialIntersection = 16556,
        Horoscope = 16557,
        HoroscopeHeal = 16558,
        Exaltation = 25873,
        Macrocosmos = 25874,
        Synastry = 3612,
        CollectiveUnconscious = 3613;

    //Action Groups


    internal static class Buffs
    {
        internal const ushort
            AspectedBenefic = 835,
            AspectedHelios = 836,
            HeliosConjunction = 3894,
            Horoscope = 1890,
            HoroscopeHelios = 1891,
            NeutralSect = 1892,
            NeutralSectShield = 1921,
            Divination = 1878,
            LordOfCrownsDrawn = 2054,
            LadyOfCrownsDrawn = 2055,
            GiantDominance = 1248,
            ClarifyingDraw = 2713,
            Macrocosmos = 2718,
            //The "Buff" that shows when you're holding onto the card
            BalanceDrawn = 913,
            BoleDrawn = 914,
            ArrowDrawn = 915,
            SpearDrawn = 916,
            EwerDrawn = 917,
            SpireDrawn = 918,
            //The actual buff that buffs players
            BalanceBuff = 3887,
            BoleBuff = 3890,
            ArrowBuff = 3888,
            SpearBuff = 3889,
            EwerBuff = 3891,
            SpireBuff = 3892,
            Lightspeed = 841,
            SelfSynastry = 845,
            TargetSynastry = 846,
            Divining = 3893,
            EarthlyDominance = 1224;
    }

    internal static class Debuffs
    {
        internal const ushort
            Combust = 838,
            Combust2 = 843,
            Combust3 = 1881;
    }

    //Debuff Pairs of Actions and Debuff

    #endregion
}

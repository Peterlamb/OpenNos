﻿/*
 * This file is part of the OpenNos Emulator Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 */

using OpenNos.Core;
using OpenNos.Data;
using OpenNos.Domain;
using OpenNos.GameObject;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenNos.Handler
{
    public class BattlePacketHandler : IPacketHandler
    {
        #region Members

        private readonly ClientSession _session;

        #endregion

        #region Instantiation

        public BattlePacketHandler(ClientSession session)
        {
            _session = session;
        }

        #endregion

        #region Properties

        public ClientSession Session
        {
            get
            {
                return _session;
            }
        }

        #endregion

        #region Methods

        [Packet("mtlist")]
        public void SpecialZoneHit(string packet)
        {
            PenaltyLogDTO penalty = Session.Account.PenaltyLogs.OrderByDescending(s => s.DateEnd).FirstOrDefault();
            if (Session.Character.IsMuted())
            {
                Session.SendPacket("cancel 0 0");
                Session.CurrentMap?.Broadcast(Session.Character.Gender == GenderType.Female ? Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_FEMALE"), 1) :
                    Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_MALE"), 1));
                Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                return;
            }
            if ((DateTime.Now - Session.Character.LastTransform).TotalSeconds < 3)
            {
                Session.SendPacket("cancel 0 0");
                Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_ATTACKNOW"), 0));
                return;
            }
            if (Session.Character.IsVehicled)
            {
                Session.SendPacket("cancel 0 0");
                return;
            }
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            ushort damage = 0;
            int hitmode = 0;
            if (packetsplit.Length > 3)
            {
                // get amount of mobs which are targeted
                short mobAmount = 0;
                short.TryParse(packetsplit[2], out mobAmount);
                if ((packetsplit.Length - 3) / 2 != mobAmount)
                {
                    return;
                }

                for (int i = 3; i < packetsplit.Length - 2; i += 2)
                {
                    List<CharacterSkill> skills = Session.Character.UseSp ? Session.Character.SkillsSp.GetAllItems() : Session.Character.Skills.GetAllItems();
                    if (skills != null)
                    {
                        short mapMonsterTargetId = -1;
                        short skillCastId = -1;

                        if (short.TryParse(packetsplit[i], out skillCastId) && short.TryParse(packetsplit[i + 1], out mapMonsterTargetId))
                        {
                            Task t = Task.Factory.StartNew((Func<Task>)(async () =>
                            {
                                CharacterSkill ski = skills.FirstOrDefault(s => s.Skill.CastId == skillCastId - 1);
                                if (ski.CanBeUsed())
                                {
                                    MapMonster mon = Session.CurrentMap.GetMonster(mapMonsterTargetId);
                                    if (mon != null && mon.IsInRange(Session.Character.MapX, Session.Character.MapY, ski.Skill.Range) && ski != null && mon.CurrentHp > 0)
                                    {
                                        Session.Character.LastSkillUse = DateTime.Now;
                                        damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                                        Session.CurrentMap?.Broadcast($"su 1 {Session.Character.CharacterId} 3 {mon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {ski.Skill.Effect} {Session.Character.MapX} {Session.Character.MapY} {(mon.IsAlive ? 1 : 0)} {(int)(((float)mon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 0 {ski.Skill.SkillType - 1}");
                                        Task.Factory.StartNew(() => { GenerateKillBonus(mon.MapMonsterId); });
                                    }

                                    await Task.Delay((ski.Skill.Cooldown) * 100);
                                    Session.SendPacket($"sr {skillCastId - 1}");
                                }
                            }));
                        }
                    }
                }
            }
        }

        public void TargetHit(int castingId, int targetId)
        {
            IList<string> broadcastPackets = new List<string>();

            List<CharacterSkill> skills = Session.Character.UseSp ? Session.Character.SkillsSp.GetAllItems() : Session.Character.Skills.GetAllItems();
            bool notcancel = false;
            if ((DateTime.Now - Session.Character.LastTransform).TotalSeconds < 3)
            {
                Session.SendPacket("cancel 0 0");
                Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_ATTACK"), 0));
                return;
            }
            if (skills != null)
            {
                ushort damage = 0;
                int hitmode = 0;
                CharacterSkill ski = skills.FirstOrDefault(s => s.Skill?.CastId == castingId && s.Skill?.UpgradeSkill == 0);
                Session.SendPacket("ms_c 0");
                if (!Session.Character.WeaponLoaded(ski))
                {
                    Session.SendPacket("cancel 2 0");
                    return;
                }
                for (int i = 0; i < 10 && !ski.CanBeUsed(); i++)
                {
                    Thread.Sleep(100);
                    if (i == 10)
                    {
                        Session.SendPacket("cancel 2 0");
                        return;
                    }
                }

                if (ski != null && Session.Character.Mp >= ski.Skill.MpCost)
                {
                    if (ski.Skill.TargetType == 1 && ski.Skill.HitType == 1)
                    {
                        Session.Character.LastSkillUse = DateTime.Now;
                        if (!Session.Character.HasGodMode)
                        {
                            Session.Character.Mp -= ski.Skill.MpCost;
                        }
                        if (Session.Character.UseSp && ski.Skill.CastEffect != -1)
                        {
                            Session.SendPackets(Session.Character.GenerateQuicklist());
                        }

                        Session.SendPacket(Session.Character.GenerateStat());
                        CharacterSkill skillinfo = Session.Character.Skills.GetAllItems().OrderBy(o => o.SkillVNum).FirstOrDefault(s => s.Skill.UpgradeSkill == ski.Skill.SkillVNum && s.Skill.Effect > 0 && s.Skill.SkillType == 2);
                        Session.CurrentMap?.Broadcast($"ct 1 {Session.Character.CharacterId} 1 {Session.Character.CharacterId} {ski.Skill.CastAnimation} {(skillinfo != null ? skillinfo.Skill.CastEffect : ski.Skill.CastEffect)} {ski.Skill.SkillVNum}");

                        // Generate scp
                        ski.LastUse = DateTime.Now;
                        if (ski.Skill.CastEffect != 0)
                        {
                            Thread.Sleep(ski.Skill.CastTime * 100);
                        }
                        notcancel = true;
                        MapMonster mmon;
                        broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 1 {Session.Character.CharacterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(skillinfo != null ? skillinfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} 1 {((int)((double)Session.Character.Hp / Session.Character.HPLoad()) * 100)} 0 -2 {ski.Skill.SkillType - 1}");
                        if (ski.Skill.TargetRange != 0)
                        {
                            foreach (MapMonster mon in Session.CurrentMap.GetListMonsterInRange(Session.Character.MapX, Session.Character.MapY, ski.Skill.TargetRange).Where(s => s.CurrentHp > 0))
                            {
                                mmon = Session.CurrentMap.GetMonster(mon.MapMonsterId);
                                if (mmon != null)
                                {
                                    damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                                    broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {mmon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(skillinfo != null ? skillinfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} {(mmon.IsAlive ? 1 : 0)} {(int)(((float)mmon.CurrentHp / (float)mon.Monster.MaxHP) * 100)} {damage} 5 {ski.Skill.SkillType - 1}");
                                    Task.Factory.StartNew(() => { GenerateKillBonus(mon.MapMonsterId); });
                                }
                            }
                        }
                    }
                    else if (ski.Skill.TargetType == 0)
                    {
                        // if monster target
                        MapMonster monsterToAttack = Session.CurrentMap.GetMonster(targetId);
                        if (monsterToAttack != null && monsterToAttack.IsAlive)
                        {
                            NpcMonster monsterToAttackInfo = ServerManager.GetNpc(monsterToAttack.MonsterVNum);
                            if (monsterToAttack != null && ski != null && ski.CanBeUsed())
                            {
                                if (Session.Character.Mp >= ski.Skill.MpCost)
                                {
                                    short distanceX = (short)(Session.Character.MapX - monsterToAttack.MapX);
                                    short distanceY = (short)(Session.Character.MapY - monsterToAttack.MapY);

                                    if (Map.GetDistance(new MapCell() { X = Session.Character.MapX, Y = Session.Character.MapY },
                                                        new MapCell() { X = monsterToAttack.MapX, Y = monsterToAttack.MapY }) <= ski.Skill.Range + (DateTime.Now - monsterToAttack.LastMove).TotalSeconds * 2 * (monsterToAttackInfo.Speed == 0 ? 1 : monsterToAttackInfo.Speed) || ski.Skill.TargetRange != 0)
                                    {
                                        Session.Character.LastSkillUse = DateTime.Now;
                                        damage = GenerateDamage(monsterToAttack.MapMonsterId, ski.Skill, ref hitmode);

                                        ski.LastUse = DateTime.Now;
                                        Task.Factory.StartNew(() => { GenerateKillBonus(monsterToAttack.MapMonsterId); });
                                        notcancel = true;
                                        if (!Session.Character.HasGodMode)
                                        {
                                            Session.Character.Mp -= ski.Skill.MpCost;
                                        }
                                        if (Session.Character.UseSp && ski.Skill.CastEffect != -1)
                                        {
                                            Session.SendPackets(Session.Character.GenerateQuicklist());
                                        }
                                        Session.SendPacket(Session.Character.GenerateStat());
                                        CharacterSkill characterSkillInfo = Session.Character.Skills.GetAllItems().OrderBy(o => o.SkillVNum).FirstOrDefault(s => s.Skill.UpgradeSkill == ski.Skill.SkillVNum && s.Skill.Effect > 0 && s.Skill.SkillType == 2);
                                        Session.CurrentMap?.Broadcast($"ct 1 {Session.Character.CharacterId} 3 {monsterToAttack.MapMonsterId} {ski.Skill.CastAnimation} {(characterSkillInfo != null ? characterSkillInfo.Skill.CastEffect : ski.Skill.CastEffect)} {ski.Skill.SkillVNum}");
                                        Session.Character.Skills.GetAllItems().Where(s => s.Id != ski.Id).ToList().ForEach(i => i.Hit = 0);

                                        // Generate scp
                                        ski.LastUse = DateTime.Now;
                                        if (damage == 0 || (DateTime.Now - ski.LastUse).TotalSeconds > 3)
                                        {
                                            ski.Hit = 0;
                                        }
                                        else
                                        {
                                            ski.Hit++;
                                        }
                                        if (ski.Skill.CastEffect != 0)
                                        {
                                            Thread.Sleep(ski.Skill.CastTime * 100);
                                        }

                                        ComboDTO skillCombo = ski.Skill.Combos.FirstOrDefault(s => ski.Hit == s.Hit);
                                        if (skillCombo != null)
                                        {
                                            if (ski.Skill.Combos.OrderByDescending(s => s.Hit).ElementAt(0).Hit == ski.Hit)
                                            {
                                                ski.Hit = 0;
                                            }
                                            broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {monsterToAttack.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {skillCombo.Animation} {skillCombo.Effect} {Session.Character.MapX} {Session.Character.MapY} {(monsterToAttack.IsAlive ? 1 : 0)} {(int)(((float)monsterToAttack.CurrentHp / (float)monsterToAttackInfo.MaxHP) * 100)} {damage} {hitmode} {ski.Skill.SkillType - 1}");
                                        }
                                        else
                                        {
                                            broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {monsterToAttack.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(characterSkillInfo != null ? characterSkillInfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} {(monsterToAttack.IsAlive ? 1 : 0)} {(int)(((float)monsterToAttack.CurrentHp / (float)monsterToAttackInfo.MaxHP) * 100)} {damage} {hitmode} {ski.Skill.SkillType - 1}");
                                        }
                                        if (ski.Skill.TargetRange != 0)
                                        {
                                            IEnumerable<MapMonster> monstersInAOERange = Session.CurrentMap?.GetListMonsterInRange(monsterToAttack.MapX, monsterToAttack.MapY, ski.Skill.TargetRange).ToList();
                                            foreach (MapMonster mon in monstersInAOERange.Where(s => s.CurrentHp > 0))
                                            {
                                                damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                                                broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {mon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(characterSkillInfo != null ? characterSkillInfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} {(mon.IsAlive ? 1 : 0)} {(int)(((float)mon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 5 {ski.Skill.SkillType - 1}");
                                                Task.Factory.StartNew(() => { GenerateKillBonus(mon.MapMonsterId); });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // send su packets
                    Session.CurrentMap.Broadcast(broadcastPackets.ToArray());

                    Task t = Task.Factory.StartNew((Func<Task>)(async () =>
                    {
                        await Task.Delay((ski.Skill.Cooldown) * 100);
                        Session.SendPacket($"sr {castingId}");
                    }));
                }
                else
                {
                    notcancel = false;
                    Session.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("NOT_ENOUGH_MP"), 10));
                }
            }
            if (!notcancel)
            {
                Session.SendPacket($"cancel 2 {targetId}");
            }
        }

        [Packet("u_s")]
        public void UseSkill(string packet)
        {
            PenaltyLogDTO penalty = Session.Account.PenaltyLogs.OrderByDescending(s => s.DateEnd).FirstOrDefault();
            if (Session.Character.IsMuted())
            {
                if (Session.Character.Gender == GenderType.Female)
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_FEMALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
                else
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_MALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
                return;
            }
            if (Session.Character.CanFight)
            {
                Logger.Debug(packet, Session.SessionId);
                string[] packetsplit = packet.Split(' ');
                if (packetsplit.Length > 6)
                {
                    short MapX = -1, MapY = -1;
                    if (!short.TryParse(packetsplit[5], out MapX) || !short.TryParse(packetsplit[6], out MapY))
                    {
                        return;
                    }
                    Session.Character.MapX = MapX;
                    Session.Character.MapY = MapY;
                }
                byte usrType;
                if (!byte.TryParse(packetsplit[3], out usrType))
                {
                    return;
                }
                byte usertype = usrType;
                if (Session.Character.IsSitting)
                {
                    Session.Character.Rest();
                }
                if (Session.Character.IsVehicled || Session.Character.InvisibleGm)
                {
                    Session.SendPacket("cancel 0 0");
                    return;
                }
                switch (usertype)
                {
                    case (byte)UserType.Monster:
                        if (packetsplit.Length > 4)
                        {
                            if (Session.Character.Hp > 0)
                            {
                                TargetHit(Convert.ToInt32(packetsplit[2]), Convert.ToInt32(packetsplit[4]));
                            }
                        }
                        break;

                    case (byte)UserType.Player:
                        if (packetsplit.Length > 4)
                        {
                            if (Session.Character.Hp > 0 && Convert.ToInt64(packetsplit[4]) == Session.Character.CharacterId)
                            {
                                TargetHit(Convert.ToInt32(packetsplit[2]), Convert.ToInt32(packetsplit[4]));
                            }
                            else
                            {
                                Session.SendPacket("cancel 2 0");
                            }
                        }
                        break;

                    default:
                        Session.SendPacket("cancel 2 0");
                        return;
                }
            }
        }

        [Packet("u_as")]
        public void UseZonesSkill(string packet)
        {
            PenaltyLogDTO penalty = Session.Account.PenaltyLogs.OrderByDescending(s => s.DateEnd).FirstOrDefault();
            if (Session.Character.IsMuted())
            {
                if (Session.Character.Gender == GenderType.Female)
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_FEMALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
                else
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_MALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
            }
            else
            {
                if (Session.Character.LastTransform.AddSeconds(3) > DateTime.Now)
                {
                    Session.SendPacket("cancel 0 0");
                    Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_ATTACK"), 0));
                    return;
                }
                if (Session.Character.IsVehicled)
                {
                    Session.SendPacket("cancel 0 0");
                    return;
                }
                Logger.Debug(packet, Session.SessionId);
                if (Session.Character.CanFight)
                {
                    string[] packetsplit = packet.Split(' ');
                    if (packetsplit.Length > 4)
                    {
                        if (Session.Character.Hp > 0)
                        {
                            int CastingId;
                            short x = -1;
                            short y = -1;
                            if (!int.TryParse(packetsplit[2], out CastingId) || !short.TryParse(packetsplit[3], out x) || !short.TryParse(packetsplit[4], out y))
                            {
                                return;
                            }
                            ZoneHit(CastingId, x, y);
                        }
                    }
                }
            }
        }

        private ushort GenerateDamage(int monsterid, Skill skill, ref int hitmode)
        {
            #region Definitions

            MapMonster monsterToAttack = Session.CurrentMap.GetMonster(monsterid);
            if (monsterToAttack == null)
            {
                return 0;
            }                

            short distanceX = (short)(Session.Character.MapX - monsterToAttack.MapX);
            short distanceY = (short)(Session.Character.MapY - monsterToAttack.MapY);
            Random random = new Random();
            int generated = random.Next(0, 100);

            // int miss_chance = 20;
            int monsterDefence = 0;

            short mainUpgrade = 0;
            int mainCritChance = 4;
            int mainCritHit = 70;
            int mainMinDmg = 0;
            int mainMaxDmg = 0;
            int mainHitRate = 0;

            short secUpgrade = 0;
            int secCritChance = 0;
            int secCritHit = 0;
            int secMinDmg = 0;
            int secMaxDmg = 0;
            int secHitRate = 0;

            // int CritChance = 4;
            // int CritHit = 70;
            // int MinDmg = 0;
            // int MaxDmg = 0;
            // int HitRate = 0;
            // sbyte Upgrade = 0;
            #endregion

            #region Sp

            SpecialistInstance specialistInstance = Session.Character.Inventory.LoadBySlotAndType<SpecialistInstance>((byte)EquipmentType.Sp, InventoryType.Wear);

            #endregion

            #region Get Weapon Stats

            WearableInstance weapon = Session.Character.Inventory.LoadBySlotAndType<WearableInstance>((byte)EquipmentType.MainWeapon, InventoryType.Wear);
            if (weapon != null)
            {
                mainUpgrade = weapon.Upgrade;
            }

            mainMinDmg += Session.Character.MinHit;
            mainMaxDmg += Session.Character.MaxHit;
            mainHitRate += Session.Character.HitRate;
            mainCritChance += Session.Character.HitCriticalRate;
            mainCritHit += Session.Character.HitCritical;

            WearableInstance weapon2 = Session.Character.Inventory.LoadBySlotAndType<WearableInstance>((byte)EquipmentType.SecondaryWeapon, InventoryType.Wear);
            if (weapon2 != null)
            {
                secUpgrade = weapon2.Upgrade;
            }

            secMinDmg += Session.Character.MinDistance;
            secMaxDmg += Session.Character.MaxDistance;
            secHitRate += Session.Character.DistanceRate;
            secCritChance += Session.Character.DistanceCriticalRate;
            secCritHit += Session.Character.DistanceCritical;

            #endregion

            #region Switch skill.Type

            switch (skill.Type)
            {
                case 0:
                    monsterDefence = monsterToAttack.Monster.CloseDefence;
                    if (Session.Character.Class == ClassType.Archer)
                    {
                        mainCritHit = secCritHit;
                        mainCritChance = secCritChance;
                        mainHitRate = secHitRate;
                        mainMaxDmg = secMaxDmg;
                        mainMinDmg = secMinDmg;
                        mainUpgrade = secUpgrade;
                    }
                    break;

                case 1:
                    monsterDefence = monsterToAttack.Monster.DistanceDefence;
                    if (Session.Character.Class == ClassType.Swordman || Session.Character.Class == ClassType.Adventurer)
                    {
                        mainCritHit = secCritHit;
                        mainCritChance = secCritChance;
                        mainHitRate = secHitRate;
                        mainMaxDmg = secMaxDmg;
                        mainMinDmg = secMinDmg;
                        mainUpgrade = secUpgrade;
                    }
                    break;

                case 2:
                    monsterDefence = monsterToAttack.Monster.MagicDefence;
                    break;
            }

            #endregion

            #region Basic Damage Data Calculation

            if (specialistInstance != null)
            {
                mainMinDmg += specialistInstance.DamageMinimum;
                mainMaxDmg += specialistInstance.DamageMaximum;
                mainCritHit += specialistInstance.CriticalRate;
                mainCritChance += specialistInstance.CriticalLuckRate;
                mainHitRate += specialistInstance.HitRate;
            }

#warning TODO: Implement BCard damage boosts, see Issue

            mainUpgrade -= monsterToAttack.Monster.DefenceUpgrade;
            if (mainUpgrade < -10)
            {
                mainUpgrade = -10;
            }
            else if (mainUpgrade > 10)
            {
                mainUpgrade = 10;
            }
            
            #endregion

            #region Detailed Calculation

            #region Base Damage

            int baseDamage = new Random().Next(mainMinDmg, mainMaxDmg + 1);
            baseDamage += (skill.Damage / 4);
            int elementalDamage = 0; // placeholder for BCard etc...
            elementalDamage += (skill.ElementalDamage / 4);
            switch (mainUpgrade)
            {
                case -10:
                    monsterDefence += (int)(monsterDefence * 2);
                    break;

                case -9:
                    monsterDefence += (int)(monsterDefence * 1.2);
                    break;

                case -8:
                    monsterDefence += (int)(monsterDefence * 0.9);
                    break;

                case -7:
                    monsterDefence += (int)(monsterDefence * 0.65);
                    break;

                case -6:
                    monsterDefence += (int)(monsterDefence * 0.54);
                    break;

                case -5:
                    monsterDefence += (int)(monsterDefence * 0.43);
                    break;

                case -4:
                    monsterDefence += (int)(monsterDefence * 0.32);
                    break;

                case -3:
                    monsterDefence += (int)(monsterDefence * 0.22);
                    break;

                case -2:
                    monsterDefence += (int)(monsterDefence * 0.15);
                    break;

                case -1:
                    monsterDefence += (int)(monsterDefence * 0.1);
                    break;

                case 0:
                    break;

                case 1:
                    baseDamage += (int)(baseDamage * 0.1);
                    break;

                case 2:
                    baseDamage += (int)(baseDamage * 0.15);
                    break;

                case 3:
                    baseDamage += (int)(baseDamage * 0.22);
                    break;

                case 4:
                    baseDamage += (int)(baseDamage * 0.32);
                    break;

                case 5:
                    baseDamage += (int)(baseDamage * 0.43);
                    break;

                case 6:
                    baseDamage += (int)(baseDamage * 0.54);
                    break;

                case 7:
                    baseDamage += (int)(baseDamage * 0.65);
                    break;

                case 8:
                    baseDamage += (int)(baseDamage * 0.9);
                    break;

                case 9:
                    baseDamage += (int)(baseDamage * 1.2);
                    break;

                case 10:
                    baseDamage += (int)(baseDamage * 2);
                    break;
            }

            #endregion

            #region Critical Damage

            if (random.Next(100) <= mainCritChance)
            {
                if (skill.Type == 2)
                {
                }
                else if (skill.Type == 3 && Session.Character.Class != ClassType.Magician)
                {
                    baseDamage = (int)(baseDamage * ((mainCritHit / 100D) + 1));
                    hitmode = 3;
                }
                else
                {
                    baseDamage = (int)(baseDamage * ((mainCritHit / 100D) + 1));
                    hitmode = 3;
                }
            }

            #endregion

            #region Elementary Damage

            #region Calculate Elemental Boost + Rate

            double elementalBoost = 0;
            short monsterResistance = 0;
            switch (Session.Character.Element)
            {
                case 0:
                    break;

                case 1:
                    monsterResistance = monsterToAttack.Monster.FireResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;

                        case 1:
                            elementalBoost = 1;
                            break;

                        case 2:
                            elementalBoost = 2;
                            break;

                        case 3:
                            elementalBoost = 0.5;
                            break;

                        case 4:
                            elementalBoost = 1.5;
                            break;
                    }
                    break;

                case 2:
                    monsterResistance = monsterToAttack.Monster.WaterResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;

                        case 1:
                            elementalBoost = 2;
                            break;

                        case 2:
                            elementalBoost = 1;
                            break;

                        case 3:
                            elementalBoost = 1.5;
                            break;

                        case 4:
                            elementalBoost = 0.5;
                            break;
                    }
                    break;

                case 3:
                    monsterResistance = monsterToAttack.Monster.LightResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;

                        case 1:
                            elementalBoost = 1.5;
                            break;

                        case 2:
                            elementalBoost = 0.5;
                            break;

                        case 3:
                            elementalBoost = 1;
                            break;

                        case 4:
                            elementalBoost = 2;
                            break;
                    }
                    break;

                case 4:
                    monsterResistance = monsterToAttack.Monster.DarkResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;

                        case 1:
                            elementalBoost = 0.5;
                            break;

                        case 2:
                            elementalBoost = 1.5;
                            break;

                        case 3:
                            elementalBoost = 2;
                            break;

                        case 4:
                            elementalBoost = 1;
                            break;
                    }
                    break;
            }

            #endregion;

            if (monsterResistance < 0)
            {
                monsterResistance = 0;
            }
            elementalDamage = (int)((elementalDamage + ((elementalDamage + baseDamage) * (((Session.Character.ElementRate + Session.Character.ElementRateSP) / 100D) + 1))) * elementalBoost);
            elementalDamage = elementalDamage / 100 * (100 - monsterResistance);

            #endregion

            #region Total Damage

            int totalDamage = baseDamage + elementalDamage - monsterDefence;
            if (totalDamage < 5)
            {
                totalDamage = random.Next(1, 6);
            }

            #endregion

            #endregion

            if (monsterToAttack.DamageList.ContainsKey(Session.Character.CharacterId))
            {
                monsterToAttack.DamageList[Session.Character.CharacterId] += totalDamage;
            }
            else
            {
                monsterToAttack.DamageList.Add(Session.Character.CharacterId, totalDamage);
            }
            if (monsterToAttack.CurrentHp <= totalDamage)
            {
                monsterToAttack.IsAlive = false;
                monsterToAttack.CurrentHp = 0;
                monsterToAttack.CurrentMp = 0;
                monsterToAttack.Death = DateTime.Now;
            }
            else
            {
                monsterToAttack.CurrentHp -= totalDamage;
            }
            ushort damage = 0;

            while (totalDamage > ushort.MaxValue)
            {
                totalDamage -= ushort.MaxValue;
            }

            monsterToAttack.LastEffect = DateTime.Now;
            damage = Convert.ToUInt16(totalDamage);
            if (monsterToAttack.IsMoving)
            {
                monsterToAttack.Target = Session.Character.CharacterId;
            }
            return damage;
        }

        private async void GenerateKillBonus(int monsterid)
        {
            MapMonster monsterToAttack = Session.CurrentMap.GetMonster(monsterid);
            if (monsterToAttack == null || monsterToAttack.IsAlive)
            {
                return;
            }
            else
            {
                // wait for the mob to die
                await Task.Delay(350);
            }

            Random random = new Random(DateTime.Now.Millisecond & monsterid);

            // owner set
            long? dropOwner = monsterToAttack.DamageList.Any() ? monsterToAttack.DamageList.First().Key : (long?)null;
            Group group = null;
            if (dropOwner != null)
            {
                group = ServerManager.Instance.Groups.FirstOrDefault(g => g.IsMemberOfGroup((long)dropOwner));
            }

            // end owner set
            int i = 1;
            List<DropDTO> droplist = monsterToAttack.Monster.Drops.Where(s => Session.CurrentMap.MapTypes.Any(m => m.MapTypeId == s.MapTypeId) || (s.MapTypeId == null)).ToList();
            if (monsterToAttack.Monster.MonsterType != MonsterType.Special)
            {
                int RateDrop = ServerManager.DropRate;
                int x = 0;

                foreach (DropDTO drop in droplist.OrderBy(s => random.Next()))
                {
                    if (x < 4)
                    {
                        i++;
                        double rndamount = random.Next(0, 100) * random.NextDouble();
                        if (rndamount <= ((double)drop.DropChance * RateDrop) / 5000.000)
                        {
                            x++;
                            if (Session.CurrentMap.MapTypes.Any(s => s.MapTypeId == (short)MapTypeEnum.Act4) || monsterToAttack.Monster.MonsterType == MonsterType.Elite)
                            {
                                Session.Character.GiftAdd(drop.ItemVNum, (byte)drop.Amount);
                            }
                            else
                            {
                                if (group != null)
                                {
                                    if (group.SharingMode == (byte)GroupSharingType.ByOrder)
                                    {
                                        dropOwner = group.GetNextOrderedCharacterId(Session.Character);
                                        if(dropOwner.HasValue)
                                        {
                                            group.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("ITEM_BOUND_TO"), ServerManager.GetItem(drop.ItemVNum).Name, group.Characters.Single(c => c.Character.CharacterId == (long)dropOwner).Character.Name, drop.Amount), 10)));
                                        }
                                    }
                                    else
                                    {
                                        group.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("DROPPED_ITEM"), ServerManager.GetItem(drop.ItemVNum).Name, drop.Amount), 10)));
                                    }
                                }
                                Session.CurrentMap.DropItemByMonster(dropOwner, drop, monsterToAttack.MapX, monsterToAttack.MapY);
                            }
                        }
                    }
                }

                int goldRate = ServerManager.GoldRate;
                int dropIt = ((random.Next(0, Session.Character.Level) < monsterToAttack.Monster.Level / 6) ? 1 : 0);
                int lowBaseGold = random.Next(6 * monsterToAttack.Monster.Level, 12 * monsterToAttack.Monster.Level);
                int isAct52 = (Session.CurrentMap.MapTypes.Any(s => s.MapTypeId == (short)MapTypeEnum.Act52) ? 10 : 1);
                int gold = Convert.ToInt32(dropIt * lowBaseGold * goldRate * isAct52);
                gold = gold > 1000000000 ? 1000000000 : gold;
                if (gold != 0)
                {
                    DropDTO drop2 = new DropDTO()
                    {
                        Amount = gold,
                        ItemVNum = 1046
                    };

                    if (Session.CurrentMap.MapTypes.Any(s => s.MapTypeId == (short)MapTypeEnum.Act4) || monsterToAttack.Monster.MonsterType == MonsterType.Elite)
                    {
                        Session.Character.Gold += drop2.Amount;
                        if (Session.Character.Gold > 1000000000)
                        {
                            Session.Character.Gold = 1000000000;
                            Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("MAX_GOLD"), 0));
                        }
                        Session.SendPacket(Session.Character.GenerateSay($"{Language.Instance.GetMessageFromKey("ITEM_ACQUIRED")}: {ServerManager.GetItem(drop2.ItemVNum).Name} x {drop2.Amount}", 10));
                        Session.SendPacket(Session.Character.GenerateGold());
                    }
                    else
                    {
                        if (group != null)
                        {
                            if (group.SharingMode == (byte)GroupSharingType.ByOrder)
                            {
                                dropOwner = group.GetNextOrderedCharacterId(Session.Character);

                                if(dropOwner.HasValue)
                                {
                                    group.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("ITEM_BOUND_TO"), ServerManager.GetItem(drop2.ItemVNum).Name, group.Characters.Single(c => c.Character.CharacterId == (long)dropOwner).Character.Name, drop2.Amount), 10)));
                                }
                            }
                            else
                            {
                                group.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("DROPPED_ITEM"), ServerManager.GetItem(drop2.ItemVNum).Name, drop2.Amount), 10)));
                            }
                        }
                        Session.CurrentMap.DropItemByMonster(dropOwner, drop2, monsterToAttack.MapX, monsterToAttack.MapY);
                    }
                }
                if (Session.Character.Hp > 0)
                {
                    Group grp = ServerManager.Instance.Groups.FirstOrDefault(g => g.IsMemberOfGroup(Session.Character.CharacterId));
                    if (grp != null)
                    {
                        foreach (ClientSession targetSession in grp.Characters.Where(g => g.Character.MapId == Session.Character.MapId))
                        {
                            targetSession.Character.GenerateXp(monsterToAttack.Monster);
                        }
                    }
                    else
                    {
                        Session.Character.GenerateXp(monsterToAttack.Monster);
                    }
                    Session.Character.GenerateDignity(monsterToAttack.Monster);
                }
            }
        }

        private void ZoneHit(int Castingid, short x, short y)
        {
            List<CharacterSkill> skills = Session.Character.UseSp ? Session.Character.SkillsSp.GetAllItems() : Session.Character.Skills.GetAllItems();
            ushort damage = 0;
            int hitmode = 0;
            CharacterSkill ski = skills.FirstOrDefault(s => s.Skill.CastId == Castingid);
            if (!Session.Character.WeaponLoaded(ski))
            {
                Session.SendPacket("cancel 2 0");
                return;
            }
            if (ski != null && ski.CanBeUsed())
            {
                if (Session.Character.Mp >= ski.Skill.MpCost)
                {
                    Task t = Task.Factory.StartNew((Func<Task>)(async () =>
                    {
                        Session.CurrentMap?.Broadcast($"ct_n 1 {Session.Character.CharacterId} 3 -1 {ski.Skill.CastAnimation} {ski.Skill.CastEffect} {ski.Skill.SkillVNum}");
                        ski.LastUse = DateTime.Now;
                        if (!Session.Character.HasGodMode)
                        {
                            Session.Character.Mp -= ski.Skill.MpCost;
                        }
                        Session.SendPacket(Session.Character.GenerateStat());
                        ski.LastUse = DateTime.Now;
                        await Task.Delay(ski.Skill.CastTime * 100);
                        Session.Character.LastSkillUse = DateTime.Now;

                        Session.CurrentMap?.Broadcast($"bs 1 {Session.Character.CharacterId} {x} {y} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {ski.Skill.Effect} 0 0 1 1 0 0 0");

                        IEnumerable<MapMonster> monstersInRange = Session.CurrentMap.GetListMonsterInRange(x, y, ski.Skill.TargetRange).ToList();
                        foreach (MapMonster mon in monstersInRange.Where(s => s.CurrentHp > 0))
                        {
                            damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                            Session.CurrentMap?.Broadcast($"su 1 {Session.Character.CharacterId} 3 {mon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {ski.Skill.Effect} {x} {y} {(mon.IsAlive ? 1 : 0)} {(int)(((float)mon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 5 {ski.Skill.SkillType - 1}");
                            Task.Factory.StartNew(() => { GenerateKillBonus(mon.MapMonsterId); });
                        }

                        await Task.Delay((ski.Skill.Cooldown) * 100);
                        Session.SendPacket($"sr {Castingid}");
                    }));
                }
                else
                {
                    Session.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("NOT_ENOUGH_MP"), 10));
                    Session.SendPacket("cancel 2 0");
                }
            }
            else
            {
                Session.SendPacket("cancel 2 0");
            }
        }

        #endregion
    }
}
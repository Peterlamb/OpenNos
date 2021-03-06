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

using EpPathFinding.cs;
using OpenNos.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenNos.GameObject
{
    public class MapNpc : MapNpcDTO
    {
        #region Members

        public NpcMonster Npc;
        private int _movetime;
        private Random _random;

        #endregion

        #region Instantiation

        public MapNpc()
        {
        }

        #endregion

        #region Properties

        public short FirstX { get; set; }

        public short FirstY { get; set; }

        public DateTime LastEffect { get; private set; }

        public DateTime LastMove { get; private set; }

        public Map Map { get; set; }

        public List<GridPos> Path { get; set; }

        public List<Recipe> Recipes { get; set; }

        public Shop Shop { get; set; }

        public int Target { get; set; }

        public List<TeleporterDTO> Teleporters { get; set; }

        #endregion

        #region Methods

        public string GenerateEff()
        {
            NpcMonster npc = ServerManager.GetNpc(this.NpcVNum);
            if (npc != null)
            {
                return $"eff 2 {MapNpcId} {Effect}";
            }
            else
            {
                return String.Empty;
            }
        }

        public string GenerateEff(short effect)
        {
            NpcMonster npc = ServerManager.GetNpc(this.NpcVNum);
            if (npc != null)
            {
                return $"eff 2 {MapNpcId} {effect}";
            }
            else
            {
                return String.Empty;
            }
        }

        public string GenerateIn2()
        {
            NpcMonster npcinfo = ServerManager.GetNpc(this.NpcVNum);
            if (npcinfo != null && !IsDisabled)
            {
                return $"in 2 {NpcVNum} {MapNpcId} {MapX} {MapY} {Position} 100 100 {Dialog} 0 0 -1 1 {(IsSitting ? 1 : 0)} -1 - 0 -1 0 0 0 0 0 0 0 0";
            }
            else
            {
                return String.Empty;
            }
        }

        public string GenerateMv2()
        {
            return $"mv 2 {MapNpcId} {MapX} {MapY} {Npc.Speed}";
        }

        public string GetNpcDialog()
        {
            return $"npc_req 2 {MapNpcId} {Dialog}";
        }

        public override void Initialize()
        {
            _random = new Random(MapNpcId);
            Npc = ServerManager.GetNpc(this.NpcVNum);
            LastEffect = DateTime.Now;
            LastMove = DateTime.Now;
            FirstX = MapX;
            FirstY = MapY;
            _movetime = _random.Next(500, 3000);
            Path = new List<GridPos>();
            Recipes = ServerManager.Instance.GetReceipesByMapNpcId(MapNpcId);
            Target = -1;
            Teleporters = ServerManager.Instance.GetTeleportersByNpcVNum((short)MapNpcId);
            Shop shop = ServerManager.Instance.GetShopByMapNpcId(MapNpcId);
            if (shop != null)
            {
                shop.Initialize();
                Shop = shop;
            }
        }

        internal void NpcLife()
        {
            double time = (DateTime.Now - LastEffect).TotalMilliseconds;
            if (Effect > 0 && time > EffectDelay)
            {
                Map.Broadcast(GenerateEff(), MapX, MapY);
                LastEffect = DateTime.Now;
            }

            time = (DateTime.Now - LastMove).TotalMilliseconds;
            if (IsMoving && Npc.Speed > 0 && time > _movetime)
            {
                _movetime = _random.Next(500, 3000);
                byte point = (byte)_random.Next(2, 4);
                byte fpoint = (byte)_random.Next(0, 2);

                byte xpoint = (byte)_random.Next(fpoint, point);
                byte ypoint = (byte)(point - xpoint);

                short mapX = FirstX;
                short mapY = FirstY;

                if (Map.GetFreePosition(ref mapX, ref mapY, xpoint, ypoint))
                {
                    Task.Factory.StartNew(async () =>
                    {
                        await Task.Delay(1000 * (xpoint + ypoint) / (2 * Npc.Speed));
                        this.MapX = mapX;
                        this.MapY = mapY;
                    });
                    LastMove = DateTime.Now.AddSeconds((xpoint + ypoint) / (2 * Npc.Speed));
                    Map.Broadcast(new BroadcastPacket(null, GenerateMv2(), ReceiverType.AllInRange, xCoordinate: mapX, yCoordinate: mapY));
                }
            }
            if (Target == -1)
            {
                if (this.Npc.IsHostile)
                {
                    MapMonster monster = this.Map.Monsters.FirstOrDefault(s => MapId == s.MapId && Map.GetDistance(new MapCell() { X = MapX, Y = MapY }, new MapCell() { X = s.MapX, Y = s.MapY }) < (Npc.NoticeRange > 5 ? Npc.NoticeRange / 2 : Npc.NoticeRange));
                    if (monster != null)
                    {
                        Target = monster.MapMonsterId;
                    }
                }
            }
            else if (Target != -1)
            {
                MapMonster monster = this.Map.Monsters.FirstOrDefault(s => s.MapMonsterId == Target);
                if (monster == null || monster.CurrentHp < 1)
                {
                    Target = -1;
                    return;
                }
                NpcMonsterSkill npcMonsterSkill = null;
                if (_random.Next(10) > 8)
                {
                    npcMonsterSkill = Npc.Skills.Where(s => (DateTime.Now - s.LastSkillUse).TotalMilliseconds >= 100 * s.Skill.Cooldown).OrderBy(rnd => _random.Next()).FirstOrDefault();
                }

                short damage = 100;
                int distance = Map.GetDistance(new MapCell() { X = MapX, Y = MapY }, new MapCell() { X = monster.MapX, Y = monster.MapY });
                if (monster != null && monster.CurrentHp > 0 && ((npcMonsterSkill != null && distance < npcMonsterSkill.Skill.Range) || (distance <= Npc.BasicRange)))
                {
                    if (((DateTime.Now - LastEffect).TotalMilliseconds >= 1000 + Npc.BasicCooldown * 200 && !Npc.Skills.Any()) || npcMonsterSkill != null)
                    {
                        if (npcMonsterSkill != null)
                        {
                            npcMonsterSkill.LastSkillUse = DateTime.Now;
                            Map.Broadcast($"ct 2 {MapNpcId} 3 {Target} {npcMonsterSkill.Skill.CastAnimation} {npcMonsterSkill.Skill.CastEffect} {npcMonsterSkill.Skill.SkillVNum}");
                        }

                        if (npcMonsterSkill != null && npcMonsterSkill.Skill.CastEffect != 0)
                        {
                            Map.Broadcast(GenerateEff());
                        }

                        monster.CurrentHp -= damage;

                        if (npcMonsterSkill != null)
                        {
                            Map.Broadcast($"su 2 {MapNpcId} 3 {Target} {npcMonsterSkill.SkillVNum} {npcMonsterSkill.Skill.Cooldown} {npcMonsterSkill.Skill.AttackAnimation} {npcMonsterSkill.Skill.Effect} 0 0 {(monster.CurrentHp > 0 ? 1 : 0)} { (int)(monster.CurrentHp / monster.Monster.MaxHP * 100) } {damage} 0 0");
                        }
                        else
                        {
                            Map.Broadcast($"su 2 {MapNpcId} 3 {Target} 0 {Npc.BasicCooldown} 11 {Npc.BasicSkill} 0 0 {(monster.CurrentHp > 0 ? 1 : 0)} { (int)(monster.CurrentHp / monster.Monster.MaxHP * 100) } {damage} 0 0");
                        }

                        LastEffect = DateTime.Now;
                        if (monster.CurrentHp  < 1)
                        {
                            if (IsMoving)
                            {
                                Path = Map.StraightPath(new GridPos() { x = this.MapX, y = this.MapY }, new GridPos() { x = FirstX, y = FirstY });
                                if (!Path.Any())
                                {
                                    Path = Map.JPSPlus(new GridPos() { x = this.MapX, y = this.MapY }, new GridPos() { x = FirstX, y = FirstY });
                                }
                            }

                            monster.IsAlive = false;
                            monster.CurrentHp = 0;
                            monster.CurrentMp = 0;
                            monster.Death = DateTime.Now;
                            Target = -1;
                        }                        
                    }
                }
                else
                {
                    int maxdistance = (Npc.NoticeRange > 5 ? Npc.NoticeRange / 2 : Npc.NoticeRange);
                    if (IsMoving)
                    {
                        short maxDistance = 5;
                        if (Path.Count() == 0 && monster != null && distance > 1 && distance < maxDistance)
                        {
                            short xoffset = (short)_random.Next(-1, 1);
                            short yoffset = (short)_random.Next(-1, 1);

                            Path = Map.StraightPath(new GridPos() { x = this.MapX, y = this.MapY }, new GridPos() { x = (short)(monster.MapX + xoffset), y = (short)(monster.MapY + yoffset) });
                            if (!Path.Any())
                            {
                                Path = Map.JPSPlus(new GridPos() { x = this.MapX, y = this.MapY }, new GridPos() { x = (short)(monster.MapX + xoffset), y = (short)(monster.MapY + yoffset) });
                            }
                        }
                        if (DateTime.Now > LastMove && Npc.Speed > 0 && Path.Any())
                        {
                            short mapX;
                            short mapY;
                            int maxindex = Path.Count > Npc.Speed / 2 ? Npc.Speed / 2 : Path.Count;
                            mapX = (short)Path.ElementAt(maxindex - 1).x;
                            mapY = (short)Path.ElementAt(maxindex - 1).y;
                            double waitingtime = (double)(Map.GetDistance(new MapCell() { X = mapX, Y = mapY, MapId = MapId }, new MapCell() { X = MapX, Y = MapY, MapId = MapId })) / (double)(Npc.Speed);
                            Map.Broadcast(new BroadcastPacket(null, $"mv 2 {this.MapNpcId} {mapX} {mapY} {Npc.Speed}", ReceiverType.AllInRange, xCoordinate: mapX, yCoordinate: mapY));
                            LastMove = DateTime.Now.AddSeconds((waitingtime > 1 ? 1 : waitingtime));
                            Task.Factory.StartNew(async () =>
                            {
                                await Task.Delay((int)((waitingtime > 1 ? 1 : waitingtime) * 1000));
                                this.MapX = mapX;
                                this.MapY = mapY;
                            });

                            for (int j = maxindex; j > 0; j--)
                            {
                                Path.RemoveAt(0);
                            }
                        }
                        if (Path.Count() == 0 && (monster == null || MapId != monster.MapId || distance > maxDistance))
                        {
                            Path = Map.StraightPath(new GridPos() { x = this.MapX, y = this.MapY }, new GridPos() { x = FirstX, y = FirstY });
                            if (!Path.Any())
                            {
                                Path = Map.JPSPlus(new GridPos() { x = this.MapX, y = this.MapY }, new GridPos() { x = FirstX, y = FirstY });
                            }
                            Target = -1;
                        }
                    }
                    else
                    {
                        if (distance > maxdistance)
                        {
                            Target = -1;
                        }
                    }
                }
            }
        }

        #endregion
    }
}
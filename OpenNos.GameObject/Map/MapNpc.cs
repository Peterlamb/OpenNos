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

using OpenNos.Data;
using System;
using System.Collections.Generic;
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

        public List<Recipe> Recipes { get; set; }

        public Shop Shop { get; set; }

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
            Recipes = ServerManager.Instance.GetReceipesByMapNpcId(MapNpcId);
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
        }

        #endregion
    }
}
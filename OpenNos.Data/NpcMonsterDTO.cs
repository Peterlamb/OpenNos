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

namespace OpenNos.Data
{
    public class NpcMonsterDTO
    {
        #region Properties

        public short NpcMonsterVNum { get; set; }
        public string Name { get; set; }
        public byte Speed { get; set; }
        public byte Level { get; set; }
        public byte AttackClass { get; set; }
        public byte AttackUpgrade { get; set; }
        public short DamageMinimum { get; set; }
        public short DamageMaximum { get; set; }
        public short Concentrate { get; set; }
        public byte Element { get; set; }
        public short ElementRate { get; set; }
        public short CriticalRate { get; set; }
        public byte CriticalLuckRate { get; set; }
        public short CloseDefence { get; set; }
        public short DefenceDodge { get; set; }
        public short MagicDefence { get; set; }
        public byte DefenceUpgrade { get; set; }
        public short DistanceDefence { get; set; }
        public short DistanceDefenceDodge { get; set; }
        public sbyte FireResistance { get; set; }
        public sbyte WaterResistance { get; set; }
        public sbyte LightResistance { get; set; }
        public sbyte DarkResistance { get; set; }
        public short MaxHP { get; set; }
        public short MaxMP { get; set; }
        public short UnknownData { get; set; }
        // public byte Race { get; set; }
        // public byte RaceType { get; set; }

        #endregion
    }
}
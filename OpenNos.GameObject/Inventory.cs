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
using OpenNos.Core;
using OpenNos.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNos.GameObject
{
    public class Inventory : ThreadSafeSortedList<Guid, ItemInstance>
    {
        #region Members

        private Random _random;

        #endregion

        #region Instantiation

        public Inventory(Character Character)
        {
            Owner = Character;
            _random = new Random();
        }

        #endregion

        #region Properties

        public Character Owner { get; set; }

        #endregion

        #region Methods

        public static ItemInstance InstantiateItemInstance(short vnum, long ownerId, byte amount = 1)
        {
            ItemInstance newItem = new ItemInstance() { ItemVNum = vnum, Amount = amount, CharacterId = ownerId };
            if (newItem.Item != null)
            {
                switch (newItem.Item.Type)
                {
                    case InventoryType.Equipment:
                        if (newItem.Item.ItemType == ItemType.Specialist)
                        {
                            newItem = new SpecialistInstance() { ItemVNum = vnum, SpLevel = 1, Amount = amount };
                        }
                        else
                        {
                            newItem = new WearableInstance() { ItemVNum = vnum, Amount = amount };
                        }
                        break;
                }
            }

            // set default itemType
            newItem.Type = newItem.Item.Type;

            return newItem;
        }

        public ItemInstance AddNewToInventory(short vnum, byte amount = 1, InventoryType? type = null)
        {
            Logger.Debug(vnum.ToString(), Owner.Session.SessionId);

            ItemInstance newItem = InstantiateItemInstance(vnum, Owner.CharacterId, amount);

            return AddToInventory(newItem, type);
        }

        public ItemInstance AddToInventory(ItemInstance newItem, InventoryType? type = null)
        {
            Logger.Debug(newItem.ItemVNum.ToString(), Owner.Session.SessionId);
            IEnumerable<ItemInstance> slotfree = null;
            ItemInstance inv = null;

            // override type if necessary
            if (type.HasValue)
            {
                newItem.Type = type.Value;
            }

            // check if item can be stapled
            if (newItem.Type != InventoryType.Equipment && newItem.Type != InventoryType.Wear)
            {
                slotfree = Owner.LoadBySlotAllowed(newItem.ItemVNum, newItem.Amount);
                inv = GetFreeSlot(slotfree);
            }

            if (inv != null)
            {
                // add item amount
                inv.Amount = (byte)(newItem.Amount + inv.Amount);
            }
            else
            {
                // create new item
                short freeSlot = newItem.Type == InventoryType.Wear ? (LoadBySlotAndType((short)newItem.Item.EquipmentSlot, InventoryType.Wear) == null
                                                                    ? (short)newItem.Item.EquipmentSlot
                                                                    : (short)-1)
                                                                  : GetFreeSlot(newItem.Type, Owner.BackPack);
                if (freeSlot != -1)
                {
                    inv = AddToInventoryWithSlotAndType(newItem, newItem.Type, freeSlot);
                }
            }

            return inv;
        }

        /// <summary>
        /// Add iteminstance to inventory with specified slot and type, iteminstance will be overridden.
        /// </summary>
        /// <param name="itemInstance"></param>
        /// <param name="type"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        public ItemInstance AddToInventoryWithSlotAndType(ItemInstance itemInstance, InventoryType type, short slot)
        {
            Logger.Debug($"Slot: {slot} Type: {type} VNUM: {itemInstance.ItemVNum}", Owner.Session.SessionId);
            itemInstance.Slot = slot;
            itemInstance.Type = type;
            itemInstance.CharacterId = Owner.Session.Character.CharacterId;

            if (this.ContainsKey(itemInstance.Id))
            {
                throw new InvalidOperationException("Cannot add the same ItemInstance twice to inventory.");
            }

            string inventoryPacket = Owner.Session.Character.GenerateInventoryAdd(itemInstance.ItemVNum, itemInstance.Amount, type, slot, itemInstance.Rare, itemInstance.Design, itemInstance.Upgrade, 0);
            if (!String.IsNullOrEmpty(inventoryPacket))
            {
                Owner.Session.SendPacket(inventoryPacket);
            }

            if (this.GetAllItems().Any(s => s.Slot == slot && s.Type == type))
            {
                return null;
            }
            CheckItemInstanceType(itemInstance);
            this[itemInstance.Id] = itemInstance;
            return itemInstance;
        }

        public bool CanAddItem(short itemVnum)
        {
            InventoryType type = ServerManager.GetItem(itemVnum).Type;
            return CanAddItem(type);
        }

        public bool CanAddItem(InventoryType type)
        {
            return GetFreeSlot(type, Owner.BackPack) != -1;
        }

        public int CountItem(int v)
        {
            int count = 0;
            foreach (ItemInstance inv in this.GetAllItems().Where(s => s.ItemVNum == v))
            {
                count += inv.Amount;
            }
            return count;
        }

        public Inventory DeepCopy()
        {
            Inventory clonedList = (Inventory)this.MemberwiseClone();

            return clonedList;
        }

        public Tuple<short, InventoryType> DeleteById(Guid id)
        {
            Logger.Debug(id.ToString(), Owner.Session.SessionId);
            Tuple<short, InventoryType> removedPlace = new Tuple<short, InventoryType>(0, 0);
            ItemInstance inv = this[id];

            if (inv != null)
            {
                removedPlace = new Tuple<short, InventoryType>(inv.Slot, inv.Type);
                this.Remove(inv.Id);
            }
            else
            {
                throw new InvalidOperationException("Expected item wasn't deleted, Type or Slot did not match!");
            }

            return removedPlace;
        }

        public void DeleteFromSlotAndType(short slot, InventoryType type)
        {
            Logger.Debug($"Slot: {slot} Type: {type}", Owner.Session.SessionId);
            ItemInstance inv = this.GetAllItems().FirstOrDefault(i => i.Slot.Equals(slot) && i.Type.Equals(type));

            if (inv != null)
            {
                this.Remove(inv.Id);
            }
            else
            {
                throw new InvalidOperationException("Expected item wasn't deleted, Type or Slot did not match!");
            }
        }

        public void GenerateClearInventory(InventoryType type)
        {
            for (short i = 0; i < 48; i++)
            {
                Owner.Session.SendPacket(Owner.GenerateInventoryAdd(-1, 0, type, i, 0, 0, 0, 0));
            }
        }

        public bool GetFreePlaceAmount(List<ItemInstance> item, int backPack)
        {
            short[] place = new short[item.Count()];
            for (byte k = 0; k < item.Count(); k++)
            {
                place[k] = (byte)(48 + (backPack * 12));
                for (short i = 0; i < 48 + (backPack * 12); i++)
                {
                    ItemInstance result = LoadBySlotAndType(i, (InventoryType)item[k].Item.Type);
                    if (result != null && result.Type == 0)
                    {
                        place[k]--;
                    }
                    else if (result != null)
                    {
                        bool check = false;
                        foreach (ItemInstance itemins in item)
                        {
                            if (itemins.Item.Type != 0 && itemins.Amount + result.Amount <= 99)
                            {
                                check = true;
                            }
                        }
                        if (!check)
                        {
                            place[k]--;
                        }
                    }
                }
            }
            for (int i = 0; i < item.Count(); i++)
            {
                if (place[i] == 0)
                {
                    return false;
                }
            }
            return true;
        }

        public ItemInstance GetFreeSlot(IEnumerable<ItemInstance> slotfree)
        {
            List<Guid> inventoryitemids = new List<Guid>();
            foreach (ItemInstance itemfree in slotfree)
            {
                inventoryitemids.Add(itemfree.Id);
            }
            return this.GetAllItems().Where(i => inventoryitemids.Contains(i.Id)).OrderBy(i => i.Slot).FirstOrDefault();
        }

        public ItemInstance GetItemInstanceById(Guid id)
        {
            return this[id];
        }

        public bool IsEmpty()
        {
            return !this.GetAllItems().Any();
        }

        public T LoadByItemInstance<T>(Guid id)
                    where T : ItemInstance
        {
            return (T)this[id];
        }

        public T LoadBySlotAndType<T>(short slot, InventoryType type)
                    where T : ItemInstance
        {
            return (T)this.GetAllItems().SingleOrDefault(i => i.GetType().Equals(typeof(T)) && i.Slot == slot && i.Type == type);
        }

        public ItemInstance LoadBySlotAndType(short slot, InventoryType type)
        {
            return this.GetAllItems().SingleOrDefault(i => i.Slot.Equals(slot) && i.Type.Equals(type));
        }

        /// <summary> Moves one item from one Inventory to another. Example: Equipment <-> Wear,
        /// Equipment <-> Costume, Equipment <-> Specialist </summary> <param
        /// name="sourceSlot"></param> <param name="sourceType"></param> <param
        /// name="targetType"></param> <returns></returns>
        public ItemInstance MoveInInventory(short sourceSlot, InventoryType sourceType, InventoryType targetType, short? targetSlot = null, bool wear = true)
        {
            var sourceInstance = LoadBySlotAndType(sourceSlot, sourceType);

            if (sourceInstance == null && wear)
            {
                throw new InvalidOperationException("SourceInstance to move does not exist.");
            }
            else
            {
                if (targetSlot.HasValue)
                {
                    if (wear)
                    {
                        // swap
                        var targetInstance = LoadBySlotAndType(targetSlot.Value, targetType);

                        sourceInstance.Slot = targetSlot.Value;
                        sourceInstance.Type = targetType;

                        targetInstance.Slot = sourceSlot;
                        targetInstance.Type = sourceType;
                    }
                    else
                    {
                        // move source to target
                        sourceInstance.Slot = GetFreeSlot(targetType, Owner.BackPack);
                        sourceInstance.Type = targetType;
                    }

                    return sourceInstance;
                }

                // check for free target slot
                short freeSlot = targetType == InventoryType.Wear ? (LoadBySlotAndType((short)sourceInstance.Item.EquipmentSlot, InventoryType.Wear) == null
                                                                    ? (short)sourceInstance.Item.EquipmentSlot
                                                                    : (short)-1)
                                                                  : GetFreeSlot(targetType, Owner.BackPack);
                if (freeSlot != -1)
                {
                    sourceInstance.Type = targetType;
                    sourceInstance.Slot = freeSlot;
                }
                else
                {
                    return null;
                }
            }

            return sourceInstance;
        }

        public void MoveItem(InventoryType type, short sourceSlot, byte amount, short destinationSlot, out ItemInstance sourceInventory, out ItemInstance destinationInventory)
        {
            Logger.Debug($"type: {type} sourceSlot: {sourceSlot} amount: {amount} destinationSlot: {destinationSlot}", Owner.Session.SessionId);

            // load source and destination slots
            sourceInventory = LoadBySlotAndType(sourceSlot, type);
            destinationInventory = LoadBySlotAndType(destinationSlot, type);
            if (sourceInventory != null && amount <= sourceInventory.Amount)
            {
                if (destinationInventory == null)
                {
                    if (sourceInventory.Amount == amount)
                    {
                        sourceInventory.Slot = destinationSlot;
                    }
                    else
                    {
                        ItemInstance itemDest = sourceInventory.DeepCopy();
                        sourceInventory.Amount -= amount;
                        itemDest.Amount = amount;
                        itemDest.Id = Guid.NewGuid();
                        destinationInventory = AddToInventoryWithSlotAndType(itemDest, sourceInventory.Type, destinationSlot);
                    }
                }
                else
                {
                    if (destinationInventory.ItemVNum == sourceInventory.ItemVNum && sourceInventory.Type != 0)
                    {
                        if (destinationInventory.Amount + amount > 99)
                        {
                            int saveItemCount = destinationInventory.Amount;
                            destinationInventory.Amount = 99;
                            sourceInventory.Amount = (byte)(saveItemCount + sourceInventory.Amount - 99);
                        }
                        else
                        {
                            destinationInventory.Amount += amount;
                            sourceInventory.Amount -= amount;

                            // item with amount of 0 should be removed
                            if (sourceInventory.Amount == 0)
                            {
                                DeleteFromSlotAndType(sourceInventory.Slot, sourceInventory.Type);
                            }
                        }
                    }
                    else
                    {
                        // add and remove save inventory
                        destinationInventory = TakeInventory(destinationInventory.Slot, destinationInventory.Type);
                        destinationInventory.Slot = sourceSlot;
                        sourceInventory = TakeInventory(sourceInventory.Slot, sourceInventory.Type);
                        sourceInventory.Slot = destinationSlot;
                        PutInventory(destinationInventory);
                        PutInventory(sourceInventory);
                    }
                }
            }
            sourceInventory = LoadBySlotAndType(sourceSlot, type);
            destinationInventory = LoadBySlotAndType(destinationSlot, type);
        }

        /// <summary>
        /// Puts a Single Invenory including ItemInstance to the List
        /// </summary>
        /// <param name="inventory"></param>
        public void PutInventory(ItemInstance inventory)
        {
            this[inventory.Id] = inventory;
        }

        public void RemoveItemAmount(int vnum, int amount = 1)
        {
            Logger.Debug($"vnum: {vnum} amount: {amount}", Owner.Session.SessionId);
            int remainingAmount = amount;

            foreach (ItemInstance inventory in this.GetAllItems().Where(s => s.ItemVNum == vnum).OrderBy(i => i.Slot))
            {
                if (remainingAmount > 0)
                {
                    Logger.Debug($"Remaining {remainingAmount}/{amount}, removing item {inventory.ItemVNum} from Slot {inventory.Slot} with amount {inventory.Amount}");
                    if (inventory.Amount > remainingAmount)
                    {
                        // amount completely removed
                        inventory.Amount -= (byte)remainingAmount;
                        remainingAmount = 0;
                        Owner.Session.SendPacket(Owner.Session.Character.GenerateInventoryAdd(inventory.ItemVNum,
                            inventory.Amount, inventory.Type, inventory.Slot, inventory.Rare, inventory.Design,
                            inventory.Upgrade, 0));
                    }
                    else
                    {
                        // amount partly removed
                        remainingAmount -= inventory.Amount;
                        DeleteById(inventory.Id);
                        Owner.Session.SendPacket(Owner.Session.Character.GenerateInventoryAdd(-1, 0, inventory.Type, inventory.Slot, 0, 0, 0, 0));
                    }
                }
                else
                {
                    // amount to remove reached
                    break;
                }
            }
        }

        public ItemInstance RemoveItemAmountFromInventory(byte amount, Guid id)
        {
            Logger.Debug($"InventoryId: {id} amount: {amount}", Owner.Session.SessionId);
            ItemInstance inv = this.GetAllItems().FirstOrDefault(i => i.Id.Equals(id));

            if (inv != null)
            {
                inv.Amount -= amount;
                if (inv.Amount <= 0)
                {
                    this.Remove(inv.Id);
                    return null;
                }
            }

            return inv;
        }

        public void Reorder(ClientSession Session, InventoryType inventoryType)
        {
            List<ItemInstance> itemsByInventoryType = new List<ItemInstance>();
            switch (inventoryType)
            {
                case InventoryType.Costume:
                    itemsByInventoryType = this.GetAllItems().Where(s => s.Type == InventoryType.Costume).OrderBy(s => s.ItemVNum).ToList();
                    break;

                case InventoryType.Specialist:
                    itemsByInventoryType = this.GetAllItems().Where(s => s.Type == InventoryType.Specialist).OrderBy(s => s.Item.LevelJobMinimum).ToList();
                    break;
            }

            short i = 0;
            
            // TODO OPTIMIZE WITH JUST REMOVE SLOT, avoid sending unneccessary empty slots
            GenerateClearInventory(inventoryType);

            foreach (ItemInstance item in itemsByInventoryType)
            {
                // remove item from inventory
                Remove(item.Id);

                // readd item to inventory
                item.Slot = i;
                Session.SendPacket(Session.Character.GenerateInventoryAdd(item.ItemVNum, item.Amount, inventoryType, item.Slot, item.Rare, item.Design, item.Upgrade, 0));
                this[item.Id] = item;

                // increment slot
                i++;
            }
        }

        /// <summary>
        /// Takes a Single Inventory including ItemInstance from the List and removes it.
        /// </summary>
        /// <param name="slot"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public ItemInstance TakeInventory(short slot, InventoryType type)
        {
            ItemInstance inventoryToTake = this.GetAllItems().SingleOrDefault(i => i.Slot == slot && i.Type == type);
            this.Remove(inventoryToTake.Id);
            return inventoryToTake;
        }

        private void CheckItemInstanceType(ItemInstance itemInstance)
        {
            if (itemInstance.Type == InventoryType.Specialist && !(itemInstance is SpecialistInstance))
            {
                throw new Exception("Cannot add an item of type Specialist without beeing a SpecialistInstance.");
            }

            if ((itemInstance.Type == InventoryType.Equipment || itemInstance.Type == InventoryType.Wear) && !(itemInstance is WearableInstance))
            {
                throw new Exception("Cannot add an item of type Equipment or Wear without beeing a WearableInstance.");
            }
        }

        private short GetFreeSlot(InventoryType type, int backPack)
        {
            ItemInstance result;
            for (short i = 0; i < 48 + (backPack * 12); i++)
            {
                result = this.GetAllItems().SingleOrDefault(c => c.Type == type && c.Slot.Equals(i));
                if (result == null)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion
    }
}